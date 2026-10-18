using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using BetterFlea.Flea;

namespace BetterFlea.Offers;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class UsedListingsPatchesLoad(
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

        UsedListingsGate.Bind(configManager, itemHelper, ragfairConfig);
        new UsedListingsPatch().Enable();
        return Task.CompletedTask;
    }
}

// SPT rolls condition with GetDouble(max.min, max.min), so worn keys/meds/food always come out the same.
// We re-roll uses left ourselves. Meds/food/fuel/repair kits keep SPT's pricing (resource left / max);
// keys get their own curve so a 1-use key still holds value.
internal static class UsedListingsGate
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static RagfairConfig? _ragfairConfig;

    // Max uses each key had when its offers were rolled. Some mods change it after the
    // startup offers exist; StartupKeyOfferResync uses this to re-roll them. Offers generate in parallel.
    private static readonly ConcurrentDictionary<MongoId, int> GeneratedKeyMaxUses = new();

    public static IReadOnlyDictionary<MongoId, int> KeyMaxUsesAtGeneration => GeneratedKeyMaxUses;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, RagfairConfig ragfairConfig)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _ragfairConfig = ragfairConfig;
    }

    public static void RollCondition(IEnumerable<Item> itemWithMods, TemplateItem itemDetails)
    {
        var used = _config?.Mod.GeneratedOffers.UsedItems;
        var props = itemDetails.Properties;
        var upd = itemWithMods.FirstOrDefault()?.Upd;
        if (used is null || props is null || upd is null)
        {
            return;
        }

        if (upd.Key is not null)
        {
            var max = props.MaximumNumberOfUsage ?? 0;
            if (RollKeyUsesLeft(max, KeySettingsFor(itemDetails.Id)) is { } left)
            {
                upd.Key.NumberOfUsages = max - left;
                GeneratedKeyMaxUses[itemDetails.Id] = max;
            }
        }
        else if (upd.MedKit is not null)
        {
            var max = props.MaxHpResource ?? 0;
            if (RollResourceLeft(max, used.Meds) is { } left)
            {
                upd.MedKit.HpResource = left;
            }
        }
        else if (upd.FoodDrink is not null)
        {
            var max = props.MaxResource ?? 0;
            if (RollResourceLeft(max, used.FoodAndDrink) is { } left)
            {
                upd.FoodDrink.HpPercent = left;
            }
        }
        else if (upd.RepairKit is not null)
        {
            var max = props.MaxRepairResource ?? 0;
            if (RollResourceLeft(max, used.RepairKits) is { } left)
            {
                upd.RepairKit.Resource = left;
            }
        }
        // SPT only adds a fuel Resource when it rolls the fuel as worn, so it may not exist yet.
        else if (_itemHelper?.IsOfBaseclass(itemDetails.Id, FleaCategories.Fuel) == true)
        {
            var max = props.MaxResource ?? 0;
            if (RollResourceLeft(max, used.Fuel) is { } left)
            {
                upd.Resource = new UpdResource { Value = left, UnitsConsumed = max - left };
            }
        }
    }

    // SPT already multiplied the price by usesLeft / max; swap that for our curve. Applied by DynamicPricePatch.
    public static double KeyPriceFactor(MongoId tpl, Item? item)
    {
        var usedCount = item?.Upd?.Key?.NumberOfUsages ?? 0;
        if (usedCount <= 0 || _itemHelper is null || _ragfairConfig is null
            || _ragfairConfig.Dynamic.IgnoreQualityPriceVarianceBlacklist.Contains(tpl))
        {
            return 1.0;
        }

        var max = _itemHelper.GetItem(tpl).Value?.Properties?.MaximumNumberOfUsage ?? 0;
        var left = max - usedCount;
        if (max < 2 || left < 1)
        {
            return 1.0;
        }

        var vanilla = (double)left / max;
        return KeyPriceCurve(left, max, KeySettingsFor(tpl)) / vanilla;
    }

    // Share of a brand new key's price: OneUsePricePercent at 1 use left, straight line up to 1.0 at max.
    internal static double KeyPriceCurve(int left, int max, KeyListingSettings settings)
    {
        if (max < 2 || left >= max)
        {
            return 1.0;
        }

        var oneUse = Math.Clamp(settings.OneUsePricePercent / 100.0, 0.0, 1.0);
        return oneUse + (1.0 - oneUse) * (Math.Max(1, left) - 1) / (max - 1);
    }

    internal static bool IgnoresCondition(MongoId tpl) =>
        _ragfairConfig?.Dynamic.IgnoreQualityPriceVarianceBlacklist.Contains(tpl) == true;

    internal static KeyListingSettings KeySettingsFor(MongoId tpl)
    {
        var used = _config!.Mod.GeneratedOffers.UsedItems;
        if (FleaCategories.MarkedKeys.Contains(tpl)) return used.MarkedKeys;
        if (_itemHelper!.IsOfBaseclass(tpl, FleaCategories.Keycard)) return used.Keycards;
        return used.Keys;
    }

    // null = don't touch, max = brand new, otherwise 1..max-1 where each step up is UsesLeftFalloff as likely.
    internal static int? RollKeyUsesLeft(int max, KeyListingSettings settings)
    {
        if (!settings.Enabled || max < 2)
        {
            return null;
        }

        if (Random.Shared.NextDouble() >= settings.UsedChance)
        {
            return max;
        }

        var total = 0.0;
        var weight = 1.0;
        for (var left = 1; left < max; left++)
        {
            total += weight;
            weight *= settings.UsesLeftFalloff;
        }

        var roll = Random.Shared.NextDouble() * total;
        weight = 1.0;
        for (var left = 1; left < max - 1; left++)
        {
            roll -= weight;
            if (roll < 0)
            {
                return left;
            }

            weight *= settings.UsesLeftFalloff;
        }

        return max - 1;
    }

    // null = don't touch, max = brand new, otherwise 1..max-1, pushed towards 1 by LowAmountBias.
    internal static int? RollResourceLeft(int max, ResourceListingSettings settings)
    {
        if (!settings.Enabled || max < 2)
        {
            return null;
        }

        if (Random.Shared.NextDouble() >= settings.UsedChance)
        {
            return max;
        }

        var share = Math.Pow(Random.Shared.NextDouble(), settings.LowAmountBias);
        return Math.Clamp((int)Math.Ceiling(share * (max - 1)), 1, max - 1);
    }
}

public class UsedListingsPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairOfferGenerator).GetMethod(
            "RandomiseOfferItemUpdProperties",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPostfix]
    public static void Postfix(IEnumerable<Item> itemWithMods, TemplateItem itemDetails, OfferCreator offerCreator)
    {
        if (offerCreator != OfferCreator.FakePlayer)
        {
            return;
        }

        UsedListingsGate.RollCondition(itemWithMods, itemDetails);
    }
}
