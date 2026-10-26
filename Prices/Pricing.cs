using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;
using BetterFlea.Flea;
using BetterFlea.Offers;

namespace BetterFlea.Prices;

[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public class DynamicPricePatchesLoad(
    ConfigManager configManager,
    ItemHelper itemHelper,
    HandbookHelper handbookHelper,
    TradersTable tradersTable,
    RagfairConfig ragfairConfig,
    RagfairPriceService ragfairPriceService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        PriceGate.Bind(configManager, itemHelper, handbookHelper, tradersTable, ragfairConfig);
        SellerPricing.Bind(configManager, itemHelper);
        GunPricing.Bind(configManager, itemHelper, handbookHelper, tradersTable, ragfairPriceService);
        EmptyFuelPricing.Bind(configManager, itemHelper, handbookHelper, ragfairPriceService);
        new DynamicPricePatch().Enable();
        return Task.CompletedTask;
    }
}

// Order: key curve, then drift, then never below what a trader pays.
internal static class PriceGate
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static HandbookHelper? _handbook;
    private static TradersTable? _traders;
    private static RagfairConfig? _ragfairConfig;

    private static readonly ConcurrentDictionary<MongoId, double> BestBuyPrices = new();
    private static readonly ConcurrentDictionary<MongoId, Drift> Drifts = new();

    private static int _raisedToFloor;

    public static int RaisedToFloor => _raisedToFloor;

    public static void Bind(
        ConfigManager configManager,
        ItemHelper itemHelper,
        HandbookHelper handbookHelper,
        TradersTable tradersTable,
        RagfairConfig ragfairConfig)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _handbook = handbookHelper;
        _traders = tradersTable;
        _ragfairConfig = ragfairConfig;

        var max = configManager.Mod.Prices.Drift.MaxDriftPercent / 100.0;
        foreach (var (tpl, state) in DataFiles.Read<Dictionary<string, double[]>>(DriftFile) ?? [])
        {
            if (state.Length == 2)
            {
                Drifts[new MongoId(tpl)] = Drift.Restore(Math.Clamp(state[0], 1.0 - max, 1.0 + max), (long)state[1]);
            }
        }

        _driftSaveTimer ??= new Timer(_ => SaveDrift(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    // Saved so prices carry on after a restart. One entry per priced item.
    private const string DriftFile = "price-drift.json";
    private static Timer? _driftSaveTimer;
    private static int _driftMoved;

    private static void SaveDrift()
    {
        if (Interlocked.Exchange(ref _driftMoved, 0) == 0)
        {
            return;
        }

        DataFiles.Write(DriftFile, Drifts.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value.State()));
    }

    // counted = false: only working out a price (a player's listing), no offer is being made.
    public static double Adjust(MongoId tpl, MongoId currency, Item? item, double price, bool counted = true)
    {
        price *= UsedListingsGate.KeyPriceFactor(tpl, item);
        price *= DriftFactor(tpl);

        var floor = FloorFor(tpl, item, currency);
        if (price < floor && !counted)
        {
            return floor;
        }

        if (price < floor)
        {
            Interlocked.Increment(ref _raisedToFloor);
            DebugLog.Write(() => $"Price floor: {Name(tpl)} raised {price:0.##} -> {floor:0.##} {CurrencyName(currency)} (best trader buy {BestTraderBuyPrice(tpl):0.##} RUB)");
            price = floor;
        }

        return price;
    }

    // Rounds in a mag are one item but a trader buys each round.
    public static double FloorForItems(IEnumerable<Item> items, MongoId currency) =>
        items.Sum(item => FloorFor(item.Template, item, currency) * (item.SlotId == "cartridges" ? Math.Max(1, item.Upd?.StackObjectsCount ?? 1) : 1));

    // 0 = no floor.
    public static double FloorFor(MongoId tpl, Item? item, MongoId currency)
    {
        var settings = _config?.Mod.Prices.Floor;
        if (settings is null || !settings.Enabled || _handbook is null)
        {
            return 0;
        }

        var bestBuy = BestTraderBuyPrice(tpl);
        if (bestBuy <= 0)
        {
            return 0;
        }

        var condition = item is not null && !UsedListingsGate.IgnoresCondition(tpl)
            ? _itemHelper!.GetItemQualityModifier(item)
            : 1.0;

        var floor = bestBuy * condition * (1.0 + settings.MarginPercent / 100.0);
        return currency == Money.ROUBLES ? floor : _handbook.FromRoubles(floor, currency);
    }

    // SPT only checks each trader's first loyalty level and ignores what they actually buy.
    public static double BestTraderBuyPrice(MongoId tpl)
    {
        return BestBuyPrices.GetOrAdd(tpl, static tpl =>
        {
            var handbookPrice = _handbook!.GetTemplatePrice(tpl);
            if (handbookPrice <= 0 || _traders is null)
            {
                return 0;
            }

            var best = 0.0;
            foreach (var trader in _traders.Values)
            {
                var traderBase = trader.Base;
                if (!Buys(traderBase, tpl) || traderBase.LoyaltyLevels is not { Count: > 0 } levels)
                {
                    continue;
                }

                var lowestCut = levels.Min(level => level.BuyPriceCoefficient ?? 100.0);
                best = Math.Max(best, handbookPrice * (100.0 - lowestCut) / 100.0);
            }

            return best;
        });
    }

    public static void ForgetTraderPrices() => BestBuyPrices.Clear();

    private static bool Buys(TraderBase trader, MongoId tpl)
    {
        return Matches(trader.ItemsBuy, tpl) && !Matches(trader.ItemsBuyProhibited, tpl);
    }

    private static bool Matches(ItemBuyData? list, MongoId tpl)
    {
        if (list is null)
        {
            return false;
        }

        return list.IdList.Contains(tpl)
            || (list.Category.Count > 0 && _itemHelper!.IsOfBaseclasses(tpl, list.Category));
    }

    private static double DriftFactor(MongoId tpl)
    {
        var settings = _config?.Mod.Prices.Drift;
        if (settings is null || !settings.Enabled || settings.MaxDriftPercent <= 0)
        {
            return 1.0;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var drift = Drifts.GetOrAdd(tpl, _ =>
        {
            Interlocked.Exchange(ref _driftMoved, 1);
            return Drift.Start(settings, now);
        });
        return drift.Current(settings, now, tpl);
    }

    // The items furthest above and below their normal price right now, as (item, factor).
    public static List<(MongoId Tpl, double Factor)> Movers(int each)
    {
        var all = Drifts.Select(entry => (entry.Key, entry.Value.State()[0])).ToList();
        return all.OrderByDescending(entry => entry.Item2).Take(each)
            .Concat(all.OrderBy(entry => entry.Item2).Take(each))
            .ToList();
    }

    public static int DriftingItems => Drifts.Count;

    public static bool DriftOn => _config?.Mod.Prices.Drift is { Enabled: true, MaxDriftPercent: > 0 };

    private static string Name(MongoId tpl) => _itemHelper?.GetItem(tpl).Value?.Name ?? tpl.ToString();

    private static string CurrencyName(MongoId currency) =>
        currency == Money.ROUBLES ? "RUB"
        : currency == Money.DOLLARS ? "USD"
        : currency == Money.EUROS ? "EUR"
        : currency.ToString();

    // Random walk that leans back to normal.
    private sealed class Drift
    {
        private const double PullBack = 0.15;
        private const int MaxCatchUpSteps = 5;

        private readonly object _lock = new();
        private double _factor;
        private long _nextMoveAt;

        public static Drift Start(PriceDriftConfig settings, long now)
        {
            var max = settings.MaxDriftPercent / 100.0;
            return new Drift
            {
                // Spread out from the start so the market isn't flat.
                _factor = 1.0 + (Random.Shared.NextDouble() - 0.5) * max,
                _nextMoveAt = now + NextWait(settings)
            };
        }

        public static Drift Restore(double factor, long nextMoveAt) => new() { _factor = factor, _nextMoveAt = nextMoveAt };

        public double[] State()
        {
            lock (_lock)
            {
                return [_factor, _nextMoveAt];
            }
        }

        public double Current(PriceDriftConfig settings, long now, MongoId tpl)
        {
            lock (_lock)
            {
                if (now < _nextMoveAt)
                {
                    return _factor;
                }

                var before = _factor;
                var max = settings.MaxDriftPercent / 100.0;
                var step = settings.StepPercent / 100.0;
                var waitSeconds = settings.UpdateMinutes * 60.0;

                // A few moves for an item nobody looked at for hours, not hundreds.
                var steps = Math.Clamp((int)((now - _nextMoveAt) / waitSeconds) + 1, 1, MaxCatchUpSteps);
                for (var i = 0; i < steps; i++)
                {
                    var nudge = (Random.Shared.NextDouble() * 2.0 - 1.0) * step;
                    _factor = Math.Clamp(_factor + (1.0 - _factor) * PullBack + nudge, 1.0 - max, 1.0 + max);
                }

                _nextMoveAt = now + NextWait(settings);
                Interlocked.Exchange(ref _driftMoved, 1);
                DebugLog.Write(() => $"Price drift: {Name(tpl)} {(before - 1.0) * 100:+0.0;-0.0}% -> {(_factor - 1.0) * 100:+0.0;-0.0}%");
                return _factor;
            }
        }

        private static long NextWait(PriceDriftConfig settings)
        {
            return (long)(settings.UpdateMinutes * 60.0 * (0.5 + Random.Shared.NextDouble()));
        }
    }
}

public class DynamicPricePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairPriceService).GetMethod(
            nameof(RagfairPriceService.GetDynamicItemPrice),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(MongoId itemTemplateId, MongoId desiredCurrency, Item? item, ref double? __result)
    {
        if (__result is not { } price)
        {
            return;
        }

        __result = PriceGate.Adjust(itemTemplateId, desiredCurrency, item, price);
    }
}

