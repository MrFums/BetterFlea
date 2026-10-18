using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services.Ragfair;

namespace BetterFlea.Selling;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class FlipPenaltyPatchesLoad(
    ConfigManager configManager,
    ItemHelper itemHelper,
    TraderHelper traderHelper,
    RagfairOfferService ragfairOfferService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        FlipPenaltyGate.Bind(configManager, itemHelper, traderHelper, ragfairOfferService);
        new RecordPurchasePatch().Enable();
        new ListingContextPatch().Enable();
        return Task.CompletedTask;
    }
}

// Remembers what each player bought recently. When they list that same item, SaleDelayGate holds
// buyers back for longer, more for bigger and more recent buys. Kept in memory, so a restart forgets it.
internal static class FlipPenaltyGate
{
    private const string FleaPurchase = "buy_from_ragfair_pmc";

    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static TraderHelper? _traderHelper;
    private static RagfairOfferService? _offers;

    // player -> item -> purchases
    private static readonly ConcurrentDictionary<MongoId, ConcurrentDictionary<MongoId, List<Purchase>>> History = new();

    // Purchases and listings are handled one request at a time per thread, so these carry context
    // from the outer SPT call to the inner one we patch.
    [ThreadStatic] private static Purchase? _pendingPurchase;
    [ThreadStatic] private static (MongoId Player, MongoId Tpl)? _listing;

    private sealed record Purchase(MongoId Tpl, double Count, long At);

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, TraderHelper traderHelper, RagfairOfferService offers)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _traderHelper = traderHelper;
        _offers = offers;
    }

    private static FlipPenaltyConfig? Settings =>
        _config?.Mod.PlayerSelling.FlipPenalty is { Enabled: true } settings ? settings : null;

    // Looked up before SPT runs the trade: a sold-out fence item or flea offer is gone afterwards.
    public static void BeforePurchase(ProcessBuyTradeRequestData request)
    {
        _pendingPurchase = null;
        if (Settings is not { } settings)
        {
            return;
        }

        var fromFlea = string.Equals(request.Type, FleaPurchase, StringComparison.OrdinalIgnoreCase);
        if (fromFlea && !settings.CountFleaPurchases)
        {
            return;
        }

        MongoId? tpl = fromFlea
            ? _offers?.GetOfferByOfferId(request.ItemId)?.Items?.FirstOrDefault()?.Template
            : _traderHelper?.GetTraderAssortsByTraderId(request.TransactionId)?.Items?
                .FirstOrDefault(item => item.Id == request.ItemId)?.Template;

        if (tpl is { } found && request.Count is > 0)
        {
            var weight = !fromFlea && request.TransactionId == Traders.FENCE ? settings.FenceMultiplier : 1.0;
            _pendingPurchase = new Purchase(found, request.Count.Value * weight, Now());
        }
    }

    public static void AfterPurchase(MongoId player, ItemEventRouterResponse output)
    {
        var purchase = _pendingPurchase;
        _pendingPurchase = null;
        if (purchase is null || output.Warnings is { Count: > 0 })
        {
            return;
        }

        var list = History.GetOrAdd(player, _ => new()).GetOrAdd(purchase.Tpl, _ => new());
        lock (list)
        {
            list.Add(purchase);
        }

        DebugLog.Write(() => $"Flip penalty: {player} bought {purchase.Count} x {Name(purchase.Tpl)}, recent weight now {RecentWeight(player, purchase.Tpl):0.0}");
    }

    public static void BeginListing(PmcData pmcData, AddOfferRequestData offerRequest, MongoId player)
    {
        _listing = null;
        var firstId = offerRequest.Items?.FirstOrDefault();
        var item = firstId is { } id ? pmcData.Inventory?.Items?.FirstOrDefault(i => i.Id == id) : null;
        if (item is not null)
        {
            _listing = (player, item.Template);
        }
    }

    public static void EndListing() => _listing = null;

    // Extra minutes before the first buyer for the listing being made right now; 0 if it isn't a flip.
    public static double ExtraWaitMinutes()
    {
        if (_listing is not { } listing || Settings is not { } settings)
        {
            return 0;
        }

        var weight = RecentWeight(listing.Player, listing.Tpl);
        var extra = ShareOfMax(settings, weight) * settings.MaxExtraMinutes;
        if (extra > 0)
        {
            DebugLog.Write(() => $"Flip penalty: {Name(listing.Tpl)} listed after buying {weight:0.0} recently, +{extra:0} min before the first buyer");
        }

        return extra;
    }

    // Each purchase counts in full when fresh and fades to nothing at the end of the window.
    private static double RecentWeight(MongoId player, MongoId tpl)
    {
        if (Settings is not { } settings
            || !History.TryGetValue(player, out var byItem)
            || !byItem.TryGetValue(tpl, out var list))
        {
            return 0;
        }

        var window = settings.WindowMinutes * 60.0;
        var now = Now();
        lock (list)
        {
            list.RemoveAll(p => now - p.At >= window);
            return list.Sum(p => p.Count * (1.0 - (now - p.At) / window));
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string Name(MongoId tpl) => _itemHelper?.GetItem(tpl).Value?.Name ?? tpl.ToString();

    // 0 up to FreePurchases, rising in a straight line to 1 at PurchasesForMaxPenalty.
    internal static double ShareOfMax(FlipPenaltyConfig settings, double weight)
    {
        var over = weight - settings.FreePurchases;
        return over <= 0 ? 0 : Math.Min(1.0, over / (settings.PurchasesForMaxPenalty - settings.FreePurchases));
    }
}

public class RecordPurchasePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(TradeHelper).GetMethod(
            nameof(TradeHelper.BuyItem),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix(ProcessBuyTradeRequestData buyRequestData)
    {
        FlipPenaltyGate.BeforePurchase(buyRequestData);
    }

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, ItemEventRouterResponse output)
    {
        FlipPenaltyGate.AfterPurchase(sessionId, output);
    }
}

public class ListingContextPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairController).GetMethod(
            nameof(RagfairController.AddPlayerOffer),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix(PmcData pmcData, AddOfferRequestData offerRequest, MongoId sessionID)
    {
        FlipPenaltyGate.BeginListing(pmcData, offerRequest, sessionID);
    }

    [PatchPostfix]
    public static void Postfix()
    {
        FlipPenaltyGate.EndListing();
    }
}
