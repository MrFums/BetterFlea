using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using BetterFlea.Flea;
using BetterFlea.Prices;

namespace BetterFlea.Web;

// The settings page's Market tab: how the flea's prices moved over a day, three days or a week, from the price
// history (PriceHistory), drift and demand. Worked out once a minute for each range, since it goes over every item.
internal static class MarketStats
{
    private static readonly Dictionary<int, (long At, JsonObject Data)> Cache = new();
    private static readonly object Gate = new();

    private sealed record Tracked(MongoId Tpl, List<(long Time, double Close, int Volume)> Points, double First, double Last, int Volume, double Change);

    private static readonly Dictionary<int, (long At, List<Tracked> Items)> TrackedCache = new();

    // Items with a price at the start and end of the range and enough hours between to mean something, kept a minute.
    private static List<Tracked> Items(int hours, long now)
    {
        lock (Gate)
        {
            if (TrackedCache.TryGetValue(hours, out var cached) && now - cached.At < 60)
            {
                return cached.Items;
            }
        }

        var enough = Math.Max(2, hours / 8);
        var items = PriceHistory.Window(hours)
            .Where(entry => entry.Points.Count >= enough)
            .Select(entry => (entry.Tpl, entry.Points, First: entry.Points[0].Close, Last: entry.Points[^1].Close))
            .Select(entry => new Tracked(entry.Tpl, entry.Points, entry.First, entry.Last, entry.Points.Sum(point => point.Volume), entry.Last / entry.First - 1))
            .ToList();
        lock (Gate)
        {
            TrackedCache[hours] = (now, items);
        }

        return items;
    }

    private static JsonArray Spark(List<(long Time, double Close, int Volume)> points)
    {
        var step = Math.Max(1, points.Count / 48);
        return new JsonArray(points.Where((_, i) => i % step == 0 || i == points.Count - 1).Select(point => (JsonNode)Math.Round(point.Close)).ToArray());
    }

    private static JsonNode Row(Tracked entry, Func<MongoId, string> name) => new JsonObject
    {
        ["id"] = entry.Tpl.ToString(), ["name"] = name(entry.Tpl), ["mod"] = ModItems.All.GetValueOrDefault(entry.Tpl), ["price"] = Math.Round(entry.Last),
        ["change"] = Math.Round(entry.Change * 100, 1), ["volume"] = entry.Volume, ["spark"] = Spark(entry.Points)
    };

    // The Watchlist: items picked by rarity ("common", "rare", "superrare" or any) and Bans-tab category ("weapons" or
    // "weapons.guns", or any), sorted "rising", "falling", "dearest" or "traded".
    public static JsonObject Watch(int hours, string? rarity, string? category, string? sort, Func<MongoId, string> name,
        Func<MongoId, string?> categoryOf, Func<MongoId, string> rarityOf)
    {
        hours = hours is 72 or 168 ? hours : 24;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var picked = Items(hours, now).Where(entry =>
            (string.IsNullOrEmpty(rarity) || rarityOf(entry.Tpl) == rarity)
            && (string.IsNullOrEmpty(category) || categoryOf(entry.Tpl) is { } found && (found == category || found.StartsWith(category + ".", StringComparison.Ordinal))));
        var sorted = sort switch
        {
            // A few roubles' swing on something cheap isn't a move.
            "falling" => picked.Where(entry => entry.Change < 0 && entry.Last >= 1000).OrderBy(entry => entry.Change),
            "dearest" => picked.OrderByDescending(entry => entry.Last),
            "traded" => picked.Where(entry => entry.Volume > 0).OrderByDescending(entry => entry.Volume),
            _ => picked.Where(entry => entry.Change > 0 && entry.Last >= 1000).OrderByDescending(entry => entry.Change)
        };
        return new JsonObject { ["items"] = new JsonArray(sorted.Take(12).Select(entry => Row(entry, name)).ToArray()) };
    }

    // name: an item's name. category: its Bans-tab category ("weapons.guns").
    public static JsonObject Build(int hours, Func<MongoId, string> name, Func<MongoId, string?> category)
    {
        hours = hours is 72 or 168 ? hours : 24;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (Gate)
        {
            if (Cache.TryGetValue(hours, out var cached) && now - cached.At < 60)
            {
                return (JsonObject)cached.Data.DeepClone();
            }
        }

        var data = Make(hours, now, name, category);
        lock (Gate)
        {
            Cache[hours] = (now, data);
        }

        return (JsonObject)data.DeepClone();
    }

