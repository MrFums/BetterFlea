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
    TemplateTable templateTable,
    RagfairConfig ragfairConfig,
    QuestConfig questConfig,
    RagfairPriceService ragfairPriceService,
    RagfairOfferService ragfairOfferService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        PriceGate.Bind(configManager, itemHelper, handbookHelper, tradersTable, ragfairConfig);
        BookPrice.Bind(itemHelper, handbookHelper, ragfairConfig, ragfairPriceService);
        SellerPricing.Bind(configManager, itemHelper, ragfairOfferService);
        QuestDifficulty.Bind(templateTable, tradersTable, questConfig, handbookHelper, itemHelper);
        GunPricing.Bind(configManager, itemHelper, handbookHelper, tradersTable, ragfairPriceService);
        EmptyFuelPricing.Bind(configManager, itemHelper, handbookHelper, ragfairPriceService);
        new DynamicPricePatch().Enable();
        new HighestTraderPricePatch().Enable();
        return Task.CompletedTask;
    }
}

// One of an item's normal flea price the way SPT prices an offer of it, before condition, drift and its random
// spread: never under what a trader pays, SPT's own multiplier for it (marked keys), and a mod or electronic whose
// flea price is silly next to its handbook price capped the way SPT caps it. Without the cap a modded muzzle with a
// 1.4M flea price and 42k offers looked like a 97% off deal, and could be listed for a fortune as "normal".
internal static class BookPrice
{
    private static ItemHelper? _itemHelper;
    private static HandbookHelper? _handbook;
    private static RagfairConfig? _ragfairConfig;
    private static RagfairPriceService? _prices;

    public static ItemHelper? Helper => _itemHelper;

    public static void Bind(ItemHelper itemHelper, HandbookHelper handbook, RagfairConfig ragfairConfig, RagfairPriceService prices)
    {
        _itemHelper = itemHelper;
        _handbook = handbook;
        _ragfairConfig = ragfairConfig;
        _prices = prices;
    }

    public static double Of(MongoId tpl)
    {
        if (_prices is null)
        {
            return 0;
        }

        var price = _prices.GetFleaPriceForItem(tpl);
        var dynamic = _ragfairConfig?.Dynamic;
        if (dynamic?.UseTraderPriceForOffersIfHigher == true)
        {
            price = Math.Max(price, PriceGate.BestTraderBuyPrice(tpl));
        }

        if (dynamic?.ItemPriceMultiplier?.TryGetValue(tpl, out var multiplier) == true)
        {
            price *= multiplier;
        }

        foreach (var (baseClass, cap) in dynamic?.UnreasonableModPrices ?? [])
        {
            if (cap.Enabled && price > 1 && _itemHelper!.IsOfBaseclass(tpl, baseClass))
            {
                var handbook = _handbook!.GetTemplatePrice(tpl);
                if (price > handbook * cap.HandbookPriceOverMultiplier)
                {
                    price = handbook * cap.NewPriceHandbookMultiplier;
                }
            }
        }

        return price;
    }
}

