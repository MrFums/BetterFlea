using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;

namespace BetterFlea.Prices;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class LivePricesLoad(ConfigManager configManager, TemplateTable templates) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        LivePrices.Bind(configManager, templates);
        BasePriceFixPatch.Bind(templates);
        new BasePriceFixPatch().Enable();
        new LivePricesPatch().Enable();
        return Task.CompletedTask;
    }
}

// The real flea's 24 hour average price (tarkov.dev) as every item's base price. Saved to data/, so if tarkov.dev
// can't be reached the last prices are used; with nothing saved, SPT's own prices stay.
internal static class LivePrices
{
    private const string Source = "https://json.tarkov.dev/{0}/items";
    private const string CacheFile = "live-prices.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly object Gate = new();

    private static ConfigManager? _config;
    private static TemplateTable? _templates;
    private static Dictionary<MongoId, double>? _sptPrices;
    private static Cache? _cache;
    private static string? _error;
    private static bool _applied;
    private static Timer? _refresh;
    private static int _fetching;

    // Known: every item tarkov.dev has, priced or not (base-game items, wherever they come from).
    // Levels: the level each item can be bought from on the real flea, where it needs one (LevelLock).
    public record Cache(string[] Modes, long Fetched, Dictionary<string, double> Prices, string[]? Known = null, Dictionary<string, int>? Levels = null);

    private static LivePricesConfig Settings => _config!.Mod.Economy.LivePrices;

    private static string[] WantedModes => [Settings.Endpoint()];

    public static void Bind(ConfigManager configManager, TemplateTable templates)
    {
        _config = configManager;
        _templates = templates;
    }

