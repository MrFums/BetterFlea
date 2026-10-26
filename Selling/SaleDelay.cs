using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;
using Item = SPTarkov.Server.Core.Models.Eft.Common.Tables.Item;

namespace BetterFlea.Selling;

[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public class SaleDelayPatchesLoad(
    ConfigManager configManager,
    GlobalTable globalTable,
    ItemHelper itemHelper,
    RagfairPriceService ragfairPriceService,
    SPTarkov.Server.Core.Models.Spt.Config.RagfairConfig ragfairConfig) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        SaleDelayGate.Bind(configManager, globalTable, itemHelper, ragfairPriceService, ragfairConfig);
        new SaleDelayPatch().Enable();
        new SellChancePatch().Enable();
        return Task.CompletedTask;
    }
}

// Sale speed goes by price against normal: undercut sells in under a minute, normal in a few minutes,
// much dearer mostly never. Gun builds still sell now and then when dear. SPT's buyer timeline is pushed
// back by one wait, so its spacing stays.
//
// "Normal" is worked out from what's actually listed, priced the way the flea's own offers are. SPT's normal is
// the average of every offer of that item, so a 1-plate armour was weighed against full-plate ones and sold at a
// full-plate price.
internal static class SaleDelayGate
{
    private static ConfigManager? _config;
    private static GlobalTable? _globals;
    private static ItemHelper? _itemHelper;
    private static RagfairPriceService? _prices;
    private static SPTarkov.Server.Core.Models.Spt.Config.RagfairConfig? _ragfairConfig;

    [ThreadStatic] private static double? _priceRatio;
    [ThreadStatic] private static bool _soughtAfter;
    [ThreadStatic] private static double? _gunValue;
    [ThreadStatic] private static double? _contentsValue;
    [ThreadStatic] private static int _packUnits;

    private const int BuildParts = 4;

    // packUnits: how many of the item a pack offer sells at once (its price is for all of them). 1 otherwise.
    public static void BeginListing(List<Item>? listed, ItemHelper itemHelper, int packUnits)
    {
        EndListing();
        _packUnits = Math.Max(1, packUnits);
        if (listed is not { Count: > 0 })
        {
            return;
        }

        if (!itemHelper.IsOfBaseclass(listed[0].Template, BaseClasses.WEAPON))
        {
            _contentsValue = ContentsValue(listed);
            return;
        }

        _soughtAfter = listed.Count(item => !itemHelper.IsOfBaseclass(item.Template, BaseClasses.AMMO)) > BuildParts;
        if (BetterFlea.Prices.GunPricing.Applies(listed))
        {
            _gunValue = BetterFlea.Prices.GunPricing.Value(listed);
        }
    }

    public static void EndListing()
    {
        _priceRatio = null;
        _soughtAfter = false;
        _gunValue = null;
        _contentsValue = null;
        _packUnits = 1;
    }

    // One of the listed item with everything in it (plates, inserts, rounds in a mag), each part priced like on a
    // generated offer: base price and SPT's multiplier for it, its own condition, then key wear, drift and never under
    // what a trader pays. Then the whole market's multiplier and the Wipe Cycle. Null leaves it to SPT.
    private static double? ContentsValue(List<Item> listed)
    {
        if (_prices is null || _itemHelper is null || _ragfairConfig is null)
        {
            return null;
        }

        var root = listed[0];
        // An ammo box's price already covers the rounds in it.
        var parts = _itemHelper.IsOfBaseclass(root.Template, BaseClasses.AMMO_BOX) ? [root] : listed;
        var dynamic = _ragfairConfig.Dynamic;
        var total = 0.0;
        foreach (var item in parts)
        {
            // Built-in soft armour comes with the armour; SPT doesn't price it either.
            if (_itemHelper.IsOfBaseclass(item.Template, BaseClasses.BUILT_IN_INSERTS))
            {
                continue;
            }

            var price = _prices.GetFleaPriceForItem(item.Template);
            if (dynamic.ItemPriceMultiplier?.TryGetValue(item.Template, out var multiplier) == true)
            {
                price *= multiplier;
            }

            if (dynamic.IgnoreQualityPriceVarianceBlacklist?.Contains(item.Template) != true)
            {
                price *= _itemHelper.GetItemQualityModifier(item);
            }

            price = BetterFlea.Prices.PriceGate.Adjust(item.Template, Money.ROUBLES, item, price, counted: false);
            var rounds = item != root && item.SlotId == "cartridges" ? Math.Max(1, item.Upd?.StackObjectsCount ?? 1) : 1;
            total += price * rounds;
        }

        total *= (_config?.Mod.Prices.PriceMultiplier ?? 1.0) * BetterFlea.Prices.WipeEconomy.Factor(root.Template);
        return total > 0 ? total : null;
    }

