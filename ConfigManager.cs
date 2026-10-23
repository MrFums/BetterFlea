using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Utils;
using BetterFlea.Flea;

namespace BetterFlea;

[Injectable(InjectionType.Singleton)]
public class ConfigManager(
    ISptLogger<ConfigManager> logger,
    ModHelper modHelper,
    JsonUtil jsonUtil)
{
    private const string ConfigFolder = "configs";

    private static readonly string[] ConfigFiles =
        ["flea.jsonc", "listings.jsonc", "condition.jsonc", "weapons.jsonc", "armor.jsonc", "market.jsonc", "prices.jsonc", "selling.jsonc", "debug.jsonc"];

    // Single settings file from older versions, ported once (see PortOldConfig).
    private const string OldConfigFile = "config.jsonc";

    public ModConfig Mod { get; private set; } = new();

    public string ModPath { get; private set; } = string.Empty;

    public void Load()
    {
        ModPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        RestoreMissingConfigs();
        PortOldConfig();
        Mod = LoadSplit();
        Validate();
        DebugLog.BindSummaries(this, message => logger.Info(message));
        DataFiles.Bind(ModPath, jsonUtil, message => logger.Warning(message));
    }

    private ModConfig LoadSplit()
    {
        var flea = LoadOrDefault<FleaConfig>(InFolder("flea.jsonc"));
        var listings = LoadOrDefault<ListingsFile>(InFolder("listings.jsonc"));
        var condition = LoadOrDefault<Offers.UsedListingsConfig>(InFolder("condition.jsonc"));
        var weapons = LoadOrDefault<WeaponsFile>(InFolder("weapons.jsonc"));
        var armor = LoadOrDefault<Offers.ArmorOffersConfig>(InFolder("armor.jsonc"));
        var market = LoadOrDefault<MarketFile>(InFolder("market.jsonc"));
        var prices = LoadOrDefault<Prices.PricesConfig>(InFolder("prices.jsonc"));
        var selling = LoadOrDefault<Selling.PlayerSellingConfig>(InFolder("selling.jsonc"));
        var debug = LoadOrDefault<DebugFile>(InFolder("debug.jsonc"));

        WarnUnknown(flea.Unknown, InFolder("flea.jsonc"), "");
        WarnUnknown(listings.Unknown, InFolder("listings.jsonc"), "");
        WarnUnknown(condition.Unknown, InFolder("condition.jsonc"), "");
        WarnUnknown(weapons.Unknown, InFolder("weapons.jsonc"), "");
        WarnUnknown(weapons.Presets.Unknown, InFolder("weapons.jsonc"), "presets.");
        WarnUnknown(armor.Unknown, InFolder("armor.jsonc"), "");
        WarnUnknown(market.Unknown, InFolder("market.jsonc"), "");
        WarnUnknown(prices.Unknown, InFolder("prices.jsonc"), "");
        WarnUnknown(selling.Unknown, InFolder("selling.jsonc"), "");
        WarnUnknown(debug.Unknown, InFolder("debug.jsonc"), "");

        return new ModConfig
        {
            Flea = flea,
            GeneratedOffers = new Offers.GeneratedOffersConfig
            {
                Listings = listings.Listings,
                ItemsPerOffer = listings.ItemsPerOffer,
                OfferCurrency = listings.OfferCurrency,
                UsedItems = condition,
                Presets = weapons.Presets,
                PlayerGuns = weapons.PlayerGuns,
                Armor = armor,
                OfferExpiry = market.OfferExpiry,
                SimulatedSales = market.SimulatedSales,
                SoldBeforeYouChance = market.SoldBeforeYouChance,
                BarterOffers = market.BarterOffers,
                SellerRating = market.SellerRating,
                Packs = market.Packs
            },
            Prices = prices,
            PlayerSelling = selling,
            DebugLogging = debug.DebugLogging
        };
    }

    private static string InFolder(string file) => Path.Combine(ConfigFolder, file);

    // Ported once, then deleted. A marker left by earlier test builds means it was already ported.
    private const string OldPortedMarker = ".ported-config.jsonc";

    private void PortOldConfig()
    {
        var oldPath = Path.Combine(ModPath, OldConfigFile);
        var markerPath = Path.Combine(ModPath, ConfigFolder, OldPortedMarker);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
                logger.Warning($"[BetterFlea] Deleted {OldConfigFile}: its settings were already copied into the {ConfigFolder} folder.");
            }

            return;
        }

        if (!File.Exists(oldPath))
        {
            return;
        }

        try
        {
            var old = LoadOrDefault<ModConfig>(OldConfigFile);

            old.Flea.Unknown = null;
            old.GeneratedOffers.Presets.Unknown = null;
            old.GeneratedOffers.UsedItems.Unknown = null;
            old.GeneratedOffers.Armor.Unknown = null;
            old.Prices.Unknown = null;
            old.PlayerSelling.Unknown = null;

            var sections = new (string File, object Values, object Defaults)[]
            {
                ("flea.jsonc", old.Flea, new FleaConfig()),
                ("listings.jsonc", new ListingsFile { Listings = old.GeneratedOffers.Listings, ItemsPerOffer = old.GeneratedOffers.ItemsPerOffer, OfferCurrency = old.GeneratedOffers.OfferCurrency }, new ListingsFile()),
                ("condition.jsonc", old.GeneratedOffers.UsedItems, new Offers.UsedListingsConfig()),
                ("weapons.jsonc", new WeaponsFile { Presets = old.GeneratedOffers.Presets, PlayerGuns = old.GeneratedOffers.PlayerGuns }, new WeaponsFile()),
                ("armor.jsonc", old.GeneratedOffers.Armor, new Offers.ArmorOffersConfig()),
                ("market.jsonc", new MarketFile
                {
                    OfferExpiry = old.GeneratedOffers.OfferExpiry, SimulatedSales = old.GeneratedOffers.SimulatedSales,
                    SoldBeforeYouChance = old.GeneratedOffers.SoldBeforeYouChance, BarterOffers = old.GeneratedOffers.BarterOffers,
                    SellerRating = old.GeneratedOffers.SellerRating, Packs = old.GeneratedOffers.Packs
                }, new MarketFile()),
                ("prices.jsonc", old.Prices, new Prices.PricesConfig()),
                ("selling.jsonc", old.PlayerSelling, new Selling.PlayerSellingConfig()),
                ("debug.jsonc", new DebugFile { DebugLogging = old.DebugLogging }, new DebugFile())
            };

            var changed = 0;
            foreach (var (file, values, defaults) in sections)
            {
                var path = Path.Combine(ModPath, ConfigFolder, file);
                var text = File.ReadAllText(path);
                var ported = JsoncPort.Apply(text, JsonSerializer.SerializeToNode(values, values.GetType(), JsonPortOptions),
                    JsonSerializer.SerializeToNode(defaults, defaults.GetType(), JsonPortOptions));
                if (ported != text)
                {
                    File.WriteAllText(path, ported);
                    changed++;
                }
            }

            File.Delete(oldPath);
            logger.Warning($"[BetterFlea] Your settings from {OldConfigFile} were copied into the {ConfigFolder} folder ({changed} file(s) changed) "
                + $"and {OldConfigFile} was deleted. Change settings in the {ConfigFolder} folder from now on.");
        }
        catch (Exception ex)
        {
            logger.Warning($"[BetterFlea] Could not copy the settings from {OldConfigFile} into the {ConfigFolder} folder, so they're not used. "
                + $"Copy any you changed by hand, then delete {OldConfigFile}. ({ex.Message})");
        }
    }

    private static readonly JsonSerializerOptions JsonPortOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private void Validate()
    {
        if (Mod.Prices.MarkedKeyMultiplier <= 0)
        {
            logger.Warning("[BetterFlea] markedKeyMultiplier (configs/prices.jsonc) must be > 0, resetting to 1.0");
            Mod.Prices.MarkedKeyMultiplier = 1.0;
        }

        var listings = Mod.GeneratedOffers.Listings;
        listings.Multiplier = ClampListingMultiplier(listings.Multiplier, "multiplier");
        listings.CategoryMultipliers = new Dictionary<string, double>(listings.CategoryMultipliers, StringComparer.OrdinalIgnoreCase);
        foreach (var name in listings.CategoryMultipliers.Keys.ToList())
        {
            if (!FleaCategories.Names.Contains(name))
            {
                logger.Warning($"[BetterFlea] Unknown category '{name}' in listings.categoryMultipliers (configs/listings.jsonc), ignoring. Use names like 'keys.markedKeys'.");
                listings.CategoryMultipliers.Remove(name);
                continue;
            }

            listings.CategoryMultipliers[name] = ClampListingMultiplier(listings.CategoryMultipliers[name], name);
        }

        listings.RarityMultipliers = new Dictionary<string, double>(listings.RarityMultipliers, StringComparer.OrdinalIgnoreCase);

        var onDemand = listings.OnDemand;
        onDemand.StartShare = Math.Clamp(onDemand.StartShare, 0.0, 1.0);
        onDemand.MinStartOffers = Math.Max(1, onDemand.MinStartOffers);
        onDemand.KeepPages = Math.Max(0, onDemand.KeepPages);
        onDemand.KeepSeconds = Math.Max(10.0, onDemand.KeepSeconds);
        onDemand.MaxExtraOffers = Math.Max(0, onDemand.MaxExtraOffers);
        onDemand.MaxItemsPerSearch = Math.Max(1, onDemand.MaxItemsPerSearch);

        var used = Mod.GeneratedOffers.UsedItems;
        foreach (var settings in new[] { used.Keys, used.MarkedKeys, used.Keycards })
        {
            settings.UsedChance = Math.Clamp(settings.UsedChance, 0.0, 1.0);
            settings.UsesLeftFalloff = Math.Clamp(settings.UsesLeftFalloff, 0.0, 1.0);
            settings.OneUsePricePercent = Math.Clamp(settings.OneUsePricePercent, 1.0, 100.0);
        }

        foreach (var settings in new[] { used.Meds, used.FoodAndDrink, used.Fuel, used.RepairKits })
        {
            settings.UsedChance = Math.Clamp(settings.UsedChance, 0.0, 1.0);
            settings.LowAmountBias = Math.Clamp(settings.LowAmountBias, 0.1, 10.0);
        }

        var delay = Mod.PlayerSelling.SaleDelay;
        delay.MinMinutes = Math.Max(0.0, delay.MinMinutes);
        delay.MaxMinutes = Math.Max(0.0, delay.MaxMinutes);
        delay.NoDelayChance = Math.Clamp(delay.NoDelayChance, 0.0, 1.0);
        delay.ExpensivePercent = Math.Clamp(delay.ExpensivePercent, 10.0, 500.0);
        if (delay.MinMinutes > delay.MaxMinutes)
        {
            logger.Warning("[BetterFlea] saleDelay.minMinutes (configs/selling.jsonc) is above maxMinutes, swapping them.");
            (delay.MinMinutes, delay.MaxMinutes) = (delay.MaxMinutes, delay.MinMinutes);
        }

        var perOffer = Mod.GeneratedOffers.ItemsPerOffer;
        foreach (var profile in new[] { perOffer.Bulk, perOffer.Stock, perOffer.Few })
        {
            profile.Falloff = Math.Clamp(profile.Falloff, 0.0, 1.0);
            profile.Max = Math.Clamp(profile.Max, 1, 100);
        }

        foreach (var rarity in listings.RarityMultipliers.Keys.ToList())
        {
            listings.RarityMultipliers[rarity] = ClampListingMultiplier(listings.RarityMultipliers[rarity], rarity);
        }

        Mod.Prices.Floor.MarginPercent = Math.Clamp(Mod.Prices.Floor.MarginPercent, 0.0, 500.0);

        Mod.GeneratedOffers.Presets.LoadedMagazineChance = Math.Clamp(Mod.GeneratedOffers.Presets.LoadedMagazineChance, 0.0, 1.0);

        var expiry = Mod.GeneratedOffers.OfferExpiry;
        expiry.MinMinutes = Math.Clamp(expiry.MinMinutes, 10, 10080);
        expiry.MaxMinutes = Math.Clamp(expiry.MaxMinutes, expiry.MinMinutes, 10080);

        var rating = Mod.GeneratedOffers.SellerRating;
        rating.Min = Math.Clamp(rating.Min, 0.01, 10000.0);
        rating.Max = Math.Clamp(rating.Max, rating.Min, 10000.0);
        rating.NewSellerChance = Math.Clamp(rating.NewSellerChance, 0.0, 1.0);
        rating.RegularChance = Math.Clamp(rating.RegularChance, 0.0, 1.0);
        rating.FlipperChance = Math.Clamp(rating.FlipperChance, 0.0, 1.0);
        Mod.GeneratedOffers.Armor.FlippedDownChance = Math.Clamp(Mod.GeneratedOffers.Armor.FlippedDownChance, 0.0, 1.0);

        var packs = Mod.GeneratedOffers.Packs;
        packs.ScamChance = Math.Clamp(packs.ScamChance, 0.0, 1.0);
        packs.Max = Math.Min(packs.Max, 50);
        packs.HugeChance = Math.Clamp(packs.HugeChance, 0.0, 1.0);
        packs.HugeMax = Math.Clamp(packs.HugeMax, 1, 200);
        packs.HugeMin = Math.Clamp(packs.HugeMin, 1, packs.HugeMax);
        rating.EverydayMax = Math.Clamp(rating.EverydayMax, rating.Min, rating.Max);

        var sales = Mod.GeneratedOffers.SimulatedSales;
        sales.EveryMinutes = Math.Clamp(sales.EveryMinutes, 1.0, 120.0);
        sales.NormalSellChancePerHour = Math.Clamp(sales.NormalSellChancePerHour, 0.0, 0.95);
        sales.DealSpeedup = Math.Clamp(sales.DealSpeedup, 1.0, 100.0);

        var playerGuns = Mod.GeneratedOffers.PlayerGuns;
        playerGuns.SavedBuildChance = Math.Clamp(playerGuns.SavedBuildChance, 0.0, 1.0);
        playerGuns.LostGunChance = Math.Clamp(playerGuns.LostGunChance, 0.0, 1.0);
        playerGuns.ScopeStripChance = Math.Clamp(playerGuns.ScopeStripChance, 0.0, 1.0);
        playerGuns.SuppressorStripChance = Math.Clamp(playerGuns.SuppressorStripChance, 0.0, 1.0);
        playerGuns.LostGunMinMinutes = Math.Max(0.0, playerGuns.LostGunMinMinutes);
        playerGuns.LostGunMaxMinutes = Math.Max(playerGuns.LostGunMinMinutes, playerGuns.LostGunMaxMinutes);
        foreach (var bulk in new[] { Mod.GeneratedOffers.ItemsPerOffer.GrenadeBulk, Mod.GeneratedOffers.ItemsPerOffer.StimBulk })
        {
            bulk.Chance = Math.Clamp(bulk.Chance, 0.0, 1.0);
            bulk.Min = Math.Clamp(bulk.Min, 1, 100);
            bulk.Max = Math.Clamp(bulk.Max, bulk.Min, 100);
        }

        var sellers = Mod.Prices.Sellers;
        sellers.OverpriceChance = Math.Clamp(sellers.OverpriceChance, 0.0, 1.0);
        sellers.WornOverpriceChance = Math.Clamp(sellers.WornOverpriceChance, 0.0, 1.0);
        sellers.QuickSellChance = Math.Clamp(sellers.QuickSellChance, 0.0, 1.0);
        sellers.OverpriceMinPercent = Math.Clamp(sellers.OverpriceMinPercent, 0.0, 80.0);
        sellers.OverpriceMaxPercent = Math.Clamp(sellers.OverpriceMaxPercent, sellers.OverpriceMinPercent, 80.0);
        sellers.QuickSellMinPercent = Math.Clamp(sellers.QuickSellMinPercent, 0.0, 90.0);
        sellers.QuickSellMaxPercent = Math.Clamp(sellers.QuickSellMaxPercent, sellers.QuickSellMinPercent, 90.0);
        Mod.Prices.Guns.TraderPricePercent = Math.Clamp(Mod.Prices.Guns.TraderPricePercent, 10.0, 200.0);

        var drift = Mod.Prices.Drift;
        drift.MaxDriftPercent = Math.Clamp(drift.MaxDriftPercent, 0.0, 90.0);
        drift.StepPercent = Math.Clamp(drift.StepPercent, 0.0, drift.MaxDriftPercent);
        drift.UpdateMinutes = Math.Max(1.0, drift.UpdateMinutes);

        var flip = Mod.PlayerSelling.FlipPenalty;
        flip.WindowMinutes = Math.Max(1.0, flip.WindowMinutes);
        flip.FreePurchases = Math.Max(0.0, flip.FreePurchases);
        flip.MaxExtraMinutes = Math.Max(0.0, flip.MaxExtraMinutes);
        flip.FenceMultiplier = Math.Max(0.0, flip.FenceMultiplier);        if (flip.PurchasesForMaxPenalty <= flip.FreePurchases)
        {
            logger.Warning("[BetterFlea] flipPenalty.purchasesForMaxPenalty (configs/selling.jsonc) must be above freePurchases, using freePurchases + 1.");
            flip.PurchasesForMaxPenalty = flip.FreePurchases + 1;
        }
    }

    private void WarnUnknown(Dictionary<string, System.Text.Json.JsonElement>? unknown, string file, string section)
    {
        if (unknown is not { Count: > 0 })
        {
            return;
        }

        var names = string.Join(", ", unknown.Keys.Select(key => section + key));
        logger.Warning($"[BetterFlea] Unknown setting(s) in {file}, ignored: {names}. Check the spelling, or compare with the file that ships with the mod.");
    }

    // Capped so a typo can't eat the server's RAM.
    private double ClampListingMultiplier(double value, string name)
    {
        const double max = 10.0;
        if (value < 0 || value > max)
        {
            logger.Warning($"[BetterFlea] Listing multiplier '{name}' must be between 0 and {max}, clamping.");
        }

        return Math.Clamp(value, 0.0, max);
    }

    // Write back any deleted file from the copy in the DLL. Existing files are never touched.
    private void RestoreMissingConfigs()
    {
        foreach (var file in ConfigFiles)
        {
            var path = Path.Combine(ModPath, ConfigFolder, file);
            if (File.Exists(path))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.Combine(ModPath, ConfigFolder));
                using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream($"BetterFlea.configs.{file}")
                    ?? throw new InvalidOperationException("the default is missing from the DLL");
                using var target = File.Create(path);
                source.CopyTo(target);
                logger.Info($"[BetterFlea] {ConfigFolder}/{file} was missing, so a new one was made with the default settings.");
            }
            catch (Exception ex)
            {
                logger.Warning($"[BetterFlea] {ConfigFolder}/{file} is missing and a new one could not be made, using defaults. ({ex.Message})");
            }
        }
    }

    private T LoadOrDefault<T>(string relativeFile) where T : new()
    {
        try
        {
            return modHelper.GetJsonDataFromFile<T>(ModPath, relativeFile) ?? new T();
        }
        catch (Exception ex)
        {
            logger.Warning($"[BetterFlea] Could not read '{relativeFile}', using defaults. ({ex.Message})");
            return new T();
        }
    }
}

