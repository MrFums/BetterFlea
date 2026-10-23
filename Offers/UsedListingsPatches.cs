using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Services.Ragfair;
using BetterFlea.Flea;
using BetterFlea.Prices;

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

// SPT rolls condition with GetDouble(min, min), so worn keys and meds always come out the same.
// Keys get their own price curve so a 1-use key still holds some value.
internal static class UsedListingsGate
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static RagfairConfig? _ragfairConfig;

    // Some mods change key max uses after the startup offers exist; this lets the resync re-roll them.
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
            // A Surv12 wears down over several raids, so any amount is as likely. A CMS (3 uses) is treated like a splint.
            var bigSurgical = max > 5 && OfferQuantityGate.IsSurgical(props);
            var left = max <= FewUsesMax && !bigSurgical ? RollFewUsesLeft(max, used.Meds)
                : RollResourceLeft(max, bigSurgical ? used.Meds with { LowAmountBias = 1.0 } : used.Meds);
            if (left is { } hp)
            {
                upd.MedKit.HpResource = hp;
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
        // SPT only adds the Resource when it rolls the fuel as worn.
        else if (_itemHelper?.IsOfBaseclass(itemDetails.Id, FleaCategories.Fuel) == true)
        {
            var max = props.MaxResource ?? 0;
            if (used.Fuel.Enabled && max > 0 && Random.Shared.NextDouble() < used.Fuel.EmptyChance)
            {
                upd.Resource = new UpdResource { Value = 0, UnitsConsumed = max };
            }
            else if (RollResourceLeft(max, used.Fuel) is { } left)
            {
                upd.Resource = new UpdResource { Value = left, UnitsConsumed = max - left };
            }
        }

        // Most guns are a little under max, a few have a lower max, a few are badly worn. wearByBuild shifts it.
        var buildKind = PresetOfferGate.TakeBuildKind();
        if (upd.Repairable is { } repairable && used.Guns.Enabled && _itemHelper?.IsOfBaseclass(itemDetails.Id, Gun) == true)
        {
            var gun = used.Guns;

            if (buildKind == "ground")
            {
                var full = props.MaxDurability ?? repairable.MaxDurability ?? 100.0;
                repairable.MaxDurability = full;
                repairable.Durability = Random.Shared.NextDouble() < 0.7 ? full : full - Random.Shared.Next(1, 4);
                return;
            }

            var wearFactor = buildKind is not null && gun.WearByBuild.TryGetValue(buildKind, out var byBuild) ? Math.Max(0, byBuild) : 1;

            // Factor 2 turns 50% into 75%, 0.4 turns it into 24%.
            double Scaled(double chance) => 1 - Math.Pow(1 - Math.Clamp(chance, 0, 1), wearFactor);

            var max = repairable.MaxDurability ?? 100.0;
            if (Random.Shared.NextDouble() < Scaled(gun.LowMaxChance))
            {
                var low = Math.Round(gun.LowMaxMin + Random.Shared.NextDouble() * (gun.LowMaxMax - gun.LowMaxMin));
                if (low < max)
                {
                    max = low;
                }
            }

            repairable.MaxDurability = max;
            var wornChance = gun.WornChanceByRarity.TryGetValue(props.RarityPvE ?? "", out var byRarity) ? byRarity : 0.5;
            var mostWear = Math.Min(gun.MaxWear * wearFactor, MostGunWear);
            var wear = Random.Shared.NextDouble() < Scaled(wornChance)
                ? Math.Round(1 + Math.Pow(Random.Shared.NextDouble(), 1.6) * Math.Max(0, mostWear - 1))
                : 0;
            repairable.Durability = Math.Max(MinGunDurability, max - wear);
        }
    }

    private const double MostGunWear = 60;
    private const double MinGunDurability = 10;

    private static readonly MongoId Gun = new("5422acb9af1c889c16000029");

    // SPT already scaled by usesLeft / max; swap that for our curve.
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

    // null = leave it, max = new, otherwise 1..max-1.
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

    // Never right down to 1: a medkit with 1 HP looks like a bug.
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
        var lowest = Math.Clamp((int)Math.Ceiling(max * LowestResourceShare), 1, max - 1);
        return Math.Clamp(lowest + (int)Math.Round(share * (max - 1 - lowest)), lowest, max - 1);
    }

    private const double LowestResourceShare = 0.15;

    // CALOK-B, splints, Ibuprofen. A used one is mostly just a use or two down; now and then nearly finished
    // (an Ibuprofen with 1-4 of 8 left).
    public const int FewUsesMax = 10;
    private const double FewUsesUsedShare = 0.4;
    private const double FewUsesFurtherDown = 0.25;
    private const double FewUsesNearlyGone = 0.25;

    internal static int? RollFewUsesLeft(int max, ResourceListingSettings settings)
    {
        if (!settings.Enabled || max < 2)
        {
            return null;
        }

        if (Random.Shared.NextDouble() >= settings.UsedChance * FewUsesUsedShare)
        {
            return max;
        }

        if (Random.Shared.NextDouble() < FewUsesNearlyGone)
        {
            return Random.Shared.Next(1, Math.Max(1, max / 2) + 1);
        }

        var left = max - 1;
        while (left > 1 && Random.Shared.NextDouble() < FewUsesFurtherDown)
        {
            left--;
        }

        return left;
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
        ArmorOffers.RollCondition(itemWithMods as List<Item> ?? itemWithMods.ToList(), itemDetails);
    }
}

