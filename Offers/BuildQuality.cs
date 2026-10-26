using System.Collections.Concurrent;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;

namespace BetterFlea.Offers;

// Budget = the most ergo and recoil for the money from cheap trader parts.
internal enum BuildStyle
{
    Ergo,
    Hybrid,
    Recoil,
    Budget
}

// How players judge parts: ergo, recoil, how easy a part is to get, and for optics which ones people like.
internal static class BuildQuality
{
    private static ItemHelper? _itemHelper;
    private static RagfairPriceService? _prices;
    private static TradersTable? _traders;
    private static SPTarkov.Server.Core.Helpers.Profile.HandbookHelper? _handbook;

    private static readonly MongoId FenceId = new("579dc571d53a0658a154fbec");

    public static void Bind(ItemHelper itemHelper, RagfairPriceService prices, TradersTable traders, SPTarkov.Server.Core.Helpers.Profile.HandbookHelper handbook)
    {
        _itemHelper = itemHelper;
        _prices = prices;
        _traders = traders;
        _handbook = handbook;
    }

    public static double HandbookPrice(MongoId tpl) => _handbook!.GetTemplatePrice(tpl);

    public static double FromRoubles(double roubles, MongoId currency) =>
        currency == Money.ROUBLES ? roubles : _handbook!.FromRoubles(roubles, currency);

    // PK-06, MRS, OKP-7, 553 and EXPS3 are all under this.
    private const double BudgetOpticPrice = 27_000;

    // No handbook price (modded items) means unknown, not free.
    public static bool IsBudgetOptic(MongoId tpl) =>
        HandbookPrice(tpl) is > 0 and <= BudgetOpticPrice || IsCheapTraderPart(tpl);

    // The cheapest 40% of what fits, plus cheap trader parts no dearer than the middle one.
    public static List<MongoId> CheapEnd(List<MongoId> fitting)
    {
        var prices = fitting.Select(HandbookPrice).Where(price => price > 0).OrderBy(price => price).ToList();
        if (fitting.Count <= 2 || prices.Count == 0)
        {
            return fitting;
        }

        var cheap = prices[(int)((prices.Count - 1) * 0.4)];
        var middle = prices[(prices.Count - 1) / 2];
        return fitting.Where(tpl => HandbookPrice(tpl) is var price && price > 0
            && (price <= cheap || IsCheapTraderPart(tpl) && price <= middle)).ToList();
    }

    public static (double Ergo, double Recoil) StyleWeights(BuildStyle style) => style switch
    {
        BuildStyle.Ergo => (0.85, 0.15),
        BuildStyle.Recoil => (0.15, 0.85),
        _ => (0.5, 0.5)
    };

    public static BuildStyle PickStyle(BuildStylesConfig styles)
    {
        var options = new[] { (BuildStyle.Ergo, styles.Ergo), (BuildStyle.Hybrid, styles.Hybrid), (BuildStyle.Recoil, styles.Recoil) };
        var total = options.Sum(option => Math.Max(0, option.Item2));
        var roll = Random.Shared.NextDouble() * total;
        foreach (var (style, weight) in options)
        {
            roll -= Math.Max(0, weight);
            if (roll < 0)
            {
                return style;
            }
        }

        return BuildStyle.Hybrid;
    }

    public static double Ergo(MongoId tpl) => _itemHelper!.GetItem(tpl).Value?.Properties?.Ergonomics ?? 0;

    public static double RecoilPercent(MongoId tpl) => _itemHelper!.GetItem(tpl).Value?.Properties?.Recoil ?? 0;

    public static double Price(MongoId tpl) => _prices!.GetFleaPriceForItem(tpl);

    public static (double Ergo, double RecoilPercent, double Vertical, double Horizontal) Stats(List<Item> built)
    {
        var gun = _itemHelper!.GetItem(built[0].Template).Value?.Properties;
        var ergo = (gun?.Ergonomics ?? 0) + built.Skip(1).Sum(item => Ergo(item.Template));
        var recoil = built.Skip(1).Sum(item => RecoilPercent(item.Template));
        var factor = Math.Max(0.05, 1 + recoil / 100);
        return (ergo, recoil, (gun?.RecoilForceUp ?? 0) * factor, (gun?.RecoilForceBack ?? 0) * factor);
    }

