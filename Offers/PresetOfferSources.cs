using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Ragfair;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Services.Ragfair;
using BetterFlea.Prices;

namespace BetterFlea.Offers;

// Builds not made from scratch: meta builds, saved builds, looted guns, ground loot, deals.
internal static partial class PresetOfferGate
{
    private static SaveServer? _saveServer;
    private static int _deals;
    private static int _metaListed;
    private static int _looted;

    public static void BindSaves(SaveServer saveServer) => _saveServer = saveServer;

    private static readonly MongoId ModBase = new("5448fe124bdc2da5018b4567");

    private const int MetaCandidates = 30;

    private static readonly ConcurrentDictionary<MongoId, Lazy<List<Item>?>> ComputedMeta = new();

    // Saved builds by gun, all of them and the "Meta..." ones. Re-read every few minutes.
    private static (DateTime Read, SavedBuildsByGun Meta, SavedBuildsByGun All) _saved = (DateTime.MinValue, [], []);
    private static readonly object SavedLock = new();

    private static SavedBuildsByGun SavedMetaBuilds() => SavedBuilds().Meta;

    private static (SavedBuildsByGun Meta, SavedBuildsByGun All) SavedBuilds()
    {
        lock (SavedLock)
        {
            if (DateTime.UtcNow - _saved.Read < TimeSpan.FromMinutes(5))
            {
                return (_saved.Meta, _saved.All);
            }

            var meta = new SavedBuildsByGun();
            var all = new SavedBuildsByGun();
            foreach (var (_, profile) in _saveServer?.GetProfiles() ?? [])
            {
                foreach (var build in profile.UserBuildData?.WeaponBuilds ?? [])
                {
                    if (build.Items is not { Count: > 1 } items || items.FirstOrDefault(item => item.Id == build.Root) is not { } root)
                    {
                        continue;
                    }

                    var name = build.Name ?? "";
                    all.Add(root.Template, (name, root.Id, items));
                    if (name.StartsWith("meta", StringComparison.OrdinalIgnoreCase))
                    {
                        meta.Add(root.Template, (name, root.Id, items));
                    }
                }
            }

            if (all.Count > 0 && _saved.All.Count != all.Count)
            {
                DebugLog.Summary(() => $"Saved builds found for {all.Count} gun(s) ({meta.Count} with a Meta build): {string.Join(", ", all.Values.SelectMany(list => list).Select(build => build.Name))}");
            }

            _saved = (DateTime.UtcNow, meta, all);
            return (meta, all);
        }
    }

    private sealed class SavedBuildsByGun : Dictionary<MongoId, List<(string Name, MongoId Root, List<Item> Items)>>
    {
        public void Add(MongoId gun, (string Name, MongoId Root, List<Item> Items) build)
        {
            if (!TryGetValue(gun, out var list))
            {
                this[gun] = list = [];
            }

            list.Add(build);
        }
    }

    private static readonly MongoId MagazineBase = new("5448bc234bdc2d3c308b4569");

    // Exactly the default preset, usually with its mag.
    private static List<Item>? GroundLootBuild(Item gun, WeaponTierConfig tier)
    {
        if (_presets!.GetDefaultPreset(gun.Template)?.Items is not { Count: > 0 } preset || CopyBuild(preset, preset[0].Id, gun) is not { } copy)
        {
            return null;
        }

        if (Random.Shared.NextDouble() >= tier.Magazine)
        {
            RemoveBranches(copy, copy.Where(item => item.SlotId == "mod_magazine").ToList());
        }

        return EmptyRequiredSlot(copy, int.MaxValue) is null && MisplacedPart(copy) is null ? copy : null;
    }