// Order: key curve and used price curve, then drift, then never below what a trader pays.
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
    // parts: the item with everything in it, for a helmet or armour whose condition is its inserts' (ShellQuality).
    public static double Adjust(MongoId tpl, MongoId currency, Item? item, double price, bool counted = true, IEnumerable<Item>? parts = null)
    {
        price *= UsedListingsGate.KeyPriceFactor(tpl, item) * UsedListingsGate.ResourcePriceFactor(tpl, item) * UsedListingsGate.ShellQuality(item, parts);
        price *= DriftFactor(tpl) * (counted ? Demand.Factor(tpl) : Demand.SellerFactor(tpl));

        var floor = FloorFor(tpl, item, currency, parts);
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
        items.Sum(item => FloorFor(item.Template, item, currency, items) * (item.SlotId == "cartridges" ? Math.Max(1, item.Upd?.StackObjectsCount ?? 1) : 1));

    // 0 = no floor.
    public static double FloorFor(MongoId tpl, Item? item, MongoId currency, IEnumerable<Item>? parts = null)
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
            ? _itemHelper!.GetItemQualityModifier(item) * UsedListingsGate.ShellQuality(item, parts)
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

    // Where the market has an item right now: drift and demand. 1 = normal.
    public static double MarketFactor(MongoId tpl) => DriftFactor(tpl) * Demand.Factor(tpl);

    public static double DriftOf(MongoId tpl) => DriftFactor(tpl);

    // A new one's normal price in this currency, with drift and demand.
    public static double NewPrice(MongoId tpl, MongoId currency)
    {
        var price = BookPrice.Of(tpl) * MarketFactor(tpl);
        return currency == Money.ROUBLES || _handbook is null ? price : _handbook.FromRoubles(price, currency);
    }

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
    public static void Postfix(MongoId itemTemplateId, MongoId desiredCurrency, Item? item, IEnumerable<Item>? offerItems, ref double? __result)
    {
        if (__result is not { } price)
        {
            return;
        }

        __result = PriceGate.Adjust(itemTemplateId, desiredCurrency, item, price, parts: offerItems);
    }
}

// SPT's "most a trader pays for this" ignores what each trader buys, so the BTR Driver, Lightkeeper and the
// Storyteller (no cut at all) count as paying the full handbook price for everything. With useTraderPriceForOffersIfHigher
// that put every item worth less than its handbook price at the handbook price on the flea (a 657k LEDX at 970k).
// Only traders that buy the item count.
public class HighestTraderPricePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(SPTarkov.Server.Core.Helpers.Traders.TraderHelper).GetMethod(
            nameof(SPTarkov.Server.Core.Helpers.Traders.TraderHelper.GetHighestSellToTraderPrice),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static bool Prefix(object[] __args, ref double __result)
    {
        if (__args is not [MongoId tpl, ..])
        {
            return true;
        }

        __result = PriceGate.BestTraderBuyPrice(tpl);
        return false;
    }
}

