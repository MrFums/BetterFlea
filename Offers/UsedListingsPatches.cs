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
        new ArmorQualityPatch().Enable();
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
            if (RollKeyUsesLeft(max, KeySettingsFor(itemDetails.Id), props.RarityPvE) is { } left)
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
            // Nobody bothers listing a half-used AI-2 or Car: the cheaper the med, the rarer a used one.
            var meds = used.Meds with { UsedChance = used.Meds.UsedChance * Math.Clamp(BuildQuality.HandbookPrice(itemDetails.Id) / CheapMedPrice, 0.2, 1) };
            var left = max <= FewUsesMax && !bigSurgical ? RollFewUsesLeft(max, meds)
                : RollResourceLeft(max, bigSurgical ? meds with { LowAmountBias = 1.0 } : meds);
            if (left is { } hp)
            {
                upd.MedKit.HpResource = hp;
            }
        }
        else if (upd.FoodDrink is not null)
        {
            var max = props.MaxResource ?? 0;
            // Sugar is the exception: people eat some and sell the rest on.
            var food = itemDetails.Id == Sugar ? used.FoodAndDrink with { UsedChance = Math.Min(1, used.FoodAndDrink.UsedChance * SugarUsedFactor) } : used.FoodAndDrink;
            if (RollResourceLeft(max, food) is { } left)
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

            // A gun someone put a build on has been used, so it's never as new: a found or near-stock one with a build
            // (an SPT named build, say) is worn like any other. Its wear factor only keeps the wear light.
            var built = buildKind is "meta" or "player" or "modded" or "budget"
                || (buildKind is not "traderflip" && PresetOfferGate.HasBuild(itemWithMods));
            if (built && buildKind is null or "ground" or "nearstock")
            {
                buildKind = "player";
            }

            if (buildKind == "traderflip")
            {
                repairable.MaxDurability = repairable.Durability = props.MaxDurability ?? repairable.MaxDurability ?? 100.0;
                return;
            }

            if (buildKind == "ground")
            {
                var full = props.MaxDurability ?? repairable.MaxDurability ?? 100.0;
                repairable.MaxDurability = full;
                repairable.Durability = Random.Shared.NextDouble() < 0.7 ? full : full - Random.Shared.Next(1, 4);
                return;
            }

            // A near-stock gun is one someone got from a drop, a reward or a raid and is selling on: often as new, or
            // used a little (and maybe repaired) like any other, only lighter.
            var lightlyUsed = false;
            if (buildKind == "nearstock")
            {
                if (Random.Shared.NextDouble() < NearStockNewShare)
                {
                    repairable.MaxDurability = repairable.Durability = props.MaxDurability ?? repairable.MaxDurability ?? 100.0;
                    return;
                }

                lightlyUsed = true;
            }

            var wearFactor = lightlyUsed ? 0.6
                : buildKind is not null && gun.WearByBuild.TryGetValue(buildKind, out var byBuild) ? Math.Max(0, byBuild) : 1;
            if (PresetOfferGate.IsBossGun(itemDetails.Id))
            {
                wearFactor *= Math.Clamp(gun.BossGunWear, 0, 1);
            }

            // Factor 2 turns 50% into 75%, 0.4 turns it into 24%.
            double Scaled(double chance) => 1 - Math.Pow(1 - Math.Clamp(chance, 0, 1), wearFactor);

            // Only a gun that's been used has been repaired. Live, most used guns sit at 82-98 max (an SR-25 list had
            // 12 of 13 lowered); guns nobody uses are 100/100.
            var wornChance = gun.WornChanceByRarity.TryGetValue(props.RarityPvE ?? "", out var byRarity) ? byRarity : 0.5;
            var worn = built || lightlyUsed || Random.Shared.NextDouble() < Scaled(wornChance);
            var max = repairable.MaxDurability ?? 100.0;
            // A light wear factor doesn't spare the max: a used gun has been repaired like any other.
            var lowMaxChance = built || lightlyUsed ? Math.Max(gun.LowMaxChance, Scaled(gun.LowMaxChance)) : Scaled(gun.LowMaxChance);
            if (worn && Random.Shared.NextDouble() < lowMaxChance)
            {
                // Mostly near the top (99, 98, 97...), down to lowMaxMin; 1 in 12 was kept going a while past it.
                var top = Math.Max(gun.LowMaxMin, gun.LowMaxMax);
                var low = Random.Shared.NextDouble() < 1.0 / 12
                    ? gun.LowMaxMin - Random.Shared.Next(1, 9)
                    : top - Math.Floor(Math.Pow(Random.Shared.NextDouble(), 1.5) * (top - gun.LowMaxMin + 1));
                low = Math.Max(MinGunDurability, Math.Min(low, top));
                if (low < max)
                {
                    max = low;
                }
            }

            repairable.MaxDurability = max;
            var mostWear = Math.Min(gun.MaxWear * wearFactor, MostGunWear);
            // A lowered max means it was repaired, and often sold straight after: full to its new max (97/97).
            var repaired = max < (props.MaxDurability ?? 100.0) && Random.Shared.NextDouble() < RepairedShare;
            var wear = worn && !repaired
                ? Math.Round(1 + Math.Pow(Random.Shared.NextDouble(), 1.6) * Math.Max(0, mostWear - 1))
                : 0;
            repairable.Durability = Math.Max(MinGunDurability, max - wear);
            if (max < (props.MaxDurability ?? 100.0) && Random.Shared.NextDouble() < gun.RepairBuffChance)
            {
                AddRepairBuff(upd, repairable.Durability ?? max, weapon: true);
            }
        }
    }

    // A repair kit's bonus, the way SPT rolls it (configs/repair.json): mostly common, 1 in 6 rare. It works while the
    // item is above a share of the durability it was repaired to.
    internal static void AddRepairBuff(Upd upd, double durability, bool weapon)
    {
        var rare = Random.Shared.NextDouble() < 1.0 / 6;
        double Between(double low, double high) => low + Random.Shared.NextDouble() * (high - low);
        var (type, value) = !weapon ? ("DamageReduction", rare ? Between(0.8, 0.9) : Between(0.9, 0.98))
            : Random.Shared.NextDouble() < 0.5 ? ("WeaponSpread", rare ? Between(0.8, 0.9) : Between(0.9, 0.99))
            : ("MalfunctionProtections", rare ? Between(0.75, 0.9) : Between(0.94, 0.96));
        if (!Enum.TryParse<RepairBuffType>(type, out var buffType))
        {
            return;
        }

        upd.Buff = new UpdBuff
        {
            Rarity = rare ? "Rare" : "Common",
            BuffType = buffType,
            Value = Math.Round(value, 4),
            ThresholdDurability = Math.Round(durability * Random.Shared.Next(75, 91) / 100.0, 2)
        };
    }

    private const double MostGunWear = 60;
    private const double MinGunDurability = 10;

    // Share of guns with a lowered max listed straight after a repair, at their new max.
    private const double RepairedShare = 0.45;

    // Share of near-stock guns listed as new (100/100).
    private const double NearStockNewShare = 0.4;

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

    // SPT scales the price by the amount left. Live sellers price a used one at about the same whatever is left: a
    // YMXC water at 18/60 and at 55/60 both sit about a third under a full one, a part-full fuel can near a full one.
    // Empty fuel is priced on its own (EmptyFuelPricing).
    public static double ResourcePriceFactor(MongoId tpl, Item? item)
    {
        if (item?.Upd is not { } upd || _itemHelper is null || _config is null || IgnoresCondition(tpl)
            || _itemHelper.GetItem(tpl).Value?.Properties is not { } props)
        {
            return 1.0;
        }

        var used = _config.Mod.GeneratedOffers.UsedItems;
        (double Left, double Max, ResourceListingSettings Settings)? resource =
            upd.MedKit is { HpResource: { } hp } ? (hp, props.MaxHpResource ?? 0, used.Meds)
            : upd.FoodDrink is { HpPercent: { } food } ? (food, props.MaxResource ?? 0, used.FoodAndDrink)
            : upd.RepairKit is { Resource: { } kit } ? (kit, props.MaxRepairResource ?? 0, used.RepairKits)
            : upd.Resource is { Value: { } fuel } && _itemHelper.IsOfBaseclass(tpl, FleaCategories.Fuel) ? (fuel, props.MaxResource ?? 0, used.Fuel)
            : null;
        if (resource is not { } found || !found.Settings.Enabled || found.Max <= 0 || found.Left <= 0 || found.Left >= found.Max)
        {
            return 1.0;
        }

        var vanilla = _itemHelper.GetItemQualityModifier(item);
        if (vanilla <= 0)
        {
            return 1.0;
        }

        return ResourcePriceCurve(found.Left / found.Max, found.Settings) / vanilla;
    }

    // Share of a full one's price at this share left.
    internal static double ResourcePriceCurve(double share, ResourceListingSettings settings)
    {
        var weight = Math.Clamp(settings.AmountPriceWeight, 0, 1);
        return Math.Clamp(settings.UsedPricePercent / 100.0, 0.01, 1.0) * (1 - weight + weight * Math.Clamp(share, 0, 1));
    }

    // How much of a full, new one's price this item's condition leaves, the way generated offers are priced: key
    // uses on the key curve, meds, food and fuel on the used price curve. 1 when its condition doesn't count.
    public static double PriceQuality(Item item)
    {
        if (_itemHelper is null || IgnoresCondition(item.Template))
        {
            return 1.0;
        }

        var quality = _itemHelper.GetItemQualityModifier(item) * KeyPriceFactor(item.Template, item) * ResourcePriceFactor(item.Template, item);
        return Math.Clamp(quality, 0.01, 1);
    }

    // How worn something with durability is as a whole (a helmet with its inserts, armour with its plates): its parts'
    // average condition by what each is worth, pulled down when one part is far worse than the rest, so a good carrier
    // with one wrecked plate is worse than the average says but not wrecked. 1 with nothing worn.
    public static double WearQuality(IEnumerable<Item> items)
    {
        double weight = 0, total = 0, worst = 1;
        foreach (var item in items)
        {
            if (item.Upd?.Repairable is not { MaxDurability: > 0 })
            {
                continue;
            }

            var quality = PriceQuality(item);
            var worth = Math.Max(1, BetterFlea.Prices.BookPrice.Of(item.Template));
            total += quality * worth;
            weight += worth;
            worst = Math.Min(worst, quality);
        }

        if (weight <= 0)
        {
            return 1.0;
        }

        // A part more than 20 points under the average takes half of the rest off: average 80% with one part at 10%
        // is 55%, and 60% with one at 5% is 42%.
        var average = total / weight;
        return Math.Clamp(average - 0.5 * Math.Max(0, average - worst - 0.2), 0.01, 1);
    }

    // Most helmets and body armour keep their durability in built-in inserts and none in the shell (a 6/14 helmet is
    // its inserts added up), and SPT prices an offer by the shell's condition, skipping the inserts: a 6/14 helmet
    // cost what a new one does. A shell without durability of its own goes by its inserts together, on the same curve
    // as other armour. 1 for anything else, or without the item's parts.
    public static double ShellQuality(Item? shell, IEnumerable<Item>? parts)
    {
        if (shell is null || parts is null || _itemHelper is null || IgnoresCondition(shell.Template)
            || _itemHelper.GetItem(shell.Template).Value?.Properties is not { } props || props.MaxDurability > 0)
        {
            return 1.0;
        }

        double full = 0, max = 0, current = 0;
        foreach (var part in parts)
        {
            if (part.ParentId != shell.Id.ToString() || part.Upd?.Repairable is not { Durability: { } left, MaxDurability: { } most }
                || !_itemHelper.IsOfBaseclass(part.Template, BaseClasses.BUILT_IN_INSERTS)
                || _itemHelper.GetItem(part.Template).Value?.Properties?.MaxDurability is not { } templateMax || templateMax <= 0)
            {
                continue;
            }

            full += templateMax;
            max += most;
            current += left;
        }

        return full > 0 && max > 0 ? ArmorQualityPatch.Quality(full, max, current) : 1.0;
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
    internal static int? RollKeyUsesLeft(int max, KeyListingSettings settings, string? rarity = null)
    {
        if (!settings.Enabled || max < 2)
        {
            return null;
        }

        var byRarity = rarity is not null && settings.UsedChanceByRarity.TryGetValue(rarity, out var factor) ? Math.Max(0, factor) : 1;
        if (Random.Shared.NextDouble() >= Math.Min(1, settings.UsedChance * byRarity))
        {
            return max;
        }

        if (Random.Shared.NextDouble() < settings.OneDownChance)
        {
            return max - 1;
        }

        // The falloff is per use on a 10-use key and stretched over longer ones, or a 40-use key compounds it 39
        // times and never shows below 30.
        var step = Math.Pow(settings.UsesLeftFalloff, Math.Min(1, 10.0 / max));
        var total = 0.0;
        var weight = 1.0;
        for (var left = 1; left < max; left++)
        {
            total += weight;
            weight *= step;
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

            weight *= step;
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

    private static readonly MongoId Sugar = new("59e3577886f774176a362503");
    private const double SugarUsedFactor = 2;

    // Meds under this handbook price are less often listed used, down to a fifth as often.
    private const double CheapMedPrice = 30_000;

    // CALOK-B, splints, Ibuprofen. A used one is mostly just a use or two down; now and then nearly finished
    // (an Ibuprofen with 1-4 of 8 left, mostly 1 or 2: the last pills of a box get sold off).
    public const int FewUsesMax = 10;
    private const double FewUsesUsedShare = 0.75;
    private const double FewUsesFurtherDown = 0.25;
    private const double FewUsesNearlyGone = 0.35;

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
            return 1 + (int)Math.Floor(Math.Pow(Random.Shared.NextDouble(), 1.8) * Math.Max(1, max / 2));
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

// What condition leaves of an armour piece's price (plates, body armour, helmets and their parts; not guns, which
// GunPricing handles). SPT takes the root of current over full durability, so a ruined 8/14 plate cost the same as an
// 8/35 one, and a 5/33 one bought and repaired made about 9k. The max is what an armour piece can ever be repaired
// to, so it counts most; the current only for the repair still to pay (a little under half the price at nothing
// left). 33/40 is about 80%, 5/33 about half, 8/14 under a quarter. Flea, listings and Fence use it; traders don't.
public class ArmorQualityPatch : AbstractPatch
{
    private const double MaxWeight = 1.2;
    private const double CurrentShare = 0.45;

    protected override MethodBase GetTargetMethod()
    {
        return typeof(ItemHelper).GetMethod("GetRepairableItemQualityValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPostfix]
    public static void Postfix(TemplateItem itemDetails, UpdRepairable repairable, ref double __result)
    {
        var full = itemDetails.Properties?.MaxDurability ?? 0;
        if (full <= 0 || BookPrice.Helper?.IsOfBaseclass(itemDetails.Id, BaseClasses.WEAPON) != false
            || repairable.Durability is not { } current || repairable.MaxDurability is not { } max || max <= 0)
        {
            return;
        }

        __result = Quality(full, max, current);
    }

    // Share of a new one's price at this max and current, out of full (the template's max).
    internal static double Quality(double full, double max, double current)
    {
        var kept = Math.Pow(Math.Clamp(max / full, 0, 1), MaxWeight);
        return Math.Clamp(kept * (1 - CurrentShare + CurrentShare * Math.Clamp(current / max, 0, 1)), 0.01, 1);
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

                var left = UsedListingsGate.RollKeyUsesLeft(max, settings, itemHelper.GetItem(tpl).Value?.Properties?.RarityPvE) ?? max;
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
