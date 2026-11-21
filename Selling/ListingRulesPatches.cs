using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace BetterFlea.Selling;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class DisableFleaSellingPatchesLoad(
    ConfigManager configManager,
    HttpResponseUtil httpResponseUtil,
    EventOutputHolder eventOutputHolder,
    RagfairConfig ragfairConfig,
    ItemHelper itemHelper) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        FleaSellingGate.Bind(configManager, httpResponseUtil, eventOutputHolder, ragfairConfig, itemHelper);
        new DisableFleaSellingPatch().Enable();
        new BuyBanSearchPatch().Enable();
        return Task.CompletedTask;
    }
}

internal static class FleaSellingGate
{
    public const string DisabledMessage = "Player listings are turned off at the Flea Market.";
    public const string NotFoundInRaidMessage = "Only found in raid items can be sold at the Flea Market.";

    private static ConfigManager? _config;
    private static HttpResponseUtil? _http;
    private static EventOutputHolder? _output;
    private static RagfairConfig? _ragfair;
    private static ItemHelper? _itemHelper;

    public static void Bind(
        ConfigManager configManager,
        HttpResponseUtil http,
        EventOutputHolder output,
        RagfairConfig ragfair,
        ItemHelper itemHelper)
    {
        _config = configManager;
        _http = http;
        _output = output;
        _ragfair = ragfair;
        _itemHelper = itemHelper;
    }

    public const string BannedPlateMessage = "Take out the armor plates that can't be sold at the Flea Market first.";

    // Armor plates the flea doesn't take (class 5 and 6 front and back plates), anywhere in the listing: as live, the
    // armor can't go up with them in. Only plates: a gun's banned parts are allowed (ModdedParts.AllowFleaBannedParts).
    private static readonly MongoId PlateBase = new("644120aa86ffbe10ee032b6f");
    private static readonly MongoId SoftInsertBase = new("65649eb40bf0ed77b8044453");

    public static bool AnyBannedPlate(IEnumerable<Item> listed) => _itemHelper is { } helper && listed.Any(item =>
        helper.IsOfBaseclass(item.Template, PlateBase) && !helper.IsOfBaseclass(item.Template, SoftInsertBase)
        && helper.GetItem(item.Template).Value?.Properties?.CanSellOnRagfair == false);

    public static bool Blocked => _config is not null && !_config.Mod.PlayerSelling.Enabled;

    public static bool FoundInRaidOnly => _config?.Mod.PlayerSelling.FoundInRaidOnly == true;

    // Children count too, so a FIR gun with non-FIR parts is refused.
    public static bool AllFoundInRaid(List<Item> listed) => listed.All(item => item.Upd?.SpawnedInSession == true);

    public static List<Item> WithChildren(PmcData pmcData, IEnumerable<MongoId> listedIds)
    {
        var found = new List<Item>();
        var inventory = pmcData.Inventory?.Items;
        if (inventory is null)
        {
            return found;
        }

        var byId = new Dictionary<string, Item>();
        foreach (var item in inventory)
        {
            byId.TryAdd(item.Id.ToString(), item);
        }

        var childrenOf = inventory.Where(item => item.ParentId is not null).ToLookup(item => item.ParentId);
        var pending = new Stack<string>(listedIds.Select(id => id.ToString()));
        while (pending.TryPop(out var id))
        {
            // Left for SPT to reject.
            if (!byId.TryGetValue(id, out var item))
            {
                continue;
            }

            found.Add(item);
            foreach (var child in childrenOf[id])
            {
                pending.Push(child.Id.ToString());
            }
        }

        return found;
    }

    public const string BoughtFromTraderMessage = "Trader items can't be sold at the Flea Market.";
    public const string BannedMessage = "This item is banned from the Flea Market.";

    // Better Flea's bans only keep generated offers off the flea, so players are stopped from listing them here.
    public static bool AnyBanned(IEnumerable<Item> listed) => listed.Any(item => IsBanned(item.Template));

    public static bool IsBanned(MongoId tpl) => _ragfair?.Dynamic.Blacklist.Custom.Contains(tpl) == true;

    public static int BanCount => _ragfair?.Dynamic.Blacklist.Custom.Count ?? 0;

    public const string SellBannedMessage = "This item can be bought but not sold at the Flea Market.";

    // flea.jsonc sellBannedItems: still on the flea to buy (no generated offers are taken away), only players can't
    // list them. Read at call time, so a settings-page save applies straight away. Beats allowedItems on purpose:
    // allowed plus sell-banned is how a game-banned item is made buy-only.
    public static bool AnySellBanned(IEnumerable<Item> listed)
    {
        var set = SellBanned();
        return set.Count > 0 && listed.Any(item => IsSellBanned(item.Template, set));
    }

    public static bool IsSellBanned(MongoId tpl) => SellBanned() is { Count: > 0 } set && IsSellBanned(tpl, set);

