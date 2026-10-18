using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Ragfair;
using BetterFlea.Offers;
using BetterFlea.Prices;
using BetterFlea.Selling;

namespace BetterFlea;

// Last thing to load, so every mod that edits key max uses (some run after PostLoad)
// or trader prices is done.
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

        startupKeyOfferResync.Apply();
        traderProfitAudit.Run();
        PurchaseLock.CleanUp(saveServer);

        // Handy for judging what listings.multiplier costs.
        DebugLog.Summary(() => $"Flea has {ragfairOfferService.GetOffers().Count} offers (listings x{configManager.Mod.GeneratedOffers.Listings.Multiplier}).");
        if (configManager.Mod.Prices.Floor.Enabled)
        {
            DebugLog.Summary(() => $"Price floor raised {raisedAtStartup} startup offer price(s) to the best trader buy price + {configManager.Mod.Prices.Floor.MarginPercent}%.");
        }

        logger.Success("[BetterFlea] Loaded.");
        DebugLog.Bind(configManager, message => logger.Info(message));
        if (configManager.Mod.DebugLogging)
        {
            logger.Warning("[BetterFlea] debugLogging is on: every price floor, drift and flip penalty decision will be logged.");
        }

        return Task.CompletedTask;
    }
}
