using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace BetterFlea.Prices;

// How hard a quest is to get to and finish, 0 to 1: its level lock, the work in it and every quest before it, and
// how hard it is by itself. Worked out from the loaded quests, so modded quests and traders count too.
// Research/quest-unlock-difficulty.md has the full table; Research/tools/quest_difficulty.py scores the same way.
internal static class QuestDifficulty
{
    public const double Medium = 0.35;
    public const double Hard = 0.55;
    public const double VeryHard = 0.75;

    private static readonly MongoId Lightkeeper = new("638f541a29ffd1183d187f57");
    private static readonly MongoId Btr = new("656f0f98d80a697f855d34b1");
    // No quest reward unlocks the BTR Driver: he's met through Skier's A Helping Hand, then in raid.
    private static readonly MongoId HelpingHand = new("6752f6d83038f7df520c83e8");

    // Map key or location id -> points. Labs needs a keycard; Lighthouse, Reserve and Streets are the deadly late maps.
    private static readonly Dictionary<string, double> MapPoints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["laboratory"] = 3, ["5b0fc42d86f7744a585f9105"] = 3, ["Labyrinth"] = 3, ["6733700029c367a3d40b02af"] = 3,
        ["Lighthouse"] = 2, ["5704e4dad2720bb55b8b4567"] = 2, ["RezervBase"] = 2, ["5704e5fad2720bc05b8b4567"] = 2,
        ["TarkovStreets"] = 2, ["5714dc692459777137212e12"] = 2,
        ["Shoreline"] = 1, ["5704e554d2720bac5b8b456e"] = 1, ["Interchange"] = 1, ["5714dbc024597771384a510d"] = 1,
        ["factory4_night"] = 1, ["59fc81d786f774390775787e"] = 1
    };

    // Per kill. Goons and Zryachiy roam or sit somewhere awful; bosses spawn by chance; followers come with a boss.
    // Roles not listed (a mod's bots, say) count 0.5, followers 1.5.
    private static readonly Dictionary<string, double> RoleWeight = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bossZryachiy"] = 5, ["followerZryachiy"] = 3, ["bossKnight"] = 4, ["followerBigPipe"] = 4, ["followerBirdEye"] = 4,
        ["bossKilla"] = 3, ["bossKojaniy"] = 3, ["bossGluhar"] = 3, ["bossSanitar"] = 3, ["bossBully"] = 3, ["bossTagilla"] = 3,
        ["bossBoar"] = 3, ["bossBoarSniper"] = 2, ["bossKolontay"] = 3, ["bossPartisan"] = 3, ["bossKillaAgro"] = 3,
        ["bossTagillaAgro"] = 3, ["sectantPriest"] = 2, ["sectantWarrior"] = 1.5, ["sectantPrizrak"] = 2,
        ["sectantPredvestnik"] = 2, ["sectantOni"] = 2, ["pmcBot"] = 1, ["exUsec"] = 1, ["marksman"] = 0.3
    };

    private static TemplateTable? _templates;
    private static TradersTable? _traders;
    private static QuestConfig? _questConfig;
    private static HandbookHelper? _handbook;
    private static ItemHelper? _itemHelper;
    private static Lazy<Scores> _scores = new(Load);

    private sealed record Scores(Dictionary<MongoId, double?> Quests, Dictionary<MongoId, MongoId> TraderGates, HashSet<MongoId> Hidden);

    private static readonly MongoId Fence = new("579dc571d53a0658a154fbec");
    private const double HiddenTrader = 0.6;

    public static void Bind(TemplateTable templates, TradersTable traders, QuestConfig questConfig, HandbookHelper handbook, ItemHelper itemHelper)
    {
        _templates = templates;
        _traders = traders;
        _questConfig = questConfig;
        _handbook = handbook;
        _itemHelper = itemHelper;
    }

    // Mods can still add quests and traders after startup.
    public static void Forget() => _scores = new(Load);

    public static string Tier(double score) =>
        score >= VeryHard ? "veryHard" : score >= Hard ? "hard" : score >= Medium ? "medium" : "easy";

    // Null: a quest SPT never gives out (a non-seasonal event quest), so nothing it unlocks can be bought.
    public static double? Of(MongoId questId) =>
        _scores.Value.Quests.TryGetValue(questId, out var score) ? score : 0;

    // How hard buying from a trader is before any quest lock: 0 when the trader is there from the start, else the
    // quest that unlocks them, and at least "hard" for a hidden trader (one no quest reward unlocks, like the BTR
    // Driver). Null when that quest is never given.
    public static double? OfTrader(MongoId traderId)
    {
        var scores = _scores.Value;
        double? score = scores.TraderGates.TryGetValue(traderId, out var gate) ? Of(gate) : 0;
        if (score is not { } s)
        {
            return null;
        }

        // Lightkeeper counts as unlocked in the data, but he's only reached at the end of the Network Provider chain.
        return traderId == Lightkeeper ? Math.Max(s, 0.9) : scores.Hidden.Contains(traderId) ? Math.Max(s, HiddenTrader) : s;
    }

    // An item unlocked when a quest is started can be bought once the quests before it are done.
    public static double? OfStart(MongoId questId)
    {
        if (_templates?.Quests.TryGetValue(questId, out var quest) != true)
        {
            return 0;
        }

        double? score = Math.Min(1, StartLevel(quest!) / 75.0);
        foreach (var before in Prerequisites(quest!))
        {
            var other = Of(before);
            score = score is null || other is null ? null : Math.Max(score.Value, other.Value);
        }

        return score;
    }

    private static Scores Load()
    {
        var quests = _templates?.Quests ?? new Dictionary<MongoId, Quest>();
        var gates = new Dictionary<MongoId, MongoId>();
        foreach (var (questId, quest) in quests)
        {
            foreach (var reward in quest.Rewards?.GetValueOrDefault("Success") ?? [])
            {
                if (reward.Type == RewardType.TraderUnlock && reward.Target is { } target && MongoId.IsValidMongoId(target))
                {
                    gates.TryAdd(new MongoId(target), questId);
                }
            }
        }

        // Locked at the start and no quest unlocks them: met some other way (in raid, for the BTR Driver).
        var hidden = (_traders ?? []).Where(entry => entry.Key != Fence && entry.Value.Base?.UnlockedByDefault == false && !gates.ContainsKey(entry.Key))
            .Select(entry => entry.Key).ToHashSet();
        if (quests.ContainsKey(HelpingHand))
        {
            gates.TryAdd(Btr, HelpingHand);
        }

        // Quests another quest fails: taking them closes the other route, so fewer players go that way.
        var failedBy = new HashSet<MongoId>();
        foreach (var quest in quests.Values)
        {
            foreach (var condition in quest.Conditions?.Fail ?? [])
            {
                if (condition.ConditionType == "Quest" && condition.Target?.Item is { } target && MongoId.IsValidMongoId(target))
                {
                    failedBy.Add(new MongoId(target));
                }
            }
        }

        var own = quests.ToDictionary(entry => entry.Key, entry => OwnPoints(entry.Value, failedBy.Contains(entry.Key), hidden.Contains(entry.Value.TraderId)));
        var chains = new Dictionary<MongoId, Chain>();

        Chain ChainOf(MongoId questId, int depth)
        {
            if (chains.TryGetValue(questId, out var known))
            {
                return known;
            }

            chains[questId] = new Chain([], 0, 0, false); // breaks loops
            if (depth > 200 || !quests.TryGetValue(questId, out var quest))
            {
                return chains[questId];
            }

            var before = Prerequisites(quest).ToList();
            if (gates.TryGetValue(quest.TraderId, out var gate) && gate != questId && !before.Contains(gate))
            {
                before.Add(gate);
            }

            var ancestors = new HashSet<MongoId>();
            var level = StartLevel(quest);
            var loyaltyGate = StartLoyalty(quest);
            var lightkeeper = quest.TraderId == Lightkeeper;
            foreach (var id in before)
            {
                var chain = ChainOf(id, depth + 1);
                ancestors.UnionWith(chain.Ancestors);
                ancestors.Add(id);
                level = Math.Max(level, chain.Level);
                loyaltyGate = Math.Max(loyaltyGate, chain.LoyaltyGate);
                lightkeeper |= chain.Lightkeeper;
            }

            return chains[questId] = new Chain(ancestors, level, loyaltyGate, lightkeeper);
        }

        var scores = new Dictionary<MongoId, double?>();
        foreach (var (questId, quest) in quests)
        {
            var chain = ChainOf(questId, 0);
            var mine = own[questId];
            var total = mine + chain.Ancestors.Sum(id => own.GetValueOrDefault(id)) + 2 * Math.Max(0, chain.LoyaltyGate - 1);
            var score = 0.35 * Math.Min(1, chain.Level / 45.0)
                        + 0.45 * Math.Min(1, Math.Log(1 + total) / Math.Log(151))
                        + 0.2 * Math.Min(1, mine / 12);
            // A high level lock is hard on its own, however short the chain: level 45 is at least "hard".
            score = Math.Max(score, chain.Level / 75.0);
            if (chain.Lightkeeper)
            {
                score = Math.Max(score, 0.9);
            }

            // A hidden trader met in raid (the BTR Driver, a mod's hidden trader): most players never get there.
            if (hidden.Contains(quest.TraderId))
            {
                score = Math.Max(score, HiddenTrader);
            }

            if (_questConfig?.EventQuests?.TryGetValue(questId, out var eventQuest) == true)
            {
                if (eventQuest!.Season == SeasonalEventType.None && !_questConfig.ShowNonSeasonalEventQuests)
                {
                    scores[questId] = null;
                    continue;
                }

                score = Math.Max(score, 0.8);
            }

            scores[questId] = Math.Min(1, score);
        }

        var counts = scores.Values.GroupBy(score => score is { } s ? Tier(s) : "never given").ToDictionary(group => group.Key, group => group.Count());
        DebugLog.Summary(() => $"Quest difficulty: {scores.Count} quests, " + string.Join(", ", counts.Select(entry => $"{entry.Value} {entry.Key}")));
        return new Scores(scores, gates, hidden);
    }

    private sealed record Chain(HashSet<MongoId> Ancestors, double Level, double LoyaltyGate, bool Lightkeeper);

    private static IEnumerable<MongoId> Prerequisites(Quest quest)
    {
        foreach (var condition in quest.Conditions?.AvailableForStart ?? [])
        {
            if (condition.ConditionType == "Quest" && condition.Target?.Item is { } target && MongoId.IsValidMongoId(target)
                && condition.Status?.Any(status => status is QuestStatusEnum.Started or QuestStatusEnum.Success) != false)
            {
                yield return new MongoId(target);
            }
        }
    }

    private static double StartLevel(Quest quest) =>
        (quest.Conditions?.AvailableForStart ?? []).Where(condition => condition.ConditionType == "Level").Select(condition => condition.Value ?? 0).DefaultIfEmpty(0).Max();

    private static double StartLoyalty(Quest quest) =>
        (quest.Conditions?.AvailableForStart ?? []).Where(condition => condition.ConditionType == "TraderLoyalty").Select(condition => condition.Value ?? 0).DefaultIfEmpty(0).Max();

    // The work in one quest, in points: kills, bosses, maps, hand-ins, skills, loyalty.
    private static double OwnPoints(Quest quest, bool failsAnother, bool hiddenTrader)
    {
        var points = 0.0;
        var mapPoints = MapPoints.GetValueOrDefault(quest.Location ?? "");
        if (quest.TraderId == Lightkeeper)
        {
            points += 2;
        }

        if (hiddenTrader)
        {
            points += 1;
        }

        foreach (var condition in quest.Conditions?.AvailableForFinish ?? [])
        {
            switch (condition.ConditionType)
            {
                case "CounterCreator":
                    var inner = condition.Counter?.Conditions ?? [];
                    var oneRaid = condition.OneSessionOnly == true || inner.Any(c => c.ResetOnSessionEnd == true);
                    var count = Math.Max(1, condition.Value ?? 1);
                    var hasKills = false;
                    foreach (var c in inner)
                    {
                        switch (c.ConditionType)
                        {
                            case "Location":
                                foreach (var map in Targets(c.Target))
                                {
                                    mapPoints = Math.Max(mapPoints, MapPoints.GetValueOrDefault(map));
                                }

                                break;
                            case "Kills" or "Shots":
                                hasKills = true;
                                points += KillPoints(c, count, oneRaid) * (c.ConditionType == "Shots" ? 0.5 : 1);
                                break;
                            case "Equipment":
                            case "HealthEffect" or "HealthBuff" or "UnderArtilleryFire" or "Time":
                                points += 0.5;
                                break;
                        }
                    }

                    if (!hasKills && inner.Any(c => c.ConditionType == "ExitStatus"))
                    {
                        points += 0.3 * count;
                    }

                    if (oneRaid && !hasKills)
                    {
                        points += 1;
                    }

                    break;
                case "HandoverItem":
                    var targets = Targets(condition.Target).Where(MongoId.IsValidMongoId).Select(id => new MongoId(id)).ToList();
                    if (targets.Any(tpl => _itemHelper?.GetItem(tpl).Value?.Properties?.QuestItem == true))
                    {
                        // A quest item: a raid to its one spot to fetch it.
                        points += 1;
                        break;
                    }

                    if (targets.Count == 0 || _handbook is null)
                    {
                        break;
                    }

                    var amount = condition.Value ?? 1;
                    var roubles = targets.Average(tpl => _handbook.GetTemplatePrice(tpl)) * amount;
                    var foundInRaid = condition.OnlyFoundInRaid == true;
                    points += Math.Min(5, roubles / (foundInRaid ? 100_000 : 300_000)) + (foundInRaid && amount >= 5 ? 0.5 : 0);
                    break;
                case "LeaveItemAtLocation" or "PlaceBeacon":
                    points += 0.3;
                    break;
                case "WeaponAssembly":
                    points += 0.5;
                    break;
                case "Skill":
                    points += 0.4 * (condition.Value ?? 0);
                    break;
                case "TraderLoyalty":
                    points += Math.Max(0, (condition.Value ?? 1) - 1);
                    break;
            }
        }

        if (quest.Conditions?.Fail is { Count: > 0 })
        {
            points += 0.5;
        }

        if (failsAnother)
        {
            points += 1;
        }

        return points + mapPoints;
    }

    private static double KillPoints(QuestConditionCounterCondition kill, double count, bool oneRaid)
    {
        double points;
        var target = kill.Target?.Item ?? kill.Target?.List?.FirstOrDefault();
        if (kill.SavageRole is { Count: > 0 } roles)
        {
            var weight = roles.Max(role => RoleWeight.TryGetValue(role, out var w) ? w : role.StartsWith("follower", StringComparison.OrdinalIgnoreCase) ? 1.5 : 0.5);
            points = weight * Math.Pow(count, 0.6);
        }
        else
        {
            points = target switch
            {
                "AnyPmc" or "Usec" or "Bear" => 0.25,
                "Any" => 0.12,
                _ => 0.04
            } * count;
        }

        if (kill.BodyPart is { Count: > 0 } parts && parts.All(part => part == "Head"))
        {
            points *= 2;
        }

        var distance = kill.Distance?.Value ?? 0;
        points *= distance >= 100 ? 2 : distance >= 50 ? 1.5 : 1;
        if (kill.Weapon is { Count: > 0 } || kill.WeaponCaliber is { Count: > 0 } || kill.WeaponModsInclusive?.Any() == true)
        {
            points *= 1.3;
        }

        if (kill.EnemyEquipmentInclusive?.Any() == true || kill.EnemyHealthEffects is { Count: > 0 })
        {
            points *= 1.3;
        }

        if (kill.Daytime is { } daytime && (daytime.From is > 0 || daytime.To is > 0))
        {
            points *= 1.2;
        }

        if (oneRaid && count > 1)
        {
            points *= 2;
        }

        return points;
    }

    private static List<string> Targets(ListOrT<string>? target) =>
        target?.List ?? (target?.Item is { } one ? [one] : []);
}