    // Null leaves it to SPT.
    public static double? SellChance(double normalPrice, double listedPrice, double quality)
    {
        var settings = _config?.Mod.PlayerSelling.SaleDelay;
        if (settings is null || !settings.Enabled || listedPrice <= 0)
        {
            return null;
        }

        // SPT sums a gun's flea part prices; use the same trader-based value the flea's guns use. Anything else goes
        // by what's in it (condition included already).
        var normal = _gunValue is { } gun ? gun * quality : _contentsValue is { } contents ? contents * _packUnits : normalPrice;
        if (normal <= 0)
        {
            return null;
        }

        var ratio = listedPrice / normal;
        _priceRatio = ratio;
        DebugLog.Write(() => $"Sale check: listed {listedPrice:0} RUB against a normal {normal:0} (SPT's average says {normalPrice:0}), x{ratio:0.00}");

        // 5% chance at expensivePercent over, falling away fast. Half as fast for a gun build.
        var over = ratio - 1.05;
        if (over <= 0)
        {
            return 100;
        }

        var steep = Math.Log(20) / Math.Max(0.01, settings.ExpensivePercent / 100.0 - 0.05);
        var chance = 100 * Math.Exp(-(_soughtAfter ? steep / 2 : steep) * over);
        return chance < 1 ? 0 : Math.Round(chance);
    }

    public static void Bind(ConfigManager configManager, GlobalTable globalTable, ItemHelper itemHelper, RagfairPriceService prices,
        SPTarkov.Server.Core.Models.Spt.Config.RagfairConfig ragfairConfig)
    {
        _config = configManager;
        _globals = globalTable;
        _itemHelper = itemHelper;
        _prices = prices;
        _ragfairConfig = ragfairConfig;
    }

    public static void Delay(List<SellResult> sales)
    {
        var settings = _config?.Mod.PlayerSelling.SaleDelay;
        if (settings is null || !settings.Enabled || sales.Count == 0)
        {
            return;
        }

        var flipMinutes = FlipPenaltyGate.ExtraWaitMinutes();
        var ratio = _priceRatio ?? 1.0;
        if (flipMinutes <= 0 && ratio <= 1.0 && Random.Shared.NextDouble() < settings.NoDelayChance)
        {
            return;
        }

        // 1 at the normal price; about a tenth of that 10% under, and 4x at 15% over.
        var past = (ratio - 0.85) / 0.15;
        var scale = past <= 0 ? 0.03 : Math.Max(0.03, past * past);
        var waitMinutes = (settings.MinMinutes + Random.Shared.NextDouble() * (settings.MaxMinutes - settings.MinMinutes)) * scale + flipMinutes;
        var waitSeconds = (long)Math.Round(waitMinutes * 60.0);
        if (waitSeconds <= 0)
        {
            return;
        }

        foreach (var sale in sales)
        {
            sale.SellTime += waitSeconds;
        }

        // Past the end of the listing: the offer just expires.
        if (OfferEndsAt() is { } end)
        {
            sales.RemoveAll(sale => sale.SellTime > end);
        }
    }

    private static long? OfferEndsAt()
    {
        var hours = _globals?.Configuration?.RagFair?.OfferDurationTimeInHour;
        if (hours is not { } duration || duration <= 0)
        {
            return null;
        }

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)TimeSpan.FromHours((int)duration).TotalSeconds;
    }
}