    // Never full, and only flea-sellable rounds so banned ammo can't come in through a gun.
    private static void MaybeLoadMagazine(List<Item> built, double chance)
    {
        if (Random.Shared.NextDouble() >= chance
            || built.FirstOrDefault(item => item.SlotId == "mod_magazine" && _itemHelper!.IsOfBaseclass(item.Template, MagazineBase)) is not { } mag
            || built.Any(item => item.ParentId == mag.Id && item.SlotId == "cartridges"))
        {
            return;
        }

        var cartridges = _itemHelper!.GetItem(mag.Template).Value?.Properties?.Cartridges?.FirstOrDefault();
        var capacity = cartridges?.MaxCount ?? 0;

        // The chamber filter, not the mag's: a STANAG takes .300 BLK but an M4 doesn't fire it.
        var gun = _itemHelper.GetItem(built[0].Template).Value?.Properties;
        var chamber = gun?.Chambers?.FirstOrDefault()?.Properties?.Filters?.FirstOrDefault()?.Filter?.ToHashSet();
        bool GunFires(MongoId tpl) => chamber is { Count: > 0 }
            ? chamber.Contains(tpl)
            : _itemHelper.GetItem(tpl).Value?.Properties?.Caliber is { } caliber && caliber == gun?.AmmoCaliber;

        var rounds = (cartridges?.Properties?.Filters?.FirstOrDefault()?.Filter ?? [])
            .Where(tpl => _itemHelper.GetItem(tpl).Key && GunFires(tpl)
                && _ragfairServerHelper!.IsItemValidRagfairItem(_itemHelper.GetItem(tpl)) && _prices!.GetFleaPriceForItem(tpl) > 0)
            .ToList();
        if (capacity < 2 || rounds.Count == 0)
        {
            return;
        }

        var round = BuildQuality.Pick(rounds, rounds.Select(tpl => 1 / Math.Pow(_prices!.GetFleaPriceForItem(tpl), 0.7)).ToArray());
        if (round is not { } ammo)
        {
            return;
        }

        var count = (int)Math.Clamp(Math.Round(capacity * (0.15 + Random.Shared.NextDouble() * 0.75)), 1, capacity - 1);
        built.Add(_itemHelper.CreateCartridges(mag.Id, ammo, count, 0));
    }

