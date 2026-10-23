using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Services.Ragfair;

namespace BetterFlea.Offers;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class PresetOffersPatchesLoad(
    ConfigManager configManager,
    PresetHelper presetHelper,
    RagfairPriceService ragfairPriceService,
    RagfairServerHelper ragfairServerHelper,
    BotGeneratorHelper botGeneratorHelper,
    BotTable botTable,
    TradersTable tradersTable,
    LocaleService localeService,
    SaveServer saveServer,
    ItemHelper itemHelper,
    SPTarkov.Server.Core.Helpers.Profile.HandbookHelper handbookHelper) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        PresetOfferGate.Bind(configManager, presetHelper, ragfairPriceService, ragfairServerHelper, botGeneratorHelper, botTable, itemHelper);
        BuildQuality.Bind(itemHelper, ragfairPriceService, tradersTable, handbookHelper);
        BuildQuality.BindNames(() => localeService.GetLocaleDb("en"));
        PresetOfferGate.BindSaves(saveServer);
        new CustomBuildPatch().Enable();
        new RandomBuildPatch().Enable();
        new OfferPricingPatch().Enable();
        DebugLog.Summary(() => "Preset patches enabled.");
        return Task.CompletedTask;
    }
}

internal static partial class PresetOfferGate
{
    private static ConfigManager? _config;
    private static PresetHelper? _presets;
    private static RagfairPriceService? _prices;
    private static RagfairServerHelper? _ragfairServerHelper;
    private static BotGeneratorHelper? _botGenerator;
    private static BotTable? _botTable;
    private static ItemHelper? _itemHelper;
    private static int _built;
    private static int _missingRequired;
    private static int _invalidBuilds;
    private static int _gaveUp;
    private static int _misplaced;

    private const int BuildAttempts = 6;

    private const double CheapMagChance = 0.7;

    private static readonly MongoId GunBase = new("5422acb9af1c889c16000029");

    private static readonly Lazy<HashSet<MongoId>> DefaultPresetIds = new(() =>
        _presets!.GetDefaultPresets().Keys.ToHashSet());

    public static void Bind(
        ConfigManager configManager,
        PresetHelper presetHelper,
        RagfairPriceService ragfairPriceService,
        RagfairServerHelper ragfairServerHelper,
        BotGeneratorHelper botGeneratorHelper,
        BotTable botTable,
        ItemHelper itemHelper)
    {
        _config = configManager;
        _presets = presetHelper;
        _prices = ragfairPriceService;
        _ragfairServerHelper = ragfairServerHelper;
        _botGenerator = botGeneratorHelper;
        _botTable = botTable;
        _itemHelper = itemHelper;
    }

    // The last build kind on this thread, for the condition roll that follows in the same offer.
    [ThreadStatic]
    private static string? _lastBuildKind;

    public static void ForgetBuildKind() => _lastBuildKind = null;

    public static string ItemName(MongoId tpl) => _itemHelper?.GetItem(tpl).Value?.Name ?? tpl.ToString();

    public static string? TakeBuildKind() => _lastBuildKind;

    // Plain guns are the same few parts, so prices bunch up without this.
    public static void ScatterPlainGunPrice(CreateFleaOfferDetails details)
    {
        if (details.Creator != OfferCreator.FakePlayer || _lastBuildKind is not ("ground" or "stock")
            || details.Items is not { Count: > 0 } items || details.BarterScheme is not { Count: 1 } scheme
            || scheme[0].Count is not { } count || !_itemHelper!.IsOfBaseclass(scheme[0].Template, BaseClasses.MONEY))
        {
            return;
        }

        var factor = 0.82 + Random.Shared.NextDouble() * 0.4;
        var floor = BetterFlea.Prices.PriceGate.FloorForItems(items, scheme[0].Template);
        scheme[0].Count = Math.Max(Math.Round(count * factor), Math.Ceiling(floor));
    }

    // SPT prices a plain gun by its parts, so the price follows whatever build it gets here.
    public static void MaybeRandomBuild(List<Item> items)
    {
        var settings = Settings;
        var gun = items.FirstOrDefault();
        if (gun is null || settings.RandomBuildChance <= 0
            || Random.Shared.NextDouble() >= settings.RandomBuildChance || !_itemHelper!.IsOfBaseclass(gun.Template, GunBase))
        {
            return;
        }

        var template = _itemHelper.GetItem(gun.Template).Value;
        if (template?.Properties?.Slots is null)
        {
            return;
        }

        var (tierName, tier) = PickTier(settings, gun.Template);

        var style = tierName == "budget" ? BuildStyle.Budget : BuildQuality.PickStyle(settings.BuildStyles);
        string? origin = null;
        var kind = tierName;
        List<Item>? built = null;
        if (Random.Shared.NextDouble() < _config!.Mod.GeneratedOffers.PlayerGuns.SavedBuildChance && PlayerBuildFor(gun) is { } saved)
        {
            (built, origin) = saved;
            tierName = kind = "player";
        }
        else if (tierName == "ground")
        {
            if (GroundLootBuild(gun, tier) is { } found)
            {
                (built, origin) = (found, "ground loot");
            }
            else
            {
                // No factory build to copy.
                (tierName, tier) = ("stock", settings.Stock);
                kind = tierName;
            }
        }
        else if (tierName == "modded" && Random.Shared.NextDouble() < settings.MetaBuildChance && MetaBuildFor(gun) is { } meta)
        {
            (built, origin) = meta;
            kind = "meta";
        }
        else if (tierName == "found" && Random.Shared.NextDouble() < settings.LootedBuildChance && LootedBuild(gun, template) is { } looted)
        {
            (built, origin) = looted;
            kind = "looted";
        }

        built ??= GenerateBuild(gun, template, settings, tierName, tier, style);
        if (built is null)
        {
            if (Interlocked.Increment(ref _gaveUp) <= 20)
            {
                DebugLog.Summary(() => $"Random build gave up for {_itemHelper.GetItem(gun.Template).Value?.Name} ({tierName}): a required part never fitted, kept the original listing.");
            }

            return;
        }

        // Priced by parts, not as a preset.
        if (gun.Upd is not null)
        {
            gun.Upd.SptPresetId = null;
        }

        if (tierName == "broken")
        {
            BreakDown(built, gun, settings.BrokenMaxParts);
            RemoveEmptyMounts(built, settings.EmptyMountChance);
        }

        if (tierName == "gutted")
        {
            Gut(built, gun);
        }

        // A part missing from the database makes the client throw.
        var invalid = built.FirstOrDefault(item => item.Template.IsEmpty || !_itemHelper.GetItem(item.Template).Key
            || _itemHelper.GetItem(item.Template).Value?.Properties is null);
        if (invalid is not null)
        {
            if (Interlocked.Increment(ref _invalidBuilds) <= 10)
            {
                DebugLog.Summary(() =>
                {
                    var found = _itemHelper.GetItem(invalid.Template);
                    return $"Random build skipped for {gun.Template}: slot {invalid.SlotId} part '{invalid.Template}' empty={invalid.Template.IsEmpty} key={found.Key} props={found.Value?.Properties is not null} name={found.Value?.Name}";
                });
            }

            return;
        }

        if (MisplacedPart(built) is { } wrong)
        {
            DebugLog.Summary(() => $"Random build dropped for {_itemHelper.GetItem(gun.Template).Value?.Name}: {wrong}");
            return;
        }

        MaybeLoadMagazine(built, settings.LoadedMagazineChance);

        items.Clear();
        items.AddRange(built);
        _lastBuildKind = kind;
        var count = Interlocked.Increment(ref _built);
        if (count <= 5 || count % 50 == 0)
        {
            var (ergo, recoil, vertical, horizontal) = BuildQuality.Stats(built);
            var label = origin is null ? tierName : $"{tierName}, {origin}";
            DebugLog.Summary(() => $"Random build ({label}): {_itemHelper.GetItem(gun.Template).Value?.Name} [{style} ergo {ergo:0} recoil {vertical:0}/{horizontal:0} {recoil:+0;-0;0}%] with {built.Count - 1} attachment(s): "
                + string.Join(", ", built.Skip(1).Select(part => $"{part.SlotId}={_itemHelper.GetItem(part.Template).Value?.Name}")));
        }

        if (count % 1000 == 0)
        {
            DebugLog.Summary(() => $"Builds so far: {count}, meta copies {_metaListed}, looted {_looted}, deals {_deals}");
        }
    }

