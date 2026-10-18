using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using BetterFlea.Flea;

namespace BetterFlea;

[Injectable(InjectionType.Singleton)]
public class ConfigManager(
    ISptLogger<ConfigManager> logger,
    ModHelper modHelper)
{
    // .jsonc so editors know the // notes in it are allowed.
    private const string ConfigFile = "config.jsonc";

    // The shipped config.jsonc, notes and all, built into the DLL (see BetterFlea.csproj).
    private const string DefaultConfigResource = "BetterFlea.config.jsonc";

    public ModConfig Mod { get; private set; } = new();

    public string ModPath { get; private set; } = string.Empty;

    public void Load()
    {
        ModPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        RestoreConfigIfMissing();
        Mod = LoadOrDefault<ModConfig>(ConfigFile);

        Validate();
        DebugLog.BindSummaries(this, message => logger.Info(message));
    }

    private void Validate()
    {
        WarnUnknown(Mod.Unknown, "");
        WarnUnknown(Mod.Flea.Unknown, "flea.");
        WarnUnknown(Mod.GeneratedOffers.Unknown, "generatedOffers.");
        WarnUnknown(Mod.Prices.Unknown, "prices.");
        WarnUnknown(Mod.PlayerSelling.Unknown, "playerSelling.");

        if (Mod.Prices.MarkedKeyMultiplier <= 0)
        {
            logger.Warning("[BetterFlea] prices.markedKeyMultiplier must be > 0, resetting to 1.0");
            Mod.Prices.MarkedKeyMultiplier = 1.0;
        }

        var listings = Mod.GeneratedOffers.Listings;
        listings.Multiplier = ClampListingMultiplier(listings.Multiplier, "multiplier");
        listings.CategoryMultipliers = new Dictionary<string, double>(listings.CategoryMultipliers, StringComparer.OrdinalIgnoreCase);
        foreach (var name in listings.CategoryMultipliers.Keys.ToList())
        {
            if (!FleaCategories.Names.Contains(name))
            {
                logger.Warning($"[BetterFlea] Unknown category '{name}' in generatedOffers.listings.categoryMultipliers, ignoring. Use names like 'keys.markedKeys'.");
                listings.CategoryMultipliers.Remove(name);
                continue;
            }

            listings.CategoryMultipliers[name] = ClampListingMultiplier(listings.CategoryMultipliers[name], name);
        }

        listings.RarityMultipliers = new Dictionary<string, double>(listings.RarityMultipliers, StringComparer.OrdinalIgnoreCase);

        var used = Mod.GeneratedOffers.UsedItems;
        foreach (var settings in new[] { used.Keys, used.MarkedKeys, used.Keycards })
        {
            settings.UsedChance = Math.Clamp(settings.UsedChance, 0.0, 1.0);
            settings.UsesLeftFalloff = Math.Clamp(settings.UsesLeftFalloff, 0.0, 1.0);
            settings.OneUsePricePercent = Math.Clamp(settings.OneUsePricePercent, 1.0, 100.0);
        }

        foreach (var settings in new[] { used.Meds, used.FoodAndDrink, used.Fuel, used.RepairKits })
        {
            settings.UsedChance = Math.Clamp(settings.UsedChance, 0.0, 1.0);
            settings.LowAmountBias = Math.Clamp(settings.LowAmountBias, 0.1, 10.0);
        }

        var delay = Mod.PlayerSelling.SaleDelay;
        delay.MinMinutes = Math.Max(0.0, delay.MinMinutes);
        delay.MaxMinutes = Math.Max(0.0, delay.MaxMinutes);
        delay.NoDelayChance = Math.Clamp(delay.NoDelayChance, 0.0, 1.0);
        if (delay.MinMinutes > delay.MaxMinutes)
        {
            logger.Warning("[BetterFlea] playerSelling.saleDelay.minMinutes is above maxMinutes, swapping them.");
            (delay.MinMinutes, delay.MaxMinutes) = (delay.MaxMinutes, delay.MinMinutes);
        }

        var perOffer = Mod.GeneratedOffers.ItemsPerOffer;
        foreach (var profile in new[] { perOffer.Bulk, perOffer.Stock, perOffer.Few })
        {
            profile.Falloff = Math.Clamp(profile.Falloff, 0.0, 1.0);
            profile.Max = Math.Clamp(profile.Max, 1, 100);
        }

        foreach (var rarity in listings.RarityMultipliers.Keys.ToList())
        {
            listings.RarityMultipliers[rarity] = ClampListingMultiplier(listings.RarityMultipliers[rarity], rarity);
        }

        Mod.Prices.Floor.MarginPercent = Math.Clamp(Mod.Prices.Floor.MarginPercent, 0.0, 500.0);

        var drift = Mod.Prices.Drift;
        drift.MaxDriftPercent = Math.Clamp(drift.MaxDriftPercent, 0.0, 90.0);
        drift.StepPercent = Math.Clamp(drift.StepPercent, 0.0, drift.MaxDriftPercent);
        drift.UpdateMinutes = Math.Max(1.0, drift.UpdateMinutes);

        var flip = Mod.PlayerSelling.FlipPenalty;
        flip.WindowMinutes = Math.Max(1.0, flip.WindowMinutes);
        flip.FreePurchases = Math.Max(0.0, flip.FreePurchases);
        flip.MaxExtraMinutes = Math.Max(0.0, flip.MaxExtraMinutes);
        flip.FenceMultiplier = Math.Max(0.0, flip.FenceMultiplier);        if (flip.PurchasesForMaxPenalty <= flip.FreePurchases)
        {
            logger.Warning("[BetterFlea] playerSelling.flipPenalty.purchasesForMaxPenalty must be above freePurchases, using freePurchases + 1.");
            flip.PurchasesForMaxPenalty = flip.FreePurchases + 1;
        }
    }

    // A misspelt or moved setting would otherwise be ignored without a word and quietly use its default.
    private void WarnUnknown(Dictionary<string, System.Text.Json.JsonElement>? unknown, string section)
    {
        if (unknown is not { Count: > 0 })
        {
            return;
        }

        var names = string.Join(", ", unknown.Keys.Select(key => section + key));
        logger.Warning($"[BetterFlea] Unknown setting(s) in {ConfigFile}, ignored: {names}. Check the spelling, or compare with the {ConfigFile} that ships with the mod.");
    }

    // 0 = no generated listings (trader offers stay). Capped so a typo can't eat the server's RAM.
    private double ClampListingMultiplier(double value, string name)
    {
        const double max = 10.0;
        if (value < 0 || value > max)
        {
            logger.Warning($"[BetterFlea] Listing multiplier '{name}' must be between 0 and {max}, clamping.");
        }

        return Math.Clamp(value, 0.0, max);
    }

    // A deleted config.jsonc is written back from the copy in the DLL, so "delete it to reset" works
    // and the user always has a file to edit. An existing file is never touched.
    private void RestoreConfigIfMissing()
    {
        var path = Path.Combine(ModPath, ConfigFile);
        if (File.Exists(path))
        {
            return;
        }

        try
        {
            using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultConfigResource)
                ?? throw new InvalidOperationException("the default config is missing from the DLL");
            using var target = File.Create(path);
            source.CopyTo(target);
            logger.Info($"[BetterFlea] {ConfigFile} was missing, so a new one was made with the default settings.");
        }
        catch (Exception ex)
        {
            logger.Warning($"[BetterFlea] {ConfigFile} is missing and a new one could not be made, using defaults. ({ex.Message})");
        }
    }

    private T LoadOrDefault<T>(string relativeFile) where T : new()
    {
        try
        {
            return modHelper.GetJsonDataFromFile<T>(ModPath, relativeFile) ?? new T();
        }
        catch (Exception ex)
        {
            logger.Warning($"[BetterFlea] Could not read '{relativeFile}', using defaults. ({ex.Message})");
            return new T();
        }
    }
}
