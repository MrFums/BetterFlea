using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Prices;

// Reports items a trader sells for less than some trader buys them back for. Usually caused by trader
// mods or price edits. Only reported, never changed: other mods' trader prices aren't ours to touch.
[Injectable(InjectionType.Singleton)]
public class TraderProfitAudit(
    ISptLogger<TraderProfitAudit> logger,
    ConfigManager configManager,
    TradersTable tradersTable,
    ItemHelper itemHelper,
    HandbookHelper handbookHelper,
    PaymentHelper paymentHelper)
{
    private const int ShownInLog = 15;

    public void Run()
    {
        if (!configManager.Mod.Prices.Floor.LogTraderProfitLoops)
        {
            return;
        }

        var loops = new List<(string Line, double Profit)>();
        foreach (var trader in tradersTable.Values)
        {
            var assort = trader.Assort;
            if (assort?.Items is null || assort.BarterScheme is null)
            {
                continue;
            }

            var roots = assort.Items
                .Where(item => string.Equals(item.ParentId, "hideout", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.Id);

            foreach (var (assortId, schemes) in assort.BarterScheme)
            {
                // Money-only deals; barters have no clean rouble cost to compare.
                if (!roots.TryGetValue(assortId, out var root)
                    || schemes.FirstOrDefault() is not [{ } cost]
                    || !paymentHelper.IsMoneyTpl(cost.Template)
                    || cost.Count is not > 0)
                {
                    continue;
                }

                var price = cost.Template == Money.ROUBLES
                    ? cost.Count.Value
                    : handbookHelper.InRoubles(cost.Count.Value, cost.Template);
                var bestBuy = PriceGate.BestTraderBuyPrice(root.Template);
                if (bestBuy <= price)
                {
                    continue;
                }

                var name = itemHelper.GetItem(root.Template).Value?.Name ?? root.Template.ToString();
                loops.Add(($"{name} ({root.Template}): {trader.Base.Nickname ?? trader.Base.Name} sells for {price:0}, a trader pays up to {bestBuy:0} RUB", bestBuy - price));
            }
        }

        if (loops.Count == 0)
        {
            DebugLog.Summary(() => "Trader check: no items a trader sells below another trader's buy price.");
            return;
        }

        logger.Warning($"[BetterFlea] Trader check: {loops.Count} item(s) can be bought from a trader and sold to a trader for profit. Biggest:");
        foreach (var (line, _) in loops.OrderByDescending(loop => loop.Profit).Take(ShownInLog))
        {
            logger.Warning("[BetterFlea]   " + line);
        }
    }
}
