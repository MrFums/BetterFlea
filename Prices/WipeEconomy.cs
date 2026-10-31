using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils.Json;

namespace BetterFlea.Prices;

// Wipe Cycle: prices follow how far into the wipe the players online are. Everything is scarce
// and dear at the very start and dips a little mid-wipe; on top of that, what people need for the hideout and
// quests is dear just before they get there, and the best guns, ammo, armour and stims are dear late on.
[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 2)]
public class WipeEconomyLoad(ConfigManager configManager, TemplateTable templates, HideoutTable hideout, TradersTable traders,
    HandbookHelper handbook, ItemHelper itemHelper, SaveServer saveServer, ProfileActivityService activity) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        WipeEconomy.Start(configManager, templates, hideout, traders, handbook, itemHelper, saveServer, activity);
        new ActivityPatch().Enable();
        return Task.CompletedTask;
    }
}

// SPT only counts a player as online from when their game starts up, so after a server restart a game that's
// still running isn't seen. Count anyone making requests (the game, or the client plugin's checks).
public class ActivityPatch : SPTarkov.Reflection.Patching.AbstractPatch
{
    protected override System.Reflection.MethodBase GetTargetMethod() =>
        typeof(ProfileActivityService).GetMethod(nameof(ProfileActivityService.SetActivityTimestamp))!;

