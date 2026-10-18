using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Config;
using BetterFlea.Flea;

namespace BetterFlea.Prices;

// SPT's per-item multiplier only feeds generated flea offers, so trader/handbook prices stay vanilla.
[Injectable(InjectionType.Singleton)]
public class MarkedKeyPricing(
    ConfigManager configManager,
    RagfairConfig ragfairConfig)
{
    public void Apply()
    {
        var multiplier = configManager.Mod.Prices.MarkedKeyMultiplier;
        if (multiplier == 1.0)
        {
            return;
        }

        var priceMultipliers = ragfairConfig.Dynamic.ItemPriceMultiplier ??= new();
        foreach (var tpl in FleaCategories.MarkedKeys)
        {
            priceMultipliers[tpl] = priceMultipliers.GetValueOrDefault(tpl, 1.0) * multiplier;
        }

        DebugLog.Summary(() => $"Marked key flea prices x{multiplier}.");
    }
}
