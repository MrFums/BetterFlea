using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Web;

// Every flea item's price over the last week, as hourly candles (open, high, low, close of its normal price) with how
// many changed hands, for the settings page's stock chart. The price is sampled every 15 minutes, so a candle's high and
// low are what the samples saw. Volume is the market's trades: what the item's busyness says the whole flea trades an
// hour (wires and bolts by the hundred, a LEDX a few), plus every real sale on this server: players buying, players'
// offers selling, and other sellers' offers bought by the flea's own buyers. Saved (gzipped, about half a megabyte) in data/price-history.bin 5 minutes after the
// start and then every 15 minutes, so a restart keeps the week.
// Players' own sales are kept too (data/player-sales.json), so a chart can show one player's listings and sales.
[Injectable(TypePriority = int.MaxValue)]
public class PriceHistory(TemplateTable templates, PresetHelper presetHelper) : IOnLoad
{
    public const int Hours = 24 * 7;
    private const int SampleMinutes = 15;
    private const string FileName = "price-history.bin";
    private const string SalesFile = "player-sales.json";
    private const int KeepSales = 2000;
    private const int FileVersion = 2;

    // Trades an hour for an item of busyness 1 (OrderBook.Volume: about 20,000 roubles, common, wanted by nothing).
    // Wires (busyness about 0.2) come to about 120 an hour, an M4A1 about 12, a LEDX about 3.
    private const double MarketTradesPerHour = 25;

    private sealed class Series
    {
        public readonly int[] Hour = new int[Hours];
        public readonly float[] Open = new float[Hours], High = new float[Hours], Low = new float[Hours], Close = new float[Hours];
        public readonly int[] Volume = new int[Hours];
        public float Last;
    }

    public sealed record PlayerSale(string Player, string Name, string Tpl, double Price, int Amount, long ListedAt, long SoldAt);

    private static readonly Dictionary<MongoId, Series> All = new();
    private static readonly object Gate = new();
    private static List<PlayerSale> _sales = new();
    private static Dictionary<MongoId, List<Item>> _builds = new();
    private static List<MongoId> _items = new();
    private static Timer? _sampler, _saver;
    private static int _dirty;