    [SPTarkov.Reflection.Patching.PatchPrefix]
    public static void Prefix(ProfileActivityService __instance, MongoId sessionId)
    {
        if (!sessionId.IsEmpty && !__instance.ContainsActiveProfile(sessionId))
        {
            __instance.AddActiveProfile(sessionId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }
}

internal static class WipeEconomy
{
    // Vanilla traders only: mod traders say nothing about the wipe.
    private static readonly HashSet<MongoId> VanillaTraders =
    [
        new("54cb50c76803fa8b248b4571"), // Prapor
        new("54cb57776803fa99248b456e"), // Therapist
        new("58330581ace78e27b8b10cee"), // Skier
        new("5935c25fb3acc3127c3d8cd9"), // Peacekeeper
        new("5a7c2eca46aef81a7ca2145d"), // Mechanic
        new("5ac3b934156ae10c4430e83c"), // Ragman
        new("5c0647fdd443bc2504c2d371")  // Jaeger
    ];

    private static readonly HashSet<MongoId> Currencies = [Money.ROUBLES, Money.DOLLARS, Money.EUROS, new("5d235b4d86f7742e017bc88a")];

    private static readonly MongoId GunBase = new("5422acb9af1c889c16000029");
    private static readonly MongoId AmmoBase = new("5485a8684bdc2da71d8b4567");
    private static readonly MongoId StimBase = new("5448f3a64bdc2d60728b456a");
    private static readonly MongoId GraphicsCard = new("57347ca924597744596b4e71");
    private static readonly MongoId[] ArmourBases =
    [
        new("5448e54d4bdc2dcc718b4568"), // body armour
        new("5448e5284bdc2dcb718b4567"), // rigs (only armoured ones have a class)
        new("5a341c4086f77401f2541505"), // headwear
        new("644120aa86ffbe10ee032b6f")  // plates
    ];

    private enum GunKind { Other, AssaultRifle, Carbine, Marksman, Sniper, Shotgun, Smg, Pistol, MachineGun }

    private static readonly Dictionary<MongoId, GunKind> GunKinds = new()
    {
        [new("5447b5f14bdc2d61278b4567")] = GunKind.AssaultRifle,
        [new("5447b5fc4bdc2d87278b4567")] = GunKind.Carbine,
        [new("5447b6194bdc2d67278b4567")] = GunKind.Marksman,
        [new("5447b6254bdc2dc3278b4568")] = GunKind.Sniper,
        [new("5447b6094bdc2dc3278b4567")] = GunKind.Shotgun,
        [new("5447b5e04bdc2d62278b4567")] = GunKind.Smg,
        [new("5447b5cf4bdc2d65278b4567")] = GunKind.Pistol,
        [new("5447bed64bdc2d97278b4568")] = GunKind.MachineGun
    };

    // Calibres people want late in a wipe, and the ones they drop.
    private static readonly Dictionary<string, double> CalibreBonus = new()
    {
        ["Caliber762x51"] = 0.3, ["Caliber86x70"] = 0.2, ["Caliber556x45NATO"] = 0.25, ["Caliber68x51"] = 0.25,
        ["Caliber762x54R"] = 0.15, ["Caliber9x39"] = 0.1, ["Caliber762x39"] = 0.15, ["Caliber545x39"] = 0.1, ["Caliber127x55"] = 0.1,
        ["Caliber12g"] = -0.15, ["Caliber20g"] = -0.15, ["Caliber9x18PM"] = -0.25, ["Caliber9x19PARA"] = -0.1,
        ["Caliber762x25TT"] = -0.2, ["Caliber1143x23ACP"] = -0.1, ["Caliber9x21"] = -0.1
    };

    // Shotgun shells go the way of the shotguns.
    private static readonly HashSet<string> ShellCalibres = ["Caliber12g", "Caliber20g", "Caliber23x75"];

    // How real flea prices moved through a wipe: for each item, its price at these levels against its own normal
    // price, and the same for whole groups (used for items with no history of their own, like modded ones).
    private record Curves(int[] Levels, Dictionary<string, double[]> Groups, Dictionary<string, double[]> Items);

    private static Curves _curves = new([1, 60], new() { ["all"] = [1, 1] }, new());
    private static readonly Dictionary<MongoId, double[]> ItemCurves = new();
    private static readonly Dictionary<MongoId, string> GroupOf = new();

    // Group names in the curves file, by base class (first match wins).
    private static readonly (string Name, MongoId Base)[] CurveGroups =
    [
        ("gun", new("5422acb9af1c889c16000029")), ("ammo", new("5485a8684bdc2da71d8b4567")), ("armour", new("57bef4c42459772e8d35a53b")),
        ("meds", new("543be5664bdc2dd4348b4569")), ("keys", new("543be5e94bdc2df1348b4568")), ("barter", new("5448eb774bdc2d0a728b4567")),
        ("gear", new("543be5f84bdc2dd4348b456a"))
    ];

    private static void LoadCurves()
    {
        try
        {
            using var stream = typeof(WipeEconomy).Assembly.GetManifestResourceStream("BetterFlea.data.wipe-curves.json");
            if (stream is null)
            {
                return;
            }

            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _curves = System.Text.Json.JsonSerializer.Deserialize<Curves>(stream, options) ?? _curves;
            foreach (var (id, curve) in _curves.Items)
            {
                if (MongoId.IsValidMongoId(id))
                {
                    ItemCurves[new MongoId(id)] = curve;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Summary(() => $"Wipe Cycle: couldn't read the wipe curves: {ex.Message}");
        }
    }

    // A curve at a level (on a 60-level wipe), eased between its points and flat past the ends.
    private static double CurveAt(double[] curve, double x)
    {
        var levels = _curves.Levels;
        if (x <= levels[0]) return curve[0];
        for (var i = 1; i < levels.Length && i < curve.Length; i++)
        {
            if (x <= levels[i])
            {
                return curve[i - 1] + (curve[i] - curve[i - 1]) * Smooth((x - levels[i - 1]) / (levels[i] - levels[i - 1]));
            }
        }

        return curve[^1];
    }

    private static double[] Group(string name) => _curves.Groups.GetValueOrDefault(name) ?? _curves.Groups["all"];

    private static ConfigManager? _config;
    private static SaveServer? _saveServer;
    private static ProfileActivityService? _activity;
    private static Timer? _timer;
    private static Dictionary<MongoId, Demand> _demand = new();
    private static Dictionary<MongoId, double> _factors = new();
    private static int _level;
    private static int _target;
    private static bool _fromTest;
    private static long _changedAt;
    private static readonly object Gate = new();

    private static WipeCycleConfig Settings => _config?.Mod.Economy.WipeCycle ?? new WipeCycleConfig();

    public static bool On => Settings.Enabled;

    // How many hideout stages and quests want the item. Known even with the Wipe Cycle off.
    public static int Uses(MongoId tpl) => _demand.TryGetValue(tpl, out var demand) ? demand.Uses.Count : 0;

    // The level prices follow now, the level they're heading for, and when it can next change.
    public static int Level => _level;
    public static int Target => _target;
    public static long NextChangeAt => _changedAt + (long)(Settings.CooldownMinutes * 60);

    // What decides an item's price over the wipe.
    private sealed class Demand
    {
        public readonly List<(double Level, double Weight)> Uses = new();
        public double? Gun;
        public GunKind Kind;
        public int Armour;
        public double LateConsumable;
        public double? AmmoPen;
        public bool Flesh;
        public bool Shell;
        public int? TraderLevel;
    }

    public static void Start(ConfigManager configManager, TemplateTable templates, HideoutTable hideout, TradersTable traders,
        HandbookHelper handbook, ItemHelper itemHelper, SaveServer saveServer, ProfileActivityService activity)
    {
        _config = configManager;
        _saveServer = saveServer;
        _activity = activity;

        // Built even when off, so it can be switched on from the settings page.
        LoadCurves();
        _demand = Build(templates, hideout, traders, handbook, itemHelper);
        foreach (var item in templates.Items.Values.Where(item => item.Type == "Item" && !ItemCurves.ContainsKey(item.Id)))
        {
            GroupOf[item.Id] = CurveGroups.FirstOrDefault(group => itemHelper.IsOfBaseclass(item.Id, group.Base)).Name ?? "other";
        }
        Update(force: true);
        _timer ??= new Timer(_ => Update(force: false), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public static void SettingsChanged() => Update(force: true);

    private static int Step => Math.Max(1, Settings.LevelStep);

    // The highest level among the players on the server right now. Nothing is saved, so a test profile that
    // isn't playing doesn't count.
    private static int OnlineLevel()
    {
        // A prestiged player counts as late wipe unless set to start them over.
        var addPrestige = !Settings.Prestige.Equals("reset", StringComparison.OrdinalIgnoreCase);
        var levels = (_activity?.GetActiveProfileIdsWithinMinutes(10) ?? [])
            .Where(MongoId.IsValidMongoId)
            .Select(id => _saveServer!.GetProfiles().GetValueOrDefault(new MongoId(id))?.CharacterData?.PmcData?.Info)
            .Select(info => (info?.Level ?? 0) + (addPrestige ? (info?.PrestigeLevel ?? 0) * 55 : 0))
            .Where(level => level > 0)
            .ToList();
        return levels.Count > 0 ? levels.Max() : 0;
    }

    // Follows the players online, but once it moves it stays put for the cooldown, so swapping profiles can't
    // be used to buy low and sell high.
    private static void Update(bool force)
    {
        try
        {
            var settings = Settings;
            var testing = settings.Level > 0;
            var online = testing ? settings.Level : OnlineLevel();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            lock (Gate)
            {
                // The test level was just cleared: drop it now, and follow the real players from here (no cooldown).
                if (_fromTest && !testing)
                {
                    _level = 0;
                    _target = 0;
                }

                _fromTest = testing;
                if (online > 0)
                {
                    _target = Math.Max(1, online / Step * Step);
                }

                var due = force || _level == 0 || now - _changedAt >= settings.CooldownMinutes * 60;
                if (_target > 0 && _target != _level && (due || settings.Level > 0))
                {
                    _level = _target;
                    _changedAt = now;
                }

                _factors = new Dictionary<MongoId, double>();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Summary(() => $"Wipe Cycle: update failed: {ex.Message}");
        }
    }

    // Price multiplier for a new offer of this item right now.
    public static double Factor(MongoId tpl)
    {
        if (!On || _level == 0)
        {
            return 1.0;
        }

        var factors = _factors;
        lock (factors)
        {
            if (!factors.TryGetValue(tpl, out var factor))
            {
                factors[tpl] = factor = FactorAt(tpl, _level);
            }

            return factor;
        }
    }

    public static double FactorAt(MongoId tpl, int level)
    {
        var settings = Settings;
        var end = Math.Max(10, settings.EndLevel);
        var x = level * 60.0 / end;
        var early = 1 - Smooth((x - 1) / 24);
        var late = Smooth((x - 8) / 52);
        _demand.TryGetValue(tpl, out var demand);

        // The real price history is the base: the item's own, else its group's. Ammo prices in that history don't
        // move, so ammo goes by the whole market (half as much) and its penetration rules instead.
        var ammo = demand?.AmmoPen is not null;
        double[]? curve = null;
        var own = !ammo && ItemCurves.TryGetValue(tpl, out curve);
        var history = ammo ? 1 + (CurveAt(Group("all"), x) - 1) * 0.5
            : own ? CurveAt(curve!, x)
            : CurveAt(Group(GroupOf.GetValueOrDefault(tpl, "all")), x);

        // The hideout, quest, gun, ammo and armour rules go on top: half weight where the item's own history
        // already shows it, full weight where it doesn't.
        var rules = demand is null ? 0 : ItemChange(demand, x, early, late) * (own ? 0.5 : 1.0);
        var strength = settings.Strength;
        return Math.Clamp(Math.Pow(history, strength) * (1 + rules * strength), 0.4, 2.5);
    }

    private static double ItemChange(Demand demand, double x, double early, double late)
    {
        var change = 0.0;

        // Needed for the hideout or a quest: dearer just before most people get there, cheap once they're past it.
        if (demand.Uses.Count > 0)
        {
            var need = demand.Uses.Sum(use => use.Weight * Math.Exp(-Math.Pow(x - use.Level + 3, 2) / (2 * 7 * 7)));
            change += 0.35 * (1 - Math.Exp(-need / 0.2));
            var lastUse = demand.Uses.Max(use => use.Level);
            change -= 0.15 * Smooth((x - lastUse - 6) / 10);
        }

        // Guns: the good ones are wanted late. Assault rifles always, DMRs a lot, SMGs all wipe as a light run
        // and gun; shotguns, snipers and pistols drop off once people have better.
        if (demand.Gun is { } gun)
        {
            change += (gun - 0.45) * 0.4 * late + 0.05 * (1 - gun) * early;
            change += demand.Kind switch
            {
                GunKind.AssaultRifle => 0.08 + 0.12 * late,
                GunKind.Carbine => 0.04 + 0.06 * late,
                GunKind.Marksman => 0.15 * early + 0.18 * late,
                GunKind.Smg => 0.06,
                GunKind.Sniper => -0.25 * late,
                GunKind.Shotgun => -0.3 * late,
                GunKind.Pistol => -0.2 * late,
                GunKind.MachineGun => -0.1 * late,
                _ => 0
            };
        }

        // Ammo by penetration: good rounds are scarce at the start and wanted late, weak rounds drop off. Flesh
        // rounds (low pen, big damage) are used all wipe, most of all late for legs.
        if (demand.AmmoPen is { } pen)
        {
            var good = Math.Clamp((pen - 20) / 35, 0, 1);
            change += 0.45 * good * early + 0.5 * good * late - 0.15 * (1 - good) * late;
            if (demand.Shell)
            {
                change -= 0.2 * late;
            }

            if (demand.Flesh)
            {
                // Also makes up for the weak-round drop above.
                change += 0.1 + 0.45 * late;
            }
        }

        // High classes are wanted late, low classes early.
        if (demand.Armour >= 2)
        {
            change += (demand.Armour - 4) * (0.12 * late - 0.05 * (1 - late));
        }

        // Expensive stims and GPUs: nobody needs them early, everyone late.
        change += demand.LateConsumable * (0.6 * late - 0.1 * (1 - late));

        // Before a trader sells it for money, the flea is the only place to get it, so it climbs towards that level.
        // After, the trader's price caps it and it eases down over a few levels. Not guns: good ones stay wanted.
        if (demand.TraderLevel is { } traderLevel && demand.Gun is null)
        {
            var scarce = traderLevel >= 20 ? 0.2 * Smooth((Math.Min(x, traderLevel) - (traderLevel - 20)) / 18) : 0;
            if (x >= traderLevel)
            {
                // Traders only sell so much ammo per restock, so wanted rounds keep a flea premium.
                var capped = demand.AmmoPen is not null ? Math.Min(change, 0.25) : Math.Min(change, 0.04) - 0.06 * Smooth((x - traderLevel) / 15);
                var settle = Smooth((x - traderLevel) / 8);
                change = (change + scarce) * (1 - settle) + capped * settle;
            }
            else
            {
                change += scarce;
            }
        }

        return Math.Max(change, -0.4);
    }

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    // The whole market's multiplier at a level (what an item with nothing special about it gets).
    public static double MarketFactor(int level) =>
        Math.Pow(CurveAt(Group("all"), level * 60.0 / Math.Max(10, Settings.EndLevel)), Settings.Strength);

    // The items that move most at a level against the rest of the market: (item, multiplier vs the market).
    public static List<(MongoId Tpl, double Factor)> Movers(int level, int each)
    {
        var market = MarketFactor(level);
        var all = _demand.Keys.Select(tpl => (tpl, FactorAt(tpl, level) / market)).ToList();
        return all.OrderByDescending(entry => entry.Item2).Take(each)
            .Concat(all.OrderBy(entry => entry.Item2).Take(each)).ToList();
    }

    private static Dictionary<MongoId, Demand> Build(TemplateTable templates, HideoutTable hideout, TradersTable traders,
        HandbookHelper handbook, ItemHelper itemHelper)
    {
        var demand = new Dictionary<MongoId, Demand>();
        Demand For(MongoId tpl) => demand.TryGetValue(tpl, out var d) ? d : demand[tpl] = new Demand();
        var items = templates.Items.Values.Where(item => item.Type == "Item").ToList();

        // When a vanilla trader first sells each item for money, as a player level.
        var traderLevels = new Dictionary<MongoId, int>();
        var loyaltyLevels = new Dictionary<MongoId, List<int>>();
        foreach (var (traderId, trader) in traders)
        {
            if (!VanillaTraders.Contains(traderId) || trader.Base?.LoyaltyLevels is not { } levels)
            {
                continue;
            }

            var minLevels = levels.Select(level => level.MinLevel ?? 1).ToList();
            loyaltyLevels[traderId] = minLevels;
            foreach (var item in trader.Assort?.Items ?? [])
            {
                // A barter doesn't cap the price.
                if (item.ParentId != "hideout" || !trader.Assort!.LoyalLevelItems.TryGetValue(item.Id, out var loyalty)
                    || !trader.Assort.BarterScheme.TryGetValue(item.Id, out var schemes)
                    || !schemes.All(scheme => scheme.All(cost => Currencies.Contains(cost.Template))))
                {
                    continue;
                }

                var level = minLevels[Math.Clamp(loyalty - 1, 0, minLevels.Count - 1)];
                traderLevels[item.Template] = Math.Min(traderLevels.GetValueOrDefault(item.Template, int.MaxValue), Math.Max(1, level));
            }
        }

        AddHideout(hideout, loyaltyLevels, For);
        AddQuests(templates, loyaltyLevels, For);

        // Guns: the trader level that sells them, how dear they are, what they shoot and what kind they are.
        var guns = items.Where(item => itemHelper.IsOfBaseclass(item.Id, GunBase)).ToList();
        var gunPrices = guns.Select(item => handbook.GetTemplatePrice(item.Id)).Order().ToList();
        foreach (var gun in guns)
        {
            var pricePct = Percentile(gunPrices, handbook.GetTemplatePrice(gun.Id));
            var traderScore = traderLevels.TryGetValue(gun.Id, out var level) ? Math.Clamp((level - 1) / 35.0, 0, 1) : pricePct;
            var calibre = CalibreBonus.GetValueOrDefault(gun.Properties?.AmmoCaliber ?? "", 0);
            var d = For(gun.Id);
            d.Gun = Math.Clamp(0.35 * traderScore + 0.35 * pricePct + calibre + 0.15, 0, 1);
            d.Kind = GunKinds.GetValueOrDefault(gun.Parent, GunKind.Other);
        }

        // Ammo by penetration. Ammo boxes go with the rounds inside.
        foreach (var round in items.Where(item => itemHelper.IsOfBaseclass(item.Id, AmmoBase)))
        {
            var d = For(round.Id);
            d.AmmoPen = round.Properties?.PenetrationPower ?? 0;
            d.Shell = ShellCalibres.Contains(round.Properties?.Caliber ?? "");
            d.Flesh = !d.Shell && d.AmmoPen < 25 && (round.Properties?.Damage ?? 0) >= 70;
        }

        // Armour by class: the item's own, or the best plate it comes with.
        foreach (var item in items.Where(item => itemHelper.IsOfBaseclasses(item.Id, ArmourBases)))
        {
            var armourClass = Math.Max(ArmourClass(item), (item.Properties?.Slots ?? [])
                .SelectMany(slot => slot.Properties?.Filters ?? [])
                .Select(filter => filter.Plate is { } plate ? ArmourClass(templates.Items.GetValueOrDefault(plate)) : 0)
                .DefaultIfEmpty(0).Max());
            if (armourClass >= 2)
            {
                For(item.Id).Armour = armourClass;
            }
        }



        // Dear stims (and GPUs for the bitcoin farm) are late-wipe items.
        var stims = items.Where(item => itemHelper.IsOfBaseclass(item.Id, StimBase)).ToList();
        var stimPrices = stims.Select(item => handbook.GetTemplatePrice(item.Id)).Order().ToList();
        foreach (var stim in stims)
        {
            For(stim.Id).LateConsumable = Percentile(stimPrices, handbook.GetTemplatePrice(stim.Id));
        }

        For(GraphicsCard).LateConsumable = 0.8;

        foreach (var (tpl, level) in traderLevels)
        {
            if (demand.TryGetValue(tpl, out var d))
            {
                d.TraderLevel = level;
            }
        }

        DebugLog.Summary(() => $"Wipe Cycle: {demand.Count} items have their own wipe pattern ({demand.Values.Count(d => d.Uses.Count > 0)} hideout or quest items); the rest follow the market.");
        return demand;
    }

    private static int ArmourClass(TemplateItem? item) =>
        int.TryParse(item?.Properties?.ArmorClass?.ToString(), out var armourClass) ? armourClass : 0;

    private static double Percentile(List<double> sorted, double value) =>
        sorted.Count < 2 ? 0.5 : sorted.Count(price => price < value) / (double)(sorted.Count - 1);

    // Each stage is placed at the player level people usually build it: its number, the trader levels it needs
    // and the areas it needs first.
    private static void AddHideout(HideoutTable hideout, Dictionary<MongoId, List<int>> loyaltyLevels, Func<MongoId, Demand> demandFor)
    {
        var areas = hideout.Areas.Where(area => area.Type is not null && area.Stages is not null).ToList();
        var levels = new Dictionary<(int Area, int Stage), double>();

        double LevelOf(int areaType, int stage, int depth)
        {
            if (stage <= 0 || depth > 20)
            {
                return 1;
            }

            if (levels.TryGetValue((areaType, stage), out var known))
            {
                return known;
            }

            var area = areas.FirstOrDefault(a => (int)a.Type!.Value == areaType);
            var requirements = area?.Stages?.GetValueOrDefault(stage.ToString())?.Requirements ?? [];
            double level = stage switch { 1 => 3, 2 => 12, 3 => 24, _ => 32 };
            foreach (var requirement in requirements)
            {
                if (requirement.Type == "TraderLoyalty" && loyaltyLevels.TryGetValue(requirement.TraderId, out var minLevels))
                {
                    level = Math.Max(level, minLevels[Math.Clamp((requirement.LoyaltyLevel ?? 1) - 1, 0, minLevels.Count - 1)] + 2);
                }
                else if (requirement.Type == "Area" && requirement.AreaType is { } other && requirement.RequiredLevel is { } otherStage
                         && (other != areaType || otherStage < stage))
                {
                    level = Math.Max(level, LevelOf(other, otherStage, depth + 1) + 1);
                }
            }

            return levels[(areaType, stage)] = level;
        }

        foreach (var area in areas)
        {
            foreach (var (stageName, stage) in area.Stages!)
            {
                if (!int.TryParse(stageName, out var stageNumber))
                {
                    continue;
                }

                var level = LevelOf((int)area.Type!.Value, stageNumber, 0);
                foreach (var requirement in stage.Requirements ?? [])
                {
                    if (requirement.Type == "Item" && !Currencies.Contains(requirement.TemplateId))
                    {
                        AddUse(demandFor(requirement.TemplateId), level, requirement.Count ?? 1, requirement.IsSpawnedInSession == true, 1);
                    }
                }
            }
        }
    }

    // Items handed in or worn for quests, at the level the quest opens: its own level requirement, the levels of
    // the quests before it, and the trader loyalty it needs.
    private static void AddQuests(TemplateTable templates, Dictionary<MongoId, List<int>> loyaltyLevels, Func<MongoId, Demand> demandFor)
    {
        var levels = new Dictionary<MongoId, double>();

        double LevelOf(MongoId questId, int depth)
        {
            if (levels.TryGetValue(questId, out var known))
            {
                return known;
            }

            double level = 1;
            if (depth < 60 && templates.Quests.TryGetValue(questId, out var quest))
            {
                foreach (var condition in quest.Conditions?.AvailableForStart ?? [])
                {
                    if (condition.ConditionType == "Level" && condition.Value is { } value)
                    {
                        level = Math.Max(level, value);
                    }
                    else if (condition.ConditionType == "TraderLoyalty" && condition.TraderId is { } trader && MongoId.IsValidMongoId(trader)
                             && loyaltyLevels.TryGetValue(new MongoId(trader), out var minLevels) && condition.Value is { } loyalty)
                    {
                        level = Math.Max(level, minLevels[Math.Clamp((int)loyalty - 1, 0, minLevels.Count - 1)]);
                    }
                    else if (condition.ConditionType == "Quest" && condition.Target?.Item is { } before && MongoId.IsValidMongoId(before))
                    {
                        // Most people finish the quest before a level or so after it opens.
                        level = Math.Max(level, LevelOf(new MongoId(before), depth + 1) + 0.5);
                    }
                }
            }

            return levels[questId] = level;
        }

        foreach (var (questId, quest) in templates.Quests)
        {
            var level = LevelOf(questId, 0);
            foreach (var condition in quest.Conditions?.AvailableForFinish ?? [])
            {
                if (condition.ConditionType == "HandoverItem")
                {
                    var targets = Targets(condition.Target);
                    if (targets.Count is > 0 and <= 4)
                    {
                        foreach (var tpl in targets)
                        {
                            AddUse(demandFor(tpl), level, (int)(condition.Value ?? 1), condition.OnlyFoundInRaid == true, targets.Count);
                        }
                    }
                }

                // Each inner list is one outfit (all worn together); the outer list is the choice between outfits.
                foreach (var counter in condition.Counter?.Conditions ?? [])
                {
                    var outfits = counter.ConditionType == "Equipment" ? counter.EquipmentInclusive?.ToList() ?? [] : [];
                    foreach (var tpl in outfits.SelectMany(outfit => outfit).Where(MongoId.IsValidMongoId))
                    {
                        AddUse(demandFor(new MongoId(tpl)), level, 1, false, outfits.Count);
                    }
                }
            }
        }
    }

    private static List<MongoId> Targets(ListOrT<string>? target) =>
        (target?.List ?? (target?.Item is { } one ? [one] : [])).Where(MongoId.IsValidMongoId).Select(id => new MongoId(id))
            .Where(tpl => !Currencies.Contains(tpl)).ToList();

    // Found-in-raid items can't be bought for this, but people keep theirs instead of selling, so they still get
    // dearer: less so early, more so late.
    private static void AddUse(Demand demand, double level, int count, bool foundInRaid, int alternatives)
    {
        var weight = (0.05 + 0.025 * Math.Log2(1 + Math.Max(1, count))) / Math.Max(1, alternatives);
        if (foundInRaid)
        {
            weight *= level < 20 ? 0.7 : 1.15;
        }

        demand.Uses.Add((level, weight));
    }
}
