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
    /// <summary>Not ours to change: stackables, presets, things SPT already lists one at a time.</summary>
    Spt,
    Single,
    Few,
    Stock,
    Bulk
}

// Only the non-stackable path is replaced. Stackables (ammo, money) keep SPT's own share-of-a-stack roll.
internal static class OfferQuantityGate
{
    private static readonly MongoId BarterItem = new("5448eb774bdc2d0a728b4567");
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

    // Who sells this in what amounts on live:
    //  - common, small barter goods get farmed and dumped by the stack;
    //  - meds, food and mods are held a few at a time;
    //  - anything rare, valuable or bulky is sold as it's found.
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

        // Gear, containers, info items and the rest.
        return QuantityTier.Few;
    }

    private enum Rarity { Common, Rare, SuperRare }

    // Modded items often have no rarity; treat them as rare rather than guess they're common.
    private static Rarity RarityOf(TemplateItemProperties props)
    {
        return props.RarityPvE switch
        {
            "Common" => Rarity.Common,
            "Superrare" or "Not_exist" => Rarity.SuperRare,
            _ => Rarity.Rare
        };
    }

    // 1 is the most likely; each extra item is Falloff times as likely as one fewer.
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