internal static class SellerPricing
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static RagfairOfferService? _offers;
    private static int _logged;

    private const double MostOverprice = 1.8;
    private static int _packsLogged;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, RagfairOfferService offers)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _offers = offers;
    }

    // Nobody lists a wrecked helmet or plate at what it's worth: a 4/13 Bastion is worth about 11k, but its seller
    // looks at the list and goes a bit under the cheapest decent one (39k). A poor deal, but the cheapest on the list.
    // Armour, helmets and anything else with durability under half a new one's worth; not guns. Never under its worth.
    // After the multiplier, before Humanise.
    private const double JunkQuality = 0.5;
    private const double JunkMinShare = 0.85;
    private const double JunkMaxShare = 0.97;

    // With no decent offer up yet, about what the cheapest of them goes for.
    private const double JunkFallbackShare = 0.75;

    public static void PriceJunk(CreateFleaOfferDetails details)
    {
        if (details.Creator != OfferCreator.FakePlayer || details.SellInOnePiece || _itemHelper is null || _offers is null
            || details.Items is not { Count: > 0 } items || details.BarterScheme is not [{ Count: { } price } barter]
            || !_itemHelper.IsOfBaseclass(barter.Template, BaseClasses.MONEY) || _itemHelper.IsOfBaseclass(items[0].Template, BaseClasses.WEAPON)
            || !items.Any(item => item.Upd?.Repairable is not null) || items.Min(UsedListingsGate.PriceQuality) >= JunkQuality)
        {
            return;
        }

        var cheapest = double.MaxValue;
        foreach (var offer in _offers.GetOffersOfType(items[0].Template) ?? [])
        {
            if (offer.CreatedBy == OfferCreator.FakePlayer && offer.SellInOnePiece != true && offer.Items is { Count: > 0 } listed
                && offer.RequirementsCost is { } cost && cost > 0
                && offer.Requirements?.All(requirement => _itemHelper.IsOfBaseclass(requirement.TemplateId, BaseClasses.MONEY)) == true
                && listed.Min(UsedListingsGate.PriceQuality) >= JunkQuality)
            {
                cheapest = Math.Min(cheapest, cost);
            }
        }

        var anchor = cheapest < double.MaxValue ? cheapest
            : PriceGate.NewPrice(items[0].Template, Money.ROUBLES) * JunkFallbackShare * (_config?.Mod.Prices.PriceMultiplier ?? 1.0);
        var wanted = BetterFlea.Offers.BuildQuality.FromRoubles(anchor * (JunkMinShare + Random.Shared.NextDouble() * (JunkMaxShare - JunkMinShare)), barter.Template);
        if (wanted > price)
        {
            barter.Count = Math.Round(wanted);
            if (Interlocked.Increment(ref _junkLogged) <= 12)
            {
                var (name, quality, found) = (_itemHelper.GetItem(items[0].Template).Value?.Name, items.Min(UsedListingsGate.PriceQuality), cheapest < double.MaxValue);
                DebugLog.Summary(() => $"Junk price: {name} at {quality:P0} condition {price:0} -> {wanted:0} ({(found ? "under the cheapest decent offer" : "no decent offer up")}, {anchor:0})");
            }
        }
    }

    private static int _junkLogged;

    // SPT's random spread comes in whole percent steps, so a list of one item had a dozen offers at exactly the same
    // price. People price to the rouble: each offer moves a little, and now and then a seller lists a little under one
    // already up, the way people undercut. Never below the floor. Last, after every other change to the price.
    public static void Humanise(CreateFleaOfferDetails details)
    {
        if (details.Creator != OfferCreator.FakePlayer || _itemHelper is null || details.Items is not { Count: > 0 } items
            || details.BarterScheme is not { Count: 1 } scheme || scheme[0].Count is not { } price || price <= 1
            || !_itemHelper.IsOfBaseclass(scheme[0].Template, BaseClasses.MONEY))
        {
            return;
        }

        var units = details.SellInOnePiece ? Math.Max(1, details.Quantity) : 1;
        var floor = Math.Ceiling(PriceGate.FloorForItems(items, scheme[0].Template) * units);
        var wanted = price * (1 + (Random.Shared.NextDouble() - 0.5) * 0.016);

        // Some sellers undercut an offer near this price (same money, single offers only): 74 444 as 74 443, 74 440,
        // 74 433. Before the easy numbers, or every 45 000 stays a 45 000.
        if (!details.SellInOnePiece && items.Count == 1 && Random.Shared.NextDouble() < UndercutChance && _offers is not null)
        {
            var near = new List<double>();
            foreach (var offer in _offers.GetOffersOfType(items[0].Template) ?? [])
            {
                if (offer.CreatedBy == OfferCreator.FakePlayer && offer.SellInOnePiece != true
                    && offer.Requirements?.ToList() is [{ } only] && only.TemplateId == scheme[0].Template
                    && only.Count is { } count && Math.Abs(count / price - 1) < 0.04)
                {
                    near.Add(count);
                }
            }

            // People undercut the offers that catch the eye: an easy number like 74 444 or 45 000 more than 45 117.
            var eyeCatching = near.Where(count => count >= 1000 && (count % 100 == 0 || count % 1000 >= 998 || SameDigits(count) == count)).ToList();
            var target = eyeCatching.Count > 0 && Random.Shared.NextDouble() < 0.7 ? eyeCatching[Random.Shared.Next(eyeCatching.Count)]
                : near.Count > 0 ? near[Random.Shared.Next(near.Count)] : 0;
            if (target > 0 && Undercut(target) is { } under && under >= Math.Max(1, floor) && !near.Contains(under))
            {
                scheme[0].Count = under;
                return;
            }
        }

        // Some sellers type an easy number instead.
        if (Random.Shared.NextDouble() < (_config?.Mod.Prices.Sellers.RoundPriceChance ?? 0) && EasyNumber(wanted) is { } easy && easy >= floor)
        {
            scheme[0].Count = easy;
            return;
        }

        scheme[0].Count = Math.Max(Math.Max(1, floor), Math.Round(wanted));
    }

    private const double UndercutChance = 0.35;

    // A rouble or two under, a few under, down to the last 0 (74 440), the last two digits one lower (74 433), or 10 under.
    private static double? Undercut(double price)
    {
        var roll = Random.Shared.NextDouble();
        var under = roll < 0.4 ? price - 1
            : roll < 0.6 ? price - Random.Shared.Next(2, 10)
            : roll < 0.75 ? Math.Floor((price - 1) / 10) * 10
            : roll < 0.88 ? price - 11
            : price - 10;
        return under >= 1 && under < price ? under : null;
    }

    // 79412 as 79000, 79500, 79400, 79250, 79999 or 79998; 81372 as 81111; 43900 as 44444; 8247 as 8250. Live, a list
    // reads 44 444, 44 900, 45 000, 45 000, 45 998, 45 999, 46 000: easy to type. Repeated digits (13 333, 44 444,
    // 211 111) are about as common as round numbers. Null when nothing easy is within 4%.
    private static double? EasyNumber(double wanted)
    {
        if (wanted < 1000)
        {
            return null;
        }

        var step = Math.Pow(10, Math.Floor(Math.Log10(wanted)) - 1);
        var roll = Random.Shared.NextDouble();
        double easy;
        if (roll < 0.3)
        {
            easy = Math.Round(wanted / step) * step;
        }
        else if (roll < 0.42)
        {
            easy = Math.Round(wanted / (step / 2)) * (step / 2);
        }
        else if (roll < 0.52)
        {
            // 29 750, 44 900: a quarter or a tenth of the step.
            var fine = Random.Shared.NextDouble() < 0.5 ? step / 4 : step / 10;
            easy = Math.Round(wanted / fine) * fine;
        }
        else if (roll < 0.7)
        {
            easy = Math.Ceiling(wanted / step) * step - (Random.Shared.NextDouble() < 0.75 ? 1 : 2);
        }
        else
        {
            // The last digits repeat the one before them (81 111), or all but the first are the same (44 444, 13 333).
            var head = Math.Floor(wanted / step);
            var digit = head % 10;
            var tail = head * step + digit * (step - 1) / 9;
            var same = SameDigits(wanted);
            easy = Math.Abs(same / wanted - 1) <= 0.04 && Random.Shared.NextDouble() < 0.5 ? same : tail;
        }

        return Math.Abs(easy / wanted - 1) <= 0.04 ? easy : null;
    }

    // The nearest number whose digits after the first are all the same: 44 444, 13 333, 211 111, 2 777 777.
    private static double SameDigits(double wanted)
    {
        var length = (int)Math.Floor(Math.Log10(wanted));
        var ones = (Math.Pow(10, length) - 1) / 9;
        var best = double.MaxValue;
        for (var lead = 1; lead <= 9; lead++)
        {
            for (var digit = 1; digit <= 9; digit++)
            {
                var candidate = lead * Math.Pow(10, length) + digit * ones;
                if (Math.Abs(candidate - wanted) < Math.Abs(best - wanted))
                {
                    best = candidate;
                }
            }
        }

        return best;
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

    // A used key never asks more than a new one's normal price: the spread and worn overpricing put 9/10 and 7/10
    // marked keys among the dearest new ones, where a buyer only expects 10/10. After the multiplier, before Humanise.
    private const double ChancerKeyShare = 0.025;

    public static void CapUsedKey(CreateFleaOfferDetails details)
    {
        if (details.Creator != OfferCreator.FakePlayer || details.SellInOnePiece || details.Items is not [{ } key]
            || key.Upd?.Key is null || details.BarterScheme is not [{ Count: { } price } barter] || UsedListingsGate.PriceQuality(key) >= 1)
        {
            return;
        }

        var cap = PriceGate.NewPrice(key.Template, barter.Template) * (_config?.Mod.Prices.PriceMultiplier ?? 1.0) * WipeEconomy.Factor(key.Template);

        // Now and then a chancer lists a used key among the dear new ones, hoping a buyer doesn't check the uses.
        // About 1 offer in 40 whatever share of the key's offers are used, so one or two to a list.
        var usedChance = UsedListingsGate.KeySettingsFor(key.Template).UsedChance;
        if (cap > 0 && usedChance > 0 && Random.Shared.NextDouble() < ChancerKeyShare / usedChance)
        {
            barter.Count = Math.Round(cap * (1.0 + Random.Shared.NextDouble() * 0.2));
            return;
        }

        if (cap > 0 && price > cap)
        {
            barter.Count = Math.Max(Math.Round(cap), Math.Ceiling(PriceGate.FloorForItems(details.Items, barter.Template)));
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
        var quality = items.Min(UsedListingsGate.PriceQuality);
        double Between(double low, double high) => low + Random.Shared.NextDouble() * Math.Max(0, high - low);

        var units = details.SellInOnePiece ? Math.Max(1, details.Quantity) : 1;
        var floor = PriceGate.FloorForItems(items, scheme[0].Template) * units;
        var price = scheme.Sum(barter => barter.Count!.Value);
        double AboveFloor(double wanted) => price > 0 && floor > 0 ? Math.Max(wanted, floor / price) : wanted;

        var quickSell = Math.Clamp(settings.QuickSellChance, 0, 1);
        var overprice = Math.Clamp(settings.OverpriceChance + (1 - quality) * settings.WornOverpriceChance, 0, 1);
        var troll = Math.Clamp(_itemHelper.IsOfBaseclass(items[0].Template, BaseClasses.WEAPON) ? settings.TrollGunChance : settings.TrollChance, 0, 1);
        var roll = Random.Shared.NextDouble();
        double factor;
        if (details.SellInOnePiece)
        {
            // Mostly scams, a little dearer than buying singly so they sit mid-list. The rest are a fair bundle
            // near the top, never cheap enough to split and sell on for a profit.
            var packs = _config!.Mod.GeneratedOffers.Packs;
            var scam = Random.Shared.NextDouble() < packs.ScamChance;
            factor = AboveFloor(scam ? Between(1.02, 1.25) : Between(0.9, 0.97));

            // Rarely a silly one (60 golden eggs), priced like a fair pack.
            if (Random.Shared.NextDouble() < packs.HugeChance && details.Quantity > 0)
            {
                var huge = Random.Shared.Next(Math.Max(details.Quantity, packs.HugeMin), Math.Max(packs.HugeMin, packs.HugeMax) + 1);
                factor = Between(0.9, 0.97) * huge / details.Quantity;
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
        // Someone lists it at a silly price, 10 times normal (an NL545 at 2,950,000). Nobody buys these; they sit at
        // the end of the list. Live, gun lists have the most.
        else if (roll < quickSell + troll)
        {
            factor = Between(settings.TrollMinMultiplier, settings.TrollMaxMultiplier);
        }
        else if (roll < quickSell + troll + overprice)
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

// SPT sums the parts' flea prices, which ends up well over what traders sell the gun for. A part (the gun itself
// too) is worth at most a trader's price for it (any loyalty level, not Fence), plus some of the gap to its flea
// price when it's a hard unlock: high loyalty, a hard quest (QuestDifficulty), a hidden trader. Never under the floor.
internal static class GunPricing
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static HandbookHelper? _handbook;
    private static TradersTable? _traders;
    private static RagfairPriceService? _prices;
    private static Lazy<Dictionary<MongoId, List<TraderOffer>>> _traderValues = new(LoadTraderValues);
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
    public static void ForgetTraderPrices()
    {
        QuestDifficulty.Forget();
        _traderValues = new(LoadTraderValues);
    }

    private static GunPricingConfig? Settings => _config?.Mod.Prices.Guns;

    // Any trader (not Fence) sells it, at any loyalty level or behind a quest SPT gives.
    public static bool TraderSells(MongoId tpl) => _traders is not null && _traderValues.Value.ContainsKey(tpl);

    public static bool Applies(List<Item> items) =>
        Settings is { Enabled: true } && _prices is not null && items.Count > 0 && _itemHelper!.IsOfBaseclass(items[0].Template, BaseClasses.WEAPON);

    // A part from a hard trader unlock keeps some of its flea price: few players can buy it there. The easiest
    // trader offer of it (the cheapest value) counts. With partsAtFleaPrice on, only the gun itself is valued this
    // way and its parts are worth what they sell for on their own, so taking a gun apart makes no money.
    public static double Value(IEnumerable<Item> items)
    {
        var settings = Settings!;
        var share = settings.TraderPricePercent / 100;
        var values = _traderValues.Value;
        var list = items as IList<Item> ?? items.ToList();
        var worth = list.Select((item, index) => (item, index)).Sum(entry =>
        {
            var item = entry.item;
            var flea = BookPrice.Of(item.Template);
            if ((entry.index > 0 && settings.PartsAtFleaPrice) || !values.TryGetValue(item.Template, out var offers))
            {
                return flea;
            }

            return offers.Min(offer =>
            {
                var trader = offer.Roubles * share;
                return trader >= flea ? flea : trader + (flea - trader) * FleaShare(offer.Unlock);
            });
        });

        // Guns drift with the market too, but less than loose items: the gun's own drift at 40%.
        return list.Count > 0 ? worth * (1 + (PriceGate.DriftOf(list[0].Template) - 1) * 0.4) : worth;
    }

    // The higher of what its loyalty level and how hard its unlock is say.
    private static double FleaShare(Unlock unlock)
    {
        var settings = Settings!;
        var share = Math.Max(settings.FleaShareByLoyalty.GetValueOrDefault(unlock.Loyalty.ToString(), 0),
            settings.FleaShareByDifficulty.GetValueOrDefault(QuestDifficulty.Tier(unlock.Difficulty), 0));
        return Math.Clamp(share, 0, 1);
    }

    // How much a gun's durability leaves of its value, for the whole gun. SPT takes the square root of it off the
    // receiver alone, so a worn gun with costly parts was barely cheaper.
    public static double Condition(List<Item> items)
    {
        var root = items[0];
        if (root.Upd?.Repairable is not { Durability: { } durability } repairable)
        {
            return 1;
        }

        var max = _itemHelper!.GetItem(root.Template).Value?.Properties?.MaxDurability ?? repairable.MaxDurability ?? 0;
        if (max <= 0)
        {
            return 1;
        }

        var half = Math.Clamp(Settings!.PriceAtHalfDurabilityPercent / 100, 0.01, 1);
        return Math.Pow(Math.Clamp(durability / max, 0.01, 1), Math.Log(half) / Math.Log(0.5));
    }

    public static void Apply(CreateFleaOfferDetails details)
    {
        if (details.Creator != OfferCreator.FakePlayer || details.Items is not { } items || !Applies(items)
            || details.BarterScheme is not { Count: 1 } scheme || scheme[0].Count is not { } count
            || !_itemHelper!.IsOfBaseclass(scheme[0].Template, BaseClasses.MONEY))
        {
            return;
        }

        var flea = items.Sum(item => BookPrice.Of(item.Template));
        // SPT's price has its own condition cut on the receiver only; take that back out first.
        var sptFlea = flea - BookPrice.Of(items[0].Template) * (1 - _itemHelper.GetItemQualityModifier(items[0]));
        var value = Value(items);
        var condition = Condition(items);
        if (sptFlea <= 0 || value <= 0)
        {
            return;
        }

        // Every gun goes by its value and condition, listed a little under it as a rule (offerMinPercent to
        // offerMaxPercent): a buyer gets a bit off, and relisting at the value earns about what the listing fee takes.
        // SPT's own price for a build could come out a third over what its parts are worth, or next to nothing.
        var settingsGuns = Settings!;
        var nearStock = BetterFlea.Offers.PresetOfferGate.TakeBuildKind() == "nearstock";
        var low = 1 + Math.Clamp(nearStock ? settingsGuns.StockOfferMinPercent : settingsGuns.OfferMinPercent, -50, 50) / 100;
        var high = 1 + Math.Clamp(nearStock ? settingsGuns.StockOfferMaxPercent : settingsGuns.OfferMaxPercent, -50, 50) / 100;
        var own = low + Random.Shared.NextDouble() * Math.Max(0, high - low);
        var floor = PriceGate.FloorForItems(items, scheme[0].Template);
        var price = Math.Max(Math.Round(BetterFlea.Offers.BuildQuality.FromRoubles(value * condition * own, scheme[0].Template)), Math.Ceiling(floor));
        if (Interlocked.Increment(ref _logged) <= 10)
        {
            DebugLog.Summary(() => $"Gun price: {_itemHelper.GetItem(items[0].Template).Value?.Name} {count:0} -> {price:0} (trader prices for its parts, {condition:P0} for condition)");
        }

        scheme[0].Count = price;
    }

    // Difficulty: of the trader and any quest the offer is locked behind, 0 to 1 (QuestDifficulty).
    private readonly record struct Unlock(int Loyalty, double Difficulty);

    private readonly record struct TraderOffer(double Roubles, Unlock Unlock);

    // Each trader offer's price split over its items by flea price, with what it takes to buy it there. The cheapest
    // share per item and unlock is kept.
    private static Dictionary<MongoId, List<TraderOffer>> LoadTraderValues()
    {
        var values = new Dictionary<MongoId, List<TraderOffer>>();
        foreach (var (traderId, trader) in _traders ?? [])
        {
            if (traderId == Fence || trader.Assort?.Items is not { Count: > 0 } assortItems || trader.Assort.BarterScheme is not { } schemes)
            {
                continue;
            }

            if (QuestDifficulty.OfTrader(traderId) is not { } traderDifficulty)
            {
                continue;
            }

            // Offer -> how hard its quest lock is. "started": buyable once the quest is taken, so its quests before.
            var questLocks = new Dictionary<MongoId, double?>();
            foreach (var (group, locks) in trader.QuestAssort ?? [])
            {
                foreach (var (assortId, questId) in locks)
                {
                    questLocks[assortId] = group == "started" ? QuestDifficulty.OfStart(questId) : QuestDifficulty.Of(questId);
                }
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

                var fleaTotal = tree.Sum(item => BookPrice.Of(item.Template));
                if (fleaTotal <= 0)
                {
                    continue;
                }

                var difficulty = traderDifficulty;
                if (questLocks.TryGetValue(root.Id, out var questDifficulty))
                {
                    // Behind a quest SPT never gives: nobody can buy it there.
                    if (questDifficulty is not { } quest)
                    {
                        continue;
                    }

                    difficulty = Math.Max(difficulty, quest);
                }

                var loyalty = trader.Assort.LoyalLevelItems is not null && trader.Assort.LoyalLevelItems.TryGetValue(root.Id, out var level) ? level : 1;
                var unlock = new Unlock(loyalty, difficulty);
                if (difficulty > 0 && _itemHelper!.IsOfBaseclasses(root.Template, [BaseClasses.WEAPON, BaseClasses.MOD]))
                {
                    DebugLog.Summary(() => $"Unlock: {_itemHelper.GetItem(root.Template).Value?.Name} at {trader.Base?.Nickname} LL{loyalty}, {QuestDifficulty.Tier(difficulty)} ({difficulty:0.00})");
                }
                foreach (var item in tree)
                {
                    var share = roubles * BookPrice.Of(item.Template) / fleaTotal;
                    if (share <= 0)
                    {
                        continue;
                    }

                    var offers = values.TryGetValue(item.Template, out var known) ? known : values[item.Template] = [];
                    var same = offers.FindIndex(offer => offer.Unlock == unlock);
                    if (same < 0)
                    {
                        offers.Add(new TraderOffer(share, unlock));
                    }
                    else if (share < offers[same].Roubles)
                    {
                        offers[same] = new TraderOffer(share, unlock);
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