    // A part can block a required slot further down (long handguard, sawn-off barrel), so retry with new picks.
    // If no try gets an optic the last complete build is used anyway.
    private static List<Item>? GenerateBuild(Item gun, TemplateItem template, PresetOffersConfig settings, string tierName, WeaponTierConfig tier, BuildStyle style)
    {
        var factory = FactoryParts(gun.Template);
        var factoryOptic = factory.Values.Any(IsOptic);
        var needsOptic = tierName == "modded";
        var wanted = Math.Max(1, tier.BestOf);
        var finished = new List<List<Item>>();
        List<Item>? withoutOptic = null;
        for (var attempt = 0; attempt < BuildAttempts + wanted - 1 && finished.Count < wanted; attempt++)
        {
            var state = new BuildState(new List<Item> { gun }, settings, tierName, tier, factory)
            {
                Style = style,
                Colour = tier.ColourMatch >= 0.5 && Random.Shared.NextDouble() < settings.TanBuildChance ? "tan" : null,
                BackupSight = Random.Shared.NextDouble() < settings.BackupSightChance,
                WantsSuppressor = Random.Shared.NextDouble() < tier.Suppressor,
                WantsOptic = factoryOptic || Random.Shared.NextDouble() < tier.Scope,
                GoodMagazine = Random.Shared.NextDouble() < tier.GoodMagazineChance,
                ZoomCap = ZoomCapFor(gun.Template),
                Group = BuildQuality.GroupOf(gun.Template),
                BipodGun = _itemHelper!.IsOfBaseclasses(gun.Template, [SniperBase, MarksmanBase, MachineGunBase])
            };
            AddAttachments(gun, gun.Template, template, state, 1);
            if (state.Missing > 0)
            {
                continue;
            }

            if (state.WantsOptic)
            {
                PlaceOptics(state);
            }

            TidyOptics(state);
            var hasOptic = state.Built.Any(item => IsOptic(item.Template));

            Strip(state);
            PairIronSights(state);
            RemoveEmptyMounts(state.Built, tierName == "modded" ? 0 : settings.EmptyMountChance);

            // Scope sold, rings kept.
            if (tierName is "stock" or "found" && !state.Built.Any(item => IsOptic(item.Template))
                && Random.Shared.NextDouble() < settings.EmptyRingsChance)
            {
                AddEmptyRings(state);
            }

            // The game shows an empty required slot in red. Only broken-down guns are allowed one.
            if (MisplacedPart(state.Built) is { } misplaced)
            {
                if (Interlocked.Increment(ref _misplaced) <= 30)
                {
                    DebugLog.Summary(() => $"Misplaced part on {_itemHelper.GetItem(gun.Template).Value?.Name} ({tierName}): {misplaced}, trying again.");
                }

                continue;
            }

            if (EmptyRequiredSlot(state.Built, settings.MaxAttachmentDepth) is { } empty)
            {
                if (Interlocked.Increment(ref _missingRequired) <= 30)
                {
                    DebugLog.Summary(() => $"Required slot {empty} empty after building {_itemHelper.GetItem(gun.Template).Value?.Name} ({tierName}), trying again.");
                }

                continue;
            }

            if (needsOptic && !hasOptic)
            {
                withoutOptic = state.Built;
                continue;
            }

            finished.Add(state.Built);
        }

        return finished.Count > 0 ? finished.MaxBy(build => BuildQuality.Score(build, style)) : withoutOptic;
    }

    // Parts bots carry, from SPT's bot data, by weapon or part then slot.
    private static readonly Lazy<Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>> CuratedMods = new(BuildCuratedMods);

    private static Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> BuildCuratedMods()
    {
        var result = new Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>();
        foreach (var bot in _botTable!.Types.Values)
        {
            foreach (var (parent, slots) in bot?.BotInventory?.Mods ?? [])
            {
                if (!result.TryGetValue(parent, out var bySlot))
                {
                    result[parent] = bySlot = new Dictionary<string, HashSet<MongoId>>();
                }

                foreach (var (slotName, parts) in slots)
                {
                    if (!bySlot.TryGetValue(slotName, out var set))
                    {
                        bySlot[slotName] = set = new HashSet<MongoId>();
                    }

                    set.UnionWith(parts);
                }
            }
        }

        return result;
    }

    // Barrel first since it decides the build, then SPT's bot generator order so blocking parts go on early.
    private static int SlotRank(string slotName) => slotName switch
    {
        "mod_barrel" => 0,
        "mod_handguard" => 1,
        "mod_mount_001" => 2,
        "mod_reciever" => 3,
        "mod_pistol_grip" => 4,
        "mod_gas_block" => 5,
        "mod_stock" => 6,
        "mod_mount" => 7,
        "mod_scope" => 8,
        _ => 9
    };

    private static readonly MongoId MountBase = new("55818b224bdc2dde698b456f");

    private static void BreakDown(List<Item> built, Item gun, int maxParts)
    {
        var parts = built.Where(item => item.ParentId == gun.Id).ToList();
        if (parts.Count == 0 || maxParts <= 0)
        {
            return;
        }

        var count = Random.Shared.Next(1, Math.Min(maxParts, parts.Count) + 1);
        var gone = new HashSet<MongoId>();
        foreach (var part in parts.OrderBy(_ => Random.Shared.Next()).Take(count))
        {
            var pending = new Stack<MongoId>();
            pending.Push(part.Id);
            while (pending.TryPop(out var id))
            {
                if (!gone.Add(id))
                {
                    continue;
                }

                foreach (var child in built.Where(item => item.ParentId == id))
                {
                    pending.Push(child.Id);
                }
            }
        }

        built.RemoveAll(item => gone.Contains(item.Id));
    }

    // Just the upper, just the lower, or the bare gun.
    private static void Gut(List<Item> built, Item gun)
    {
        bool IsUpper(Item item) => item.SlotId is "mod_reciever" or "mod_barrel";
        bool IsLower(Item item) => item.SlotId is { } slot
            && (slot.StartsWith("mod_pistol_grip", StringComparison.Ordinal) || slot == "mod_pistolgrip" || slot.StartsWith("mod_stock", StringComparison.Ordinal));

        List<Item> Keep(Func<Item, bool> wanted)
        {
            var kept = new HashSet<MongoId> { gun.Id };
            bool added;
            do
            {
                added = false;
                foreach (var item in built.Where(item => !kept.Contains(item.Id) && item.ParentId is { } parentId && kept.Contains(parentId) && wanted(item)))
                {
                    added |= kept.Add(item.Id);
                }
            }
            while (added);

            return built.Where(item => kept.Contains(item.Id)).ToList();
        }

        var choices = new[] { Keep(IsUpper), Keep(IsLower), Keep(_ => false) }
            .Where(choice => choice.Count < built.Count)
            .GroupBy(choice => choice.Count)
            .Select(group => group.First())
            .ToList();
        if (choices.Count == 0)
        {
            return;
        }

        var pick = choices[Random.Shared.Next(choices.Count)];
        built.RemoveAll(item => !pick.Contains(item));
    }

    // Nobody leaves an empty rail section on. Scope rings sometimes stay. Mounts in required slots and
    // slotless "mounts" (the G36 magwell) stay.
    private static void RemoveEmptyMounts(List<Item> built, double keepScopeMountChance)
    {
        var kept = new HashSet<MongoId>();
        while (true)
        {
            var byId = built.ToDictionary(item => item.Id);
            var empty = built.FirstOrDefault(item =>
                !kept.Contains(item.Id)
                && item.ParentId is not null
                && _itemHelper!.IsOfBaseclass(item.Template, MountBase)
                && _itemHelper.GetItem(item.Template).Value?.Properties?.Slots?.Any() == true
                && !IsInRequiredSlot(item, byId)
                && !built.Any(child => child.ParentId == item.Id));
            if (empty is null)
            {
                return;
            }

            if ((empty.SlotId ?? "").StartsWith("mod_scope", StringComparison.Ordinal) && Random.Shared.NextDouble() < keepScopeMountChance)
            {
                kept.Add(empty.Id);
                continue;
            }

            built.Remove(empty);
        }
    }

    private static readonly MongoId MuzzleBase = new("5448fe394bdc2d0d028b456c");
    private static readonly MongoId SilencerBase = new("550aa4cd4bdc2dd8348b456c");
    private static readonly MongoId ForegripBase = new("55818af64bdc2d5b648b4570");
    private static readonly MongoId TacticalBase = new("55818b164bdc2ddc698b456c");

    // A can, or a muzzle part a can mounts on (directly or through one adapter).
    private static readonly ConcurrentDictionary<MongoId, bool> CanPaths = new();

    private static bool LeadsToSuppressor(MongoId tpl) => LeadsToSuppressorWithin(tpl, 3);

    private static bool LeadsToSuppressorWithin(MongoId tpl, int depth)
    {
        if (tpl.IsEmpty || _itemHelper!.IsOfBaseclass(tpl, SilencerBase))
        {
            return !tpl.IsEmpty;
        }

        return depth > 0 && CanPaths.GetOrAdd(tpl, id => (_itemHelper.GetItem(id).Value?.Properties?.Slots ?? [])
            .Where(slot => slot.Name?.StartsWith("mod_muzzle", StringComparison.Ordinal) == true)
            .Any(slot => slot.Properties?.Filters?.FirstOrDefault()?.Filter?.Any(child => LeadsToSuppressorWithin(child, depth - 1)) == true));
    }

