using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services.Ragfair;
using BetterFlea.Prices;

namespace BetterFlea.Offers;

// Startup flea offers are generated before some mods change key max uses (after PostLoad),
// so a 10 -> 20 use key would only ever show 11-19 uses left. Re-roll those offers against the real max.
// Offers SPT generates later (expired replacements) already see the final max and are left alone.
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
                    // Barter price is a fixed item count; keep the same share of uses spent instead of re-rolling.
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

                    // The re-roll can't take the key below what a trader would pay for it.
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

    // Requirements is a lazy Select over the barter scheme, so it has to be replaced, not edited in place.
    private static void RescalePrice(RagfairOffer offer, List<OfferRequirement> requirements, double ratio)
    {
        var money = requirements[0];
        requirements[0] = money with { Count = Math.Round((money.Count ?? 0) * ratio, 2) };
        offer.Requirements = requirements;
        offer.RequirementsCost = Math.Round((offer.RequirementsCost ?? 0) * ratio);
        offer.SummaryCost = Math.Round((offer.SummaryCost ?? 0) * ratio);
    }
}
