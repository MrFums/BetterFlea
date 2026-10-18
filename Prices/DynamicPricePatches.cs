using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;
using BetterFlea.Offers;

namespace BetterFlea.Prices;

// After the database and trader mods, before RagfairCallbacks builds the startup offers.
[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public class DynamicPricePatchesLoad(
    ConfigManager configManager,
    ItemHelper itemHelper,
    HandbookHelper handbookHelper,
    TradersTable tradersTable,
    RagfairConfig ragfairConfig) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        PriceGate.Bind(configManager, itemHelper, handbookHelper, tradersTable, ragfairConfig);
        new DynamicPricePatch().Enable();
        return Task.CompletedTask;
    }
}

// Every generated offer's price passes through here, in this order:
// partly used key curve -> slow price drift -> never below what a trader would pay.
internal static class PriceGate
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static HandbookHelper? _handbook;
    private static TradersTable? _traders;
    private static RagfairConfig? _ragfairConfig;

    // Best rouble price any trader pays for a brand new one, per item.
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
    }

    public static double Adjust(MongoId tpl, MongoId currency, Item? item, double price)
    {
        price *= UsedListingsGate.KeyPriceFactor(tpl, item);
        price *= DriftFactor(tpl);

        var floor = FloorFor(tpl, item, currency);
        if (price < floor)
        {
            Interlocked.Increment(ref _raisedToFloor);
            DebugLog.Write(() => $"Price floor: {Name(tpl)} raised {price:0.##} -> {floor:0.##} {CurrencyName(currency)} (best trader buy {BestTraderBuyPrice(tpl):0.##} RUB)");
            price = floor;
        }

        return price;
    }

    // 0 = no floor. In the offer's currency, scaled by condition like the trader's own price would be.
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

    // SPT's own check only looks at each trader's first loyalty level and ignores what the trader buys.
    // A player at max loyalty with the right trader gets more, so work it out properly.
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

    // Trader mods can still change prices after the startup offers exist.
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
        var drift = Drifts.GetOrAdd(tpl, _ => Drift.Start(settings, now));
        return drift.Current(settings, now, tpl);
    }

    private static string Name(MongoId tpl) => _itemHelper?.GetItem(tpl).Value?.Name ?? tpl.ToString();

    private static string CurrencyName(MongoId currency) =>
        currency == Money.ROUBLES ? "RUB"
        : currency == Money.DOLLARS ? "USD"
        : currency == Money.EUROS ? "EUR"
        : currency.ToString();

    // A random walk that leans back towards normal, so prices wander instead of running off to an edge.
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
                // Spread items out at startup so the market isn't flat until the first move.
                _factor = 1.0 + (Random.Shared.NextDouble() - 0.5) * max,
                _nextMoveAt = now + NextWait(settings)
            };
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

                // An item nobody looked at for hours gets a few moves, not hundreds.
                var steps = Math.Clamp((int)((now - _nextMoveAt) / waitSeconds) + 1, 1, MaxCatchUpSteps);
                for (var i = 0; i < steps; i++)
                {
                    var nudge = (Random.Shared.NextDouble() * 2.0 - 1.0) * step;
                    _factor = Math.Clamp(_factor + (1.0 - _factor) * PullBack + nudge, 1.0 - max, 1.0 + max);
                }

                _nextMoveAt = now + NextWait(settings);
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