    // A loose mag (one per offer) sometimes has rounds in it: full, half, one, any amount, or a scam of cheap
    // rounds under one good one, priced as if all were good. Never under the floor.
    public static void MaybeLoadLooseMagazine(CreateFleaOfferDetails details)
    {
        var chance = Settings.LoadedMagazineChance * LooseMagazineShare;
        if (details.Creator != OfferCreator.FakePlayer || details.Quantity != 1 || details.SellInOnePiece
            || details.Items is not { Count: 1 } items || !_itemHelper!.IsOfBaseclass(items[0].Template, MagazineBase)
            || details.BarterScheme is not { Count: 1 } scheme || scheme[0].Count is not { } count
            || !_itemHelper.IsOfBaseclass(scheme[0].Template, BaseClasses.MONEY) || Random.Shared.NextDouble() >= chance)
        {
            return;
        }

        var mag = items[0];
        var cartridges = _itemHelper.GetItem(mag.Template).Value?.Properties?.Cartridges?.FirstOrDefault();
        var capacity = (int)(cartridges?.MaxCount ?? 0);
        var fits = (cartridges?.Properties?.Filters?.FirstOrDefault()?.Filter ?? [])
            .Where(tpl => _itemHelper.GetItem(tpl).Key && _ragfairServerHelper!.IsItemValidRagfairItem(_itemHelper.GetItem(tpl))
                && _prices!.GetFleaPriceForItem(tpl) > 0)
            .ToList();
        if (capacity < 2 || fits.Count == 0)
        {
            return;
        }

        // One calibre: a STANAG takes 5.56 and .300 BLK, but nobody mixes them.
        var first = fits[Random.Shared.Next(fits.Count)];
        var calibre = _itemHelper.GetItem(first).Value?.Properties?.Caliber;
        var rounds = fits.Where(tpl => _itemHelper.GetItem(tpl).Value?.Properties?.Caliber == calibre).ToList();
        double Penetration(MongoId tpl) => _itemHelper.GetItem(tpl).Value?.Properties?.PenetrationPower ?? 0;
        MongoId? Cheapish() => BuildQuality.Pick(rounds, rounds.Select(tpl => 1 / Math.Pow(_prices!.GetFleaPriceForItem(tpl), 0.7)).ToArray());

        // Loaded first sits at the bottom, so the last stack is the round on top.
        var loads = new List<(MongoId Ammo, int Count)>();
        var roll = Random.Shared.NextDouble();
        var roubles = 0.0;
        if (roll < 0.15 && rounds.Count > 1)
        {
            var byPenetration = rounds.OrderBy(Penetration).ToList();
            var better = byPenetration.Skip(Math.Max(1, byPenetration.Count / 2)).ToList();
            var good = better[Random.Shared.Next(better.Count)];
            var total = Random.Shared.Next(Math.Max(2, capacity / 2), capacity + 1);
            loads.Add((byPenetration[0], total - 1));
            loads.Add((good, 1));
            roubles = total * _prices!.GetFleaPriceForItem(good) * (0.95 + Random.Shared.NextDouble() * 0.2);
        }
        else if (Cheapish() is { } ammo)
        {
            var amount = roll switch
            {
                < 0.4 => capacity,
                < 0.65 => Math.Max(1, capacity / 2),
                < 0.8 => 1,
                _ => Random.Shared.Next(1, capacity)
            };
            loads.Add((ammo, amount));
            if (Random.Shared.NextDouble() < PricedInShare)
            {
                roubles = amount * _prices!.GetFleaPriceForItem(ammo);
            }
        }

        for (var i = 0; i < loads.Count; i++)
        {
            items.Add(_itemHelper.CreateCartridges(mag.Id, loads[i].Ammo, loads[i].Count, i));
        }

        var floor = PriceGate.FloorForItems(items, scheme[0].Template);
        scheme[0].Count = Math.Max(Math.Round(count + BuildQuality.FromRoubles(roubles, scheme[0].Template)), Math.Ceiling(floor));
    }

    // Half a gun's chance; half the sellers price the rounds in.
    private const double LooseMagazineShare = 0.5;
    private const double PricedInShare = 0.5;

    // SPT prices each item once whatever its stack size, so add the rest of the rounds.
    public static void PriceLoadedAmmo(CreateFleaOfferDetails details)
    {
        if (details.Creator != OfferCreator.FakePlayer || details.Items is not { Count: > 1 } items || details.BarterScheme is not { Count: 1 } scheme
            || scheme[0].Count is not { } count || !_itemHelper!.IsOfBaseclass(scheme[0].Template, BaseClasses.MONEY))
        {
            return;
        }

        var extraRoubles = items.Where(item => item.SlotId == "cartridges" && item.Upd?.StackObjectsCount > 1)
            .Sum(item => (item.Upd!.StackObjectsCount!.Value - 1) * _prices!.GetFleaPriceForItem(item.Template));
        if (extraRoubles > 0)
        {
            scheme[0].Count = Math.Round(count + BuildQuality.FromRoubles(extraRoubles, scheme[0].Template));
        }
    }

    private static (List<Item> Build, string Origin)? PlayerBuildFor(Item gun)
    {
        if (!SavedBuilds().All.TryGetValue(gun.Template, out var saved) || saved.Count == 0)
        {
            return null;
        }

        var (name, root, items) = saved[Random.Shared.Next(saved.Count)];
        if (CopyBuild(items, root, gun) is not { } copy || MisplacedPart(copy) is not null)
        {
            return null;
        }

        var settings = _config!.Mod.GeneratedOffers.PlayerGuns;
        StripForResale(copy, settings.ScopeStripChance, settings.SuppressorStripChance);
        return (copy, $"player build \"{name}\"");
    }

