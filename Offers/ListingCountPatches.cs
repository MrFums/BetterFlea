using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;
using BetterFlea.Flea;

namespace BetterFlea.Offers;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class ListingCountPatchesLoad(
    ConfigManager configManager,
    ItemHelper itemHelper) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        ListingCountGate.Bind(configManager, itemHelper);
        new TrackAssortItemPatch().Enable();
        new ListingCountPatch().Enable();
        new AddOfferCapPatch().Enable();
        return Task.CompletedTask;
    }
}

// SPT only passes the parent when picking a count, which can't tell marked keys apart. Each assort entry
// runs on its own task, so remember the item per thread.
internal static class ListingCountGate
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static readonly ConcurrentDictionary<MongoId, double> MultiplierCache = new();

    [ThreadStatic]
    private static MongoId? _currentTpl;

    // Asked twice: how many to make, and the cap inside AddOffer.
    [ThreadStatic]
    private static bool _inAddOffer;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper)
    {
        _config = configManager;
        _itemHelper = itemHelper;
    }

    public static void Enter(MongoId? tpl) => _currentTpl = tpl;

    public static void Exit() => _currentTpl = null;

    // Lost guns go on whatever the cap says.
    [ThreadStatic]
    public static bool NoCap;

    public static void EnterAddOffer() => _inAddOffer = true;

    public static void ExitAddOffer() => _inAddOffer = false;

    public static int Scale(int count)
    {
        if (NoCap && _inAddOffer)
        {
            return int.MaxValue;
        }

        if (_currentTpl is not { } tpl || count <= 0)
        {
            return count;
        }

        // Money only uses currencyMultiplier.
        if (_itemHelper!.IsOfBaseclass(tpl, BaseClasses.MONEY))
        {
            var currency = MultiplierCache.GetOrAdd(tpl, MultiplierFor);
            return currency == 1.0 ? count : (int)Math.Round(count * currency);
        }

        var full = OnDemandOffers.FullCount(tpl, FullCount(tpl, count));
        return _inAddOffer ? full : OnDemandOffers.CountToMake(tpl, full);
    }

    private static int FullCount(MongoId tpl, int count)
    {
        if (!_config!.Mod.GeneratedOffers.Listings.Enabled)
        {
            return count;
        }

        // Group multiplier goes on last so 3.0 really means three times as many.
        var multiplier = MultiplierCache.GetOrAdd(tpl, MultiplierFor);
        var scaled = multiplier == 1.0 ? count : (int)Math.Round(count * multiplier);
        var floored = AtLeastMinimum(scaled, MinimumCache.GetOrAdd(tpl, MinimumFor));
        var group = GroupMultiplierCache.GetOrAdd(tpl, GroupMultiplierFor);
        return group == 1.0 ? floored : (int)Math.Round(floored * group);
    }

    private static readonly ConcurrentDictionary<MongoId, double> GroupMultiplierCache = new();

    private static double GroupMultiplierFor(MongoId tpl)
    {
        var listings = _config!.Mod.GeneratedOffers.Listings;
        var item = _itemHelper!.GetItem(tpl).Value;
        return listings.CategoryMultipliers.Count > 0 && item is not null
            && FleaCategories.Find(item, _itemHelper) is { } category
            && listings.CategoryMultipliers.TryGetValue(category.Name, out var groupMultiplier)
            ? Math.Clamp(groupMultiplier, 0, MaxMultiplier)
            : 1.0;
    }

    // Land a bit above the minimum so everything below it doesn't sit on the same number.
    private static int AtLeastMinimum(int scaled, int minimum) =>
        scaled >= minimum ? scaled : minimum + Random.Shared.Next((int)Math.Round(minimum * MinimumSpread) + 1);

    private const double MinimumSpread = 0.5;

    private static readonly ConcurrentDictionary<MongoId, int> MinimumCache = new();

    private static int MinimumFor(MongoId tpl)
    {
        var listings = _config!.Mod.GeneratedOffers.Listings;
        var minimums = listings.MinimumOffers;
        var everything = minimums.TryGetValue("all", out var all) ? all : 0;

        var item = _itemHelper!.GetItem(tpl).Value;
        if (item is null || FleaCategories.Find(item, _itemHelper) is not { } category)
        {
            return everything;
        }

        // A superrare item is rare on the flea too, so its own minimum replaces the others.
        if (item.Properties?.RarityPvE == "Superrare" && listings.SuperrareMinimumOffers.TryGetValue(category.Name, out var superrare))
        {
            return superrare;
        }

        var forCategory = minimums.TryGetValue(category.Name, out var minimum) ? minimum : 0;
        return Math.Max(everything, forCategory);
    }

    private static double MultiplierFor(MongoId tpl)
    {
        var listings = _config!.Mod.GeneratedOffers.Listings;
        var item = _itemHelper!.GetItem(tpl).Value;
        if (_itemHelper.IsOfBaseclass(tpl, BaseClasses.MONEY))
        {
            return Math.Clamp(listings.CurrencyMultiplier, 0, MaxMultiplier);
        }

        var multiplier = listings.Multiplier;
        if (item?.Properties?.RarityPvE is { } rarity
            && listings.RarityMultipliers.TryGetValue(rarity, out var rarityMultiplier))
        {
            multiplier *= rarityMultiplier;
        }

        return Math.Min(multiplier, MaxMultiplier);
    }

    private const double MaxMultiplier = 10.0;
}

public class TrackAssortItemPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairOfferGenerator).GetMethod(
            "CreateOffersFromAssort",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPrefix]
    public static bool Prefix(List<Item> assortItemWithChildren, bool isExpiredOffer)
    {
        var tpl = assortItemWithChildren.FirstOrDefault()?.Template;
        ListingCountGate.Enter(tpl);

        return !isExpiredOffer || tpl is not { } template || OnDemandOffers.ShouldReplaceExpired(template);
    }

    // Only entries that were actually listed, not ones a prefix skipped.
    [PatchPostfix]
    public static void Postfix(List<Item> assortItemWithChildren, bool isExpiredOffer, bool __runOriginal)
    {
        if (__runOriginal && !isExpiredOffer && assortItemWithChildren.FirstOrDefault()?.Template is { } tpl)
        {
            OnDemandOffers.Remember(tpl, assortItemWithChildren);
        }

        ListingCountGate.Exit();
    }
}

public class AddOfferCapPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairOfferHolder).GetMethod(nameof(RagfairOfferHolder.AddOffer), BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix()
    {
        ListingCountGate.EnterAddOffer();
    }

    [PatchPostfix]
    public static void Postfix()
    {
        ListingCountGate.ExitAddOffer();
    }
}

public class ListingCountPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairServerHelper).GetMethod(
            nameof(RagfairServerHelper.GetOfferCountByBaseType),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(ref int __result)
    {
        __result = ListingCountGate.Scale(__result);
    }
}