internal static class SellerPricing
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static int _logged;

    private const double MostOverprice = 1.8;
    private static int _packsLogged;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper)
    {
        _config = configManager;
        _itemHelper = itemHelper;
    }

    // The whole market dearer or cheaper. Last, so deals and overpriced offers move with it. Never below the floor.
    public static void ApplyMultiplier(CreateFleaOfferDetails details)
    {
        var multiplier = (_config?.Mod.Prices.PriceMultiplier ?? 1.0)
            * (details.Items is { Count: > 0 } offerItems ? WipeEconomy.Factor(offerItems[0].Template) : 1.0);
        if (Math.Abs(multiplier - 1) < 0.001 || _itemHelper is null || details.Creator != OfferCreator.FakePlayer
            || details.Items is not { Count: > 0 } items || details.BarterScheme is not { Count: > 0 } scheme
            || scheme.Any(barter => !barter.Count.HasValue || !_itemHelper.IsOfBaseclass(barter.Template, BaseClasses.MONEY)))
        {
            return;
        }

        var units = details.SellInOnePiece ? Math.Max(1, details.Quantity) : 1;
        var floor = PriceGate.FloorForItems(items, scheme[0].Template) * units;
        var price = scheme.Sum(barter => barter.Count!.Value);
        var factor = multiplier < 1 && price > 0 && floor > 0 ? Math.Max(multiplier, floor / price) : multiplier;
        foreach (var barter in scheme)
        {
            barter.Count = Math.Max(1, Math.Round(barter.Count!.Value * factor));
        }
    }

    public static void Apply(CreateFleaOfferDetails details)
    {
        var settings = _config?.Mod.Prices.Sellers;
        if (settings is null || !settings.Enabled || _itemHelper is null || details.Creator != OfferCreator.FakePlayer
            || details.Items is not { Count: > 0 } items || details.BarterScheme is not { Count: > 0 } scheme)
        {
            return;
        }

        if (scheme.Any(barter => !barter.Count.HasValue || !_itemHelper.IsOfBaseclass(barter.Template, BaseClasses.MONEY)))
        {
            return;
        }

        // The worst part decides how worn the offer is.
        var quality = items.Min(item => UsedListingsGate.IgnoresCondition(item.Template) ? 1.0 : Math.Clamp(_itemHelper.GetItemQualityModifier(item), 0, 1));
        double Between(double low, double high) => low + Random.Shared.NextDouble() * Math.Max(0, high - low);

        var units = details.SellInOnePiece ? Math.Max(1, details.Quantity) : 1;
        var floor = PriceGate.FloorForItems(items, scheme[0].Template) * units;
        var price = scheme.Sum(barter => barter.Count!.Value);
        double AboveFloor(double wanted) => price > 0 && floor > 0 ? Math.Max(wanted, floor / price) : wanted;

        var quickSell = Math.Clamp(settings.QuickSellChance, 0, 1);
        var overprice = Math.Clamp(settings.OverpriceChance + (1 - quality) * settings.WornOverpriceChance, 0, 1);
        var roll = Random.Shared.NextDouble();
        double factor;
        if (details.SellInOnePiece)
        {
            // Mostly scams, a little dearer than buying singly so they sit mid-list. The rest are a fair bundle
            // near the top.
            var packs = _config!.Mod.GeneratedOffers.Packs;
            var scam = Random.Shared.NextDouble() < packs.ScamChance;
            factor = AboveFloor(scam ? Between(1.02, 1.25) : Between(0.8, 0.92));

            // Rarely a silly one (60 golden eggs), priced like a fair pack.
            if (Random.Shared.NextDouble() < packs.HugeChance && details.Quantity > 0)
            {
                var huge = Random.Shared.Next(Math.Max(details.Quantity, packs.HugeMin), Math.Max(packs.HugeMin, packs.HugeMax) + 1);
                factor = Between(0.8, 0.92) * huge / details.Quantity;
                details.Quantity = huge;
                units = huge;
                floor = PriceGate.FloorForItems(items, scheme[0].Template) * units;
                factor = AboveFloor(factor);
            }

            if (Interlocked.Increment(ref _packsLogged) <= 20)
            {
                var (name, count, f) = (_itemHelper.GetItem(items[0].Template).Value?.Name, details.Quantity, factor);
                DebugLog.Summary(() => $"Pack: {count} x {name}, {(scam ? "scam" : "fair")}, price x{f:0.00}");
            }
        }
        else if (roll < quickSell)
        {
            factor = AboveFloor(1 - Between(settings.QuickSellMinPercent, settings.QuickSellMaxPercent) / 100);
        }
        else if (roll < quickSell + overprice)
        {
            factor = 1 + Between(settings.OverpriceMinPercent, settings.OverpriceMaxPercent) / 100 + (1 - quality) * Between(0.2, 0.5);
            factor = Math.Min(factor, MostOverprice);
        }
        else
        {
            return;
        }

        if (Math.Abs(factor - 1) < 0.01)
        {
            return;
        }

        foreach (var barter in scheme)
        {
            barter.Count = Math.Max(1, Math.Round(barter.Count!.Value * factor));
        }

        if (Interlocked.Increment(ref _logged) <= 12)
        {
            DebugLog.Summary(() => $"Seller price: {_itemHelper.GetItem(items[0].Template).Value?.Name} at {quality:P0} condition listed at {factor:P0} of normal ({(factor < 1 ? "quick sell" : "asks too much")})");
        }
    }
}

