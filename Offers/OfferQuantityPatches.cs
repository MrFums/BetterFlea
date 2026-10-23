using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using BetterFlea.Flea;

namespace BetterFlea.Offers;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class OfferQuantityPatchesLoad(
    ConfigManager configManager,
    ItemHelper itemHelper,
    RagfairConfig ragfairConfig) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        OfferQuantityGate.Bind(configManager, itemHelper, ragfairConfig);
        new OfferQuantityPatch().Enable();
        return Task.CompletedTask;
    }
}

public enum QuantityTier
{
    Spt,
    Single,
    Few,
    Stock,
    Bulk
}

// Stackables keep SPT's own roll.
internal static class OfferQuantityGate
{
    private static readonly MongoId BarterItem = new("5448eb774bdc2d0a728b4567");
    private const string SignalFlareCaliber = "Caliber26x75";
    private static readonly MongoId Throwable = new("543be6564bdc2df4348b4568");
    private static readonly MongoId AmmoBox = new("543be5cb4bdc2deb348b4568");
    private static readonly MongoId[] EverydayKinds =
    [
        new("543be5664bdc2dd4348b4569"), // Meds
        new("543be6674bdc2df1348b4569"), // Food and drink
        new("5448fe124bdc2da5018b4567")  // Weapon mods, incl. magazines and sights
    ];

    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static RagfairConfig? _ragfairConfig;
    private static readonly ConcurrentDictionary<MongoId, QuantityTier> Tiers = new();

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, RagfairConfig ragfairConfig)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _ragfairConfig = ragfairConfig;
    }

    public static int Quantity(MongoId tpl, bool isPreset, int sptCount)
    {
        var settings = _config?.Mod.GeneratedOffers.ItemsPerOffer;
        if (settings is null || !settings.Enabled || isPreset || _itemHelper is null)
        {
            return sptCount;
        }

        // Flares share the ammo class, so tell them apart by calibre.
        if (_itemHelper.GetItem(tpl).Value?.Properties?.Caliber == SignalFlareCaliber)
        {
            return Roll(settings.SignalFlares);
        }

        if (_itemHelper.IsOfBaseclass(tpl, AmmoBox))
        {
            return Roll(settings.AmmoBoxes);
        }

        if (_itemHelper.IsOfBaseclass(tpl, Throwable))
        {
            return IsBulkGrenade(tpl) && RollBulk(settings.GrenadeBulk) is { } pile ? pile : RollWeighted(settings.GrenadeWeights);
        }

        if (_itemHelper.IsOfBaseclass(tpl, StimBase) && RollBulk(settings.StimBulk) is { } handful)
        {
            return handful;
        }

        return TierFor(tpl) switch
        {
            QuantityTier.Single => 1,
            QuantityTier.Few => Roll(settings.Few),
            QuantityTier.Stock => Roll(settings.Stock),
            QuantityTier.Bulk => Roll(settings.Bulk),
            _ => sptCount
        };
    }

    public static QuantityTier TierFor(MongoId tpl) => Tiers.GetOrAdd(tpl, Classify);

    // Cheap common barter goods get dumped by the stack, meds/food/mods a few at a time, anything rare
    // or bulky one at a time.
    private static QuantityTier Classify(MongoId tpl)
    {
        var item = _itemHelper!.GetItem(tpl).Value;
        var props = item?.Properties;
        if (props is null || (props.StackMaxSize ?? 1) != 1
            || _itemHelper.IsOfBaseclasses(tpl, _ragfairConfig!.Dynamic.ShowAsSingleStack))
        {
            return QuantityTier.Spt;
        }

        if (_config!.Mod.GeneratedOffers.ItemsPerOffer.ArmoredRigsSingle && FleaCategories.Find(item!, _itemHelper)?.Name == "gear.armoredRigs")
        {
            return QuantityTier.Single;
        }

        var cells = (props.Width ?? 1) * (props.Height ?? 1);
        var rarity = RarityOf(props);

        if (_itemHelper.IsOfBaseclass(tpl, BarterItem))
        {
            return rarity switch
            {
                Rarity.Common when cells <= 1 => QuantityTier.Bulk,
                Rarity.Common or Rarity.Rare when cells <= 2 => QuantityTier.Stock,
                _ => QuantityTier.Few
            };
        }

        if (_itemHelper.IsOfBaseclasses(tpl, EverydayKinds))
        {
            return rarity == Rarity.Common && cells <= 2 ? QuantityTier.Stock : QuantityTier.Few;
        }

        return QuantityTier.Few;
    }

    private enum Rarity { Common, Rare, SuperRare }

    // No rarity (often modded): treat as rare.
    private static Rarity RarityOf(TemplateItemProperties props)
    {
        return props.RarityPvE switch
        {
            "Common" => Rarity.Common,
            "Superrare" or "Not_exist" => Rarity.SuperRare,
            _ => Rarity.Rare
        };
    }

    private static readonly MongoId FoodBase = new("5448e8d04bdc2ddf718b4569");
    private static readonly MongoId DrinkBase = new("5448e8d64bdc2dce718b4568");

    // Nobody has three AFAKs all at 322. Once used, big kits, food, fuel and repair kits are one per offer;
    // few-use meds can still come a couple at a time. Full meds come in ones, or now and then a few (fullMedWeights).
    public static void MatchCondition(SPTarkov.Server.Core.Models.Spt.Ragfair.CreateFleaOfferDetails details)
    {
        var settings = _config?.Mod.GeneratedOffers.ItemsPerOffer;
        if (details.Creator != SPTarkov.Server.Core.Models.Enums.OfferCreator.FakePlayer || details.SellInOnePiece
            || details.Items is not { Count: 1 } items || _itemHelper is null || settings is not { Enabled: true })
        {
            return;
        }

        var root = items[0];
        var props = _itemHelper.GetItem(root.Template).Value?.Properties;
        if (props is null || ResourceOf(root, props) is not { } resource || resource.Max <= 1)
        {
            return;
        }

        var full = resource.Left >= resource.Max;
        var medical = _itemHelper.IsOfBaseclass(root.Template, MedicalBase);
        if (full && medical)
        {
            details.Quantity = RollWeighted(settings.FullMedWeights);
            return;
        }

        if (details.Quantity <= 1)
        {
            return;
        }

        var big = !medical || resource.Max > UsedListingsGate.FewUsesMax || IsSurgical(props);
        details.Quantity = (big, full) switch
        {
            (true, false) => 1,
            (true, true) => Random.Shared.NextDouble() < BigFullSeveralChance ? Math.Min(details.Quantity, 5) : 1,
            (false, false) => Random.Shared.NextDouble() < FewUsedSeveralChance ? Math.Min(details.Quantity, 3) : 1,
            _ => details.Quantity
        };
    }

    private const double BigFullSeveralChance = 0.25;
    private const double FewUsedSeveralChance = 0.2;
    private static readonly MongoId MedicalBase = new("543be5664bdc2dd4348b4569");
    private static readonly MongoId FuelBase = new("5d650c3e815116009f6201d2");
    private static readonly MongoId RepairKitBase = new("616eb7aea207f41933308f46");

    private static (double Left, double Max)? ResourceOf(Item item, TemplateItemProperties props)
    {
        var upd = item.Upd;
        if (_itemHelper!.IsOfBaseclass(item.Template, MedicalBase) && (props.MaxHpResource ?? 0) > 0)
        {
            return (upd?.MedKit?.HpResource ?? props.MaxHpResource!.Value, props.MaxHpResource!.Value);
        }

        if (_itemHelper.IsOfBaseclasses(item.Template, [FoodBase, DrinkBase]) && (props.MaxResource ?? 0) > 0)
        {
            return (upd?.FoodDrink?.HpPercent ?? props.MaxResource!.Value, props.MaxResource!.Value);
        }

        if (_itemHelper.IsOfBaseclass(item.Template, FuelBase) && (props.MaxResource ?? 0) > 0)
        {
            return (upd?.Resource?.Value ?? props.MaxResource!.Value, props.MaxResource!.Value);
        }

        if (_itemHelper.IsOfBaseclass(item.Template, RepairKitBase) && (props.MaxRepairResource ?? 0) > 0)
        {
            return (upd?.RepairKit?.Resource ?? props.MaxRepairResource!.Value, props.MaxRepairResource!.Value);
        }

        return null;
    }

    internal static bool IsSurgical(TemplateItemProperties props) =>
        props.EffectsDamage?.Keys.Any(effect => effect.ToString() == "DestroyedPart") == true;

    private static readonly MongoId StimBase = new("5448f3a64bdc2d60728b456a");

    private static readonly HashSet<MongoId> BulkGrenades =
    [
        new("5448be9a4bdc2dfd2f8b456a"), // RGD-5
        new("5710c24ad2720bc3458b45a3"), // F-1
        new("58d3db5386f77426186285a0"), // M67
        new("5a0c27731526d80618476ac4"), // Zarya
        new("5e32f56fcb6d5863cc5e5ee4"), // VOG-17
        new("5e340dcdcb6d5863cc5e5efb"), // VOG-25
        new("5a2a57cfc4a2826c6e06d44a"), // RDG-2B
        new("617aa4dd8166f034d57de9c5")  // M18
    ];

    // Plus any cheap common/rare grenade a mod adds.
    private static bool IsBulkGrenade(MongoId tpl) =>
        BulkGrenades.Contains(tpl)
        || _itemHelper!.GetItem(tpl).Value?.Properties is { } props && props.RarityPvE is "Common" or "Rare"
            && BuildQuality.HandbookPrice(tpl) is > 0 and <= 20_000;

    private static int? RollBulk(BulkOfferConfig bulk)
    {
        if (bulk.Chance <= 0 || Random.Shared.NextDouble() >= bulk.Chance)
        {
            return null;
        }

        var low = Math.Max(1, bulk.Min);
        var high = Math.Max(low, bulk.Max);
        return low + (int)Math.Floor(Math.Pow(Random.Shared.NextDouble(), 1.5) * (high - low + 1));
    }

    internal static int RollWeighted(IReadOnlyList<double> weights)
    {
        var total = weights.Sum(w => Math.Max(0, w));
        if (total <= 0)
        {
            return 1;
        }

        var roll = Random.Shared.NextDouble() * total;
        for (var i = 0; i < weights.Count; i++)
        {
            roll -= Math.Max(0, weights[i]);
            if (roll < 0)
            {
                return i + 1;
            }
        }

        return weights.Count;
    }

    internal static int Roll(QuantityProfile profile)
    {
        var total = 0.0;
        var weight = 1.0;
        for (var count = 1; count <= profile.Max; count++)
        {
            total += weight;
            weight *= profile.Falloff;
        }

        var roll = Random.Shared.NextDouble() * total;
        weight = 1.0;
        for (var count = 1; count < profile.Max; count++)
        {
            roll -= weight;
            if (roll < 0)
            {
                return count;
            }

            weight *= profile.Falloff;
        }

        return profile.Max;
    }
}

public class OfferQuantityPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairServerHelper).GetMethod(
            nameof(RagfairServerHelper.CalculateDynamicStackCount),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(MongoId tplId, bool isPreset, ref int __result)
    {
        __result = OfferQuantityGate.Quantity(tplId, isPreset, __result);
    }
}
