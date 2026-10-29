using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;

namespace BetterFlea.Prices;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class LivePricesLoad(ISptLogger<LivePricesLoad> logger, ConfigManager configManager, TemplateTable templates, TradersTable traders) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        LivePrices.Bind(configManager, templates, traders, message => logger.Warning(message));
        new LivePricesPatch().Enable();
        return Task.CompletedTask;
    }
}

// The real flea's 24 hour average price (tarkov.dev) as every item's base price. Saved to data/, so if tarkov.dev
// can't be reached the last prices are used; with nothing saved, Better Flea's prices are used until it can be.
// Better Flea pricing goes the same way, from the fixed price index inside the DLL. While it's on, live prices rest.
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
    private static bool _indexApplied;
    // Live prices are on, but there are none yet and tarkov.dev can't be reached: Better Flea's prices stand in.
    private static bool _fallback;
    private static Action<string>? _warn;
    private static readonly Lazy<Cache?> Index = new(ReadIndex);

    // Known: every item tarkov.dev has, priced or not (base-game items, wherever they come from).
    // Levels: the level each item can be bought from on the real flea, where it needs one (LevelLock).
    // Backport: the price index's items that only WTT ContentBackport adds (SPT 4.1.6 doesn't have them yet).
    // TraderCuts: each trader's cut when buying from a player in the live game (SPT's buy_price_coef), by trader id.
    public record Cache(string[] Modes, long Fetched, Dictionary<string, double> Prices, string[]? Known = null, Dictionary<string, int>? Levels = null,
        string[]? Backport = null, Dictionary<string, double>? TraderCuts = null);

    private static LivePricesConfig Settings => _config!.Mod.Economy.LivePrices;

    private static BetterFleaPricingConfig IndexSettings => _config!.Mod.Economy.BetterFleaPricing;

    private static bool IndexOn => IndexSettings.Enabled && Index.Value is not null;

    // Better Flea's prices are in use, chosen or standing in for live ones.
    private static bool IndexWanted => IndexOn || _fallback;

    private static string[] WantedModes => [Settings.Endpoint()];

    public static void Bind(ConfigManager configManager, TemplateTable templates, TradersTable traders, Action<string> warn)
    {
        _config = configManager;
        _templates = templates;
        _traders = traders;
        _warn = warn;
    }

    private static TradersTable? _traders;
    private static Dictionary<MongoId, double?[]>? _sptCuts;
    private static HashSet<MongoId> _cutsSetByMod = [];

    // SPT 4.1's own cuts. A trader whose cut is something else was changed by another mod (SVM's trader markup, say),
    // and keeps that mod's cut.
    private static readonly Dictionary<string, double> SptDefaultCuts = new()
    {
        ["54cb50c76803fa8b248b4571"] = 50, ["54cb57776803fa99248b456e"] = 37, ["579dc571d53a0658a154fbec"] = 60,
        ["58330581ace78e27b8b10cee"] = 51, ["5935c25fb3acc3127c3d8cd9"] = 55, ["5a7c2eca46aef81a7ca2145d"] = 44,
        ["5ac3b934156ae10c4430e83c"] = 38, ["5c0647fdd443bc2504c2d371"] = 40
    };

    // Traders whose cut another mod changed: their cut is left alone. For the settings page.
    public static IReadOnlyList<string> CutsSetByMod =>
        _traders is null ? [] : _cutsSetByMod.Select(id => _traders.GetValueOrDefault(id)?.Base?.Nickname ?? id.ToString()).ToList();

    // Live trader prices: what traders pay a player, with each trader's live cut (Therapist keeps 49%, not SPT's 37%).
    // Null puts SPT's back. Players pick it up when they next log in.
    private static void UseTraderCuts(Dictionary<string, double>? cuts)
    {
        if (_traders is null)
        {
            return;
        }

        if (_sptCuts is null)
        {
            _sptCuts = _traders.Values.Where(trader => trader.Base?.LoyaltyLevels is not null)
                .ToDictionary(trader => trader.Base!.Id, trader => trader.Base!.LoyaltyLevels!.Select(level => level.BuyPriceCoefficient).ToArray());
            _cutsSetByMod = _sptCuts
                .Where(entry => SptDefaultCuts.TryGetValue(entry.Key.ToString(), out var cut) && entry.Value.Any(level => level != cut))
                .Select(entry => entry.Key).ToHashSet();
            if (_cutsSetByMod.Count > 0)
            {
                _warn?.Invoke($"[BetterFlea] Live trader prices: another mod changed what these traders pay, so they keep that mod's prices: {string.Join(", ", CutsSetByMod)}.");
            }
        }

        foreach (var trader in _traders.Values)
        {
            if (trader.Base?.LoyaltyLevels is not { } levels || !_sptCuts.TryGetValue(trader.Base.Id, out var spt))
            {
                continue;
            }

            if (_cutsSetByMod.Contains(trader.Base.Id))
            {
                continue;
            }

            for (var i = 0; i < levels.Count && i < spt.Length; i++)
            {
                levels[i].BuyPriceCoefficient = cuts?.TryGetValue(trader.Base.Id.ToString(), out var cut) == true ? cut : spt[i];
            }
        }

        PriceGate.ForgetTraderPrices();
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

        _fallback = false;
        if (IndexOn)
        {
            Use(Index.Value, true);
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

        if (_cache is null && Index.Value is not null)
        {
            _fallback = true;
            _warn?.Invoke($"[BetterFlea] Live prices: couldn't reach tarkov.dev ({_error}). Using Better Flea pricing until it can, trying again every 10 minutes.");
            Use(Index.Value, true);
            return;
        }

        Use(_cache, false);
        RefreshIfOld();
    }

    // Every 10 minutes: fetched again once the prices are old, or until the first ones come.
    private static void RefreshIfOld()
    {
        if (!Settings.Enabled || IndexOn
            || (_cache is not null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _cache.Fetched < Settings.RefreshHours * 3600))
        {
            return;
        }

        Task.Run(() =>
        {
            Fetch();
            if (_fallback && _cache is not null)
            {
                _fallback = false;
                _warn?.Invoke("[BetterFlea] Live prices: tarkov.dev reached, live prices in use from the next offers.");
            }

            Use(_cache, false);
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

    private static void Use(Cache? cache, bool index)
    {
        if (cache is null || (index ? !IndexWanted : !Settings.Enabled || IndexOn))
        {
            lock (Gate)
            {
                ForgetOverrides();
                UseOverrides();
            }

            return;
        }

        Flea.LevelLock.UseLive(cache.Levels);

        lock (Gate)
        {
            ForgetOverrides();
            var prices = _templates!.Prices;
            var live = new Dictionary<MongoId, double>();
            // ContentBackport's items keep their own price unless its option is on.
            var skip = index && !IndexSettings.ContentBackport ? cache.Backport?.ToHashSet() : null;
            foreach (var (id, price) in cache.Prices)
            {
                if (MongoId.IsValidMongoId(id) && skip?.Contains(id) != true && new MongoId(id) is var tpl && _sptPrices!.TryGetValue(tpl, out var spt)
                    && (index || Plausible(tpl, spt, price)))
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
            _estimated = (index ? IndexSettings.EstimateModdedItems : Settings.EstimateModdedItems) ? MatchModdedItems(live, baseGame) : 0;
            UseOverrides();
            _applied = true;
            _indexApplied = index;
        }

        UseTraderCuts(index && !_fallback && IndexSettings.LiveTraderPrices ? cache.TraderCuts : null);

        // Modded items' levels go by price.
        Flea.LevelLock.Forget();
    }

    private static int _estimated;

    // Some 24 hour averages are junk (a handful of joke listings at a few roubles). Far below or far above SPT's
    // own price, the live price is ignored and the item keeps its own, unless the handbook agrees with it: SPT's own
    // price is the odd one out for some items (44k Glock sights, a 275k X-17 that sells for 17k).
    private static bool Plausible(MongoId tpl, double spt, double live) =>
        spt < 1000 || (live >= spt * 0.15 && live <= spt * 40)
        || (Handbook.Value.GetValueOrDefault(tpl) is > 0 and var book && live >= book * 0.2 && live <= book * 10);

    private static readonly Lazy<Dictionary<MongoId, double>> Handbook = new(() =>
        (_templates?.Handbook?.Items ?? []).Where(entry => entry.Price > 0).GroupBy(entry => entry.Id)
            .ToDictionary(group => group.Key, group => (double)group.First().Price!));

    private static Cache? ReadIndex()
    {
        try
        {
            using var stream = typeof(LivePrices).Assembly.GetManifestResourceStream("BetterFlea.data.price-index.json");
            return stream is null ? null : JsonSerializer.Deserialize<Cache>(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // tarkov.dev only knows the base game's items, so the rest (modded items) would stay on SPT's price scale next
    // to live-priced items. Each is moved onto the live scale twice, by how far live prices sit from the flea price
    // and from the handbook price for the base-game items of the same kind, and the one nearer what that kind of item
    // costs is kept. A mod's flea price or its handbook price can be a guess: a gun on the flea at 10x its handbook
    // price, parts at just their handbook price when the base game's sell for 6x, plate carriers with a 200 handbook
    // price, or helmets at a sensible handbook price when the base game's sell for 15x theirs.
    private static int MatchModdedItems(Dictionary<MongoId, double> live, HashSet<string> baseGame)
    {
        var items = _templates!.Items;
        var handbook = Handbook.Value;
        var byBook = new Medians();
        var byFlea = new Medians();
        var prices = new Medians();
        foreach (var (tpl, price) in live)
        {
            if (items.GetValueOrDefault(tpl)?.Parent is not { } kind || price <= 0)
            {
                continue;
            }

            prices.Add(kind, price);

            if (handbook.GetValueOrDefault(tpl) is > 0 and var book)
            {
                byBook.Add(kind, price / book);
            }

            if (_sptPrices!.GetValueOrDefault(tpl) is > 0 and var spt)
            {
                byFlea.Add(kind, price / spt);
            }
        }

        if (byBook.IsEmpty && byFlea.IsEmpty)
        {
            return 0;
        }

        var count = 0;
        var modded = new HashSet<MongoId>();
        foreach (var (tpl, spt) in _sptPrices!)
        {
            // Base-game items (a content backport mod's too) aren't modded: they keep their own price.
            if (live.ContainsKey(tpl) || spt <= 0 || baseGame.Contains(tpl.ToString()) || items.GetValueOrDefault(tpl) is not { } item)
            {
                continue;
            }

            modded.Add(tpl);

            // Armour sells for 15-18x its handbook price, so the handbook ratio is allowed far wider.
            var fromFlea = spt * Math.Clamp(byFlea.For(item.Parent), 0.25, 4.0);
            var estimate = fromFlea;
            if (handbook.GetValueOrDefault(tpl) is > 0 and var book && !byBook.IsEmpty)
            {
                var fromBook = book * Math.Clamp(byBook.For(item.Parent), 0.1, 25.0);
                var typical = prices.For(item.Parent);
                estimate = Math.Abs(Math.Log(fromBook / typical)) < Math.Abs(Math.Log(fromFlea / typical)) ? fromBook : fromFlea;
            }

            // A mod can price both high (an upper receiver at 168k when 9 in 10 base-game ones are under 49k): kept
            // under what 9 in 10 base-game items of its kind cost, so a premium part is dear but not 3x the dearest.
            // No floor: mods price joke and decor items at a rouble or two on purpose.
            if (prices.Ceiling(item.Parent) is { } ceiling)
            {
                estimate = Math.Min(estimate, ceiling);
            }

            _templates.Prices[tpl] = Math.Round(estimate);
            count++;
        }

        AtLeastTraderPrice(modded, tpl => items.GetValueOrDefault(tpl)?.Parent is { } kind ? prices.Ceiling(kind) : null);
        return count;
    }

    private static readonly MongoId Fence = new("579dc571d53a0658a154fbec");

    // What a trader sells a modded item for is real, where its mod's flea and handbook prices are a guess: one a trader
    // sells is worth at least the share gun pricing keeps of a trader's price (traderPricePercent, 95%), on the
    // cheapest such offer. An offer of several items (a gun with its magazine) is split by price. Not Fence. Still
    // under what 9 in 10 base-game items of its kind cost (ceiling), as a mod's trader can ask silly money too.
    private static void AtLeastTraderPrice(HashSet<MongoId> modded, Func<MongoId, double?> ceiling)
    {
        if (_traders is null || modded.Count == 0)
        {
            return;
        }

        var prices = _templates!.Prices;
        var handbook = Handbook.Value;
        var share = Math.Clamp((_config?.Mod.Prices.Guns.TraderPricePercent ?? 95) / 100, 0, 1);
        var floors = new Dictionary<MongoId, double>();
        foreach (var (traderId, trader) in _traders)
        {
            if (traderId == Fence || trader.Assort?.Items is not { Count: > 0 } assort || trader.Assort.BarterScheme is not { } schemes)
            {
                continue;
            }

            var children = assort.Where(entry => entry.ParentId is not null).ToLookup(entry => entry.ParentId!);
            foreach (var root in assort.Where(entry => entry.ParentId == "hideout"))
            {
                // Money only: a barter's worth is another guess.
                if (!schemes.TryGetValue(root.Id, out var options) || options.FirstOrDefault() is not { Count: 1 } payment
                    || payment[0].Count is not { } cost || cost <= 0
                    || (payment[0].Template != Money.ROUBLES && handbook.GetValueOrDefault(payment[0].Template) is not > 0))
                {
                    continue;
                }

                var roubles = payment[0].Template == Money.ROUBLES ? cost : cost * handbook[payment[0].Template];
                var tree = new List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> { root };
                for (var i = 0; i < tree.Count; i++)
                {
                    tree.AddRange(children[tree[i].Id.ToString()]);
                }

                if (!tree.Any(entry => modded.Contains(entry.Template)))
                {
                    continue;
                }

                var total = tree.Sum(entry => prices.GetValueOrDefault(entry.Template));
                if (total <= 0)
                {
                    continue;
                }

                foreach (var entry in tree.Where(entry => modded.Contains(entry.Template)))
                {
                    var floor = roubles * share * prices.GetValueOrDefault(entry.Template) / total;
                    if (floor > 0)
                    {
                        floors[entry.Template] = floors.TryGetValue(entry.Template, out var known) ? Math.Min(known, floor) : floor;
                    }
                }
            }
        }

        var raised = new List<(MongoId Tpl, double From, double To)>();
        foreach (var (tpl, traderFloor) in floors)
        {
            var floor = ceiling(tpl) is { } cap ? Math.Min(traderFloor, cap) : traderFloor;
            if (prices.GetValueOrDefault(tpl) is var from && from < floor)
            {
                prices[tpl] = Math.Round(floor);
                raised.Add((tpl, from, Math.Round(floor)));
            }
        }

        DebugLog.Summary(() => $"Modded items: {raised.Count} raised to at least what a trader sells them for; most: "
            + string.Join(", ", raised.OrderByDescending(entry => entry.To / Math.Max(1, entry.From)).Take(15)
                .Select(entry => $"{_templates.Items.GetValueOrDefault(entry.Tpl)?.Name ?? entry.Tpl.ToString()} {entry.From:0} -> {entry.To:0}")));
    }

    // The median by item kind. A kind with fewer than 5 items uses the median of all of them.
    private sealed class Medians
    {
        private readonly Dictionary<MongoId, List<double>> _byKind = new();
        private readonly List<double> _all = new();
        private Dictionary<MongoId, double>? _medians;
        private double _overall;

        public bool IsEmpty => _all.Count == 0;

        // The 90th percentile of a kind with at least 10 items, or null.
        public double? Ceiling(MongoId kind)
        {
            if (!_byKind.TryGetValue(kind, out var values) || values.Count < 10)
            {
                return null;
            }

            values.Sort();
            return values[Math.Min(values.Count - 1, values.Count * 9 / 10)];
        }

        public void Add(MongoId kind, double value)
        {
            (_byKind.TryGetValue(kind, out var list) ? list : _byKind[kind] = new List<double>()).Add(value);
            _all.Add(value);
        }

        public double For(MongoId kind)
        {
            if (_medians is null)
            {
                _overall = IsEmpty ? 1.0 : Median(_all);
                _medians = _byKind.Where(entry => entry.Value.Count >= 5).ToDictionary(entry => entry.Key, entry => Median(entry.Value));
            }

            return _medians.GetValueOrDefault(kind, _overall);
        }

        private static double Median(List<double> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }
    }

    private static void PutBackSpt()
    {
        lock (Gate)
        {
            if (_sptPrices is null)
            {
                return;
            }

            ForgetOverrides();
            if (_applied)
            {
                foreach (var (tpl, price) in _sptPrices)
                {
                    _templates!.Prices[tpl] = price;
                }

                _applied = false;
                _indexApplied = false;
            }

            UseOverrides();
        }

        UseTraderCuts(null);
        Flea.LevelLock.Forget();
    }

    // Items with the admin's own base price (Economy tab, Your prices). Inside Gate, last, so nothing else changes them.
    private static readonly HashSet<MongoId> Overridden = new();

    private static void UseOverrides()
    {
        foreach (var (id, price) in _config!.Mod.Economy.PriceOverrides)
        {
            if (price > 0 && MongoId.IsValidMongoId(id) && new MongoId(id) is var tpl && _templates!.Items.ContainsKey(tpl))
            {
                _templates.Prices[tpl] = price;
                Overridden.Add(tpl);
            }
        }
    }

    // Back to SPT's own price (or none), so a removed override doesn't stay.
    private static void ForgetOverrides()
    {
        foreach (var tpl in Overridden)
        {
            if (_sptPrices!.TryGetValue(tpl, out var spt))
            {
                _templates!.Prices[tpl] = spt;
            }
            else
            {
                _templates!.Prices.Remove(tpl);
            }
        }

        Overridden.Clear();
    }

    // For the settings page: SPT's own price, the live price (null if the real flea doesn't sell it, or it was too
    // far from SPT's price and the handbook's to trust: LiveIgnored), Better Flea's price, and whether the real game
    // has the item at all. Override: the admin's own base price, if set.
    public record PriceLookup(double? Spt, double? Live, bool LiveIgnored, double? Index, bool BaseGame, double? Override);

    public static PriceLookup Lookup(MongoId tpl)
    {
        _cache ??= DataFiles.Read<Cache>(CacheFile);
        var id = tpl.ToString();
        var spt = _sptPrices?.GetValueOrDefault(tpl);
        double? live = _cache?.Prices.TryGetValue(id, out var price) == true ? price : null;
        var ignored = live is { } value && !Plausible(tpl, spt ?? 0, value);
        double? index = Index.Value?.Prices.TryGetValue(id, out var fixedPrice) == true
            && (IndexSettings.ContentBackport || Index.Value.Backport?.Contains(id) != true) ? fixedPrice : null;
        var baseGame = _cache?.Known?.Contains(id) == true || Index.Value?.Known?.Contains(id) == true;
        double? own = _config?.Mod.Economy.PriceOverrides.TryGetValue(id, out var set) == true && set > 0 ? set : null;
        return new PriceLookup(spt, ignored ? null : live, ignored, index, baseGame, own);
    }

    // The items whose live price (or Better Flea's, with index) is furthest from SPT's, each way: (item, SPT price,
    // that price).
    public static List<(MongoId Tpl, double Spt, double Live)> Differences(int each, bool index = false)
    {
        _cache ??= DataFiles.Read<Cache>(CacheFile);
        var cache = index ? Index.Value : _cache;
        if (cache is null || _sptPrices is null)
        {
            return [];
        }

        var all = cache.Prices
            .Where(entry => MongoId.IsValidMongoId(entry.Key))
            .Select(entry => (Tpl: new MongoId(entry.Key), Live: entry.Value))
            .Where(entry => _sptPrices.GetValueOrDefault(entry.Tpl) >= 3000 && (index || Plausible(entry.Tpl, _sptPrices[entry.Tpl], entry.Live)))
            .Select(entry => (entry.Tpl, Spt: _sptPrices[entry.Tpl], entry.Live))
            .ToList();
        return all.OrderByDescending(entry => entry.Live / entry.Spt).Take(each)
            .Concat(all.OrderBy(entry => entry.Live / entry.Spt).Take(each)).ToList();
    }

    // For the settings page.
    public static (bool On, int Prices, long Fetched, string[] Modes, string? Error, int Estimated, bool Fallback) Status()
    {
        _cache ??= DataFiles.Read<Cache>(CacheFile);
        var live = _applied && !_indexApplied;
        return (live, _cache?.Prices.Count ?? 0, _cache?.Fetched ?? 0, _cache?.Modes ?? [], _error, live ? _estimated : 0, _fallback);
    }

    // In the real game (the price index knows it, content backports included), so not from a mod.
    public static bool IsBaseGame(MongoId tpl) => BaseGameItems.Value.Contains(tpl.ToString());

    private static readonly Lazy<HashSet<string>> BaseGameItems = new(() => Index.Value?.Known?.ToHashSet() ?? []);

    public static (bool On, int Prices, long Built, int Estimated) IndexStatus()
    {
        var on = _applied && _indexApplied && !_fallback;
        return (on, Index.Value?.Prices.Count ?? 0, Index.Value?.Fetched ?? 0, on ? _estimated : 0);
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