    private static bool Strippable(string slotName) =>
        slotName.StartsWith("mod_scope", StringComparison.Ordinal) || slotName.StartsWith("mod_mount", StringComparison.Ordinal)
        || slotName.StartsWith("mod_tactical", StringComparison.Ordinal) || slotName.StartsWith("mod_foregrip", StringComparison.Ordinal)
        || slotName == "mod_magazine" || slotName == "mod_launcher" || slotName == "mod_flashlight";

    // Scope, can, sometimes the laser, foregrip, then good stock and grip swapped for cheap ones.
    private static void Strip(BuildState state)
    {
        var tier = state.Tier;
        var steps = Random.Shared.Next(Math.Max(0, tier.StripMin), Math.Max(tier.StripMin, tier.StripMax) + 1);
        var built = state.Built;
        for (var step = 0; step < steps; step++)
        {
            switch (step)
            {
                case 0:
                    RemoveBranches(built, built.Where(item => IsOptic(item.Template)));
                    break;
                case 1:
                    foreach (var can in built.Where(item => _itemHelper!.IsOfBaseclass(item.Template, SilencerBase)).ToList())
                    {
                        RemoveBranches(built, [can]);
                        PutBackFactoryMuzzle(state, can);
                    }

                    break;
                case 2:
                    if (Random.Shared.NextDouble() < 0.5)
                    {
                        RemoveBranches(built, built.Where(item => _itemHelper!.IsOfBaseclass(item.Template, TacticalBase)));
                    }

                    break;
                case 3:
                    RemoveBranches(built, built.Where(item => _itemHelper!.IsOfBaseclass(item.Template, ForegripBase)));
                    break;
                case 4:
                    CheapenStocks(state);
                    break;
            }
        }
    }

    // Every part placed on a build goes through this, whatever list it came from.
    private static bool SlotAccepts(Slot? slot, MongoId tpl)
    {
        // Mods sometimes list parts that aren't installed.
        var filters = slot?.Properties?.Filters;
        if (filters is null || tpl.IsEmpty || !_itemHelper!.GetItem(tpl).Key)
        {
            return false;
        }

        return filters.Any(filter => filter.Filter is { } ids && ids.Any(id => id == tpl || _itemHelper!.IsOfBaseclass(tpl, id)));
    }

    private static Slot? SlotOn(MongoId parentTpl, string? slotName) =>
        _itemHelper!.GetItem(parentTpl).Value?.Properties?.Slots?.FirstOrDefault(slot => slot.Name == slotName);

    private static string? MisplacedPart(List<Item> built)
    {
        var byId = built.ToDictionary(item => item.Id);
        foreach (var item in built)
        {
            if (item.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent)
                && !SlotAccepts(SlotOn(parent.Template, item.SlotId), item.Template))
            {
                return $"{_itemHelper!.GetItem(item.Template).Value?.Name} in {item.SlotId} on {_itemHelper.GetItem(parent.Template).Value?.Name}";
            }
        }