    private static bool IsSellBanned(MongoId tpl, HashSet<MongoId> set) =>
        set.Contains(tpl) || Flea.HandbookTree.InAny(tpl, set) || _itemHelper?.IsOfBaseclasses(tpl, set) == true;

    public static HashSet<MongoId> SellBanned() => IdSet(_config?.Mod.Flea.SellBannedItems, ref _sellBanned);

    public const string BuyBannedMessage = "This item can be sold but not bought at the Flea Market.";

    // flea.jsonc buyBannedItems: players can list them, but nobody sees an offer for one (traders', generated or
    // other players'), except the seller their own. Offers are still made, so selling them works as before. Read
    // at call time, so a settings-page save applies straight away.
    public static bool IsBuyBanned(MongoId tpl) => BuyBanned() is { Count: > 0 } set && IsSellBanned(tpl, set);

    public static bool IsBuyBanned(RagfairOffer offer, MongoId viewer) =>
        offer.Items is { Count: > 0 } items && IsBuyBanned(items[0].Template) && offer.User?.Id.ToString() != viewer.ToString();

    public static HashSet<MongoId> BuyBanned() => IdSet(_config?.Mod.Flea.BuyBannedItems, ref _buyBanned);

    // Built again when the config is reloaded (a save makes a new list).
    private static (List<string> Source, HashSet<MongoId> Set)? _sellBanned;
    private static (List<string> Source, HashSet<MongoId> Set)? _buyBanned;
    private static readonly object IdSetSync = new();

    private static HashSet<MongoId> IdSet(List<string>? source, ref (List<string> Source, HashSet<MongoId> Set)? cache)
    {
        if (source is null)
        {
            return [];
        }

        lock (IdSetSync)
        {
            if (cache is { } cached && ReferenceEquals(cached.Source, source))
            {
                return cached.Set;
            }

            var set = source.Where(MongoId.IsValidMongoId).Select(id => new MongoId(id)).ToHashSet();
            cache = (source, set);
            return set;
        }
    }

    // No error code on purpose: RagfairUnavailable kicks the player to the menu.
    public static ItemEventRouterResponse Fail(MongoId sessionID, string message)
    {
        var output = _output!.GetOutput(sessionID);
        return _http!.AppendErrorToOutput(output, message);
    }
}

// Hides offers for buyBannedItems from every flea search (before paging, so counts stay right), builds included.
public class BuyBanSearchPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() =>
        typeof(RagfairOfferHelper).GetMethod("PassesSearchFilterCriteria", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [PatchPostfix]
    public static void Postfix(RagfairOffer offer, PmcData pmcData, ref bool __result)
    {
        if (__result && FleaSellingGate.IsBuyBanned(offer, pmcData.Id ?? default))
        {
            __result = false;
        }
    }
}

public class DisableFleaSellingPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairController).GetMethod(
            nameof(RagfairController.AddPlayerOffer),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static bool Prefix(PmcData pmcData, AddOfferRequestData offerRequest, MongoId sessionID, ref ItemEventRouterResponse __result)
    {
        if (FleaSellingGate.Blocked)
        {
            __result = FleaSellingGate.Fail(sessionID, FleaSellingGate.DisabledMessage);
            return false;
        }

        if (offerRequest.Items is not { } listedIds)
        {
            return true;
        }

        var listed = FleaSellingGate.WithChildren(pmcData, listedIds);
        if (FleaSellingGate.FoundInRaidOnly && !FleaSellingGate.AllFoundInRaid(listed))
        {
            __result = FleaSellingGate.Fail(sessionID, FleaSellingGate.NotFoundInRaidMessage);
            return false;
        }

        var roots = listedIds.Select(id => id.ToString()).ToHashSet();
        if (FleaSellingGate.AnyBanned(listed.Where(item => roots.Contains(item.Id.ToString()))))
        {
            __result = FleaSellingGate.Fail(sessionID, FleaSellingGate.BannedMessage);
            return false;
        }

        if (FleaSellingGate.AnySellBanned(listed.Where(item => roots.Contains(item.Id.ToString()))))
        {
            __result = FleaSellingGate.Fail(sessionID, FleaSellingGate.SellBannedMessage);
            return false;
        }

        if (FleaSellingGate.AnyBannedPlate(listed))
        {
            __result = FleaSellingGate.Fail(sessionID, FleaSellingGate.BannedPlateMessage);
            return false;
        }

        if (Flea.LevelLock.SellRefusal(pmcData, listed.Where(item => roots.Contains(item.Id.ToString()))) is { } tooLow)
        {
            __result = FleaSellingGate.Fail(sessionID, tooLow);
            return false;
        }

        if (PurchaseLock.AnyLocked(sessionID, listed))
        {
            __result = FleaSellingGate.Fail(sessionID, FleaSellingGate.BoughtFromTraderMessage);
            return false;
        }

        return true;
    }
}