// SPT sums the parts' flea prices, which ends up well over what traders sell the gun for. A part is
// worth at most a trader's price for it (any loyalty level, not Fence). Never under the floor.
internal static class GunPricing
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static HandbookHelper? _handbook;
    private static TradersTable? _traders;
    private static RagfairPriceService? _prices;
    private static Lazy<Dictionary<MongoId, double>> _traderValues = new(LoadTraderValues);
    private static int _logged;

    private static readonly MongoId Fence = new("579dc571d53a0658a154fbec");

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, HandbookHelper handbook, TradersTable traders, RagfairPriceService prices)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _handbook = handbook;
        _traders = traders;
        _prices = prices;
    }

    // Trader mods can still change assorts after the startup offers exist.
    public static void ForgetTraderPrices() => _traderValues = new(LoadTraderValues);

    private static GunPricingConfig? Settings => _config?.Mod.Prices.Guns;

    public static bool Applies(List<Item> items) =>
        Settings is { Enabled: true } && _prices is not null && items.Count > 0 && _itemHelper!.IsOfBaseclass(items[0].Template, BaseClasses.WEAPON);

    public static double Value(IEnumerable<Item> items)
    {
        var share = Settings!.TraderPricePercent / 100;
        var values = _traderValues.Value;
        return items.Sum(item =>
        {
            var flea = _prices!.GetFleaPriceForItem(item.Template);
            return values.TryGetValue(item.Template, out var trader) ? Math.Min(flea, trader * share) : flea;
        });
    }

    public static void Apply(CreateFleaOfferDetails details)
    {
        if (details.Creator != OfferCreator.FakePlayer || details.Items is not { } items || !Applies(items)
            || details.BarterScheme is not { Count: 1 } scheme || scheme[0].Count is not { } count
            || !_itemHelper!.IsOfBaseclass(scheme[0].Template, BaseClasses.MONEY))
        {
            return;
        }

        var flea = items.Sum(item => _prices!.GetFleaPriceForItem(item.Template));
        var value = Value(items);
        if (flea <= 0 || value >= flea)
        {
            return;
        }

        var floor = PriceGate.FloorForItems(items, scheme[0].Template);
        var price = Math.Max(Math.Round(count * value / flea), Math.Ceiling(floor));
        if (Interlocked.Increment(ref _logged) <= 10)
        {
            DebugLog.Summary(() => $"Gun price: {_itemHelper.GetItem(items[0].Template).Value?.Name} {count:0} -> {price:0} (trader prices for its parts)");
        }

        scheme[0].Count = price;
    }

    // Each trader offer's price split over its items by flea price; the cheapest share per item is kept.
    private static Dictionary<MongoId, double> LoadTraderValues()
    {
        var values = new Dictionary<MongoId, double>();
        foreach (var (traderId, trader) in _traders ?? [])
        {
            if (traderId == Fence || trader.Assort?.Items is not { Count: > 0 } assortItems || trader.Assort.BarterScheme is not { } schemes)
            {
                continue;
            }

            var children = assortItems.Where(item => item.ParentId is not null).ToLookup(item => item.ParentId!);
            foreach (var root in assortItems.Where(item => item.ParentId == "hideout"))
            {
                if (!schemes.TryGetValue(root.Id, out var options) || options.FirstOrDefault() is not { Count: 1 } payment
                    || payment[0].Count is not { } cost || !_itemHelper!.IsOfBaseclass(payment[0].Template, BaseClasses.MONEY))
                {
                    continue;
                }

                var roubles = payment[0].Template == Money.ROUBLES ? cost : _handbook!.InRoubles(cost, payment[0].Template);
                var tree = new List<Item> { root };
                for (var i = 0; i < tree.Count; i++)
                {
                    tree.AddRange(children[tree[i].Id.ToString()]);
                }

                var fleaTotal = tree.Sum(item => _prices!.GetFleaPriceForItem(item.Template));
                if (fleaTotal <= 0)
                {
                    continue;
                }

                foreach (var item in tree)
                {
                    var share = roubles * _prices!.GetFleaPriceForItem(item.Template) / fleaTotal;
                    if (share > 0 && (!values.TryGetValue(item.Template, out var old) || share < old))
                    {
                        values[item.Template] = share;
                    }
                }
            }
        }

        return values;
    }
}