    // Undone if it would leave a required slot empty.
    internal static void StripForResale(List<Item> built, double scopeChance, double suppressorChance)
    {
        var before = built.ToList();
        if (Random.Shared.NextDouble() < scopeChance)
        {
            foreach (var optic in built.Where(item => IsOptic(item.Template)).ToList())
            {
                if (built.Contains(optic))
                {
                    RemoveOpticBranch(built, optic);
                }
            }
        }

        if (Random.Shared.NextDouble() < suppressorChance)
        {
            RemoveBranches(built, built.Where(item => _itemHelper!.IsOfBaseclass(item.Template, SilencerBase)).ToList());
        }

        if (EmptyRequiredSlot(built, int.MaxValue) is not null || MisplacedPart(built) is not null)
        {
            built.Clear();
            built.AddRange(before);
        }
    }

    internal static List<Item>? LostGunListing(Item weapon, List<Item> inventory)
    {
        if (!_itemHelper!.IsOfBaseclass(weapon.Template, GunBase))
        {
            return null;
        }

        var gun = new Item
        {
            Id = new MongoId(),
            Template = weapon.Template,
            Upd = new Upd
            {
                StackObjectsCount = 1,
                Repairable = weapon.Upd?.Repairable is { } repairable
                    ? new UpdRepairable { Durability = repairable.Durability, MaxDurability = repairable.MaxDurability }
                    : null
            }
        };
        var copy = CopyBuild(inventory, weapon.Id, gun) ?? [gun];
        if (MisplacedPart(copy) is not null)
        {
            return null;
        }

        var settings = _config!.Mod.GeneratedOffers.PlayerGuns;
        StripForResale(copy, settings.ScopeStripChance, settings.SuppressorStripChance);
        return copy;
    }

    // A saved "Meta" build if there is one, otherwise the best of MetaCandidates. The computed one is kept
    // for the session so every meta listing of a gun matches.
    private static (List<Item> Build, string Origin)? MetaBuildFor(Item gun)
    {
        if (SavedMetaBuilds().TryGetValue(gun.Template, out var saved) && saved.Count > 0)
        {
            var (name, root, items) = saved[Random.Shared.Next(saved.Count)];
            if (CopyBuild(items, root, gun) is { } copy && MisplacedPart(copy) is null)
            {
                Interlocked.Increment(ref _metaListed);
                return (copy, $"meta \"{name}\"");
            }
        }

        var computed = ComputedMeta.GetOrAdd(gun.Template, tpl => new Lazy<List<Item>?>(() => ComputeMeta(tpl))).Value;
        if (computed is null || CopyBuild(computed, computed[0].Id, gun) is not { } build)
        {
            return null;
        }

        Interlocked.Increment(ref _metaListed);
        return (build, "meta");
    }

    // Hybrid, liked optic, usually a can.
    private static List<Item>? ComputeMeta(MongoId gunTpl)
    {
        var template = _itemHelper!.GetItem(gunTpl).Value;
        if (template?.Properties?.Slots is null)
        {
            return null;
        }

        var settings = Settings;
        var metaTier = settings.Modded with
        {
            StatFocus = 8, Factory = 0, Scope = 1, Magazine = 1, Suppressor = 0.85, StripMin = 0, StripMax = 0,
            ColourMatch = 1, BestOf = 1, GoodMagazineChance = 0
        };
        var group = BuildQuality.GroupOf(gunTpl);
        List<Item>? best = null;
        var bestScore = double.MinValue;
        for (var i = 0; i < MetaCandidates; i++)
        {
            var root = new Item { Id = new MongoId(), Template = gunTpl };
            if (GenerateBuild(root, template, settings, "modded", metaTier, BuildStyle.Hybrid) is not { } build)
            {
                continue;
            }

            var optic = build.Where(item => IsOptic(item.Template))
                .Select(item => BuildQuality.OpticRating(item.Template))
                .Select(rating => rating.Rating * BuildQuality.Affinity(rating.Kind, group))
                .DefaultIfEmpty(0).Max();
            var score = BuildQuality.Score(build, BuildStyle.Hybrid) + 10 * optic;
            if (score > bestScore)
            {
                best = build;
                bestScore = score;
            }
        }

        if (best is not null)
        {
            DebugLog.Summary(() => $"Meta build for {template.Name}: " + string.Join(", ", best.Skip(1).Select(part => _itemHelper.GetItem(part.Template).Value?.Name)));
        }

        return best;
    }