// Writes values into a .jsonc file's text, keeping its notes and layout. Tables that differ are
// replaced whole; settings the file doesn't have are added only when they aren't at their default.
internal static class JsoncPort
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string Apply(string text, JsonNode? values, JsonNode? defaults)
    {
        var edits = new List<(int Start, int End, string Text)>();
        var i = Skip(text, 0);
        if (i < text.Length && text[i] == '{' && values is JsonObject root)
        {
            Section(text, i, root, defaults as JsonObject, edits);
        }

        var result = new System.Text.StringBuilder(text);
        foreach (var (start, end, replacement) in edits.OrderByDescending(edit => edit.Start))
        {
            result.Remove(start, end - start).Insert(start, replacement);
        }

        return result.ToString();
    }

    private static void Section(string text, int open, JsonObject values, JsonObject? defaults, List<(int, int, string)> edits)
    {
        var seen = new HashSet<string>();
        string? indent = null;
        var i = open + 1;
        while (true)
        {
            i = Skip(text, i);
            if (i >= text.Length || text[i] == '}')
            {
                break;
            }

            if (text[i] == ',')
            {
                i++;
                continue;
            }

            indent ??= IndentAt(text, i);
            var keyEnd = ValueEnd(text, i);
            var key = JsonNode.Parse(text[i..keyEnd])!.GetValue<string>();
            i = Skip(text, keyEnd) + 1;
            var start = Skip(text, i);
            var end = ValueEnd(text, start);
            i = end;
            seen.Add(key);
            if (!values.TryGetPropertyValue(key, out var wanted))
            {
                continue;
            }

            var current = JsonNode.Parse(text[start..end], documentOptions: Lenient);
            if (current is JsonObject currentSection && wanted is JsonObject wantedSection
                && currentSection.All(entry => wantedSection.ContainsKey(entry.Key)))
            {
                Section(text, start, wantedSection, defaults?[key] as JsonObject, edits);
            }
            else if (!Same(current, wanted))
            {
                edits.Add((start, end, wanted?.ToJsonString() ?? "null"));
            }
        }

        var added = values.Where(entry => !seen.Contains(entry.Key) && !Same(entry.Value, defaults?[entry.Key]))
            .Select(entry => $"\n{indent ?? IndentAt(text, open) + "  "}\"{entry.Key}\": {entry.Value?.ToJsonString() ?? "null"},")
            .ToList();
        if (added.Count > 0)
        {
            edits.Add((open + 1, open + 1, string.Concat(added)));
        }
    }

    private static bool Same(JsonNode? a, JsonNode? b) => (a, b) switch
    {
        (null, null) => true,
        (JsonObject x, JsonObject y) => x.Count == y.Count && x.All(entry => y.TryGetPropertyValue(entry.Key, out var other) && Same(entry.Value, other)),
        (JsonArray x, JsonArray y) => x.Count == y.Count && x.Zip(y).All(pair => Same(pair.First, pair.Second)),
        (JsonValue x, JsonValue y) when x.TryGetValue<double>(out var dx) && y.TryGetValue<double>(out var dy) => Math.Abs(dx - dy) < 1e-9,
        _ => a?.ToJsonString() == b?.ToJsonString()
    };

    private static int Skip(string text, int i)
    {
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                i = text.IndexOf('\n', i) is var line and >= 0 ? line + 1 : text.Length;
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i = text.IndexOf("*/", i + 2, StringComparison.Ordinal) is var close and >= 0 ? close + 2 : text.Length;
            }
            else
            {
                break;
            }
        }

        return i;
    }

    private static int ValueEnd(string text, int i)
    {
        if (text[i] == '"')
        {
            for (i++; i < text.Length && text[i] != '"'; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                }
            }

            return i + 1;
        }

        if (text[i] is '{' or '[')
        {
            var depth = 0;
            while (i < text.Length)
            {
                i = Skip(text, i);
                if (text[i] == '"')
                {
                    i = ValueEnd(text, i);
                    continue;
                }

                depth += text[i] is '{' or '[' ? 1 : text[i] is '}' or ']' ? -1 : 0;
                i++;
                if (depth == 0)
                {
                    return i;
                }
            }

            return i;
        }

        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not (',' or '}' or ']' or '/'))
        {
            i++;
        }

        return i;
    }

    private static string IndentAt(string text, int i)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, i - 1)) + 1;
        return new string(' ', text[lineStart..i].TakeWhile(c => c == ' ').Count());
    }
}

