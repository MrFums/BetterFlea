using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;
using BetterFlea.Flea;
using BetterFlea.Prices;

namespace BetterFlea;

// Before RagfairCallbacks, otherwise the startup offers are generated before the blacklist exists.
[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public class BetterFleaLoad(
    ConfigManager configManager,
    FleaCategoryFilter categoryFilter,
    MarkedKeyPricing markedKeyPricing,
    GlobalTable globalTable) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        categoryFilter.Apply();
        markedKeyPricing.Apply();

        // Only ever switched on, so another mod that wants FIR-only selling isn't overridden.
        if (configManager.Mod.PlayerSelling.FoundInRaidOnly)
        {
            globalTable.Configuration.RagFair.IsOnlyFoundInRaidAllowed = true;
        }

        return Task.CompletedTask;
    }
}
