using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Services.Ragfair;

namespace BetterFlea.Prices;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class DemandLoad(ConfigManager configManager, ItemHelper itemHelper, RagfairOfferService ragfairOfferService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        Demand.Bind(configManager, itemHelper, ragfairOfferService);
        new DemandPurchasePatch().Enable();
        new DemandSalePatch().Enable();
        return Task.CompletedTask;
    }
}

// What the market has done to an item lately, on top of drift. Buying up other sellers' offers makes the next ones
// dearer; a player putting a lot of one item up now and then makes it cheaper. Either wears off over time. Kept in
// memory only: after a restart the market has settled.
internal static class Demand
{
    private const string FleaPurchase = "buy_from_ragfair_pmc";

    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static RagfairOfferService? _offers;

    private sealed record Shift(double Factor, long At);

    private static readonly ConcurrentDictionary<MongoId, Shift> Shifts = new();

    // Player and item: the lots sold lately, and when.
    private static readonly ConcurrentDictionary<(MongoId, MongoId), Shift> Listed = new();

    [ThreadStatic] private static (MongoId Tpl, double Share)? _pending;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, RagfairOfferService offers)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _offers = offers;
    }

    private static DemandConfig? Settings => _config?.Mod.Prices.Demand is { Enabled: true } settings ? settings : null;

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // Halfway back to normal every recoverMinutes.
    private static double Faded(Shift shift, DemandConfig settings) =>
        1 + (shift.Factor - 1) * Math.Pow(0.5, (Now() - shift.At) / 60.0 / Math.Max(1, settings.RecoverMinutes));

    public static double Factor(MongoId tpl) =>
        Settings is { } settings && Shifts.TryGetValue(tpl, out var shift) ? Faded(shift, settings) : 1;

    // What a player's listing is measured against: a rise from buying doesn't count, so buying an item up and
    // listing it at the raised price makes no money. A drop does.
    public static double SellerFactor(MongoId tpl) => Math.Min(1, Factor(tpl));

    private static void Move(MongoId tpl, double by, DemandConfig settings)
    {
        var low = 1 - Math.Clamp(settings.MaxDropPercent, 0, 90) / 100;
        var high = 1 + Math.Max(0, settings.MaxRisePercent) / 100;
        var moved = Shifts.AddOrUpdate(tpl,
            _ => new Shift(Math.Clamp(1 + by, low, high), Now()),
            (_, shift) => new Shift(Math.Clamp(Faded(shift, settings) + by, low, high), Now()));
        DebugLog.Write(() => $"Demand: {Name(tpl)} {(by > 0 ? "bought" : "flooded")}, price now x{moved.Factor:0.000}");
    }

    public static void BeforePurchase(ProcessBuyTradeRequestData request)
    {
        _pending = null;
        if (!string.Equals(request.Type, FleaPurchase, StringComparison.OrdinalIgnoreCase)
            || _offers?.GetOfferByOfferId(request.ItemId) is not { } offer || !offer.IsFakePlayerOffer()
            || offer.Items?.FirstOrDefault() is not { } root || request.Count is not > 0)
        {
            return;
        }

        // Buying a whole offer counts in full, part of a stack less.
        var share = offer.SellInOnePiece == true ? 1 : Math.Min(1, request.Count.Value / Math.Max(1, offer.Quantity));
        _pending = (root.Template, 0.5 + 0.5 * share);
    }

    public static void AfterPurchase(ItemEventRouterResponse output)
    {
        var pending = _pending;
        _pending = null;
        if (pending is not { } bought || output.Warnings is { Count: > 0 })
        {
            return;
        }

        if (bought.Share >= 1)
        {
            BetterFlea.Selling.OrderBook.Gone(bought.Tpl);
        }

        if (Settings is not { } settings)
        {
            return;
        }

        Move(bought.Tpl, settings.BuyRisePercent / 100 * bought.Share, settings);
    }

    // A player's offer sold some. lots: full stacks' worth (a single item is one). From 5 lots sold in a short while,
    // now and then the price drops. Sales only, not listings: an offer pulled again never moves the market.
    public static void PlayerSold(MongoId player, MongoId tpl, double lots)
    {
        if (Settings is not { } settings || lots <= 0)
        {
            return;
        }

        var recent = Listed.AddOrUpdate((player, tpl),
            _ => new Shift(lots, Now()),
            (_, shift) => new Shift((Now() - shift.At < 30 * 60 ? shift.Factor : 0) + lots, Now()));
        if (recent.Factor < 5 || Random.Shared.NextDouble() >= Math.Clamp(settings.FloodChance, 0, 1))
        {
            return;
        }

        Listed[(player, tpl)] = new Shift(0, Now());
        Move(tpl, -Math.Max(0, settings.FloodDropPercent) / 100 * (0.5 + Random.Shared.NextDouble()), settings);
    }

    public static void PlayerOfferSold(MongoId player, RagfairOffer offer, int boughtAmount)
    {
        if (Settings is null || offer.Items?.FirstOrDefault() is not { } root || _itemHelper is null)
        {
            return;
        }

        var units = offer.SellInOnePiece == true ? offer.Items.Where(item => item.ParentId == root.ParentId).Sum(item => item.Upd?.StackObjectsCount ?? 1) : boughtAmount;
        var stackMax = Math.Max(1, _itemHelper.GetItem(root.Template).Value?.Properties?.StackMaxSize ?? 1);
        PlayerSold(player, root.Template, units / stackMax);
    }

    private static string Name(MongoId tpl) => _itemHelper?.GetItem(tpl).Value?.Name ?? tpl.ToString();
}

public class DemandSalePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairOfferHelper).GetMethod(nameof(RagfairOfferHelper.CompleteOffer), BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix(MongoId offerOwnerSessionId, RagfairOffer offer, int boughtAmount)
    {
        Demand.PlayerOfferSold(offerOwnerSessionId, offer, boughtAmount);
    }
}

public class DemandPurchasePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(TradeHelper).GetMethod(nameof(TradeHelper.BuyItem), BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix(ProcessBuyTradeRequestData buyRequestData)
    {
        Demand.BeforePurchase(buyRequestData);
    }

    [PatchPostfix]
    public static void Postfix(ItemEventRouterResponse output)
    {
        Demand.AfterPurchase(output);
    }
}