// Empty fuel is mostly the cheapest fuel offer. Some are priced like a full can.
internal static class EmptyFuelPricing
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static HandbookHelper? _handbook;
    private static RagfairPriceService? _prices;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, HandbookHelper handbook, RagfairPriceService prices)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _handbook = handbook;
        _prices = prices;
    }

    public static bool Apply(CreateFleaOfferDetails details)
    {
        if (details.Creator != OfferCreator.FakePlayer || details.Items is not { Count: > 0 } items || items[0].Upd?.Resource is not { Value: <= 0 }
            || !_itemHelper!.IsOfBaseclass(items[0].Template, FleaCategories.Fuel)
            || details.BarterScheme is not { Count: 1 } scheme || !_itemHelper.IsOfBaseclass(scheme[0].Template, BaseClasses.MONEY))
        {
            return false;
        }

        var currency = scheme[0].Template;
        var full = _prices!.GetFleaPriceForItem(items[0].Template);
        if (currency != Money.ROUBLES)
        {
            full = _handbook!.FromRoubles(full, currency);
        }

        var scam = Random.Shared.NextDouble() < _config!.Mod.GeneratedOffers.UsedItems.Fuel.EmptyScamChance;
        var share = scam ? 0.85 + Random.Shared.NextDouble() * 0.2 : 0.04 + Random.Shared.NextDouble() * 0.08;
        var floor = PriceGate.FloorFor(items[0].Template, items[0], currency);
        scheme[0].Count = Math.Max(1, Math.Max(Math.Round(full * share), Math.Ceiling(floor)));
        return true;
    }
}