// Startup offers are made before some mods raise key max uses, so a 10 -> 20 key would only ever show
// 11-19 left. Re-roll those against the real max.
[Injectable(InjectionType.Singleton)]
public class StartupKeyOfferResync(
    ItemHelper itemHelper,
    PaymentHelper paymentHelper,
    RagfairOfferService ragfairOfferService)
{
    public void Apply()
    {
        var changedKeys = 0;
        var resynced = 0;

        foreach (var (tpl, generatedMax) in UsedListingsGate.KeyMaxUsesAtGeneration.ToList())
        {
            var max = itemHelper.GetItem(tpl).Value?.Properties?.MaximumNumberOfUsage ?? 0;
            if (max == generatedMax || max < 1)
            {
                continue;
            }

            changedKeys++;
            var settings = UsedListingsGate.KeySettingsFor(tpl);
            var ignoresCondition = UsedListingsGate.IgnoresCondition(tpl);

            foreach (var offer in ragfairOfferService.GetOffersOfType(tpl) ?? [])
            {
                if (offer.CreatedBy != OfferCreator.FakePlayer || offer.SellInOnePiece == true)
                {
                    continue;
                }

                var key = offer.Items?.FirstOrDefault()?.Upd?.Key;
                if (key is null)
                {
                    continue;
                }

                var usedBefore = Math.Clamp(key.NumberOfUsages ?? 0, 0, generatedMax);
                var requirements = offer.Requirements?.ToList();
                var isCurrencyOffer = requirements is { Count: 1 } && paymentHelper.IsMoneyTpl(requirements[0].TemplateId);

                if (!isCurrencyOffer)
                {
                    // Barters are a fixed count, so keep the same share of uses spent.
                    key.NumberOfUsages = Math.Clamp((int)Math.Round((double)usedBefore * max / generatedMax), 0, Math.Max(0, max - 1));
                    resynced++;
                    continue;
                }

                var left = UsedListingsGate.RollKeyUsesLeft(max, settings) ?? max;
                key.NumberOfUsages = max - left;

                if (!ignoresCondition)
                {
                    var before = UsedListingsGate.KeyPriceCurve(generatedMax - usedBefore, generatedMax, settings);
                    var after = UsedListingsGate.KeyPriceCurve(left, max, settings);
                    var ratio = after / before;

                    // Not below what a trader pays.
                    var current = requirements![0].Count ?? 0;
                    var floor = PriceGate.FloorFor(tpl, offer.Items![0], requirements[0].TemplateId);
                    if (current > 0 && current * ratio < floor)
                    {
                        ratio = floor / current;
                    }

                    RescalePrice(offer, requirements, ratio);
                }

                resynced++;
            }
        }

        if (changedKeys > 0)
        {
            DebugLog.Summary(() => $"{changedKeys} key(s) changed max uses after flea generation, re-rolled {resynced} startup offer(s).");
        }
    }

    // Requirements is a lazy Select over the scheme, so replace it rather than edit it.
    private static void RescalePrice(RagfairOffer offer, List<OfferRequirement> requirements, double ratio)
    {
        var money = requirements[0];
        requirements[0] = money with { Count = Math.Round((money.Count ?? 0) * ratio, 2) };
        offer.Requirements = requirements;
        offer.RequirementsCost = Math.Round((offer.RequirementsCost ?? 0) * ratio);
        offer.SummaryCost = Math.Round((offer.SummaryCost ?? 0) * ratio);
    }
}