    private static int HourNow() => (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // A gun is charted as its default build, the way it's listed and the way Item lookup prices it.
        _builds = presetHelper.GetDefaultPresets().Values
            .Where(preset => preset.Encyclopedia is not null && preset.Items is { Count: > 1 })
            .GroupBy(preset => preset.Encyclopedia!.Value).ToDictionary(group => group.Key, group => group.First().Items!);
        var listable = templates.Handbook.Items.Select(entry => entry.Id).ToHashSet();
        _items = templates.Items.Values
            .Where(item => item.Type == "Item" && listable.Contains(item.Id) && item.Properties?.CanSellOnRagfair == true)
            .Select(item => item.Id).ToList();
        Load();
        _sampler ??= new Timer(_ => Sample(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(SampleMinutes));
        _saver ??= new Timer(_ => Save(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(SampleMinutes));
        return Task.CompletedTask;
    }

    // How long the last pass over every item took, in milliseconds (shown with the chart).
    public static long LastSampleMs { get; private set; }

    private static void Sample()
    {
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var hour = HourNow();
            foreach (var tpl in _items)
            {
                var items = _builds.GetValueOrDefault(tpl) ?? [new Item { Id = new MongoId(), Template = tpl }];
                if (BetterFlea.Selling.SaleDelayGate.NormalPrice(items) is not { } price || price <= 0)
                {
                    continue;
                }

                var trades = Poisson(MarketTradesPerHour / Math.Max(0.05, BetterFlea.Selling.OrderBook.Volume(tpl)) * SampleMinutes / 60.0);
                lock (Gate)
                {
                    var series = All.TryGetValue(tpl, out var found) ? found : All[tpl] = new Series();
                    var slot = Slot(series, hour, (float)price);
                    series.High[slot] = Math.Max(series.High[slot], (float)price);
                    series.Low[slot] = Math.Min(series.Low[slot], (float)price);
                    series.Close[slot] = (float)price;
                    series.Volume[slot] += trades;
                    series.Last = (float)price;
                }
            }

            LastSampleMs = watch.ElapsedMilliseconds;
            Interlocked.Exchange(ref _dirty, 1);
        }
        catch (Exception ex)
        {
            DebugLog.Summary(() => $"Price history: sampling failed: {ex.GetBaseException().Message}");
        }
    }

    // A random count with this mean (Knuth's way for small means, a rounded normal for big ones).
    private static int Poisson(double mean)
    {
        if (mean <= 0)
        {
            return 0;
        }

        if (mean > 30)
        {
            var normal = Math.Sqrt(-2 * Math.Log(1 - Random.Shared.NextDouble())) * Math.Cos(2 * Math.PI * Random.Shared.NextDouble());
            return Math.Max(0, (int)Math.Round(mean + normal * Math.Sqrt(mean)));
        }

        var limit = Math.Exp(-mean);
        var count = 0;
        for (var product = Random.Shared.NextDouble(); product > limit; product *= Random.Shared.NextDouble())
        {
            count++;
        }

        return count;
    }

    // The slot for this hour, started from the last price seen if it's a new hour.
    private static int Slot(Series series, int hour, float price)
    {
        var slot = hour % Hours;
        if (series.Hour[slot] != hour)
        {
            var start = series.Last > 0 ? series.Last : price;
            series.Hour[slot] = hour;
            series.Open[slot] = series.High[slot] = series.Low[slot] = series.Close[slot] = start;
            series.Volume[slot] = 0;
        }

        return slot;
    }

    // Some of an item changed hands on the flea (any buyer, any seller).
    public static void Sold(MongoId tpl, int units)
    {
        if (units <= 0)
        {
            return;
        }

        lock (Gate)
        {
            if (!All.TryGetValue(tpl, out var series) || series.Last <= 0)
            {
                return;
            }

            series.Volume[Slot(series, HourNow(), series.Last)] += units;
        }

        Interlocked.Exchange(ref _dirty, 1);
    }

    // A player's own offer sold some.
    public static void PlayerSold(string player, string name, RagfairOffer offer, int amount)
    {
        if (offer.Items?.FirstOrDefault() is not { } root)
        {
            return;
        }

        var units = offer.SellInOnePiece == true ? Math.Max(1, offer.Quantity) : Math.Max(1, amount);
        Sold(root.Template, units);
        lock (Gate)
        {
            _sales.Add(new PlayerSale(player, name, root.Template.ToString(), Math.Round(offer.RequirementsCost ?? 0), units,
                offer.StartTime ?? 0, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            if (_sales.Count > KeepSales)
            {
                _sales.RemoveRange(0, _sales.Count - KeepSales);
            }
        }

        Interlocked.Exchange(ref _dirty, 1);
    }

    // The item's candles, oldest first: (hour start in unix seconds, open, high, low, close, volume).
    public static List<(long Time, double Open, double High, double Low, double Close, int Volume)> Candles(MongoId tpl)
    {
        var list = new List<(long, double, double, double, double, int)>();
        var now = HourNow();
        lock (Gate)
        {
            if (!All.TryGetValue(tpl, out var series))
            {
                return list;
            }

            for (var hour = now - Hours + 1; hour <= now; hour++)
            {
                var slot = hour % Hours;
                if (series.Hour[slot] == hour)
                {
                    list.Add((hour * 3600L, series.Open[slot], series.High[slot], series.Low[slot], series.Close[slot], series.Volume[slot]));
                }
            }
        }

        return list;
    }

    public static List<PlayerSale> Sales(MongoId tpl)
    {
        var id = tpl.ToString();
        lock (Gate)
        {
            return _sales.Where(sale => sale.Tpl == id).ToList();
        }
    }

    private static void Save()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0)
        {
            return;
        }

        try
        {
            var path = DataFiles.PathOf(FileName);
            var temp = path + ".tmp";
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            List<PlayerSale> sales;
            lock (Gate)
            {
                using (var writer = new BinaryWriter(new System.IO.Compression.GZipStream(File.Create(temp), System.IO.Compression.CompressionLevel.Fastest)))
                {
                    writer.Write(FileVersion);
                    writer.Write(Hours);
                    writer.Write(All.Count);
                    foreach (var (tpl, series) in All)
                    {
                        writer.Write(tpl.ToString());
                        writer.Write(series.Last);
                        for (var i = 0; i < Hours; i++)
                        {
                            writer.Write(series.Hour[i]);
                            writer.Write(series.Open[i]);
                            writer.Write(series.High[i]);
                            writer.Write(series.Low[i]);
                            writer.Write(series.Close[i]);
                            writer.Write(series.Volume[i]);
                        }
                    }
                }

                sales = _sales.ToList();
            }

            File.Move(temp, path, overwrite: true);
            DataFiles.Write(SalesFile, sales);
        }
        catch (Exception ex)
        {
            DebugLog.Summary(() => $"Price history: couldn't save: {ex.GetBaseException().Message}");
        }
    }

    private static void Load()
    {
        _sales = DataFiles.Read<List<PlayerSale>>(SalesFile) ?? new();
        var path = DataFiles.PathOf(FileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var reader = new BinaryReader(new System.IO.Compression.GZipStream(File.OpenRead(path), System.IO.Compression.CompressionMode.Decompress));
            if (reader.ReadInt32() != FileVersion || reader.ReadInt32() != Hours)
            {
                return;
            }

            var count = reader.ReadInt32();
            lock (Gate)
            {
                for (var n = 0; n < count; n++)
                {
                    var id = reader.ReadString();
                    var series = new Series { Last = reader.ReadSingle() };
                    for (var i = 0; i < Hours; i++)
                    {
                        series.Hour[i] = reader.ReadInt32();
                        series.Open[i] = reader.ReadSingle();
                        series.High[i] = reader.ReadSingle();
                        series.Low[i] = reader.ReadSingle();
                        series.Close[i] = reader.ReadSingle();
                        series.Volume[i] = reader.ReadInt32();
                    }

                    if (MongoId.IsValidMongoId(id))
                    {
                        All[new MongoId(id)] = series;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Summary(() => $"Price history: couldn't read data/{FileName}, starting a new one ({ex.GetBaseException().Message}).");
            lock (Gate)
            {
                All.Clear();
            }
        }
    }
}