// debugLogging output. Summary lines go live once the config is read; Write is only bound after
// startup so the startup offers don't flood the log.
internal static class DebugLog
{
    private static Action<string>? _summary;
    private static Action<string>? _write;

    public static void BindSummaries(ConfigManager configManager, Action<string> write)
    {
        _summary = configManager.Mod.DebugLogging ? write : null;
    }

    public static void Bind(ConfigManager configManager, Action<string> write)
    {
        _write = configManager.Mod.DebugLogging ? write : null;
    }

    public static void Summary(Func<string> message)
    {
        _summary?.Invoke("[BetterFlea] " + message());
    }

    public static void Write(Func<string> message)
    {
        _write?.Invoke("[BetterFlea] " + message());
    }
}

// Files in data/ that last through a restart. Temp file first so a crash can't leave half a file.
// Each caller keeps its own file small.
internal static class DataFiles
{
    private static string _folder = string.Empty;
    private static JsonUtil? _json;
    private static Action<string>? _warn;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Locks = new();

    public static void Bind(string modPath, JsonUtil json, Action<string> warn)
    {
        _folder = Path.Combine(modPath, "data");
        _json = json;
        _warn = warn;
    }

    public static T? Read<T>(string name) where T : class
    {
        var path = Path.Combine(_folder, name);
        if (_json is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return _json.Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            _warn?.Invoke($"[BetterFlea] Could not read data/{name}, starting without it. ({ex.Message})");
            return null;
        }
    }

    public static void Write<T>(string name, T data)
    {
        if (_json is null || string.IsNullOrEmpty(_folder))
        {
            return;
        }

        var path = Path.Combine(_folder, name);
        lock (Locks.GetOrAdd(name, _ => new object()))
        {
            try
            {
                Directory.CreateDirectory(_folder);
                var temp = path + ".tmp";
                File.WriteAllText(temp, _json.Serialize(data));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                _warn?.Invoke($"[BetterFlea] Could not save data/{name}. ({ex.Message})");
            }
        }
    }
}