    // Ergo points and recoil % are on similar scales, so they're weighted directly.
    // Budget builds also lose a point per 20k roubles.
    public static double Score(List<Item> built, BuildStyle style)
    {
        var (ergo, recoil, _, _) = Stats(built);
        var (wErgo, wRecoil) = StyleWeights(style);
        var cost = style == BuildStyle.Budget ? built.Skip(1).Sum(item => Price(item.Template)) / 20000 : 0;
        return wErgo * ergo - wRecoil * recoil - cost;
    }

    // Best first. Fence doesn't count.
    public enum Source
    {
        TraderCash,
        TraderBarter,
        QuestLocked,
        NotSold
    }

    private static readonly Lazy<Dictionary<MongoId, (Source Source, int Level)>> Sources = new(LoadSources);

    private static Dictionary<MongoId, (Source Source, int Level)> LoadSources()
    {
        var result = new Dictionary<MongoId, (Source Source, int Level)>();
        foreach (var (traderId, trader) in _traders!)
        {
            var assort = trader.Assort;
            if (traderId == FenceId || assort?.Items is null)
            {
                continue;
            }

            var questLocked = trader.QuestAssort?.Values.SelectMany(group => group.Keys).ToHashSet() ?? [];
            foreach (var item in assort.Items.Where(item => string.Equals(item.ParentId, "hideout", StringComparison.OrdinalIgnoreCase)))
            {
                var schemes = assort.BarterScheme is not null && assort.BarterScheme.TryGetValue(item.Id, out var found) ? found : [];
                var cash = schemes.Any(scheme => scheme.All(cost => _itemHelper!.IsOfBaseclass(cost.Template, BaseClasses.MONEY)));
                var source = questLocked.Contains(item.Id) ? Source.QuestLocked : cash ? Source.TraderCash : Source.TraderBarter;
                var level = assort.LoyalLevelItems is not null && assort.LoyalLevelItems.TryGetValue(item.Id, out var loyalty) ? loyalty : 1;
                if (!result.TryGetValue(item.Template, out var known) || (source, level).CompareTo((known.Source, known.Level)) < 0)
                {
                    result[item.Template] = (source, level);
                }
            }
        }

        return result;
    }

    public static Source SourceOf(MongoId tpl) => Sources.Value.TryGetValue(tpl, out var found) ? found.Source : Source.NotSold;

    public static bool IsCheapTraderPart(MongoId tpl) =>
        Sources.Value.TryGetValue(tpl, out var found) && found.Source == Source.TraderCash && found.Level <= 2;

    private static double SourceFactor(MongoId tpl, PartPreferences prefs, double power) =>
        prefs.Style == BuildStyle.Budget ? (IsCheapTraderPart(tpl) ? 1 : 0.6) : Math.Pow(Availability(tpl), power);

    private static double Availability(MongoId tpl) => SourceOf(tpl) switch
    {
        Source.TraderCash => 1.0,
        Source.TraderBarter => 0.85,
        Source.QuestLocked => 0.6,
        _ => 0.45
    };

    // From the item name (_fde, _blk) or the colour in brackets in its English name.
    // red/white/yellow are "odd". Unmarked parts are null and go with anything.
    private static readonly ConcurrentDictionary<MongoId, string?> Colours = new();

    public const string OddColour = "odd";

    public static string? ColourOf(MongoId tpl) => Colours.GetOrAdd(tpl, id =>
    {
        var words = (_itemHelper!.GetItem(id).Value?.Name ?? "").Split('_').ToList();
        if (_englishNames?.TryGetValue($"{id} Name", out var display) == true
            && System.Text.RegularExpressions.Regex.Match(display, @"\(([^)]+)\)\s*$") is { Success: true } bracket)
        {
            words.AddRange(bracket.Groups[1].Value.ToLowerInvariant().Split(' '));
        }

        foreach (var word in words)
        {
            switch (word)
            {
                case "fde" or "tan" or "coyote" or "sand" or "snd" or "khk" or "khaki":
                    return "tan";
                case "blk" or "black":
                    return "black";
                case "od" or "olive":
                    return "olive";
                case "plum" or "plm":
                    return "plum";
                case "gry" or "grey" or "gray":
                    return "grey";
                case "red" or "wht" or "white" or "yellow":
                    return OddColour;
            }
        }

        return null;
    });