    // New ids, same parts in the same slots. Ammo and non-weapon parts left out.
    private static List<Item>? CopyBuild(List<Item> items, MongoId rootId, Item gun)
    {
        var ids = new Dictionary<MongoId, MongoId> { [rootId] = gun.Id };
        var copy = new List<Item> { gun };
        var pending = items.Where(item => item.Id != rootId).ToList();
        var progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            foreach (var item in pending.ToList())
            {
                if (item.ParentId is not { } parentId || !ids.TryGetValue(parentId, out var newParent))
                {
                    continue;
                }

                pending.Remove(item);
                progress = true;
                if (!_itemHelper!.IsOfBaseclass(item.Template, ModBase))
                {
                    continue;
                }

                var id = new MongoId();
                ids[item.Id] = id;
                copy.Add(new Item { Id = id, Template = item.Template, ParentId = newParent, SlotId = item.SlotId });
            }
        }

        return copy.Count > 1 ? copy : null;
    }

    // Mostly scavs. No PMCs, raiders, rogues or event bots.
    private static readonly (string Prefix, double Weight)[] LootRoles =
    [
        ("assault", 60), ("marksman", 4), ("boss", 2), ("follower", 2.5)
    ];

    private static readonly string[] SkippedRoles = ["bossboarsniper", "bosskillaagro", "bosstagillaagro"];

    private static readonly EquipmentSlots[] WeaponSlots = [EquipmentSlots.FirstPrimaryWeapon, EquipmentSlots.SecondPrimaryWeapon, EquipmentSlots.Holster];

    private static readonly Lazy<Dictionary<MongoId, List<(string Role, double Weight)>>> LootRolesByGun = new(() =>
    {
        var result = new Dictionary<MongoId, List<(string Role, double Weight)>>();
        foreach (var (role, bot) in _botTable!.Types)
        {
            var weight = LootRoles.FirstOrDefault(entry => role.StartsWith(entry.Prefix, StringComparison.OrdinalIgnoreCase)).Weight;
            if (weight <= 0 || SkippedRoles.Contains(role, StringComparer.OrdinalIgnoreCase) || bot?.BotInventory?.Equipment is not { } equipment)
            {
                continue;
            }

            foreach (var slot in WeaponSlots)
            {
                if (!equipment.TryGetValue(slot, out var weapons))
                {
                    continue;
                }

                foreach (var gun in weapons.Keys)
                {
                    if (!result.TryGetValue(gun, out var roles))
                    {
                        result[gun] = roles = [];
                    }

                    if (!roles.Any(entry => entry.Role == role))
                    {
                        roles.Add((role, weight));
                    }
                }
            }
        }

        return result;
    });

    // Each slot filled from the role's own parts at the role's own chance, like SPT gears bots.
    private static (List<Item> Build, string Origin)? LootedBuild(Item gun, TemplateItem template)
    {
        if (!LootRolesByGun.Value.TryGetValue(gun.Template, out var roles) || roles.Count == 0)
        {
            return null;
        }

        var total = roles.Sum(entry => entry.Weight);
        var roll = Random.Shared.NextDouble() * total;
        var role = roles[^1].Role;
        foreach (var (name, weight) in roles)
        {
            roll -= weight;
            if (roll < 0)
            {
                role = name;
                break;
            }
        }

        var bot = _botTable!.Types[role];
        var mods = bot?.BotInventory?.Mods;
        var chances = bot?.BotChances?.WeaponModsChances ?? [];
        if (mods is null)
        {
            return null;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var built = new List<Item> { gun };
            AddBotParts(gun, gun.Template, template, built, mods, chances, 1);
            RemoveEmptyMounts(built, Settings.EmptyMountChance);
            if (MisplacedPart(built) is null && EmptyRequiredSlot(built, Settings.MaxAttachmentDepth) is null)
            {
                Interlocked.Increment(ref _looted);
                return (built, $"looted off {role}");
            }
        }

        return null;
    }

    private static void AddBotParts(Item parent, MongoId parentTpl, TemplateItem parentTemplate, List<Item> built,
        Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> mods, Dictionary<string, double> chances, int depth)
    {
        if (depth > Settings.MaxAttachmentDepth || parentTemplate.Properties?.Slots is null || !mods.TryGetValue(parentTpl, out var pool))
        {
            return;
        }

        foreach (var slot in parentTemplate.Properties.Slots.OrderBy(slot => SlotRank(slot.Name)))
        {
            if (slot.Name is not { } slotName || slotName.StartsWith("camora", StringComparison.Ordinal)
                || !pool.TryGetValue(slotName, out var options) || options.Count == 0)
            {
                continue;
            }

            if (slot.Required != true && Random.Shared.NextDouble() * 100 >= BotSlotChance(chances, slotName))
            {
                continue;
            }

            var fitting = options.Where(tpl => SlotAccepts(slot, tpl)
                && _ragfairServerHelper!.IsItemValidRagfairItem(_itemHelper!.GetItem(tpl))
                && !_botGenerator!.IsItemIncompatibleWithCurrentItems(built, tpl, slotName).Incompatible.GetValueOrDefault(false)).ToList();
            if (fitting.Count == 0)
            {
                continue;
            }

            var pick = fitting[Random.Shared.Next(fitting.Count)];
            var part = new Item { Id = new MongoId(), Template = pick, ParentId = parent.Id, SlotId = slotName };
            built.Add(part);
            if (_itemHelper!.GetItem(pick).Value is { } pickTemplate)
            {
                AddBotParts(part, pick, pickTemplate, built, mods, chances, depth + 1);
            }
        }
    }

    // Falls back to the slot without its number (mod_mount_001 -> mod_mount), then 50.
    private static double BotSlotChance(Dictionary<string, double> chances, string slotName)
    {
        if (chances.TryGetValue(slotName, out var chance))
        {
            return chance;
        }

        var baseName = System.Text.RegularExpressions.Regex.Replace(slotName, @"_\d+$", "");
        return chances.TryGetValue(baseName, out chance) ? chance : 50;
    }

    // Never below the price floor (what traders pay for the parts plus the margin), so a deal can't be flipped.
    public static bool MaybeDeal(CreateFleaOfferDetails details)
    {
        var settings = Settings;
        var items = details.Items;
        if (details.Creator != OfferCreator.FakePlayer || items is null || items.Count < 2 || details.BarterScheme is null
            || settings.BuildDealChance <= 0 || !(_itemHelper!.IsOfBaseclass(items[0].Template, GunBase) || ArmorOffers.IsCarrier(items[0].Template))
            || Random.Shared.NextDouble() >= settings.BuildDealChance)
        {
            return false;
        }

        if (details.BarterScheme is not { Count: 1 } scheme || scheme[0].Count is not { } price || price <= 0)
        {
            return false;
        }

        var low = Math.Clamp(settings.BuildDealMinDiscount, 0, 0.9);
        var high = Math.Clamp(Math.Max(settings.BuildDealMaxDiscount, low), 0, 0.9);
        var discount = low + Random.Shared.NextDouble() * (high - low);
        var floor = PriceGate.FloorForItems(items, scheme[0].Template);
        var ratio = Math.Max(1 - discount, floor / price);
        if (ratio >= 1)
        {
            return false;
        }

        foreach (var barter in details.BarterScheme.Where(barter => barter.Count.HasValue && _itemHelper.IsOfBaseclass(barter.Template, BaseClasses.MONEY)))
        {
            barter.Count = Math.Max(1, Math.Round(barter.Count!.Value * ratio));
        }

        if (Interlocked.Increment(ref _deals) <= 5)
        {
            DebugLog.Summary(() => $"Deal: {_itemHelper.GetItem(items[0].Template).Value?.Name} with {items.Count - 1} part(s) listed at {ratio:P0} of its parts' value");
        }

        return true;
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class LostGunOffersLoad(
    ConfigManager configManager,
    RagfairOfferGenerator ragfairOfferGenerator,
    RagfairOfferService ragfairOfferService,
    RagfairPriceService ragfairPriceService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        LostGunOffers.Bind(configManager, ragfairOfferGenerator, ragfairOfferService, ragfairPriceService);
        new LostGunPatch().Enable();
        return Task.CompletedTask;
    }
}