        return null;
    }

    private static string? EmptyRequiredSlot(List<Item> built, int maxDepth)
    {
        var byId = built.ToDictionary(item => item.Id);
        foreach (var item in built)
        {
            var depth = 1;
            for (var node = item; node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent); node = parent)
            {
                depth++;
            }

            var template = _itemHelper!.GetItem(item.Template).Value;
            if (depth > maxDepth || template?.Properties?.Slots is null)
            {
                continue;
            }

            foreach (var slot in template.Properties.Slots.Where(slot => slot.Required == true))
            {
                if (!built.Any(child => child.ParentId == item.Id && child.SlotId == slot.Name))
                {
                    return $"{slot.Name} on {template.Name}";
                }
            }
        }

        return null;
    }

    // Parts in required slots stay.
    private static void RemoveBranches(List<Item> built, IEnumerable<Item> parts)
    {
        var byId = built.ToDictionary(item => item.Id);
        var gone = new HashSet<MongoId>();
        foreach (var part in parts.ToList())
        {
            if (IsInRequiredSlot(part, byId))
            {
                continue;
            }

            var pending = new Stack<MongoId>();
            pending.Push(part.Id);
            while (pending.TryPop(out var id))
            {
                if (gone.Add(id))
                {
                    foreach (var child in built.Where(item => item.ParentId == id))
                    {
                        pending.Push(child.Id);
                    }
                }
            }
        }

        built.RemoveAll(item => gone.Contains(item.Id));
    }

    private static bool IsInRequiredSlot(Item part, Dictionary<MongoId, Item> byId) =>
        part.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent)
        && _itemHelper!.GetItem(parent.Template).Value?.Properties?.Slots?.FirstOrDefault(slot => slot.Name == part.SlotId)?.Required == true;

    // A can straight on the barrel leaves a bare thread, so a muzzle device goes back on.
    private static void PutBackFactoryMuzzle(BuildState state, Item can)
    {
        var parent = state.Built.FirstOrDefault(item => item.Id == can.ParentId);
        if (parent is null || can.SlotId is null || _itemHelper!.IsOfBaseclass(parent.Template, MuzzleBase))
        {
            return;
        }

        var slotDef = SlotOn(parent.Template, can.SlotId);
        bool Fits(MongoId tpl) => SlotAccepts(slotDef, tpl) && !_itemHelper.IsOfBaseclass(tpl, SilencerBase)
            && _ragfairServerHelper!.IsItemValidRagfairItem(_itemHelper.GetItem(tpl))
            && !_botGenerator!.IsItemIncompatibleWithCurrentItems(state.Built, tpl, can.SlotId).Incompatible.GetValueOrDefault(false);

        var allowed = _itemHelper.GetItem(parent.Template).Value?.Properties?.Slots?.FirstOrDefault(slot => slot.Name == can.SlotId)
            ?.Properties?.Filters?.FirstOrDefault()?.Filter ?? [];
        MongoId? device = state.Factory.TryGetValue((parent.Template, can.SlotId), out var factoryDevice) && Fits(factoryDevice)
            ? factoryDevice
            : allowed.Where(Fits).OrderBy(tpl => _prices!.GetFleaPriceForItem(tpl)).Cast<MongoId?>().FirstOrDefault();
        if (device is { } tpl)
        {
            state.Built.Add(new Item { Id = new MongoId(), Template = tpl, ParentId = parent.Id, SlotId = can.SlotId });
        }
    }

    // A lone front or rear sight comes off. The M4's gas block counts as a front sight.
    private static void PairIronSights(BuildState state)
    {
        var built = state.Built;
        bool InSlot(Item item, string slot) => item.SlotId == slot;
        var hasFront = built.Any(item => InSlot(item, "mod_sight_front"))
            || built.Any(item => InSlot(item, "mod_gas_block") && (_itemHelper!.GetItem(item.Template).Value?.Name ?? "").Contains("front_sight", StringComparison.Ordinal));
        var hasRear = built.Any(item => InSlot(item, "mod_sight_rear"));

        // AKs have the front sight on the barrel.
        var factoryFront = state.Factory.Any(pair => pair.Key.Slot == "mod_sight_front"
            || pair.Key.Slot == "mod_gas_block" && (_itemHelper!.GetItem(pair.Value).Value?.Name ?? "").Contains("front_sight", StringComparison.Ordinal));
        var factoryRearOnly = !factoryFront && state.Factory.Keys.Any(key => key.Slot == "mod_sight_rear");
        if (factoryRearOnly || hasFront == hasRear)
        {
            return;
        }

        RemoveBranches(built, built.Where(item => InSlot(item, hasFront ? "mod_sight_front" : "mod_sight_rear")));
    }

    // A good stock or grip sells on its own, so swap it for the factory or cheapest one.
    private static void CheapenStocks(BuildState state)
    {
        var built = state.Built;
        var targets = built.Where(item => item.SlotId is { } slot
            && (slot.StartsWith("mod_stock", StringComparison.Ordinal) || slot.StartsWith("mod_pistol_grip", StringComparison.Ordinal) || slot == "mod_pistolgrip")
            && !built.Any(child => child.ParentId == item.Id)).ToList();
        foreach (var part in targets)
        {
            var parent = built.FirstOrDefault(item => item.Id == part.ParentId);
            var slot = parent is null ? null : _itemHelper!.GetItem(parent.Template).Value?.Properties?.Slots?.FirstOrDefault(s => s.Name == part.SlotId);
            if (parent is null || slot is null)
            {
                continue;
            }

            var others = built.Where(item => item.Id != part.Id).ToList();
            bool Fits(MongoId tpl) => SlotAccepts(slot, tpl) && _ragfairServerHelper!.IsItemValidRagfairItem(_itemHelper!.GetItem(tpl))
                && _itemHelper.GetItem(tpl).Value?.Properties?.Slots?.Any(s => s.Required == true) != true
                && !_botGenerator!.IsItemIncompatibleWithCurrentItems(others, tpl, part.SlotId!).Incompatible.GetValueOrDefault(false);

            var price = _prices!.GetFleaPriceForItem(part.Template);
            MongoId? cheaper = state.Factory.TryGetValue((parent.Template, part.SlotId!), out var factoryPart) && factoryPart != part.Template && Fits(factoryPart)
                ? factoryPart
                : (slot.Properties?.Filters?.FirstOrDefault()?.Filter ?? []).Where(Fits).OrderBy(tpl => _prices.GetFleaPriceForItem(tpl)).Cast<MongoId?>().FirstOrDefault();
            if (cheaper is { } swap && swap != part.Template && _prices.GetFleaPriceForItem(swap) < price)
            {
                built[built.IndexOf(part)] = new Item { Id = part.Id, Template = swap, ParentId = part.ParentId, SlotId = part.SlotId };
            }
        }
    }

    // Weights move from weight (cheapest gun) to dearWeight (priciest). Broken guns lean hard to cheap ones,
    // gutted half as hard.
    private static (string Name, WeaponTierConfig Tier) PickTier(PresetOffersConfig settings, MongoId gunTpl)
    {
        var rank = PriceRank(gunTpl);
        var cheapness = 1 - rank;
        var bias = Math.Max(0, settings.BrokenPriceBias);
        double WeightOf(WeaponTierConfig tier) => Math.Max(0, tier.Weight + ((tier.DearWeight ?? tier.Weight) - tier.Weight) * rank);
        var tiers = new (string Name, WeaponTierConfig Tier, double Weight)[]
        {
            ("stripped", settings.Stripped, WeightOf(settings.Stripped)),
            ("stock", settings.Stock, WeightOf(settings.Stock)),
            ("found", settings.Found, WeightOf(settings.Found)),
            ("modded", settings.Modded, WeightOf(settings.Modded)),
            ("broken", settings.Broken, WeightOf(settings.Broken) * Math.Pow(cheapness, bias)),
            ("gutted", settings.Gutted, WeightOf(settings.Gutted) * Math.Pow(cheapness, bias / 2)),
            ("budget", settings.Budget, WeightOf(settings.Budget)),
            ("ground", settings.Ground, WeightOf(settings.Ground))
        };
        var total = tiers.Sum(tier => tier.Weight);
        if (total <= 0)
        {
            return (tiers[2].Name, tiers[2].Tier);
        }

        var roll = Random.Shared.NextDouble() * total;
        foreach (var tier in tiers)
        {
            roll -= tier.Weight;
            if (roll < 0)
            {
                return (tier.Name, tier.Tier);
            }
        }

        return (tiers[3].Name, tiers[3].Tier);
    }

    private static readonly Lazy<List<double>> GunPrices = new(() =>
        _presets!.GetDefaultPresets().Values.Select(preset => PriceOf(preset.Items)).Where(price => price > 0).OrderBy(price => price).ToList());

    private static double PriceOf(IEnumerable<Item>? items) => items?.Sum(item => _prices!.GetFleaPriceForItem(item.Template)) ?? 0;

    // 0 = cheapest gun, near 1 = priciest.
    private static double PriceRank(MongoId gunTpl)
    {
        var prices = GunPrices.Value;
        if (prices.Count == 0)
        {
            return 0;
        }

        var preset = _presets!.GetDefaultPreset(gunTpl);
        var price = preset is not null ? PriceOf(preset.Items) : _prices!.GetFleaPriceForItem(gunTpl);
        var index = prices.BinarySearch(price);
        return (double)(index < 0 ? ~index : index) / prices.Count;
    }

    private static readonly ConcurrentDictionary<MongoId, Dictionary<(MongoId Parent, string Slot), MongoId>> FactoryBuilds = new();

    private static Dictionary<(MongoId Parent, string Slot), MongoId> FactoryParts(MongoId gunTpl) =>
        FactoryBuilds.GetOrAdd(gunTpl, tpl =>
        {
            var result = new Dictionary<(MongoId Parent, string Slot), MongoId>();
            var items = _presets!.GetDefaultPreset(tpl)?.Items;
            if (items is null)
            {
                return result;
            }

            var templates = items.ToDictionary(item => item.Id, item => item.Template);
            foreach (var item in items)
            {
                if (item.ParentId is { } parentId && item.SlotId is { } slotId && templates.TryGetValue(parentId, out var parentTpl))
                {
                    result.TryAdd((parentTpl, slotId), item.Template);
                }
            }

            return result;
        });

    // Optional in the game but a gun looks broken without them (stock on a buffer tube, lower handguard).
    private static bool CompletesParent(string? parentSlot, string slotName) =>
        parentSlot is not null
        && (parentSlot.StartsWith("mod_stock", StringComparison.Ordinal) && slotName.StartsWith("mod_stock", StringComparison.Ordinal)
            || parentSlot == "mod_handguard" && slotName == "mod_handguard");

    private static int ForwardSize(MongoId tpl) => _itemHelper!.GetItem(tpl).Value?.Properties?.ExtraSizeLeft ?? 0;

    private static double TierChance(string slotName, WeaponTierConfig tier)
    {
        if (slotName == "mod_magazine")
        {
            return tier.Magazine;
        }

        // mod_mount_000 and up sit on handguards, so they count as grips.
        if (slotName.StartsWith("mod_scope", StringComparison.Ordinal) || slotName == "mod_mount")
        {
            return tier.Scope;
        }

        if (slotName.StartsWith("mod_muzzle", StringComparison.Ordinal))
        {
            return tier.Muzzle;
        }

        if (slotName.StartsWith("mod_stock", StringComparison.Ordinal))
        {
            return tier.Stock;
        }

        var isGrip = slotName.StartsWith("mod_pistol_grip", StringComparison.Ordinal) || slotName == "mod_pistolgrip"
            || slotName.StartsWith("mod_mount_", StringComparison.Ordinal);
        return isGrip ? tier.Grip : tier.Other;
    }

    private enum TacticalKind
    {
        Light,
        Laser,
        Combo
    }

    // Lasers, lights and combos share one class. Several modes = combo; one mode = laser if the name says so.
    // The WMX200 has a strobe mode but is a light.
    private static TacticalKind KindOf(MongoId tpl)
    {
        var item = _itemHelper!.GetItem(tpl).Value;
        var name = item?.Name ?? "";
        if (name.Contains("wmx200", StringComparison.OrdinalIgnoreCase))
        {
            return TacticalKind.Light;
        }

        var modes = item?.Properties?.ModesCount is { } count
            ? count.List is { Count: > 0 } list ? list.Max() : count.Item
            : 1;
        if (modes >= 2)
        {
            return TacticalKind.Combo;
        }

        return name.Contains("laser", StringComparison.OrdinalIgnoreCase) ? TacticalKind.Laser : TacticalKind.Light;
    }

    // Laser only: maybe add a light. Has a light: rarely add a laser. Null = leave it empty.
    private static List<MongoId>? SecondTacticalChoices(List<MongoId> fitting, List<Item> built, PresetOffersConfig settings)
    {
        var hasLight = built.Where(item => (item.SlotId ?? "").StartsWith("mod_tactical", StringComparison.Ordinal))
            .Any(item => KindOf(item.Template) != TacticalKind.Laser);
        if (!hasLight)
        {
            return Random.Shared.NextDouble() < settings.LaserLightChance
                ? fitting.Where(tpl => KindOf(tpl) == TacticalKind.Light).ToList()
                : null;
        }

        return Random.Shared.NextDouble() < settings.ExtraTacticalChance
            ? fitting.Where(tpl => KindOf(tpl) == TacticalKind.Laser).ToList()
            : null;
    }

    private static readonly HashSet<MongoId> OpticBases =
    [
        new("55818ae44bdc2dde698b456c"), new("55818add4bdc2d5b648b456f"), new("55818ad54bdc2ddc698b4569"),
        new("55818acf4bdc2dde698b456b"), new("55818aeb4bdc2ddc698b456a")
    ];

    private static bool IsOptic(MongoId tpl) => _itemHelper!.IsOfBaseclasses(tpl, OpticBases);

    // RMR, Deltapoint, T1, PK-06...
    private static readonly MongoId CompactCollimatorBase = new("55818acf4bdc2dde698b456b");

    // Thermals and NV.
    private static readonly MongoId SpecialScopeBase = new("55818aeb4bdc2ddc698b456a");

    private const double BackupMinZoom = 3;

    private static readonly ConcurrentDictionary<MongoId, double> Zooms = new();

    private static double MaxZoom(MongoId tpl) => Zooms.GetOrAdd(tpl, id =>
        _itemHelper!.GetItem(id).Value?.Properties?.Zooms?.SelectMany(modes => modes).DefaultIfEmpty(1).Max() ?? 1);

    private static readonly MongoId PistolBase = new("5447b5cf4bdc2d65278b4567");
    private static readonly MongoId RevolverBase = new("617f1ef5e8b54b0998387733");
    private static readonly MongoId SmgBase = new("5447b5e04bdc2d62278b4567");
    private static readonly MongoId ShotgunBase = new("5447b6094bdc2dc3278b4567");
    private static readonly MongoId MarksmanBase = new("5447b6194bdc2d67278b4567");
    private static readonly MongoId SniperBase = new("5447b6254bdc2dc3278b4568");
    private static readonly MongoId MachineGunBase = new("5447bed64bdc2d97278b4568");

    private const double RareBipodChance = 0.05;

    // Pistols: dots. SMGs and shotguns: up to 4x. Rifles and DMRs: up to 8x. Bolt-actions: anything.
    private static double ZoomCapFor(MongoId gunTpl) =>
        _itemHelper!.IsOfBaseclasses(gunTpl, [PistolBase, RevolverBase]) ? 1
        : _itemHelper.IsOfBaseclasses(gunTpl, [SmgBase, ShotgunBase]) ? 4
        : IsLongRange(gunTpl) ? double.MaxValue
        : 8;

    private static bool IsLongRange(MongoId gunTpl) => _itemHelper!.IsOfBaseclass(gunTpl, SniperBase);

    // Highest zoom is the main sight. A second optic only stays as a small dot next to a 3x+ scope.
    private static void TidyOptics(BuildState state)
    {
        var built = state.Built;
        var optics = built.Where(item => IsOptic(item.Template)).OrderByDescending(item => MaxZoom(item.Template)).ToList();
        if (optics.Count == 0)
        {
            return;
        }

        var main = optics[0];
        var backupKept = false;
        foreach (var other in optics.Skip(1))
        {
            var keep = !backupKept && MaxZoom(main.Template) >= BackupMinZoom
                && _itemHelper!.IsOfBaseclass(other.Template, CompactCollimatorBase) && other.Template != main.Template;
            if (keep || IsAncestor(built, other, main))
            {
                backupKept |= keep;
                continue;
            }

            RemoveOpticBranch(built, other);
        }

        if (built.Count(item => IsOptic(item.Template)) == 1 && OnCantedMount(built, main))
        {
            RemoveOpticBranch(built, main);
        }
    }

    private static bool IsAncestor(List<Item> built, Item maybeAncestor, Item item)
    {
        var byId = built.ToDictionary(part => part.Id);
        for (var node = item; node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent); node = parent)
        {
            if (parent.Id == maybeAncestor.Id)
            {
                return true;
            }
        }

        return false;
    }

    private static bool OnCantedMount(List<Item> built, Item optic)
    {
        var byId = built.ToDictionary(part => part.Id);
        for (var node = optic; node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent); node = parent)
        {
            if (IsCantedMount(parent.Template))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveOpticBranch(List<Item> built, Item optic)
    {
        var top = optic;
        while (built.FirstOrDefault(item => item.Id == top.ParentId) is { } parent
               && _itemHelper!.IsOfBaseclass(parent.Template, MountBase)
               && built.Count(item => item.ParentId == parent.Id) == 1
               && !IsInRequiredSlot(parent, built.ToDictionary(item => item.Id)))
        {
            top = parent;
        }

        RemoveBranches(built, [top]);
    }

    private static double Capacity(MongoId tpl) =>
        _itemHelper!.GetItem(tpl).Value?.Properties?.Cartridges?.FirstOrDefault()?.MaxCount ?? 0;

    // Bigger of the factory mag and the middle of what fits: the ADAR comes with a 10 but people run 30s.
    private static double TypicalMagazine(MongoId factoryMag, IEnumerable<MongoId> fitting)
    {
        var sizes = fitting.Select(Capacity).Where(size => size > 0).OrderBy(size => size).ToList();
        var middle = sizes.Count == 0 ? 0 : sizes[sizes.Count / 2];
        return Math.Max(middle, factoryMag.IsEmpty ? 0 : Capacity(factoryMag));
    }

    private static bool HasScopeSlot(MongoId tpl) =>
        _itemHelper!.GetItem(tpl).Value?.Properties?.Slots?.Any(slot => slot.Name?.StartsWith("mod_scope", StringComparison.Ordinal) == true) == true;

    private static readonly MongoId ReceiverBase = new("55818a304bdc2db5418b457d");
    private static readonly MongoId HandguardBase = new("55818a104bdc2db9688b4569");
    private static readonly MongoId GasblockBase = new("56ea9461d2720b67698b456f");
    private static readonly MongoId IronSightBase = new("55818ac54bdc2d5b648b456e");

    private const int MaxOpticMounts = 2;

    // Ways to get each optic into a slot (a Vudu needs 30mm rings first, a T1 its base), cached per holder and slot.
    private const int RoutesPerOptic = 6;

    private static readonly ConcurrentDictionary<(MongoId Holder, string Slot, int Mounts), Dictionary<MongoId, List<List<(string Slot, MongoId Tpl)>>>> OpticRouteCache = new();

    private static Dictionary<MongoId, List<List<(string Slot, MongoId Tpl)>>> OpticRoutes(MongoId holderTpl, string slotName, int mountsLeft) =>
        OpticRouteCache.GetOrAdd((holderTpl, slotName, mountsLeft), key =>
        {
            var result = new Dictionary<MongoId, List<List<(string Slot, MongoId Tpl)>>>();
            void Add(MongoId optic, List<(string Slot, MongoId Tpl)> route)
            {
                if (!result.TryGetValue(optic, out var routes))
                {
                    result[optic] = routes = [];
                }

                routes.Add(route);
            }

            foreach (var tpl in SlotOn(key.Holder, key.Slot)?.Properties?.Filters?.FirstOrDefault()?.Filter ?? [])
            {
                if (_itemHelper!.GetItem(tpl).Value?.Properties is null)
                {
                    continue;
                }

                if (IsOptic(tpl))
                {
                    Add(tpl, [(key.Slot, tpl)]);
                    continue;
                }

                if (key.Mounts <= 0 || !_itemHelper.IsOfBaseclass(tpl, MountBase))
                {
                    continue;
                }

                foreach (var sub in _itemHelper.GetItem(tpl).Value?.Properties?.Slots ?? [])
                {
                    if (sub.Name is not { } subName || !(subName.StartsWith("mod_scope", StringComparison.Ordinal) || subName.StartsWith("mod_mount", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    foreach (var (optic, routes) in OpticRoutes(tpl, subName, key.Mounts - 1))
                    {
                        foreach (var route in routes)
                        {
                            Add(optic, [(key.Slot, tpl), .. route]);
                        }
                    }
                }
            }

            foreach (var optic in result.Keys.ToList())
            {
                result[optic] = result[optic].OrderBy(route => route.Count).Take(RoutesPerOptic).ToList();
            }

            return result;
        });

    private const double TopTacticalChance = 0.05;

    // Side and under rails take a mount, the top one doesn't. So a tactical slot that takes no mount, on a part
    // where another tactical slot does, is the top rail. If they all take mounts (most AK handguards) the top is
    // the scope slot.
    private static readonly ConcurrentDictionary<(MongoId Part, string Slot), bool> TopTacticalSlots = new();

    private static bool IsTopTacticalSlot(TemplateItem part, string slotName) => TopTacticalSlots.GetOrAdd((part.Id, slotName), key =>
    {
        bool TakesMount(Slot slot) => slot.Properties?.Filters?.Any(filter => filter.Filter?.Any(id =>
            _itemHelper!.GetItem(id).Key && _itemHelper.IsOfBaseclass(id, MountBase)) == true) == true;

        var tactical = (part.Properties?.Slots ?? []).Where(slot => slot.Name?.StartsWith("mod_tactical", StringComparison.Ordinal) == true).ToList();
        return tactical.FirstOrDefault(slot => slot.Name == key.Slot) is { } own && !TakesMount(own) && tactical.Any(TakesMount);
    });

    // Receiver top rail (or AK dovetail) first, then handguard top, carry handle, side rails.
    private static double SpotPriority(Item holder, string slotName, Dictionary<MongoId, Item> byId)
    {
        var node = holder;
        var attachSlot = slotName;
        while (_itemHelper!.IsOfBaseclass(node.Template, MountBase) && node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
        {
            attachSlot = node.SlotId ?? attachSlot;
            node = parent;
        }

        var side = attachSlot.StartsWith("mod_mount_", StringComparison.Ordinal);
        if (_itemHelper.IsOfBaseclasses(node.Template, [GunBase, ReceiverBase]))
        {
            return side ? 3.5 : 4;
        }

        if (_itemHelper.IsOfBaseclass(node.Template, SilencerBase))
        {
            return 3.5;
        }

        if (_itemHelper.IsOfBaseclasses(node.Template, [HandguardBase, GasblockBase]))
        {
            return side ? 1 : 2.5;
        }

        return _itemHelper.IsOfBaseclass(node.Template, IronSightBase) ? 1.5 : 1;
    }

    private static List<(Item Holder, string Slot, double Priority)> OpticSpots(List<Item> built)
    {
        var byId = built.ToDictionary(item => item.Id);
        var spots = new List<(Item Holder, string Slot, double Priority)>();
        foreach (var holder in built.ToList())
        {
            foreach (var slot in _itemHelper!.GetItem(holder.Template).Value?.Properties?.Slots ?? [])
            {
                if (slot.Name is { } name
                    && (name.StartsWith("mod_scope", StringComparison.Ordinal) || name.StartsWith("mod_mount", StringComparison.Ordinal))
                    && !built.Any(child => child.ParentId == holder.Id && child.SlotId == name))
                {
                    spots.Add((holder, name, SpotPriority(holder, name, byId)));
                }
            }
        }

        return spots;
    }

    // All or nothing.
    private static bool TryPlaceRoute(List<Item> built, Item holder, List<(string Slot, MongoId Tpl)> route)
    {
        var placed = new List<Item>();
        var parent = holder;
        foreach (var (slotName, tpl) in route)
        {
            var slot = SlotOn(parent.Template, slotName);
            if (built.Any(child => child.ParentId == parent.Id && child.SlotId == slotName)
                || !SlotAccepts(slot, tpl)
                || !_ragfairServerHelper!.IsItemValidRagfairItem(_itemHelper!.GetItem(tpl))
                || _botGenerator!.IsItemIncompatibleWithCurrentItems(built, tpl, slotName).Incompatible.GetValueOrDefault(false))
            {
                built.RemoveAll(placed.Contains);
                return false;
            }

            var item = new Item { Id = new MongoId(), Template = tpl, ParentId = parent.Id, SlotId = slotName };
            built.Add(item);
            placed.Add(item);
            parent = item;
        }

        // Parts a mount can't go without (a Geissele mount's top cap).
        foreach (var item in placed.ToList())
        {
            foreach (var slot in _itemHelper!.GetItem(item.Template).Value?.Properties?.Slots ?? [])
            {
                if (slot.Required != true || slot.Name is not { } slotName || built.Any(child => child.ParentId == item.Id && child.SlotId == slotName))
                {
                    continue;
                }

                var fill = (slot.Properties?.Filters?.FirstOrDefault()?.Filter ?? []).FirstOrDefault(tpl => SlotAccepts(slot, tpl) && !IsOptic(tpl)
                    && _ragfairServerHelper!.IsItemValidRagfairItem(_itemHelper.GetItem(tpl))
                    && !_botGenerator!.IsItemIncompatibleWithCurrentItems(built, tpl, slotName).Incompatible.GetValueOrDefault(false));
                if (fill.IsEmpty)
                {
                    built.RemoveAll(placed.Contains);
                    return false;
                }

                var part = new Item { Id = new MongoId(), Template = fill, ParentId = item.Id, SlotId = slotName };
                built.Add(part);
                placed.Add(part);
            }
        }

        return true;
    }

    // Optic picked first, then the mounts to get it on from the best spot. A factory optic is usually kept.
    private static void PlaceOptics(BuildState state)
    {
        if (!PlaceOptic(state, null))
        {
            return;
        }

        var main = state.Built.Where(item => IsOptic(item.Template)).MaxBy(item => MaxZoom(item.Template));
        if (main is not null && state.BackupSight && MaxZoom(main.Template) >= BackupMinZoom)
        {
            PlaceOptic(state, main);
        }
    }

    // With mainOptic set this places a backup dot instead: on the main optic or its mount, or a canted mount.
    private static bool PlaceOptic(BuildState state, Item? mainOptic)
    {
        var built = state.Built;
        var prefs = state.Preferences;
        var backup = mainOptic is not null;
        var factoryOptics = state.Factory.Values.Where(IsOptic).ToHashSet();
        var keepFactoryOptic = factoryOptics.Count > 0 && Random.Shared.NextDouble() < state.Tier.Factory;

        bool OpticFits(MongoId optic) =>
            MaxZoom(optic) <= state.ZoomCap
            && !(_itemHelper!.IsOfBaseclass(optic, SpecialScopeBase) && state.TierName != "modded")
            && _ragfairServerHelper!.IsItemValidRagfairItem(_itemHelper.GetItem(optic))
            && (!backup || _itemHelper.IsOfBaseclass(optic, CompactCollimatorBase) && optic != mainOptic!.Template);

        var spots = OpticSpots(built);
        if (backup)
        {
            var byId = built.ToDictionary(item => item.Id);
            var mainChain = new HashSet<MongoId> { mainOptic!.Id };
            for (var node = mainOptic; node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) && _itemHelper!.IsOfBaseclass(parent.Template, MountBase); node = parent)
            {
                mainChain.Add(parent.Id);
            }

            spots = spots
                .Select(spot => (spot.Holder, spot.Slot, Priority: mainChain.Contains(spot.Holder.Id) ? 3.0 : spot.Priority >= 2.5 ? 2.0 : 0.0))
                .Where(spot => spot.Priority > 0)
                .ToList();
        }

        foreach (var group in spots.GroupBy(spot => spot.Priority).OrderByDescending(group => group.Key))
        {
            var options = new List<(Item Holder, MongoId Optic, List<List<(string Slot, MongoId Tpl)>> Routes)>();
            foreach (var (holder, slotName, priority) in group)
            {
                foreach (var (optic, routes) in OpticRoutes(holder.Template, slotName, MaxOpticMounts))
                {
                    // Away from the main optic it has to be a canted mount.
                    var usable = backup && priority < 3 ? routes.Where(route => route.Any(step => IsCantedMount(step.Tpl))).ToList() : routes;
                    var budgetOk = state.Style != BuildStyle.Budget || BuildQuality.IsBudgetOptic(optic) || factoryOptics.Contains(optic);
                    if (usable.Count > 0 && budgetOk && OpticFits(optic))
                    {
                        options.Add((holder, optic, usable));
                    }
                }
            }

            var optics = options.Select(option => option.Optic).Distinct().ToList();
            var weights = optics.Select(optic => BuildQuality.OpticWeight(optic, prefs, state.Group, backup || state.ZoomCap <= 1)
                * (keepFactoryOptic && factoryOptics.Contains(optic) ? 50 : 1)).ToList();
            for (var tries = 0; tries < 8 && optics.Count > 0; tries++)
            {
                if (BuildQuality.Pick(optics, weights.ToArray()) is not { } choice)
                {
                    break;
                }

                // Shortest first, then easy-to-get and matching colour.
                foreach (var (holder, _, routes) in options.Where(option => option.Optic == choice).OrderBy(_ => Random.Shared.Next()))
                {
                    foreach (var route in routes.OrderBy(route => route.Count).ThenByDescending(route => RouteWeight(route, prefs) * Random.Shared.NextDouble()))
                    {
                        if (TryPlaceRoute(built, holder, route))
                        {
                            if (state.Tier.ColourMatch > 0)
                            {
                                state.Colour ??= route.Select(step => BuildQuality.BuildColourFrom(step.Tpl)).FirstOrDefault(colour => colour is not null);
                            }

                            return true;
                        }
                    }
                }

                var index = optics.IndexOf(choice);
                optics.RemoveAt(index);
                weights.RemoveAt(index);
            }
        }

        return false;
    }

    private static readonly ConcurrentDictionary<(MongoId Holder, string Slot), List<List<(string Slot, MongoId Tpl)>>> RingRouteCache = new();

    private static List<List<(string Slot, MongoId Tpl)>> RingRoutes(MongoId holderTpl, string slotName) =>
        RingRouteCache.GetOrAdd((holderTpl, slotName), key =>
        {
            var routes = new List<List<(string Slot, MongoId Tpl)>>();
            foreach (var tpl in SlotOn(key.Holder, key.Slot)?.Properties?.Filters?.FirstOrDefault()?.Filter ?? [])
            {
                if (BuildQuality.IsRingMount(tpl))
                {
                    routes.Add([(key.Slot, tpl)]);
                    continue;
                }

                if (!_itemHelper!.IsOfBaseclass(tpl, MountBase))
                {
                    continue;
                }

                foreach (var sub in _itemHelper.GetItem(tpl).Value?.Properties?.Slots ?? [])
                {
                    if (sub.Name is not { } subName || !(subName.StartsWith("mod_scope", StringComparison.Ordinal) || subName.StartsWith("mod_mount", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    routes.AddRange((sub.Properties?.Filters?.FirstOrDefault()?.Filter ?? []).Where(BuildQuality.IsRingMount)
                        .Select(ring => new List<(string Slot, MongoId Tpl)> { (key.Slot, tpl), (subName, ring) }));
                }
            }

            return routes;
        });

    // Rifles, 7.62s and DMRs (30mm and under) and bolt-actions (any).
    private static void AddEmptyRings(BuildState state)
    {
        if (state.Group is not (BuildQuality.GunGroup.Rifle or BuildQuality.GunGroup.BigBore or BuildQuality.GunGroup.Sniper))
        {
            return;
        }

        var prefs = state.Preferences;
        var options = OpticSpots(state.Built)
            .Where(spot => spot.Priority >= 3.5)
            .SelectMany(spot => RingRoutes(spot.Holder.Template, spot.Slot).Select(route => (spot.Holder, Route: route)))
            .Where(option => state.Group == BuildQuality.GunGroup.Sniper || !BuildQuality.IsSniperRing(option.Route[^1].Tpl))
            .ToList();
        for (var tries = 0; tries < 6 && options.Count > 0; tries++)
        {
            var weights = options.Select(option => RouteWeight(option.Route, prefs) * BuildQuality.MountPreference(option.Route[^1].Tpl, prefs)
                * BuildQuality.PartFactor(option.Route[^1].Tpl, prefs)).ToArray();
            var total = weights.Sum();
            var roll = Random.Shared.NextDouble() * total;
            var index = 0;
            while (index < options.Count - 1 && (roll -= weights[index]) >= 0)
            {
                index++;
            }

            if (TryPlaceRoute(state.Built, options[index].Holder, options[index].Route))
            {
                return;
            }

            options.RemoveAt(index);
        }
    }

    private static double RouteWeight(List<(string Slot, MongoId Tpl)> route, BuildQuality.PartPreferences prefs) =>
        route.Take(route.Count - 1).Aggregate(1.0, (weight, step) => weight * BuildQuality.PartFactor(step.Tpl, prefs) * BuildQuality.MountPreference(step.Tpl, prefs));

    private static bool IsCantedMount(MongoId tpl)
    {
        var name = _itemHelper!.GetItem(tpl).Value?.Name ?? "";
        return name.Contains("mpr45", StringComparison.Ordinal) || name.Contains("offset", StringComparison.Ordinal) && name.Contains("optic", StringComparison.Ordinal);
    }

    private sealed class BuildState(
        List<Item> built,
        PresetOffersConfig settings,
        string tierName,
        WeaponTierConfig tier,
        Dictionary<(MongoId Parent, string Slot), MongoId> factory)
    {
        public List<Item> Built { get; } = built;
        public PresetOffersConfig Settings { get; } = settings;
        public string TierName { get; } = tierName;
        public WeaponTierConfig Tier { get; } = tier;
        public Dictionary<(MongoId Parent, string Slot), MongoId> Factory { get; } = factory;

        public int Missing { get; set; }

        // Set by the first coloured part or rolled up front. Null = not yet.
        public string? Colour { get; set; }

        public bool CustomBarrel { get; set; }

        public bool BackupSight { get; init; }

        public bool WantsSuppressor { get; init; }

        public double ZoomCap { get; init; } = double.MaxValue;

        public BuildQuality.GunGroup Group { get; init; } = BuildQuality.GunGroup.Other;

        public bool BipodGun { get; init; }

        public bool FactoryHasMuzzle { get; } = factory.Keys.Any(key => key.Slot.StartsWith("mod_muzzle", StringComparison.Ordinal));

        public BuildStyle Style { get; init; } = BuildStyle.Hybrid;
        public bool WantsOptic { get; init; }

        // 60-round PMAG or MAG5.
        public bool GoodMagazine { get; init; }

        public BuildQuality.PartPreferences Preferences => new(Style, Tier.StatFocus, Tier.ColourMatch, Colour);
    }

    private static void AddAttachments(Item parent, MongoId parentTpl, TemplateItem parentTemplate, BuildState state, int depth)
    {
        var built = state.Built;
        var settings = state.Settings;
        var tier = state.Tier;
        if (depth > settings.MaxAttachmentDepth || parentTemplate.Properties?.Slots is null)
        {
            return;
        }

        CuratedMods.Value.TryGetValue(parentTpl, out var curated);
        foreach (var slot in parentTemplate.Properties.Slots.OrderBy(slot => SlotRank(slot.Name)))
        {
            // Always fitted: pieces of a part (but the ADAR's stock can block its tube on purpose, so an empty one is ok),
            // what the factory gun has that people don't take off, a muzzle device on a swapped barrel, and the optic
            // on a scope mount.
            state.Factory.TryGetValue((parentTpl, slot.Name), out var factoryPart);
            var required = slot.Required.GetValueOrDefault(false);
            var isScope = slot.Name.StartsWith("mod_scope", StringComparison.Ordinal);

            // Optics and top mounts go on after the rest (PlaceOptics).
            if (!required && (isScope || slot.Name == "mod_mount"))
            {
                continue;
            }

            var isFirstMuzzle = slot.Name.StartsWith("mod_muzzle", StringComparison.Ordinal) && !_itemHelper!.IsOfBaseclass(parentTpl, MuzzleBase);
            var alwaysFill = required || CompletesParent(parent.SlotId, slot.Name)
                || (!factoryPart.IsEmpty && !Strippable(slot.Name))
                || (isFirstMuzzle && state.FactoryHasMuzzle)
                || (slot.Name.StartsWith("mod_stock", StringComparison.Ordinal) && state.Factory.Keys.Any(key => key.Slot == slot.Name));

            // Wanting a can fills every muzzle slot on the way to it.
            var isMuzzle = slot.Name.StartsWith("mod_muzzle", StringComparison.Ordinal);
            var wantCan = isMuzzle && state.WantsSuppressor
                && !built.Any(item => _itemHelper!.IsOfBaseclass(item.Template, SilencerBase))
                && (slot.Properties?.Filters?.FirstOrDefault()?.Filter ?? []).Any(LeadsToSuppressor);
            alwaysFill |= wantCan;
            if (!alwaysFill && Random.Shared.NextDouble() >= TierChance(slot.Name, tier))
            {
                continue;
            }

            // Revolver chambers hold rounds, listed empty.
            if (slot.Name.StartsWith("camora", StringComparison.Ordinal))
            {
                continue;
            }

            if (slot.Name == "mod_bipod" && !required && !state.BipodGun && Random.Shared.NextDouble() >= RareBipodChance)
            {
                continue;
            }

            var isTactical = slot.Name.StartsWith("mod_tactical", StringComparison.Ordinal);
            if (isTactical && !required && built.Count(item => (item.SlotId ?? "").StartsWith("mod_tactical", StringComparison.Ordinal)) >= settings.MaxTacticalDevices)
            {
                continue;
            }

            // Nobody puts a laser on the top rail in front of the scope.
            if (isTactical && !required && IsTopTacticalSlot(parentTemplate, slot.Name) && Random.Shared.NextDouble() >= TopTacticalChance)
            {
                continue;
            }

            // No optics this pass (an AK side mount would grab one), one vertical grip, and flip-ups instead of a
            // carry handle when an optic is coming.
            var hasForegrip = built.Any(item => _itemHelper!.IsOfBaseclass(item.Template, ForegripBase));
            var avoidCarryHandle = state.WantsOptic && !required && slot.Name == "mod_sight_rear";
            bool OpticAllowed(MongoId tpl) =>
                !(hasForegrip && _itemHelper!.IsOfBaseclass(tpl, ForegripBase))
                && (!IsOptic(tpl) || required && MaxZoom(tpl) <= state.ZoomCap)
                && !(avoidCarryHandle && HasScopeSlot(tpl));

            var allowedBySlot = slot.Properties?.Filters?.FirstOrDefault()?.Filter?.ToList() ?? [];
            var isBarrel = slot.Name == "mod_barrel";

            // No handguard longer than the barrel.
            var barrel = slot.Name == "mod_handguard"
                ? built.FirstOrDefault(item => item.ParentId == parent.Id && item.SlotId == "mod_barrel")
                : null;
            var barrelSize = barrel is null ? (int?)null : ForwardSize(barrel.Template);

            // Bot lists have parts filed under the wrong slot, so the slot check comes first.
            bool Fits(MongoId tpl) => SlotAccepts(slot, tpl) && OpticAllowed(tpl)
                && _ragfairServerHelper!.IsItemValidRagfairItem(_itemHelper!.GetItem(tpl))
                && !_botGenerator!.IsItemIncompatibleWithCurrentItems(built, tpl, slot.Name).Incompatible.GetValueOrDefault(false);

            // Someone who swapped the barrel swapped the handguard too. The factory muzzle only stays if it suits the
            // can plan. Builds without a premium mag usually keep the cheap factory one.
            var isMag = slot.Name == "mod_magazine";
            var shortFactoryMag = isMag && !factoryPart.IsEmpty
                && Capacity(factoryPart) < 0.6 * TypicalMagazine(factoryPart, allowedBySlot.Where(tpl => SlotAccepts(slot, tpl)));
            var keepFactory = Random.Shared.NextDouble() < (isBarrel ? 1 - tier.BarrelSwap : isMag ? (state.GoodMagazine || shortFactoryMag ? 0 : CheapMagChance) : tier.Factory)
                && !(slot.Name == "mod_handguard" && state.CustomBarrel)
                && !(wantCan && !LeadsToSuppressor(factoryPart))
                && !(isBarrel && state.WantsSuppressor && !LeadsToSuppressor(factoryPart) && allowedBySlot.Any(LeadsToSuppressor))
                && !(isMuzzle && !wantCan && !required && !factoryPart.IsEmpty && _itemHelper!.IsOfBaseclass(factoryPart, SilencerBase));
            MongoId? pick = null;
            List<MongoId> usable = [];
            if (keepFactory && !factoryPart.IsEmpty && Fits(factoryPart) && (barrelSize is null || ForwardSize(factoryPart) <= barrelSize))
            {
                pick = factoryPart;
            }
            else
            {
                // statFocus 1+ looks at everything the slot takes with a nudge for bot parts; lower tiers stick to bot parts.
                var curatedForSlot = curated is not null && curated.TryGetValue(slot.Name, out var parts) ? parts.ToList() : [];
                var curatedSet = curatedForSlot.ToHashSet();
                var wide = isBarrel || tier.StatFocus >= 1 || curatedForSlot.Count == 0;
                var candidates = wide ? allowedBySlot : curatedForSlot;
                usable = candidates.Where(tpl => !tpl.IsEmpty && _ragfairServerHelper!.IsItemValidRagfairItem(_itemHelper!.GetItem(tpl))).ToList();
                var fitting = usable.Where(Fits).ToList();

                // Everything in the pool can clash; a required slot then takes anything that fits.
                if (fitting.Count == 0 && alwaysFill && candidates != allowedBySlot)
                {
                    fitting = allowedBySlot.Where(Fits).ToList();
                }

                // Threaded, not compensated or sawn off.
                if (isBarrel && state.WantsSuppressor)
                {
                    var threaded = fitting.Where(LeadsToSuppressor).ToList();
                    if (threaded.Count > 0)
                    {
                        fitting = threaded;
                    }
                }

                if (isBarrel && fitting.Count > 1)
                {
                    fitting.Remove(factoryPart);
                }

                if (barrelSize is not null)
                {
                    var suits = fitting.Where(tpl => ForwardSize(tpl) <= barrelSize).ToList();
                    if (suits.Count > 0)
                    {
                        fitting = suits;
                    }
                }

                // Not wanting a can: no cans, and a mount-only slot on a muzzle device stays empty.
                if (isMuzzle)
                {
                    if (wantCan)
                    {
                        var leads = fitting.Where(LeadsToSuppressor).ToList();
                        if (leads.Count == 0)
                        {
                            leads = allowedBySlot.Where(tpl => LeadsToSuppressor(tpl) && Fits(tpl)).ToList();
                        }

                        if (leads.Count > 0)
                        {
                            fitting = leads;
                        }
                    }
                    else if (!required)
                    {
                        var noCan = fitting.Where(tpl => !_itemHelper!.IsOfBaseclass(tpl, SilencerBase)).ToList();
                        if (noCan.Count == 0 && !isFirstMuzzle)
                        {
                            continue;
                        }

                        if (noCan.Count > 0)
                        {
                            fitting = noCan;
                        }
                    }
                }

                var firstTactical = isTactical && !built.Any(item => (item.SlotId ?? "").StartsWith("mod_tactical", StringComparison.Ordinal));
                if (isTactical && !firstTactical)
                {
                    var allowed = SecondTacticalChoices(fitting, built, settings);
                    if (allowed is null)
                    {
                        continue;
                    }

                    fitting = allowed;
                }

                var typicalMag = isMag ? TypicalMagazine(factoryPart, fitting) : 0;
                var bigMags = typicalMag > 0 && state.GoodMagazine ? fitting.Where(tpl => Capacity(tpl) >= 1.5 * typicalMag).ToList() : [];
                if (bigMags.Count > 0)
                {
                    fitting = bigMags;
                }

                // Short mags score well on ergo but nobody runs them: a 10 hardly ever, a 20 now and then.
                double MagFit(MongoId tpl) =>
                    typicalMag <= 0 || bigMags.Count > 0 ? 1
                    : Capacity(tpl) < 0.6 * typicalMag ? 0.005
                    : Capacity(tpl) < 0.85 * typicalMag ? 0.1
                    : Capacity(tpl) > 1.7 * typicalMag ? 0.15
                    : 1;
                double Extra(MongoId tpl) =>
                    (curatedSet.Contains(tpl) ? 1.5 : 1)
                    * (firstTactical && KindOf(tpl) == TacticalKind.Combo ? Math.Max(0, settings.ComboTacticalWeight) : 1)
                    * MagFit(tpl);
                if (state.Style == BuildStyle.Budget)
                {
                    var cheap = BuildQuality.CheapEnd(fitting);
                    if (fitting.Contains(factoryPart) && !cheap.Contains(factoryPart))
                    {
                        cheap.Add(factoryPart);
                    }

                    if (cheap.Count > 0)
                    {
                        fitting = cheap;
                    }
                }

                pick = BuildQuality.Pick(fitting, BuildQuality.Weights(fitting, state.Preferences, Extra));
            }

            if (pick is null)
            {
                if (required)
                {
                    state.Missing++;
                    if (Interlocked.Increment(ref _missingRequired) <= 30)
                    {
                        var reason = usable.Count == 0 ? "no usable parts" : _botGenerator!.IsItemIncompatibleWithCurrentItems(built, usable[0], slot.Name).Reason;
                        DebugLog.Summary(() => $"Required slot {slot.Name} on {_itemHelper!.GetItem(parent.Template).Value?.Name} left empty (will retry): {reason}");
                    }
                }

                continue;
            }

            var pickTemplate = _itemHelper!.GetItem(pick.Value).Value;
            var part = new Item
            {
                Id = new MongoId(),
                Template = pick.Value,
                ParentId = parent.Id,
                SlotId = slot.Name
            };
            built.Add(part);
            if (isBarrel && pick.Value != factoryPart)
            {
                state.CustomBarrel = true;
            }

            if (state.Tier.ColourMatch > 0)
            {
                state.Colour ??= BuildQuality.BuildColourFrom(pick.Value);
            }

            if (pickTemplate is not null)
            {
                AddAttachments(part, pick.Value, pickTemplate, state, depth + 1);
            }
        }
    }

    public static PresetOffersConfig Settings => _config!.Mod.GeneratedOffers.Presets;

    public static bool IsDefault(MongoId presetId) => DefaultPresetIds.Value.Contains(presetId);

    public static bool KeepBuild(MongoId presetId)
    {
        if (IsDefault(presetId))
        {
            return true;
        }

        return Random.Shared.NextDouble() < Settings.CustomBuildChance;
    }
}

public class CustomBuildPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairOfferGenerator).GetMethod(
            "CreateOffersFromAssort",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPrefix]
    public static bool Prefix(List<Item> assortItemWithChildren, bool isExpiredOffer)
    {
        if (isExpiredOffer)
        {
            return true;
        }

        var root = assortItemWithChildren.FirstOrDefault();
        var presetId = root?.Upd?.SptPresetId;
        if (presetId is null)
        {
            return true;
        }

        if (!PresetOfferGate.KeepBuild(presetId.Value))
        {
            return false;
        }

        // SPT's preset pricing throws for custom builds.
        if (!PresetOfferGate.IsDefault(presetId.Value))
        {
            root!.Upd!.SptPresetId = null;
        }

        return true;
    }
}

// SPT copies the assort entry per offer, so building here gives each offer its own build.
public class RandomBuildPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairOfferGenerator).GetMethod(
            "CreateSingleOfferForItem",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPrefix]
    public static void Prefix(List<Item> itemWithChildren, bool isExpiredOffer, OfferCreator offerCreator)
    {
        PresetOfferGate.ForgetBuildKind();
        if (isExpiredOffer || offerCreator != OfferCreator.FakePlayer)
        {
            return;
        }

        ArmorOffers.FitPlates(itemWithChildren);
        PresetOfferGate.MaybeRandomBuild(itemWithChildren);
    }
}

public class OfferPricingPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairOfferGenerator).GetMethod(
            "CreateAndAddFleaOffer",
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix(CreateFleaOfferDetails details)
    {
        if (!BarterOffers.MaybeInsteadOfMoney(details))
        {
            BetterFlea.Prices.GunPricing.Apply(details);
            PresetOfferGate.PriceLoadedAmmo(details);
            PresetOfferGate.MaybeLoadLooseMagazine(details);
            PresetOfferGate.ScatterPlainGunPrice(details);
            if (!BetterFlea.Prices.EmptyFuelPricing.Apply(details) && !PresetOfferGate.MaybeDeal(details))
            {
                BetterFlea.Prices.SellerPricing.Apply(details);
            }

            BetterFlea.Prices.SellerPricing.ApplyMultiplier(details);
        }

        OfferQuantityGate.MatchCondition(details);
        if (details.Creator == OfferCreator.FakePlayer && details.Items is { } offerItems)
        {
            ArmorOffers.FlipHinges(offerItems);
        }

        ArmorOffers.LogPriced(details);
    }
}