    public static string? BuildColourFrom(MongoId tpl) => ColourOf(tpl) is { } colour && colour != OddColour ? colour : null;

    // Locales load lazily, so read on first use.
    private static Lazy<Dictionary<string, string>?> _names = new(() => null);

    private static Dictionary<string, string>? _englishNames => _names.Value;

    public static void BindNames(Func<Dictionary<string, string>?> englishNames) =>
        _names = new Lazy<Dictionary<string, string>?>(() =>
        {
            try
            {
                return englishNames();
            }
            catch (Exception)
            {
                return null;
            }
        });

    private static double ColourFactor(MongoId tpl, string? buildColour, double colourMatch)
    {
        if (colourMatch <= 0 || ColourOf(tpl) is not { } colour)
        {
            return 1;
        }

        if (colour == OddColour)
        {
            return Math.Max(0.02, 1 - 0.95 * colourMatch);
        }

        if (buildColour is null)
        {
            return 1;
        }

        return colour == buildColour ? 1 + 3 * colourMatch : Math.Max(0.02, 1 - 0.97 * colourMatch);
    }

    public static double PartFactor(MongoId tpl, PartPreferences prefs) =>
        SourceFactor(tpl, prefs, prefs.StatFocus / 2) * ColourFactor(tpl, prefs.Colour, prefs.ColourMatch);

    public sealed record PartPreferences(BuildStyle Style, double StatFocus, double ColourMatch, string? Colour);

    public static double[] Weights(IReadOnlyList<MongoId> fitting, PartPreferences prefs, Func<MongoId, double>? extra = null)
    {
        var weights = new double[fitting.Count];
        if (fitting.Count == 0)
        {
            return weights;
        }

        var ergos = fitting.Select(Ergo).ToArray();
        var recoils = fitting.Select(RecoilPercent).ToArray();
        var prices = fitting.Select(Price).ToArray();
        var (wErgo, wRecoil) = StyleWeights(prefs.Style);
        var ergoMin = ergos.Min();
        var ergoRange = ergos.Max() - ergoMin;
        var recoilMax = recoils.Max();
        var recoilRange = recoilMax - recoils.Min();
        var sortedPrices = prices.OrderBy(price => price).ToArray();
        var cheapBias = prefs.Style == BuildStyle.Budget ? 1.5 : 1.5 * Math.Max(0, 1 - prefs.StatFocus);

        for (var i = 0; i < fitting.Count; i++)
        {
            var ergoScore = ergoRange > 0 ? (ergos[i] - ergoMin) / ergoRange : 0.5;
            var recoilScore = recoilRange > 0 ? (recoilMax - recoils[i]) / recoilRange : 0.5;
            var styleScore = wErgo * ergoScore + wRecoil * recoilScore;
            var pricePercentile = fitting.Count > 1 ? (double)Array.IndexOf(sortedPrices, prices[i]) / (fitting.Count - 1) : 0;

            weights[i] = Math.Exp(2 * prefs.StatFocus * (styleScore - 1))
                * SourceFactor(fitting[i], prefs, prefs.StatFocus / 2)
                * Math.Exp(-cheapBias * pricePercentile)
                * ColourFactor(fitting[i], prefs.Colour, prefs.ColourMatch)
                * (extra?.Invoke(fitting[i]) ?? 1);
        }

        return weights;
    }

    public static MongoId? Pick(IReadOnlyList<MongoId> options, double[] weights)
    {
        var total = weights.Sum();
        if (options.Count == 0)
        {
            return null;
        }

        if (total <= 0)
        {
            return options[Random.Shared.Next(options.Count)];
        }

        var roll = Random.Shared.NextDouble() * total;
        for (var i = 0; i < options.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0)
            {
                return options[i];
            }
        }