// Saved in data/lost-guns.json. After a restart waiting guns still turn up and listed ones go back on
// with the time they had left. Sold or expired ones are dropped, newest MaxGuns kept.
internal static class LostGunOffers
{
    private const string FileName = "lost-guns.json";
    private const int MaxGuns = 100;

    private static ConfigManager? _config;
    private static RagfairOfferGenerator? _generator;
    private static RagfairOfferService? _offers;
    private static RagfairPriceService? _prices;
    private static Timer? _timer;

    private static List<LostGun> _guns = [];

    private static readonly string[] WeaponSlots = ["FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster"];

    public sealed class LostGun
    {
        public List<Item> Items { get; set; } = [];
        public string? Killer { get; set; }
        public string Name { get; set; } = string.Empty;
        public long ListAt { get; set; }

        // Set once it's on the flea.
        public string? OfferId { get; set; }
        public long EndsAt { get; set; }
    }

    public static void Bind(ConfigManager configManager, RagfairOfferGenerator generator, RagfairOfferService offers, RagfairPriceService prices)
    {
        _config = configManager;
        _generator = generator;
        _offers = offers;
        _prices = prices;

        // Offers don't survive a restart, so listed ones go on again.
        _guns = DataFiles.Read<List<LostGun>>(FileName) ?? [];
        foreach (var gun in _guns.Where(gun => gun.OfferId is not null))
        {
            gun.OfferId = null;
            gun.ListAt = 0;
        }

        _timer ??= new Timer(_ => ListDue(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
    }

    private static PlayerGunsConfig Settings => _config!.Mod.GeneratedOffers.PlayerGuns;

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // Before their gear is taken away.
    public static void PlayerDied(PmcData profile)
    {
        var settings = Settings;
        var inventory = profile.Inventory?.Items;
        if (settings.LostGunChance <= 0 || inventory is null || profile.Inventory?.Equipment is not { } equipment)
        {
            return;
        }

        var killer = profile.Stats?.Eft?.Aggressor?.Name;
        var added = false;
        foreach (var weapon in inventory.Where(item => item.ParentId == equipment && WeaponSlots.Contains(item.SlotId)).ToList())
        {
            if (Random.Shared.NextDouble() >= settings.LostGunChance || PresetOfferGate.LostGunListing(weapon, inventory) is not { } listing)
            {
                continue;
            }

            var low = Math.Max(0, settings.LostGunMinMinutes);
            var minutes = low + Random.Shared.NextDouble() * Math.Max(0, settings.LostGunMaxMinutes - low);
            var name = PresetOfferGate.ItemName(weapon.Template);
            lock (_guns)
            {
                _guns.Add(new LostGun { Items = listing, Killer = killer, Name = name, ListAt = Now() + (long)(minutes * 60) });
            }

            added = true;
            DebugLog.Summary(() => $"Lost gun: {profile.Info?.Nickname} died with {name} ({listing.Count - 1} part(s)); it goes on the flea in {minutes:0.#} min{(killer is null ? "" : $", sold by {killer}")}.");
        }

        if (added)
        {
            Save();
        }
    }

    private static void ListDue()
    {
        // Wait for the flea to be made at startup.
        if (_offers is null || _offers.GetOffers().Count == 0)
        {
            return;
        }

        var changed = false;
        var now = Now();
        lock (_guns)
        {
            foreach (var gun in _guns.ToList())
            {
                if (gun.OfferId is { } offerId)
                {
                    // Bought or expired.
                    if (gun.EndsAt <= now || _offers.GetOfferByOfferId(new MongoId(offerId)) is null)
                    {
                        _guns.Remove(gun);
                        changed = true;
                    }

                    continue;
                }

                if (gun.ListAt > now)
                {
                    continue;
                }

                if (gun.EndsAt > 0 && gun.EndsAt <= now)
                {
                    _guns.Remove(gun);
                    changed = true;
                    continue;
                }

                try
                {
                    List(gun, now);
                }
                catch (Exception exception)
                {
                    _guns.Remove(gun);
                    DebugLog.Summary(() => $"Lost gun: listing {gun.Name} failed: {exception.GetBaseException().Message}");
                }

                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    private static void List(LostGun gun, long now)
    {
        var price = _prices!.GetDynamicOfferPriceForOffer(gun.Items, Money.ROUBLES, false);
        var details = new CreateFleaOfferDetails
        {
            UserId = new MongoId(),
            Time = now,
            Items = gun.Items,
            BarterScheme = [new BarterScheme { Count = Math.Max(1, Math.Round(price)), Template = Money.ROUBLES }],
            LoyalLevel = 1,
            Quantity = 1,
            Creator = OfferCreator.FakePlayer,
            SellInOnePiece = false
        };

        // Goes on regardless of the cap, and isn't a generated build.
        ListingCountGate.NoCap = true;
        PresetOfferGate.ForgetBuildKind();
        try
        {
            var offer = _generator!.CreateAndAddFleaOffer(details);
            OnDemandOffers.Keep(offer.Id);
            if (Settings.KillerAsSeller && !string.IsNullOrWhiteSpace(gun.Killer) && offer.User is { } user)
            {
                // The killer's own rating and badge, not the generated name's.
                user.Nickname = gun.Killer;
                user.MemberType = user.SelectedMemberCategory = SPTarkov.Server.Core.Models.Enums.MemberCategory.Default;
                OfferLifecycle.SetSeller(offer);
            }

            // Back after a restart: the time it had left.
            if (gun.EndsAt > now)
            {
                offer.EndTime = gun.EndsAt;
            }

            gun.EndsAt = offer.EndTime ?? now + 24 * 3600;
            gun.OfferId = offer.Id.ToString();
            DebugLog.Summary(() => $"Lost gun: {gun.Name} is on the flea for {offer.RequirementsCost:0} RUB, sold by {offer.User?.Nickname}.");
        }
        finally
        {
            ListingCountGate.NoCap = false;
        }
    }

    private static void Save()
    {
        List<LostGun> copy;
        lock (_guns)
        {
            if (_guns.Count > MaxGuns)
            {
                _guns.RemoveRange(0, _guns.Count - MaxGuns);
            }

            copy = _guns.ToList();
        }

        DataFiles.Write(FileName, copy);
    }
}
public class LostGunPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod("HandlePostRaidPmc", BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPrefix]
    public static void Prefix(bool isDead, EndLocalRaidRequestData request)
    {
        if (!isDead || request.Results?.Profile is not { } profile)
        {
            return;
        }

        try
        {
            LostGunOffers.PlayerDied(profile);
        }
        catch (Exception exception)
        {
            DebugLog.Summary(() => $"Lost gun: couldn't read the raid's guns: {exception.GetBaseException().Message}");
        }
    }
}
