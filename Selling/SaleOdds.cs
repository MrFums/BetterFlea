using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace BetterFlea.Selling;

// What a player's offer of these items can expect at a range of prices: how often it sells before the offer runs out,
// and how long that usually takes. Played out a couple of thousand times with the rules the sales themselves use, for
// the settings page. It leaves out what changes from minute to minute: another seller's cheaper offer (buyers go there
// first) and the flip penalty. An item the player bought lately sells as shown up to expensivePercent over, never past.
internal static class SaleOdds
{
    public static readonly double[] Ratios = [0.9, 1.0, 1.05, 1.1, 1.15, 1.2, 1.3, 1.4, 1.6, 1.8, 2.0];
    private const int Runs = 2000;

    public sealed record Row(double Ratio, double Price, double Sells, double? MedianMinutes);

    public sealed record Table(double Normal, bool Wrecked, bool Plain, double Hours, List<Row> Rows);

    public static Table? For(List<Item> items, ItemHelper itemHelper)
    {
        if (items.Count == 0 || SaleDelayGate.Settings is not { } settings || SaleDelayGate.NormalPrice(items) is not { } normal || normal <= 0)
        {
            return null;
        }

        var wrecked = BetterFlea.Prices.SellerPricing.IsWrecked(items);
        var plain = OrderBook.On && items.Count == 1 && items[0].Upd?.Repairable is null
            && !itemHelper.IsOfBaseclass(items[0].Template, BaseClasses.WEAPON);
        var hours = SaleDelayGate.OfferHours;
        var volume = plain ? OrderBook.Volume(items[0].Template) : 1;
        var rows = new List<Row>();
        foreach (var ratio in Ratios)
        {
            var times = new List<double>();
            for (var run = 0; run < Runs; run++)
            {
                var minutes = plain && ratio <= 1 + settings.ExpensivePercent / 100.0
                    ? OrderBookSale(ratio, volume, settings, hours)
                    : RolledSale(ratio, normal, wrecked, settings, hours, firstRoll: !plain);
                if (minutes is { } m)
                {
                    times.Add(m);
                }
            }

            times.Sort();
            rows.Add(new Row(ratio, Math.Round(normal * ratio), (double)times.Count / Runs, times.Count > 0 ? times[times.Count / 2] : null));
        }

        return new Table(Math.Round(normal), wrecked, plain, hours, rows);
    }

    // Gear and anything else the order book doesn't take: one roll when listed (its buyer comes after a wait), then
    // another buyer every half unique wait, each taking it as often as BuyerTakes says. Wrecked gear only gets the
    // first. A plain item past what the order book's buyers pay starts with the re-rolls.
    private static double? RolledSale(double ratio, double normal, bool wrecked, SaleDelayConfig settings, double hours, bool firstRoll)
    {
        var end = hours * 60;
        var chance = SaleDelayGate.BuyerTakes(ratio) * (wrecked ? SaleDelayGate.WreckedBuyerShare : 1);
        if (chance <= 0)
        {
            return null;
        }

        if (firstRoll && Random.Shared.NextDouble() < chance)
        {
            var dear = SaleDelayGate.Dearness(normal);
            var wait = (settings.UniqueMinMinutes + Random.Shared.NextDouble() * Math.Max(0, settings.UniqueMaxMinutes - settings.UniqueMinMinutes)) * dear;
            var at = SaleDelayGate.SlowSaleSeconds(chance, settings.SlowSaleMaxHours) / 60.0
                + wait * SaleDelayGate.WaitScale(ratio) * (wrecked ? SaleDelayGate.WreckedWaitMultiplier : 1);
            return at <= end ? at : null;
        }

        if (wrecked)
        {
            return null;
        }

        for (var t = 0.0; ;)
        {
            t += SaleDelayGate.UniqueWaitMinutes(normal) * OrderBook.BuyerGap;
            if (t > end)
            {
                return null;
            }

            if (Random.Shared.NextDouble() < chance)
            {
                return t;
            }
        }
    }

    // A plain item up to expensivePercent over: the order book's buyers come at the item's pace and each pays up to a
    // price of their own (most near normal, a few up to expensivePercent over), as if yours were the cheapest offer.
    private static double? OrderBookSale(double ratio, double volume, SaleDelayConfig settings, double hours)
    {
        var buyers = OrderBook.BuyerSettings;
        if (buyers is null)
        {
            return null;
        }

        var end = hours * 60;
        var over = ratio - 1;
        var expensive = Math.Max(0.01, settings.ExpensivePercent / 100.0);
        var low = Math.Max(0.1, buyers.MinMinutes);
        var t = ratio <= 1 ? 5 / 60.0 : 1;
        while (t <= end)
        {
            var spread = Math.Abs(Math.Sqrt(-2 * Math.Log(1 - Random.Shared.NextDouble())) * Math.Cos(2 * Math.PI * Random.Shared.NextDouble()));
            if (over <= Math.Min(expensive, spread * 0.06))
            {
                return t;
            }

            t += (low + Random.Shared.NextDouble() * Math.Max(0, buyers.MaxMinutes - low)) * volume;
        }

        return null;
    }
}