        return options[^1];
    }

    public enum OpticKind
    {
        Lpvo,
        Specter,
        Holo,
        Hhs,
        Dot,
        Compact,
        Prism,
        Pk120,
        Fixed,
        Sniper,
        Special,
        Unique
    }

    // Rating 0-1 by name, kind, and whether it's a budget pick.
    private static readonly (string Key, double Rating, OpticKind Kind, bool Budget)[] OpticRules =
    [
        // LPVOs
        ("vudu_1_6", 1.0, OpticKind.Lpvo, false), ("tango6t", 0.97, OpticKind.Lpvo, false), ("razor_hd_gen_2_1_6", 0.85, OpticKind.Lpvo, false),
        ("pm_ii_1_8", 0.8, OpticKind.Lpvo, false), ("valday_ps320", 0.55, OpticKind.Lpvo, true), ("tac30", 0.3, OpticKind.Lpvo, false),

        ("specter_dr", 0.7, OpticKind.Specter, false),

        // Holos
        ("eotech_xps3", 0.7, OpticKind.Holo, false), ("eotech_exps3", 0.6, OpticKind.Holo, true), ("eotech_553", 0.6, OpticKind.Holo, true),
        ("eotech_hhs_1", 0.6, OpticKind.Hhs, false), ("razor_amg_uh-1", 0.15, OpticKind.Holo, false),

        // Red dots
        ("specter_hco", 0.85, OpticKind.Dot, false), ("romeo_8t", 0.65, OpticKind.Dot, false), ("comp_m4", 0.6, OpticKind.Dot, false),
        ("aimpoint_pro", 0.6, OpticKind.Dot, false), ("romeo7", 0.55, OpticKind.Dot, false), ("holosun_hs401g5", 0.45, OpticKind.Dot, false),
        ("walther_mrs", 0.45, OpticKind.Dot, true), ("falke", 0.3, OpticKind.Dot, false), ("p1x42", 0.25, OpticKind.Dot, false),
        ("milkor", 0.2, OpticKind.Dot, false), ("target_ring", 0.2, OpticKind.Dot, false), ("wilcox_boss", 0.15, OpticKind.Dot, false),
        ("trijicon_srs", 0.15, OpticKind.Dot, false), ("utg_38", 0.1, OpticKind.Dot, false),

        // Russian reflex sights. PK-120 is a shotgun thing.
        ("okp7", 0.55, OpticKind.Dot, true), ("1p87", 0.5, OpticKind.Pk120, true), ("ekp_8_02", 0.45, OpticKind.Dot, true),
        ("pk_06", 0.45, OpticKind.Dot, true), ("krechet", 0.4, OpticKind.Dot, true), ("ekp_1s_03", 0.4, OpticKind.Dot, true),
        ("pk_aa", 0.4, OpticKind.Dot, true), ("kp_sr2", 0.4, OpticKind.Dot, true), ("npz_pk_1", 0.35, OpticKind.Dot, true),
        ("komz_rusak", 0.3, OpticKind.Dot, true), ("ekp_8_18", 0.1, OpticKind.Dot, true),

        // Small dots for pistols and backups
        ("micro_t1", 0.7, OpticKind.Compact, false), ("trijicon_rmr", 0.7, OpticKind.Compact, false), ("acro_p1", 0.65, OpticKind.Compact, false),
        ("micro_h2", 0.65, OpticKind.Compact, false), ("deltapoint", 0.6, OpticKind.Compact, false), ("romeo_4", 0.6, OpticKind.Compact, false),
        ("fast_fire", 0.5, OpticKind.Compact, false), ("swampfox_justice", 0.45, OpticKind.Compact, false),

        // Prisms
        ("monstrum", 0.75, OpticKind.Prism, false), ("kiba_compact_prism", 0.4, OpticKind.Prism, false),

        // Fixed power, rarely used (no 1x)
        ("acog_ta01", 0.5, OpticKind.Fixed, false), ("hamr", 0.5, OpticKind.Fixed, false), ("acog_ta11", 0.45, OpticKind.Fixed, false),
        ("spectre_os4x", 0.35, OpticKind.Fixed, false), ("trihawk", 0.3, OpticKind.Fixed, false), ("bravo4", 0.3, OpticKind.Fixed, false),
        ("pilad_4x32", 0.25, OpticKind.Fixed, false), ("ncstar_advance_dual", 0.1, OpticKind.Fixed, false),
        ("pso_1", 0.45, OpticKind.Fixed, true), ("1p59", 0.4, OpticKind.Fixed, true), ("1p69", 0.4, OpticKind.Fixed, true),
        ("1p29", 0.35, OpticKind.Fixed, true), ("1p78", 0.35, OpticKind.Fixed, true), ("pu_3", 0.35, OpticKind.Fixed, true),

        // Sniper scopes, bolt-actions only
        ("pm_ii_5_25", 0.9, OpticKind.Sniper, false), ("mark_5hd", 0.9, OpticKind.Sniper, false), ("atacr", 0.85, OpticKind.Sniper, false),
        ("pm_ii_3_12", 0.85, OpticKind.Sniper, false), ("hensoldt_zf_4_16", 0.8, OpticKind.Sniper, false), ("march_tactical", 0.8, OpticKind.Sniper, false),
        ("mark4_lr", 0.7, OpticKind.Sniper, false), ("nightforce_nxs", 0.3, OpticKind.Sniper, false),

        // Thermals
        ("flir_rs32", 0.4, OpticKind.Special, false), ("echo1", 0.2, OpticKind.Special, false), ("torrey_pines_logic_t12", 0.1, OpticKind.Special, false),

        // Gun-specific (carry handles, built-in)
        ("hensoldt_hkv_red_dot", 0.4, OpticKind.Unique, false), ("hkv", 0.5, OpticKind.Unique, false), ("stg77", 0.5, OpticKind.Unique, false),
        ("steyr_rail_optic", 0.5, OpticKind.Unique, false), ("p90_fn_ring", 0.55, OpticKind.Unique, false)
    ];

    private static readonly MongoId SpecialScopeBase = new("55818aeb4bdc2ddc698b456a");

    private static readonly ConcurrentDictionary<MongoId, (double Rating, OpticKind Kind, bool Budget)> OpticRatings = new();

    public static (double Rating, OpticKind Kind, bool Budget) OpticRating(MongoId tpl) => OpticRatings.GetOrAdd(tpl, id =>
    {
        var name = _itemHelper!.GetItem(id).Value?.Name ?? "";
        foreach (var (key, rating, kind, budget) in OpticRules)
        {
            if (name.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return (rating, kind, budget);
            }
        }

        return OpticRatingFromStats(id);
    });

    public static bool IsKnownOptic(MongoId tpl)
    {
        var name = _itemHelper!.GetItem(tpl).Value?.Name ?? "";
        return OpticRules.Any(rule => name.Contains(rule.Key, StringComparison.OrdinalIgnoreCase));
    }

    // Unknown optics get a kind from type and zoom and a middling rating, so a modded 1-8x counts as an LPVO.
    private static (double Rating, OpticKind Kind, bool Budget) OpticRatingFromStats(MongoId id)
    {
        if (_itemHelper!.IsOfBaseclass(id, SpecialScopeBase))
        {
            return (0.2, OpticKind.Special, false);
        }

        var budget = IsBudgetOptic(id);
        if (_itemHelper.IsOfBaseclass(id, CompactCollimatorBase))
        {
            return (0.5, OpticKind.Compact, budget);
        }

        var low = LowestZoom(id);
        var high = _itemHelper.GetItem(id).Value?.Properties?.Zooms?.SelectMany(mode => mode).DefaultIfEmpty(1).Max() ?? 1;
        return low switch
        {
            >= 2 when high >= 10 => (0.6, OpticKind.Sniper, false),
            <= 1.01 when high >= 4 => (0.5, OpticKind.Lpvo, budget),
            > 1.01 => (0.4, OpticKind.Fixed, budget),
            _ when high > 1.01 => (0.5, OpticKind.Holo, budget),
            _ => (0.45, OpticKind.Dot, budget)
        };
    }

    public enum GunGroup
    {
        Pistol,
        Smg,
        Shotgun,
        BigBore,
        Rifle,
        Sniper,
        Other
    }

    private static readonly MongoId PistolBase = new("5447b5cf4bdc2d65278b4567");
    private static readonly MongoId RevolverBase = new("617f1ef5e8b54b0998387733");
    private static readonly MongoId SmgBase = new("5447b5e04bdc2d62278b4567");
    private static readonly MongoId ShotgunBase = new("5447b6094bdc2dc3278b4567");
    private static readonly MongoId MarksmanBase = new("5447b6194bdc2d67278b4567");
    private static readonly MongoId SniperBase = new("5447b6254bdc2dc3278b4568");
    private static readonly MongoId AssaultRifleBase = new("5447b5f14bdc2d61278b4567");
    private static readonly MongoId AssaultCarbineBase = new("5447b5fc4bdc2d87278b4567");
    private static readonly MongoId MachineGunBase = new("5447bed64bdc2d97278b4568");

    // Guns that usually wear a 1-6x. Anything not listed is judged by size (IsBigBore).
    private static readonly HashSet<string> BigBoreCalibres =
    [
        "Caliber762x51", "Caliber762x39", "Caliber762x54R", "Caliber762x35", "Caliber68x51", "Caliber86x70", "Caliber366TKM", "Caliber127x55"
    ];

    public static bool IsListedBigBore(string calibre) => BigBoreCalibres.Contains(calibre);

    // "Caliber792x57" = 7.92mm bullet, 57mm case. Big bore is a 6.5-9mm bullet in a 39mm+ case, or bigger in a
    // long case. Not pistol rounds or 9x39.
    public static bool IsBigBore(string calibre)
    {
        if (BigBoreCalibres.Contains(calibre))
        {
            return true;
        }

        var match = System.Text.RegularExpressions.Regex.Match(calibre, @"^Caliber(\d+)x(\d+)");
        if (!match.Success)
        {
            return false;
        }

        var digits = match.Groups[1].Value;
        var whole = digits.Length >= 3 && digits[0] == '1' ? 2 : 1;
        var bullet = double.Parse(digits.Length > whole ? $"{digits[..whole]}.{digits[whole..]}" : digits, System.Globalization.CultureInfo.InvariantCulture);
        var length = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return bullet is >= 6.5 and < 9 && length >= 39 || bullet >= 9 && length >= 55;
    }

    public static GunGroup GroupOf(MongoId gunTpl)
    {
        var item = _itemHelper!;
        if (item.IsOfBaseclasses(gunTpl, [PistolBase, RevolverBase]))
        {
            return GunGroup.Pistol;
        }

        if (item.IsOfBaseclass(gunTpl, SmgBase))
        {
            return GunGroup.Smg;
        }

        if (item.IsOfBaseclass(gunTpl, ShotgunBase))
        {
            return GunGroup.Shotgun;
        }

        if (item.IsOfBaseclass(gunTpl, SniperBase))
        {
            return GunGroup.Sniper;
        }

        var calibre = item.GetItem(gunTpl).Value?.Properties?.AmmoCaliber ?? "";
        if (item.IsOfBaseclass(gunTpl, MarksmanBase) || IsBigBore(calibre)
            && item.IsOfBaseclasses(gunTpl, [AssaultRifleBase, AssaultCarbineBase, MachineGunBase]))
        {
            return GunGroup.BigBore;
        }

        return item.IsOfBaseclasses(gunTpl, [AssaultRifleBase, AssaultCarbineBase, MachineGunBase]) ? GunGroup.Rifle : GunGroup.Other;
    }

    // 1 = no lean either way.
    public static double Affinity(OpticKind kind, GunGroup group) => (kind, group) switch
    {
        (OpticKind.Compact or OpticKind.Unique, GunGroup.Pistol) => 1,
        (_, GunGroup.Pistol) => 0.1,
        (OpticKind.Hhs or OpticKind.Prism, GunGroup.Sniper) => 0.3,
        (OpticKind.Lpvo, GunGroup.BigBore) => 3,
        (OpticKind.Lpvo, GunGroup.Rifle) => 1.3,
        (OpticKind.Lpvo, GunGroup.Sniper) => 2,
        (OpticKind.Lpvo, GunGroup.Smg or GunGroup.Shotgun) => 0.5,
        (OpticKind.Specter, GunGroup.Smg) => 3,
        (OpticKind.Specter, GunGroup.Rifle or GunGroup.BigBore) => 0.75,
        (OpticKind.Holo, GunGroup.BigBore) => 0.4,
        (OpticKind.Holo, GunGroup.Sniper) => 0.3,
        (OpticKind.Hhs, GunGroup.Rifle) => 1.8,
        (OpticKind.Hhs, GunGroup.BigBore) => 0.5,
        (OpticKind.Dot, GunGroup.BigBore) => 0.5,
        (OpticKind.Dot, GunGroup.Sniper) => 0.3,
        (OpticKind.Prism, GunGroup.Smg) => 2,
        (OpticKind.Pk120, GunGroup.Shotgun) => 2.5,
        (OpticKind.Special, GunGroup.BigBore or GunGroup.Sniper) => 1,
        (OpticKind.Special, _) => 0.2,
        _ => 1
    };

    private static readonly MongoId CompactCollimatorBase = new("55818acf4bdc2dde698b456b");

    public static double LowestZoom(MongoId tpl) =>
        _itemHelper!.GetItem(tpl).Value?.Properties?.Zooms?.SelectMany(mode => mode).DefaultIfEmpty(1).Min() ?? 1;

    // People want a 1x setting: anything starting at 2.5x or more is hardly used (a 2x prism is fine),
    // and a lone pistol dot on a long gun is rare.
    public static double OpticWeight(MongoId tpl, PartPreferences prefs, GunGroup group, bool pistol)
    {
        var (rating, kind, budget) = OpticRating(tpl);
        var weight = Math.Pow(rating, 0.5 + prefs.StatFocus) * Affinity(kind, group);
        weight *= budget ? (prefs.Style == BuildStyle.Budget ? 2 : prefs.StatFocus >= 3 ? 0.5 : 1.3) : 1;
        weight *= !pistol && _itemHelper!.IsOfBaseclass(tpl, CompactCollimatorBase) ? 0.3 : 1;
        if (LowestZoom(tpl) > 2)
        {
            weight *= group == GunGroup.Sniper ? 0.35 : 0.08;
        }

        // People go out of their way for the optic they want, so availability barely counts.
        return weight * SourceFactor(tpl, prefs, prefs.StatFocus / 8) * ColourFactor(tpl, prefs.Colour, prefs.ColourMatch);
    }

    private static readonly MongoId MountBase = new("55818b224bdc2dde698b456f");

    private static readonly ConcurrentDictionary<MongoId, bool> Rings = new();

    // A 25mm flashlight ring has no scope slot.
    public static bool IsRingMount(MongoId tpl) => Rings.GetOrAdd(tpl, id =>
    {
        var item = _itemHelper!.GetItem(id).Value;
        if (!_itemHelper.IsOfBaseclass(id, MountBase)
            || item?.Properties?.Slots?.Any(slot => slot.Name?.StartsWith("mod_scope", StringComparison.Ordinal) == true) != true)
        {
            return false;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(RingNames(id), @"25_4mm|30mm|34mm|35mm|\brings?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    });

    public static bool IsSniperRing(MongoId tpl) =>
        System.Text.RegularExpressions.Regex.IsMatch(RingNames(tpl), @"34mm|35mm", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string RingNames(MongoId id) =>
        (_itemHelper!.GetItem(id).Value?.Name ?? "") + " "
        + (_englishNames?.TryGetValue($"{id} ShortName", out var shortName) == true ? shortName : "");

    // Nightforce rings on higher tiers, multi-slot mounts (JP, Recknagel, AR-PEPR) on lower ones.
    public static double MountPreference(MongoId tpl, PartPreferences prefs)
    {
        var item = _itemHelper!.GetItem(tpl).Value;
        var name = item?.Name ?? "";
        if (name.Contains("nightforce_x_treme_duty", StringComparison.Ordinal))
        {
            return prefs.StatFocus >= 3 ? 3 : prefs.StatFocus >= 1 ? 1.5 : 1;
        }

        if (name.Contains("t1_sunshade", StringComparison.Ordinal))
        {
            return 0.3;
        }

        var scopeSlots = item?.Properties?.Slots?.Count(slot => slot.Name?.StartsWith("mod_scope", StringComparison.Ordinal) == true) ?? 0;
        return scopeSlots >= 2 && prefs.StatFocus < 3 ? 1.5 : 1;
    }
}