public class SellChancePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairSellHelper).GetMethod(
            nameof(RagfairSellHelper.CalculateSellChance),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static bool Prefix(double averageOfferPriceRub, double playerListedPriceRub, double qualityMultiplier, ref double __result)
    {
        if (SaleDelayGate.SellChance(averageOfferPriceRub, playerListedPriceRub, qualityMultiplier) is not { } chance)
        {
            return true;
        }

        __result = chance;
        return false;
    }
}

public class SaleDelayPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairSellHelper).GetMethod(
            nameof(RagfairSellHelper.RollForSale),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(List<SellResult> __result)
    {
        SaleDelayGate.Delay(__result);
    }
}

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

// Recent buys per player, so a quick flip waits longer for buyers. Saved in data/recent-purchases.json;
// anything older than the window is dropped.
internal static class FlipPenaltyGate
{
    private const string FleaPurchase = "buy_from_ragfair_pmc";
    private const string FileName = "recent-purchases.json";
    private const int MaxPerItem = 200;

    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static TraderHelper? _traderHelper;
    private static RagfairOfferService? _offers;

    private static readonly ConcurrentDictionary<MongoId, ConcurrentDictionary<MongoId, List<Purchase>>> History = new();

    [ThreadStatic] private static Purchase? _pendingPurchase;
    [ThreadStatic] private static (MongoId Player, MongoId Tpl)? _listing;

    private sealed record Purchase(MongoId Tpl, double Count, long At);

    // Player -> item -> [count, when] per purchase.
    public sealed class PurchaseFile
    {
        public Dictionary<string, Dictionary<string, List<double[]>>> Players { get; set; } = new();
    }

    private static int _dirty;
    private static Timer? _saveTimer;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, TraderHelper traderHelper, RagfairOfferService offers)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _traderHelper = traderHelper;
        _offers = offers;

        foreach (var (player, items) in DataFiles.Read<PurchaseFile>(FileName)?.Players ?? [])
        {
            foreach (var (tpl, purchases) in items)
            {
                var list = History.GetOrAdd(new MongoId(player), _ => new()).GetOrAdd(new MongoId(tpl), _ => new());
                list.AddRange(purchases.Where(entry => entry.Length == 2).Select(entry => new Purchase(new MongoId(tpl), entry[0], (long)entry[1])));
            }
        }

        _saveTimer ??= new Timer(_ => SaveIfChanged(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private static void SaveIfChanged()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0)
        {
            return;
        }

        var window = (Settings?.WindowMinutes ?? 0) * 60.0;
        var now = Now();
        var file = new PurchaseFile();
        foreach (var (player, items) in History)
        {
            foreach (var (tpl, list) in items)
            {
                List<double[]> kept;
                lock (list)
                {
                    list.RemoveAll(p => now - p.At >= window);
                    if (list.Count > MaxPerItem)
                    {
                        list.RemoveRange(0, list.Count - MaxPerItem);
                    }

                    kept = list.Select(p => new[] { p.Count, p.At }).ToList();
                }

                if (kept.Count == 0)
                {
                    continue;
                }

                if (!file.Players.TryGetValue(player.ToString(), out var byItem))
                {
                    file.Players[player.ToString()] = byItem = new();
                }

                byItem[tpl.ToString()] = kept;
            }
        }

        DataFiles.Write(FileName, file);
    }

    private static FlipPenaltyConfig? Settings =>
        _config?.Mod.PlayerSelling.FlipPenalty is { Enabled: true } settings ? settings : null;

    // Before SPT runs the trade: a sold-out offer is gone afterwards.
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

        Interlocked.Exchange(ref _dirty, 1);

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

        // A pack's price is for every item in it; SPT counts them the same way.
        var packUnits = offerRequest.SellInOnePiece == true
            ? (int)(offerRequest.Items ?? []).Sum(id => pmcData.Inventory!.Items!.FirstOrDefault(i => i.Id == id)?.Upd?.StackObjectsCount ?? 1)
            : 1;
        SaleDelayGate.BeginListing(item is null ? null : pmcData.Inventory!.Items!.GetItemWithChildren(item.Id), _itemHelper!, packUnits);
    }

    public static void EndListing()
    {
        _listing = null;
        SaleDelayGate.EndListing();
    }

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

    // Fades from full to nothing over the window.
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
