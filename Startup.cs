using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Services.Ragfair;
using BetterFlea.Flea;
using BetterFlea.Offers;
using BetterFlea.Prices;
using BetterFlea.Selling;

namespace BetterFlea;

// Before RagfairCallbacks or the startup offers are made before the blacklist exists.
[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public class BetterFleaLoad(
    ConfigManager configManager,
    FleaCategoryFilter categoryFilter,
    MarkedKeyPricing markedKeyPricing,
    ModSupport modSupport,
    GlobalTable globalTable) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        modSupport.Check();
        categoryFilter.Apply();
        markedKeyPricing.Apply();

        // Only ever switched on, so another mod wanting FIR-only isn't overridden.
        if (configManager.Mod.PlayerSelling.FoundInRaidOnly)
        {
            globalTable.Configuration.RagFair.IsOnlyFoundInRaidAllowed = true;
        }

        return Task.CompletedTask;
    }
}

// Last, so mods that change key uses or trader prices late are done.
[Injectable(TypePriority = int.MaxValue)]
public class BetterFleaLateLoad(
    ISptLogger<BetterFleaLateLoad> logger,
    ConfigManager configManager,
    StartupKeyOfferResync startupKeyOfferResync,
    TraderProfitAudit traderProfitAudit,
    RagfairOfferService ragfairOfferService,
    SaveServer saveServer) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        var raisedAtStartup = PriceGate.RaisedToFloor;
        PriceGate.ForgetTraderPrices();
        GunPricing.ForgetTraderPrices();

        startupKeyOfferResync.Apply();
        traderProfitAudit.Run();
        PurchaseLock.CleanUp(saveServer);

        DebugLog.Summary(() => $"Flea has {ragfairOfferService.GetOffers().Count} offers (listings x{configManager.Mod.GeneratedOffers.Listings.Multiplier}).");
        if (configManager.Mod.Prices.Floor.Enabled)
        {
            DebugLog.Summary(() => $"Price floor raised {raisedAtStartup} startup offer price(s) to the best trader buy price + {configManager.Mod.Prices.Floor.MarginPercent}%.");
        }

        // Building the flea leaves a lot of garbage. Hand it back now.
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        logger.Success("[BetterFlea] Loaded.");
        DebugLog.Bind(configManager, message => logger.Info(message));
        if (configManager.Mod.DebugLogging)
        {
            logger.Warning("[BetterFlea] debugLogging is on: every price floor, drift and flip penalty decision will be logged.");
        }

        return Task.CompletedTask;
    }
}

// Items are handled by type, stats and price, so modded items just work. The few things the game only tells
// apart by name are found here and logged.
[Injectable(InjectionType.Singleton)]
public class ModSupport(
    ISptLogger<ModSupport> logger,
    IReadOnlyList<SptMod> loadedMods,
    TemplateTable templateTable,
    ItemHelper itemHelper,
    LocaleService localeService)
{
    private static readonly MongoId OpticBase = new("5448fe7a4bdc2d6f028b456b");
    private static readonly MongoId GunBase = new("5422acb9af1c889c16000029");

    public void Check()
    {
        var english = SafeEnglish();
        var items = templateTable.Items.Values.Where(item => item.Type == "Item").ToList();

        var markedKeys = items
            .Where(item => itemHelper.IsOfBaseclasses(item.Id, [FleaCategories.Key, FleaCategories.Keycard])
                && english.TryGetValue($"{item.Id} Name", out var name) && name.Contains("marked key", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Id);
        var newMarkedKeys = FleaCategories.AddMarkedKeys(markedKeys);

        var optics = items.Where(item => itemHelper.IsOfBaseclass(item.Id, OpticBase)
            && itemHelper.IsOfBaseclasses(item.Id, [new("55818ad54bdc2ddc698b4569"), new("55818acf4bdc2dde698b456b"),
                new("55818ae44bdc2dde698b456c"), new("55818aeb4bdc2ddc698b456a"), new("55818add4bdc2d5b648b456f")])).ToList();
        var byStats = optics.Where(item => !BuildQuality.IsKnownOptic(item.Id)).ToList();
        var newCalibres = items.Where(item => itemHelper.IsOfBaseclass(item.Id, GunBase))
            .Select(item => item.Properties?.AmmoCaliber ?? "")
            .Where(calibre => BuildQuality.IsBigBore(calibre) && !BuildQuality.IsListedBigBore(calibre))
            .Distinct().ToList();

        var others = loadedMods
            .Where(mod => !string.Equals(mod.ModMetadata?.ModGuid, "com.fums.betterflea", StringComparison.OrdinalIgnoreCase))
            .Select(mod => $"{mod.ModMetadata?.Name} {mod.ModMetadata?.Version}")
            .ToList();

        logger.Info($"[BetterFlea] Other server mods: {(others.Count == 0 ? "none" : string.Join(", ", others))}. "
            + $"{items.Count} items. Optics rated by type and zoom: {byStats.Count} of {optics.Count}. "
            + $"Extra full-power calibres: {(newCalibres.Count == 0 ? "none" : string.Join(", ", newCalibres.Select(calibre => calibre.Replace("Caliber", ""))))}. "
            + $"Extra marked keys: {newMarkedKeys}.");

        DebugLog.Summary(() => "Optics rated by type and zoom: " + string.Join(", ", byStats.Select(item =>
        {
            var (rating, kind, budget) = BuildQuality.OpticRating(item.Id);
            var name = english.TryGetValue($"{item.Id} ShortName", out var shortName) ? shortName : item.Name;
            return $"{name} ({kind} {rating:0.##}{(budget ? ", budget" : "")})";
        })));
    }

    private Dictionary<string, string> SafeEnglish()
    {
        try
        {
            return localeService.GetLocaleDb("en") ?? new Dictionary<string, string>();
        }
        catch (Exception)
        {
            return new Dictionary<string, string>();
        }
    }
}
