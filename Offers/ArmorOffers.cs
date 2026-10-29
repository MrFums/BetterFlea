using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace BetterFlea.Offers;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class ArmorOffersLoad(
    ConfigManager configManager,
    ItemHelper itemHelper,
    RagfairServerHelper ragfairServerHelper,
    PresetHelper presetHelper,
    RagfairConfig ragfairConfig) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        ArmorOffers.Bind(configManager, itemHelper, ragfairServerHelper, presetHelper);

        // SPT pulls a plate out of 40% of armor offers. We fit plates ourselves instead.
        ArmorOffers.SptPlateRemovalChance = ragfairConfig.Dynamic.Armor.RemoveRemovablePlateChance;
        ArmorOffers.ApplyPlateRemoval(ragfairConfig);

        return Task.CompletedTask;
    }
}

// Armor and rigs listed with both plates, one or none; mostly class 4, some class 3 or mixed; side plates now
// and then. Class 5/6 front and back plates are flea banned so never show up.
internal static class ArmorOffers
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static RagfairServerHelper? _ragfair;
    private static PresetHelper? _presets;
    private static int _fitted;

    private static readonly MongoId ArmorVestBase = new("5448e54d4bdc2dcc718b4568");
    private static readonly MongoId VestBase = new("5448e5284bdc2dcb718b4567");
    private static readonly MongoId PlateBase = new("644120aa86ffbe10ee032b6f");
    private static readonly MongoId SoftInsertBase = new("65649eb40bf0ed77b8044453");
    private static readonly MongoId HeadwearBase = new("5a341c4086f77401f2541505");

    private static readonly string[] FavouredPlates = ["monoclete", "elaphros"];

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, RagfairServerHelper ragfairServerHelper, PresetHelper presetHelper)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _ragfair = ragfairServerHelper;
        _presets = presetHelper;
    }

    private static ArmorOffersConfig Settings => _config!.Mod.GeneratedOffers.Armor;

    public static int SptPlateRemovalChance { get; set; }

    public static void ApplyPlateRemoval(RagfairConfig ragfairConfig) =>
        ragfairConfig.Dynamic.Armor.RemoveRemovablePlateChance = Settings.Enabled ? 0 : SptPlateRemovalChance;

    private static bool IsPlate(MongoId tpl) => _itemHelper!.IsOfBaseclass(tpl, PlateBase) && !_itemHelper.IsOfBaseclass(tpl, SoftInsertBase);

    private static bool IsPlateSlot(string? slotName) => slotName?.EndsWith("_plate", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsCarrier(MongoId tpl) =>
        _itemHelper!.IsOfBaseclasses(tpl, [ArmorVestBase, VestBase])
        && _itemHelper.GetItem(tpl).Value?.Properties?.Slots?.Any(slot => IsPlateSlot(slot.Name)) == true;

    private static int ArmorClass(MongoId tpl) =>
        int.TryParse(_itemHelper!.GetItem(tpl).Value?.Properties?.ArmorClass?.ToString(), out var armorClass) ? armorClass : 0;

    private static double Weight(MongoId tpl) => _itemHelper!.GetItem(tpl).Value?.Properties?.Weight ?? 0;

    // Face shields, visors and night vision on a helmet are listed up or flipped down, however the seller last had them.
    public static void FlipHinges(List<Item> items)
    {
        var chance = Settings.FlippedDownChance;
        if (!Settings.Enabled || items.Count < 2)
        {
            return;
        }

        foreach (var part in items.Skip(1).Where(item => _itemHelper!.GetItem(item.Template).Value?.Properties?.HasHinge == true))
        {
            part.Upd ??= new Upd();
            part.Upd.Togglable = new UpdTogglable { On = Random.Shared.NextDouble() < chance };
        }
    }

    // Now and then a helmet has something on it, as its last owner had it: a Bastion's armor plate, a shroud, ears.
    public static void FitHelmetAttachment(List<Item> items)
    {
        var settings = Settings;
        var helmet = items.FirstOrDefault();
        if (!settings.Enabled || helmet is null || !_itemHelper!.IsOfBaseclass(helmet.Template, HeadwearBase)
            || Random.Shared.NextDouble() >= settings.HelmetAttachmentChance)
        {
            return;
        }

        var free = (_itemHelper.GetItem(helmet.Template).Value?.Properties?.Slots ?? [])
            .Where(slot => slot.Required != true && slot.Name is not null
                && !items.Any(item => item.ParentId == helmet.Id && item.SlotId == slot.Name))
            .ToList();
        if (free.Count == 0)
        {
            return;
        }

        var slot = free[Random.Shared.Next(free.Count)];
        var parts = PresetOfferGate.Parts(slot).Where(tpl => PresetOfferGate.CanGoOnBuild(tpl, slot)).ToList();
        if (parts.Count == 0)
        {
            return;
        }

        var tpl = parts[Random.Shared.Next(parts.Count)];
        var max = _itemHelper.GetItem(tpl).Value?.Properties?.MaxDurability ?? 0;
        items.Add(new Item
        {
            Id = new MongoId(),
            Template = tpl,
            ParentId = helmet.Id,
            SlotId = slot.Name,
            Upd = max > 0 ? new Upd { Repairable = new UpdRepairable { Durability = max, MaxDurability = max } } : null
        });
    }

    public static void FitPlates(List<Item> items)
    {
        var settings = Settings;
        var carrier = items.FirstOrDefault();
        if (!settings.Enabled || carrier is null || !IsCarrier(carrier.Template))
        {
            return;
        }

        var slots = _itemHelper!.GetItem(carrier.Template).Value!.Properties!.Slots!.Where(slot => IsPlateSlot(slot.Name)).ToList();
        items.RemoveAll(item => item.ParentId == carrier.Id && IsPlateSlot(item.SlotId));

        var front = slots.FirstOrDefault(slot => slot.Name!.StartsWith("front", StringComparison.OrdinalIgnoreCase));
        var back = slots.FirstOrDefault(slot => slot.Name!.StartsWith("back", StringComparison.OrdinalIgnoreCase));
        var sides = slots.Where(slot => slot.Name!.Contains("side", StringComparison.OrdinalIgnoreCase)).ToList();
        var carrierWeight = Weight(carrier.Template);
        var premium = Premium(carrier.Template);

        // Class 4 front and back, class 6 sides, all mint.
        var bestKit = sides.Count > 0 && Random.Shared.NextDouble() < settings.BestKitChance
            && sides.All(side => Options(side, 6).Count > 0) && front is not null && Options(front, 4).Count > 0;

        var (wantFront, wantBack) = bestKit ? (true, true) : PickMix(settings.Plates);
        var frontClass = Random.Shared.NextDouble() < settings.Class3Chance ? 3 : 4;
        var backClass = frontClass;
        if (!bestKit && Random.Shared.NextDouble() < settings.MixedClassChance)
        {
            (frontClass, backClass) = (4, 3);
        }

        MongoId? frontPick = null;
        if (front is not null && wantFront)
        {
            frontPick = PickPlate(Options(front, bestKit ? 4 : frontClass, fallback: true), carrierWeight, premium);
        }

        MongoId? backPick = null;
        if (back is not null && wantBack)
        {
            var options = Options(back, bestKit ? 4 : backClass, fallback: true);

            // Back usually matches the front.
            backPick = frontPick is { } same && options.Contains(same) && Random.Shared.NextDouble() < 0.75
                ? same
                : PickPlate(options, carrierWeight, premium);
        }

        // The better plate usually goes in the front.
        if (frontPick is { } frontPlate && backPick is { } backPlate && frontPlate != backPlate
            && Options(back!, ArmorClass(frontPlate)).Contains(frontPlate) && Options(front!, ArmorClass(backPlate)).Contains(backPlate))
        {
            var (frontClassPicked, backClassPicked) = (ArmorClass(frontPlate), ArmorClass(backPlate));
            var backIsBetter = backClassPicked != frontClassPicked ? backClassPicked > frontClassPicked : Weight(backPlate) > Weight(frontPlate);
            var betterInFront = backClassPicked != frontClassPicked ? 0.85 : 0.75;
            if (backIsBetter == Random.Shared.NextDouble() < betterInFront)
            {
                (frontPick, backPick) = (backPlate, frontPlate);
            }
        }

        if (front is not null)
        {
            Add(items, carrier, front, frontPick);
        }

        if (back is not null)
        {
            Add(items, carrier, back, backPick);
        }

        if (sides.Count > 0 && (bestKit || Random.Shared.NextDouble() < settings.SidePlateChance))
        {
            // Usually a pair, sometimes mixed or one missing.
            var sideClass = bestKit ? 6 : Random.Shared.NextDouble() < 0.5 ? 5 : 6;
            var sidePick = PickPlate(Options(sides[0], sideClass, fallback: true), carrierWeight, premium);
            var missing = !bestKit && sides.Count > 1 && Random.Shared.NextDouble() < 0.15 ? sides[Random.Shared.Next(sides.Count)] : null;
            foreach (var side in sides.Where(side => side != missing))
            {
                var mixed = !bestKit && Random.Shared.NextDouble() < 0.25;
                var options = Options(side, mixed ? (Random.Shared.NextDouble() < 0.5 ? 5 : 6) : sideClass, fallback: true);
                Add(items, carrier, side, !mixed && sidePick is { } pick && options.Contains(pick) ? pick : PickPlate(options, carrierWeight, premium));
            }
        }

        if (bestKit)
        {
            BestKits[carrier.Id] = true;
        }

        if (Interlocked.Increment(ref _fitted) <= 8)
        {
            DebugLog.Summary(() => $"Armor offer: {_itemHelper.GetItem(carrier.Template).Value?.Name}{(bestKit ? " (best kit)" : "")} (price {BuildQuality.Price(carrier.Template):0}, premium {premium:0.00}) with "
                + string.Join(", ", items.Where(item => item.ParentId == carrier.Id && IsPlateSlot(item.SlotId))
                    .Select(item => $"{item.SlotId}={_itemHelper.GetItem(item.Template).Value?.Name}")));
        }
    }

    // Best-kit carriers stay mint in the condition roll.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<MongoId, bool> BestKits = new();

    private static (bool Front, bool Back) PickMix(ArmorPlateMixConfig mix)
    {
        var options = new (bool, bool, double)[] { (true, true, mix.Both), (true, false, mix.FrontOnly), (false, true, mix.BackOnly), (false, false, mix.None) };
        var roll = Random.Shared.NextDouble() * options.Sum(option => Math.Max(0, option.Item3));
        foreach (var (front, back, weight) in options)
        {
            roll -= Math.Max(0, weight);
            if (roll < 0)
            {
                return (front, back);
            }
        }

        return (true, true);
    }

    // With fallback, the nearest class there is (some Russian carriers only take their own plates).
    private static List<MongoId> Options(Slot slot, int armorClass, bool fallback = false)
    {
        var fitting = PresetOfferGate.Parts(slot)
            .Where(tpl => IsPlate(tpl) && _ragfair!.IsItemValidRagfairItem(_itemHelper!.GetItem(tpl))
                && (_itemHelper.GetItem(tpl).Value?.Properties?.Weight ?? 0) > 0)
            .ToList();
        var exact = fitting.Where(tpl => ArmorClass(tpl) == armorClass).ToList();
        if (exact.Count > 0 || !fallback || fitting.Count == 0)
        {
            return exact;
        }

        var nearest = fitting.Min(tpl => Math.Abs(ArmorClass(tpl) - armorClass));
        return fitting.Where(tpl => Math.Abs(ArmorClass(tpl) - armorClass) == nearest).ToList();
    }

    // Heavy plates in light carriers, light plates in heavy ones, and the dearer the carrier the more
    // often it has the light class 4s.
    private static MongoId? PickPlate(List<MongoId> options, double carrierWeight, double premium)
    {
        if (options.Count == 0)
        {
            return null;
        }

        // Pick light or heavy first. There are more light class 4s, so picking from all of them would skew it.
        bool IsLight(MongoId tpl) => Weight(tpl) < LightPlateWeight;
        if (options.All(tpl => ArmorClass(tpl) == 4) && options.Any(IsLight) && !options.All(IsLight))
        {
            var shift = carrierWeight < 3 ? -0.1 : carrierWeight >= 5 ? 0.15 : 0;
            var lightShare = Math.Clamp(0.3 + 0.3 * premium + shift, 0.1, 0.85);
            var wantLight = Random.Shared.NextDouble() < lightShare;
            options = options.Where(tpl => IsLight(tpl) == wantLight).ToList();
        }

        var lean = carrierWeight < 3 ? 0.5 : carrierWeight >= 5 ? -0.8 : -0.25;
        var weights = options.Select(tpl =>
        {
            var name = _itemHelper!.GetItem(tpl).Value?.Name ?? "";
            var favoured = FavouredPlates.Any(key => name.Contains(key, StringComparison.OrdinalIgnoreCase)) ? 1.5 : 1;
            return Math.Exp(lean * (Weight(tpl) - 2.5)) * favoured;
        }).ToArray();
        return BuildQuality.Pick(options, weights);
    }

    // Monoclete, Elaphros, Titan, NewSphereTech.
    private const double LightPlateWeight = 2.5;

    // Carriers by price with their default plates: 0 for the cheapest fifth, 1 for the dearest few. By rank, not
    // roubles, so SPT's price scale doesn't matter.
    private static double Premium(MongoId carrier)
    {
        var prices = CarrierPrices.Value;
        var price = PriceWithPlates(carrier);
        if (prices.Count == 0 || price <= 0)
        {
            return 0;
        }

        var index = prices.BinarySearch(price);
        var rank = (double)(index < 0 ? ~index : index) / prices.Count;
        return Math.Clamp((rank - 0.2) / 0.75, 0, 1);
    }

    private static readonly Lazy<List<double>> CarrierPrices = new(() =>
        _presets!.GetDefaultPresets().Values.Where(preset => preset.Items is { Count: > 0 } && IsCarrier(preset.Items[0].Template))
            .Select(preset => PriceWithPlates(preset.Items[0].Template)).Where(price => price > 0).OrderBy(price => price).ToList());

    private static double PriceWithPlates(MongoId carrier) =>
        _presets!.GetDefaultPreset(carrier)?.Items is { Count: > 0 } items
            ? items.Sum(item => BuildQuality.Price(item.Template))
            : BuildQuality.Price(carrier);

    private static void Add(List<Item> items, Item carrier, Slot slot, MongoId? plate)
    {
        if (plate is not { } tpl)
        {
            return;
        }

        var max = _itemHelper!.GetItem(tpl).Value?.Properties?.MaxDurability ?? 0;
        items.Add(new Item
        {
            Id = new MongoId(),
            Template = tpl,
            ParentId = carrier.Id,
            SlotId = slot.Name,
            Upd = new Upd { Repairable = new UpdRepairable { Durability = max, MaxDurability = max } }
        });
    }

    private static int _logged;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<MongoId, int> LoggedPerItem = new();

    public static void LogPriced(SPTarkov.Server.Core.Models.Spt.Ragfair.CreateFleaOfferDetails details)
    {
        var items = details.Items;
        if (items is not { Count: > 0 } || !_config!.Mod.DebugLogging)
        {
            return;
        }

        var root = items[0].Template;
        var isCarrierOrPlate = IsCarrier(root) || IsPlate(root);
        if (!isCarrierOrPlate && !IsHardArmor(root) && !items.Any(item => _itemHelper!.IsOfBaseclass(item.Template, SoftInsertBase)))
        {
            return;
        }

        if (LoggedPerItem.AddOrUpdate(root, 1, (_, count) => count + 1) > 4 || Interlocked.Increment(ref _logged) > 600)
        {
            return;
        }

        var money = details.BarterScheme?.Where(barter => _itemHelper!.IsOfBaseclass(barter.Template, SPTarkov.Server.Core.Models.Enums.BaseClasses.MONEY))
            .Sum(barter => barter.Count ?? 0) ?? 0;
        string Piece(Item item) => $"{_itemHelper!.GetItem(item.Template).Value?.Name}({item.Upd?.Repairable?.Durability:0}/{item.Upd?.Repairable?.MaxDurability:0})";
        var pieces = isCarrierOrPlate
            ? items.Where(item => item == items[0] && IsPlate(item.Template) || IsPlateSlot(item.SlotId))
            : items.Where(item => IsHardArmor(item.Template) || _itemHelper!.IsOfBaseclass(item.Template, SoftInsertBase));
        DebugLog.Summary(() => $"{(isCarrierOrPlate ? "Armor" : "Armor gear")} priced: {_itemHelper!.GetItem(root).Value?.Name} {money:0} premium={Premium(root):0.00} | "
            + string.Join(", ", pieces.Select(Piece)));
    }

    private enum Condition
    {
        Mint,
        Worn,
        Destroyed,
        Obliterated,
        Shot
    }

    // Plates, face shields, armored masks and visors, helmet ears and tops.
    private static bool IsHardArmor(MongoId tpl) =>
        !_itemHelper!.IsOfBaseclass(tpl, SoftInsertBase) && ArmorClass(tpl) > 0
        && (_itemHelper.GetItem(tpl).Value?.Properties?.MaxDurability ?? 0) > 0;

    // Runs after SPT's own roll, which barely varies.
    public static void RollCondition(List<Item> items, TemplateItem template)
    {
        var settings = Settings;
        var root = items.FirstOrDefault();
        if (!settings.Enabled || root is null
            || !(IsCarrier(template.Id) || IsHardArmor(template.Id) || items.Any(item => _itemHelper!.IsOfBaseclass(item.Template, SoftInsertBase))))
        {
            return;
        }

        var weights = settings.ConditionByRarity.TryGetValue(template.Properties?.RarityPvE ?? "", out var byRarity)
            ? byRarity
            : new ArmorConditionWeights();

        // Helmets are rarely wrecked, but a flea of near-max helmets looked wrong, rare ones (Ulach, Bastion) worst,
        // so they are a bit less often mint than other armor.
        var helmet = _itemHelper!.IsOfBaseclass(template.Id, HeadwearBase);
        if (helmet)
        {
            var damage = Math.Clamp(settings.HelmetDamage, 0, 1);
            weights = weights with { Mint = weights.Mint * HelmetMintShare, Destroyed = weights.Destroyed * damage, Obliterated = weights.Obliterated * damage };
        }
        var bestKit = BestKits.TryRemove(root.Id, out _);

        // Taken off a boss or guard: they were shot until they dropped, so a good max but a low current.
        var overall = bestKit ? Condition.Mint
            : helmet && PresetOfferGate.IsBossHelmet(template.Id) && Random.Shared.NextDouble() < Math.Clamp(settings.BossHelmetChance, 0, 1) ? Condition.Shot
            : PickCondition(weights);

        // Soft armor mostly follows the plates; mint often comes out a bit worn.
        var softWeights = weights with { Mint = weights.Mint / 2, Worn = weights.Worn + weights.Mint / 2 };
        var soft = bestKit ? Condition.Mint
            : Random.Shared.NextDouble() < SoftOwnRollChance ? PickCondition(softWeights)
            : overall == Condition.Mint && Random.Shared.NextDouble() < 0.5 ? Condition.Worn
            : overall;

        foreach (var item in items)
        {
            var isSoftInsert = _itemHelper!.IsOfBaseclass(item.Template, SoftInsertBase);
            if (!isSoftInsert && !IsHardArmor(item.Template))
            {
                continue;
            }

            // The odd one out is only ever mint or worn, so one wrecked plate doesn't turn up on a good offer.
            var state = bestKit ? Condition.Mint
                : Random.Shared.NextDouble() < PieceOddOneOutChance ? PickMild(isSoftInsert ? softWeights : weights)
                : isSoftInsert ? soft : overall;
            Apply(item, state, helmet);
        }
    }

    private const double HelmetMintShare = 0.6;
    private const double SoftOwnRollChance = 0.3;
    private const double PieceOddOneOutChance = 0.2;

    private static Condition PickMild(ArmorConditionWeights weights) =>
        Random.Shared.NextDouble() * (weights.Mint + weights.Worn) < weights.Mint ? Condition.Mint : Condition.Worn;

    private static Condition PickCondition(ArmorConditionWeights weights)
    {
        var options = new (Condition, double)[]
        {
            (Condition.Mint, weights.Mint), (Condition.Worn, weights.Worn), (Condition.Destroyed, weights.Destroyed), (Condition.Obliterated, weights.Obliterated)
        };
        var roll = Random.Shared.NextDouble() * options.Sum(option => Math.Max(0, option.Item2));
        foreach (var (condition, weight) in options)
        {
            roll -= Math.Max(0, weight);
            if (roll < 0)
            {
                return condition;
            }
        }

        return Condition.Worn;
    }

    // Destroyed: 15-35% left but a good max (worth repairing). Obliterated: the max is worn down too.
    // Never 1 HP: a plate only has about 40.
    private static void Apply(Item item, Condition state, bool helmet)
    {
        var full = _itemHelper!.GetItem(item.Template).Value?.Properties?.MaxDurability ?? 0;
        if (full <= 0)
        {
            return;
        }

        double Between(double low, double high) => low + Random.Shared.NextDouble() * (high - low);
        var (max, current) = state switch
        {
            Condition.Worn when helmet => Worn(full * Between(0.8, 0.98), Between(0.6, 0.97)),
            Condition.Shot => Worn(full * Between(0.88, 1.0), Between(0.2, 0.55)),
            Condition.Worn => Worn(full * Between(0.85, 1.0), Between(0.45, 0.98)),
            Condition.Destroyed => Worn(full * Between(0.8, 1.0), Between(0.15, 0.35)),
            Condition.Obliterated => Worn(full * Between(0.25, 0.45), Between(0.35, 0.55)),
            _ => (full, full)
        };

        item.Upd ??= new Upd();
        item.Upd.Repairable = new UpdRepairable { Durability = current, MaxDurability = max };
        if (max < full && Random.Shared.NextDouble() < Settings.RepairBuffChance)
        {
            UsedListingsGate.AddRepairBuff(item.Upd, current, weapon: false);
        }
    }

    // At least 2: SPT prices 0 as new and 1 looks broken.
    private static (double Max, double Current) Worn(double max, double share)
    {
        var roundedMax = Math.Max(2, Math.Round(max, 1));
        return (roundedMax, Math.Clamp(Math.Round(roundedMax * share, 1), 2, roundedMax));
    }
}
