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
    public static readonly double[] Ratios = [0.9, 1.0, 1.05, 1.1, 1.15, 1.2, 1.25, 1.3, 1.4, 1.5, 1.6, 1.8, 2.0];
    private const int Runs = 2000;

    public sealed record Row(double Ratio, double Price, double Sells, double? MedianMinutes);

    public sealed record Table(double Normal, bool Wrecked, bool Plain, double Hours, List<Row> Rows);

    public static Table? For(List<Item> items, ItemHelper itemHelper)
    {
        if (items.Count > 0 && SaleDelayGate.Fixed is { } set)
        {
            return Fixed(set, items);
        }

        if (items.Count == 0 || SaleDelayGate.Settings is null || SaleDelayGate.NormalPrice(items) is not { } normal || normal <= 0)
        {
            return null;
        }

        var wrecked = BetterFlea.Prices.SellerPricing.IsWrecked(items);
        var plain = OrderBook.On && items.Count == 1 && items[0].Upd?.Repairable is null
            && !itemHelper.IsOfBaseclass(items[0].Template, BaseClasses.WEAPON);
        var gear = SaleDelayGate.IsGear(items, itemHelper);
        var hours = SaleDelayGate.OfferHours;
        var rows = new List<Row>();
        foreach (var ratio in Ratios)
        {
            var times = new List<double>();
            for (var run = 0; run < Runs; run++)
            {
                var minutes = plain
                    ? PlainSale(ratio, normal, items[0].Template, hours)
                    : RolledSale(ratio, normal, items[0].Template, wrecked, gear, hours);
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

    // Set sale times: sells as often as the price says, in the middle of its rarity's wait on average.
    private static Table? Fixed(SaleTimeConfig set, List<Item> items)
    {
        if (SaleDelayGate.NormalPrice(items) is not { } normal || normal <= 0)
        {
            return null;
        }

        var hours = SaleDelayGate.OfferHours;
        var (low, high) = SaleDelayGate.FixedWindow(set, items[0].Template);
        var rows = new List<Row>();
        foreach (var ratio in Ratios)
        {
            var minutes = (low + high) / 2 * SaleDelayGate.FixedScale(set, ratio);
            var sells = minutes <= hours * 60 ? SaleDelayGate.FixedChance(set, normal * ratio, normal) : 0;
            rows.Add(new Row(ratio, Math.Round(normal * ratio), sells, sells > 0 ? minutes : null));
        }

        return new Table(Math.Round(normal), false, false, hours, rows);
    }

    // Gear and anything else the order book doesn't take: it sells as often as OverpriceSells says, decided once.
    // If it does, its buyer comes after a unique wait (less when it's cheaper than normal, three times as long when
    // wrecked), plus OverpriceWait for how far over normal it is.
    // gear: more room over normal (SaleDelayGate.IsGear, gearOverpricePercent).
    private static double? RolledSale(double ratio, double normal, MongoId tpl, bool wrecked, bool gear, double hours)
    {
        var price = normal * ratio;
        if (Random.Shared.NextDouble() >= SaleDelayGate.OverpriceSells(price, normal, gear: gear))
        {
            return null;
        }

        var slow = wrecked ? SaleDelayGate.WreckedWaitMultiplier : 1;
        var t = Math.Max(5 / 60.0, SaleDelayGate.RolledPriceWait(normal, tpl) * SaleDelayGate.WaitScale(Math.Min(1, ratio)) * slow)
            + SaleDelayGate.OverWait(price, normal, SaleDelayGate.OverpriceX(price, normal, gear));
        return t <= hours * 60 ? t : null;
    }

    // A plain item: buyers get to it after the item's first wait (OrderBook.Listed), less when it's cheaper than
    // normal. At or under normal it then sells, as if yours were the cheapest offer. Over normal it sells as often as
    // OverpriceSells says, after an OverpriceWait more (OrderBook.OverpricedFate).
    private static double? PlainSale(double ratio, double normal, MongoId tpl, double hours)
    {
        var wait = OrderBook.FirstBuyerSeconds(tpl);
        var t = (ratio <= OrderBook.QuickSellBelow ? 5 : Math.Max(5, wait * SaleDelayGate.WaitScale(Math.Min(1, ratio)))) / 60.0;
        if (ratio > 1)
        {
            if (Random.Shared.NextDouble() >= SaleDelayGate.OverpriceSells(normal * ratio, normal))
            {
                return null;
            }

            t += SaleDelayGate.OverWait(normal * ratio, normal, SaleDelayGate.OverpriceX(normal * ratio, normal));
        }

        return t <= hours * 60 ? t : null;
    }
}
