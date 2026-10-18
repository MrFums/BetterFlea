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
        return Task.CompletedTask;
    }
}

// SPT only knows the item's parent when picking a listing count, which can't tell marked keys from
// other mechanical keys. CreateOffersFromAssort runs per item on its own task, so remember the item per thread.
internal static class ListingCountGate
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static readonly ConcurrentDictionary<MongoId, double> MultiplierCache = new();

    [ThreadStatic]
    private static MongoId? _currentTpl;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper)
    {
        _config = configManager;
        _itemHelper = itemHelper;
    }

    public static void Enter(MongoId? tpl) => _currentTpl = tpl;

    public static void Exit() => _currentTpl = null;

    public static int Scale(int count)
    {
        if (_currentTpl is not { } tpl || count <= 0)
        {
            return count;
        }

        var multiplier = MultiplierCache.GetOrAdd(tpl, MultiplierFor);
        return multiplier == 1.0 ? count : (int)Math.Round(count * multiplier);
    }

    private static double MultiplierFor(MongoId tpl)
    {
        var listings = _config!.Mod.GeneratedOffers.Listings;
        var item = _itemHelper!.GetItem(tpl).Value;
        var multiplier = listings.Multiplier;
        if (listings.CategoryMultipliers.Count > 0
            && item is not null
            && FleaCategories.Find(item, _itemHelper) is { } category
            && listings.CategoryMultipliers.TryGetValue(category.Name, out var categoryMultiplier))
        {
            multiplier = categoryMultiplier;
        }

        if (item?.Properties?.RarityPvE is { } rarity
            && listings.RarityMultipliers.TryGetValue(rarity, out var rarityMultiplier))
        {
            multiplier *= rarityMultiplier;
        }

        return Math.Min(multiplier, MaxMultiplier);
    }

    // Same cap ConfigManager puts on each multiplier, so stacking them can't eat the server's RAM.
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
    public static void Prefix(List<Item> assortItemWithChildren)
    {
        ListingCountGate.Enter(assortItemWithChildren.FirstOrDefault()?.Template);
    }

    [PatchPostfix]
    public static void Postfix()
    {
        ListingCountGate.Exit();
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