// Only feeds generated offers, so trader and handbook prices stay vanilla.
[Injectable(InjectionType.Singleton)]
public class MarkedKeyPricing(
    ConfigManager configManager,
    RagfairConfig ragfairConfig)
{
    private double _applied = 1.0;

    // Applied again on every settings change: the last multiplier is taken back out first, so they don't stack.
    public void Apply()
    {
        var multiplier = configManager.Mod.Prices.MarkedKeyMultiplier;
        if (multiplier == _applied)
        {
            return;
        }

        var priceMultipliers = ragfairConfig.Dynamic.ItemPriceMultiplier ??= new();
        foreach (var tpl in FleaCategories.MarkedKeys)
        {
            priceMultipliers[tpl] = priceMultipliers.GetValueOrDefault(tpl, 1.0) / _applied * multiplier;
        }

        _applied = multiplier;

        DebugLog.Summary(() => $"Marked key flea prices x{multiplier}.");
    }
}

// Only reported, never changed: other mods' trader prices aren't ours to touch.
[Injectable(InjectionType.Singleton)]
public class TraderProfitAudit(
    ISptLogger<TraderProfitAudit> logger,
    ConfigManager configManager,
    TradersTable tradersTable,
    ItemHelper itemHelper,
    HandbookHelper handbookHelper,
    PaymentHelper paymentHelper)
{
    private const int ShownInLog = 15;

    public void Run()
    {
        if (!configManager.Mod.Prices.Floor.LogTraderProfitLoops)
        {
            return;
        }

        var loops = new List<(string Line, double Profit)>();
        foreach (var trader in tradersTable.Values)
        {
            var assort = trader.Assort;
            if (assort?.Items is null || assort.BarterScheme is null)
            {
                continue;
            }

            var roots = assort.Items
                .Where(item => string.Equals(item.ParentId, "hideout", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.Id);

            foreach (var (assortId, schemes) in assort.BarterScheme)
            {
                if (!roots.TryGetValue(assortId, out var root)
                    || schemes.FirstOrDefault() is not [{ } cost]
                    || !paymentHelper.IsMoneyTpl(cost.Template)
                    || cost.Count is not > 0)
                {
                    continue;
                }

                var price = cost.Template == Money.ROUBLES
                    ? cost.Count.Value
                    : handbookHelper.InRoubles(cost.Count.Value, cost.Template);
                var bestBuy = PriceGate.BestTraderBuyPrice(root.Template);
                if (bestBuy <= price)
                {
                    continue;
                }

                var name = itemHelper.GetItem(root.Template).Value?.Name ?? root.Template.ToString();
                loops.Add(($"{name} ({root.Template}): {trader.Base.Nickname ?? trader.Base.Name} sells for {price:0}, a trader pays up to {bestBuy:0} RUB", bestBuy - price));
            }
        }

        if (loops.Count == 0)
        {
            DebugLog.Summary(() => "Trader check: no items a trader sells below another trader's buy price.");
            return;
        }

        logger.Warning($"[BetterFlea] Trader check: {loops.Count} item(s) can be bought from a trader and sold to a trader for profit. Biggest:");
        foreach (var (line, _) in loops.OrderByDescending(loop => loop.Profit).Take(ShownInLog))
        {
            logger.Warning("[BetterFlea]   " + line);
        }
    }
}