    private static JsonObject Make(int hours, long now, Func<MongoId, string> name, Func<MongoId, string?> category)
    {
        var items = Items(hours, now);

        // The market index: each hour, the middle item's price against its price at the start of the range.
        var byHour = new SortedDictionary<long, List<double>>();
        foreach (var entry in items)
        {
            foreach (var point in entry.Points)
            {
                if (!byHour.TryGetValue(point.Time, out var list))
                {
                    byHour[point.Time] = list = [];
                }

                list.Add(point.Close / entry.First - 1);
            }
        }

        var index = new JsonArray();
        foreach (var (hour, moves) in byHour)
        {
            moves.Sort();
            if (moves.Count >= 10)
            {
                index.Add(new JsonArray(hour, Math.Round(moves[moves.Count / 2] * 100, 2)));
            }
        }

        JsonNode Row(Tracked entry) => MarketStats.Row(entry, name);

        // Cheap things swing by a lot for a few roubles: risers and crashers start at 2,000.
        var priced = items.Where(entry => entry.Last >= 2000).ToList();
        var risers = priced.Where(entry => entry.Change > 0.005).OrderByDescending(entry => entry.Change).Take(8).Select(Row).ToArray();
        var crashers = priced.Where(entry => entry.Change < -0.005).OrderBy(entry => entry.Change).Take(8).Select(Row).ToArray();

        // The busiest items get their full chart.
        var from = now - hours * 3600L;
        var busiest = items.Where(entry => entry.Volume > 0).OrderByDescending(entry => entry.Volume).Take(4).Select(entry => (JsonNode)new JsonObject
        {
            ["id"] = entry.Tpl.ToString(), ["name"] = name(entry.Tpl), ["mod"] = ModItems.All.GetValueOrDefault(entry.Tpl), ["price"] = Math.Round(entry.Last),
            ["change"] = Math.Round(entry.Change * 100, 1), ["volume"] = entry.Volume,
            ["candles"] = new JsonArray(PriceHistory.Candles(entry.Tpl).Where(candle => candle.Time >= from).Select(candle => (JsonNode)new JsonArray(candle.Time,
                Math.Round(candle.Open), Math.Round(candle.High), Math.Round(candle.Low), Math.Round(candle.Close), candle.Volume)).ToArray())
        }).ToArray();

        // What's on the flea now, by the Bans tab's groups.
        var groups = new Dictionary<string, int>();
        var groupOf = new Dictionary<MongoId, string>();
        foreach (var offer in FleaHistory.Offers())
        {
            if (offer.IsTraderOffer() || offer.Items is not { Count: > 0 } offerItems)
            {
                continue;
            }

            var tpl = offerItems[0].Template;
            if (!groupOf.TryGetValue(tpl, out var group))
            {
                group = groupOf[tpl] = (category(tpl) ?? "other").Split('.')[0];
            }

            groups[group] = groups.GetValueOrDefault(group) + 1;
        }

        // Drift and demand together: where each item sits against its normal price. Both ease back to normal, so
        // what's under is likelier to rise and what's over likelier to fall.
        var demand = Demand.Shifted().ToDictionary(entry => entry.Tpl, entry => entry.Factor);
        var drifts = PriceGate.AllDrifts();
        var market = drifts.Select(entry => (entry.Tpl, Factor: entry.Factor * demand.GetValueOrDefault(entry.Tpl, 1)))
            .Concat(demand.Where(entry => drifts.All(drift => drift.Tpl != entry.Key)).Select(entry => (Tpl: entry.Key, Factor: entry.Value)))
            .ToList();
        JsonNode Lean((MongoId Tpl, double Factor) entry) => new JsonObject
        {
            ["id"] = entry.Tpl.ToString(), ["name"] = name(entry.Tpl), ["mod"] = ModItems.All.GetValueOrDefault(entry.Tpl), ["percent"] = Math.Round((entry.Factor - 1) * 100, 1),
            ["demand"] = demand.TryGetValue(entry.Tpl, out var shift) ? Math.Round((shift - 1) * 100, 1) : null
        };

        var sales = PriceHistory.RecentSales(10).Select(sale => (JsonNode)new JsonObject
        {
            ["player"] = sale.Name, ["item"] = MongoId.IsValidMongoId(sale.Tpl) ? name(new MongoId(sale.Tpl)) : sale.Tpl, ["mod"] = MongoId.IsValidMongoId(sale.Tpl) ? ModItems.All.GetValueOrDefault(new MongoId(sale.Tpl)) : null,
            ["price"] = sale.Price, ["amount"] = sale.Amount, ["soldAt"] = sale.SoldAt
        }).ToArray();

        return new JsonObject
        {
            ["hours"] = hours, ["now"] = now, ["items"] = items.Count, ["recording"] = !PriceHistory.Off,
            ["up"] = items.Count(entry => entry.Change > 0.005), ["down"] = items.Count(entry => entry.Change < -0.005),
            ["index"] = index,
            ["risers"] = new JsonArray(risers), ["crashers"] = new JsonArray(crashers),
            ["busiest"] = new JsonArray(busiest),
            ["groups"] = new JsonArray(groups.OrderByDescending(entry => entry.Value).Select(entry => (JsonNode)new JsonArray(entry.Key, entry.Value)).ToArray()),
            ["drift"] = new JsonObject
            {
                ["up"] = drifts.Count(entry => entry.Factor > 1.01), ["down"] = drifts.Count(entry => entry.Factor < 0.99),
                ["flat"] = drifts.Count(entry => entry.Factor is >= 0.99 and <= 1.01)
            },
            ["bought"] = new JsonArray(demand.Where(entry => entry.Value > 1).OrderByDescending(entry => entry.Value).Take(6)
                .Select(entry => Lean((entry.Key, entry.Value))).ToArray()),
            ["flooded"] = new JsonArray(demand.Where(entry => entry.Value < 1).OrderBy(entry => entry.Value).Take(6)
                .Select(entry => Lean((entry.Key, entry.Value))).ToArray()),
            ["mightRise"] = new JsonArray(market.Where(entry => entry.Factor < 0.97).OrderBy(entry => entry.Factor).Take(8).Select(Lean).ToArray()),
            ["mightFall"] = new JsonArray(market.Where(entry => entry.Factor > 1.03).OrderByDescending(entry => entry.Factor).Take(8).Select(Lean).ToArray()),
            ["sales"] = new JsonArray(sales)
        };
    }
}