    // Right after SPT works out its own prices, before any offer is made.
    public static void AfterSptPrices()
    {
        lock (Gate)
        {
            _sptPrices = new Dictionary<MongoId, double>(_templates!.Prices);
        }

        SettingsChanged();
        _refresh ??= new Timer(_ => RefreshIfOld(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public static void SettingsChanged()
    {
        if (_sptPrices is null)
        {
            return;
        }

        if (!Settings.Enabled || WantedModes.Length == 0)
        {
            PutBackSpt();
            return;
        }

        _cache ??= DataFiles.Read<Cache>(CacheFile);
        // A 2.1.1 file has no levels and averaged guns' builds: fetched again once.
        var usable = _cache is { Known: not null, Levels: not null } && _cache.Modes.Order().SequenceEqual(WantedModes.Order());
        if (!usable)
        {
            // Nothing for these game modes yet: wait for it, so the first offers already use it.
            Fetch();
        }

        Use(_cache);
        RefreshIfOld();
    }

    private static void RefreshIfOld()
    {
        if (!Settings.Enabled || _cache is null
            || DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _cache.Fetched < Settings.RefreshHours * 3600)
        {
            return;
        }

        Task.Run(() =>
        {
            Fetch();
            Use(_cache);
        });
    }

    private static void Fetch()
    {
        if (Interlocked.Exchange(ref _fetching, 1) == 1)
        {
            return;
        }

        try
        {
            var modes = WantedModes;
            var sums = new Dictionary<string, (double Sum, int Count)>();
            var known = new HashSet<string>();
            var levels = new Dictionary<string, int>();
            foreach (var mode in modes)
            {
                using var stream = Http.GetStreamAsync(string.Format(Source, mode)).GetAwaiter().GetResult();
                using var json = JsonDocument.Parse(stream);
                foreach (var item in json.RootElement.GetProperty("data").GetProperty("items").EnumerateObject())
                {
                    known.Add(item.Name);
                    if (item.Value.TryGetProperty("minLevelForFlea", out var level) && level.ValueKind == JsonValueKind.Number && level.GetInt32() > 0)
                    {
                        levels[item.Name] = level.GetInt32();
                    }

                    if (PriceOf(item.Value) is var value and > 0)
                    {
                        var (sum, count) = sums.GetValueOrDefault(item.Name);
                        sums[item.Name] = (sum + value, count + 1);
                    }
                }
            }

            if (sums.Count < 500)
            {
                throw new InvalidDataException($"only {sums.Count} prices came back");
            }

            _cache = new Cache(modes, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                sums.ToDictionary(entry => entry.Key, entry => Math.Round(entry.Value.Sum / entry.Value.Count)), known.ToArray(), levels);
            DataFiles.Write(CacheFile, _cache);
            Flea.LevelLock.UseLive(levels);
            _error = null;
            DebugLog.Summary(() => $"Live prices: {_cache.Prices.Count} prices from tarkov.dev ({string.Join(", ", modes)}).");
        }
        catch (Exception ex)
        {
            _error = ex.GetBaseException().Message;
            DebugLog.Summary(() => $"Live prices: couldn't get prices from tarkov.dev ({_error}), keeping the last ones.");
        }
        finally
        {
            Interlocked.Exchange(ref _fetching, 0);
        }
    }

    // A gun's 24 hour average is over every build listed (a 17k stock AKS-74U averages 69k), so a gun gets the day's
    // low instead: about a stock gun. Everything else gets its average.
    private static double PriceOf(JsonElement item)
    {
        static double Number(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

        var gun = item.TryGetProperty("types", out var types) && types.ValueKind == JsonValueKind.Array
            && types.EnumerateArray().Any(type => type.ValueKind == JsonValueKind.String && type.GetString() == "gun");
        var low = gun ? Number(item, "low24hPrice") : 0;
        if (gun && low <= 0)
        {
            low = Number(item, "lastLowPrice");
        }

        return low > 0 ? low : Number(item, "avg24hPrice");
    }

    private static void Use(Cache? cache)
    {
        Flea.LevelLock.UseLive(cache?.Levels);
        if (cache is null || !Settings.Enabled)
        {
            return;
        }

        lock (Gate)
        {
            var prices = _templates!.Prices;
            var live = new Dictionary<MongoId, double>();
            foreach (var (id, price) in cache.Prices)
            {
                if (MongoId.IsValidMongoId(id) && new MongoId(id) is var tpl && _sptPrices!.TryGetValue(tpl, out var spt) && Plausible(spt, price))
                {
                    prices[tpl] = price;
                    live[tpl] = price;
                }
            }

            // Everything without a live price starts from SPT's own price again, then modded items are estimated.
            foreach (var (tpl, spt) in _sptPrices!)
            {
                if (!live.ContainsKey(tpl))
                {
                    prices[tpl] = spt;
                }
            }

            var baseGame = cache.Known?.ToHashSet() ?? [];
            _estimated = Settings.EstimateModdedItems ? MatchModdedItems(live, baseGame) : 0;
            _applied = true;
        }

        // Modded items' levels go by price.
        Flea.LevelLock.Forget();
    }

    private static int _estimated;

    // Some 24 hour averages are junk (a handful of joke listings at a few roubles). Far below or far above SPT's
    // own price, the live price is ignored and the item keeps its own.
    private static bool Plausible(double spt, double live) => spt < 1000 || (live >= spt * 0.15 && live <= spt * 40);

    // tarkov.dev only knows the base game's items, so the rest (modded items) would stay on SPT's price scale next
    // to live-priced items. Move each onto the live scale by how far live prices sit from SPT's for the base-game
    // items of the same kind.
    private static int MatchModdedItems(Dictionary<MongoId, double> live, HashSet<string> baseGame)
    {
        var items = _templates!.Items;
        var ratiosByKind = new Dictionary<MongoId, List<double>>();
        var allRatios = new List<double>();
        foreach (var (tpl, price) in live)
        {
            if (_sptPrices!.GetValueOrDefault(tpl) is > 0 and var spt && items.GetValueOrDefault(tpl)?.Parent is { } kind)
            {
                var ratio = price / spt;
                (ratiosByKind.TryGetValue(kind, out var list) ? list : ratiosByKind[kind] = new List<double>()).Add(ratio);
                allRatios.Add(ratio);
            }
        }

        if (allRatios.Count == 0)
        {
            return 0;
        }

        static double Median(List<double> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }

        var overall = Median(allRatios);
        var byKind = ratiosByKind.Where(entry => entry.Value.Count >= 5).ToDictionary(entry => entry.Key, entry => Median(entry.Value));
        var count = 0;
        foreach (var (tpl, spt) in _sptPrices!)
        {
            // Base-game items (a content backport mod's too) aren't modded: they keep their own price.
            if (live.ContainsKey(tpl) || spt <= 0 || baseGame.Contains(tpl.ToString()) || items.GetValueOrDefault(tpl) is not { } item)
            {
                continue;
            }

            var ratio = Math.Clamp(byKind.GetValueOrDefault(item.Parent, overall), 0.25, 4.0);
            _templates.Prices[tpl] = Math.Round(spt * ratio);
            count++;
        }

        return count;
    }

    private static void PutBackSpt()
    {
        lock (Gate)
        {
            if (!_applied || _sptPrices is null)
            {
                return;
            }

            foreach (var (tpl, price) in _sptPrices)
            {
                _templates!.Prices[tpl] = price;
            }

            _applied = false;
        }

        Flea.LevelLock.Forget();
    }

    // For the settings page: SPT's own price, the live price (null if the real flea doesn't sell it), and whether
    // the real game has the item at all.
    public static (double? Spt, double? Live, bool BaseGame) Lookup(MongoId tpl)
    {
        _cache ??= DataFiles.Read<Cache>(CacheFile);
        var id = tpl.ToString();
        return (_sptPrices?.GetValueOrDefault(tpl), _cache?.Prices.TryGetValue(id, out var live) == true && Plausible(_sptPrices?.GetValueOrDefault(tpl) ?? 0, live) ? live : null,
            _cache?.Known?.Contains(id) == true);
    }

    // The items whose live price is furthest from SPT's, each way: (item, SPT price, live price).
    public static List<(MongoId Tpl, double Spt, double Live)> Differences(int each)
    {
        _cache ??= DataFiles.Read<Cache>(CacheFile);
        if (_cache is null || _sptPrices is null)
        {
            return [];
        }

        var all = _cache.Prices
            .Where(entry => MongoId.IsValidMongoId(entry.Key))
            .Select(entry => (Tpl: new MongoId(entry.Key), Live: entry.Value))
            .Where(entry => _sptPrices.GetValueOrDefault(entry.Tpl) >= 3000 && Plausible(_sptPrices[entry.Tpl], entry.Live))
            .Select(entry => (entry.Tpl, Spt: _sptPrices[entry.Tpl], entry.Live))
            .ToList();
        return all.OrderByDescending(entry => entry.Live / entry.Spt).Take(each)
            .Concat(all.OrderBy(entry => entry.Live / entry.Spt).Take(each)).ToList();
    }

    // For the settings page.
    public static (bool On, int Prices, long Fetched, string[] Modes, string? Error, int Estimated) Status()
    {
        _cache ??= DataFiles.Read<Cache>(CacheFile);
        return (_applied, _cache?.Prices.Count ?? 0, _cache?.Fetched ?? 0, _cache?.Modes ?? [], _error, _applied ? _estimated : 0);
    }
}

public class LivePricesPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() =>
        typeof(RagfairPriceService).GetMethod(nameof(RagfairPriceService.Load), BindingFlags.Instance | BindingFlags.Public)!;

    [PatchPostfix]
    public static void Postfix()
    {
        LivePrices.AfterSptPrices();
    }
}
