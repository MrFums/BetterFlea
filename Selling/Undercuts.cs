using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Ragfair;
using SPTarkov.Server.Core.Services.Ragfair;
using SPTarkov.Server.Core.Utils;
using BetterFlea.Offers;
using BetterFlea.Prices;

namespace BetterFlea.Selling;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class UndercutsLoad(
    ConfigManager configManager,
    ItemHelper itemHelper,
    RagfairOfferGenerator ragfairOfferGenerator,
    RagfairOfferService ragfairOfferService,
    RagfairOfferHolder ragfairOfferHolder,
    JsonUtil jsonUtil) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        Undercuts.Bind(configManager, itemHelper, ragfairOfferGenerator, ragfairOfferService, ragfairOfferHolder, jsonUtil);
        return Task.CompletedTask;
    }
}

// Other sellers watch the market. A player's offer that's the cheapest one up, but not by much, now and then gets
// another seller listing just under it a few minutes later, who then takes most of its buyers. Priced well under
// normal, it sells before anyone bothers.
internal static class Undercuts
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static RagfairOfferGenerator? _generator;
    private static RagfairOfferService? _offers;
    private static RagfairOfferHolder? _holder;
    private static JsonUtil? _json;
    private static int _made;

    public static int Made => _made;

    // Making an undercut offer: its price is set, the flea's own pricing leaves it alone.
    [ThreadStatic] public static bool Creating;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, RagfairOfferGenerator generator, RagfairOfferService offers, RagfairOfferHolder holder, JsonUtil json)
    {
        _holder = holder;
        _config = configManager;
        _itemHelper = itemHelper;
        _generator = generator;
        _offers = offers;
        _json = json;
    }

    private static UndercutConfig? Settings => _config?.Mod.PlayerSelling.Undercut is { Enabled: true } settings ? settings : null;

    // The cheapest other seller's single offer of the item, per item and scaled to the given condition. Null when
    // there's none (or it's only barters and packs).
    public static double? Cheapest(MongoId tpl, double quality)
    {
        if (OnDemandOffers.Enabled)
        {
            OnDemandOffers.FillUp([tpl]);
        }

        var others = Others(tpl, quality);
        return others.Count > 0 ? others.Min(other => other.Each) : null;
    }

    // Other sellers' single money offers of the item (buyers skip packs and barters), each with its price per item
    // scaled to the given condition.
    private static List<(RagfairOffer Offer, double Each)> Others(MongoId tpl, double quality)
    {
        var others = new List<(RagfairOffer, double)>();
        if (_offers is null || _itemHelper is null)
        {
            return others;
        }

        foreach (var offer in _offers.GetOffersOfType(tpl) ?? [])
        {
            if (!offer.IsFakePlayerOffer() || offer.SellInOnePiece == true || offer.Items is not { Count: 1 } items
                || offer.RequirementsCost is not { } cost || cost <= 0
                || offer.Requirements?.All(requirement => _itemHelper.IsOfBaseclass(requirement.TemplateId, BaseClasses.MONEY)) != true)
            {
                continue;
            }

            var condition = UsedListingsGate.PriceQuality(items[0]);
            others.Add((offer, cost / condition * quality));
        }

        return others;
    }

    // ratio: the listing's price against normal. lowest: no other seller was cheaper when it went up.
    public static void AfterListing(MongoId sessionId, List<RagfairOffer>? profileOffers, ItemEventRouterResponse? output, double? ratio, bool lowest)
    {
        if (output is null || output.Warnings is { Count: > 0 }
            || output.ProfileChanges?.GetValueOrDefault(sessionId)?.RagFairOffers?.LastOrDefault() is not { } offer
            || offer.SellInOnePiece == true || offer.Items is not { Count: > 0 })
        {
            return;
        }

        if (OrderBook.On && OrderBook.Handles(offer))
        {
            OrderBook.Listed(offer, ratio, FlipPenaltyGate.ExtraWaitMinutes());
        }
        else
        {
            OrderBook.Rolled(offer, FlipPenaltyGate.ExtraWaitMinutes());
        }

        if (!lowest || Settings is not { } settings || ratio is not { } r || profileOffers is null)
        {
            return;
        }

        var expensive = (_config!.Mod.PlayerSelling.SaleDelay.ExpensivePercent) / 100;
        var safe = 1 - Math.Clamp(settings.SafeBelowPercent, 0, 90) / 100;
        var atNormal = Math.Clamp(settings.ChanceAtNormal, 0, 1);
        var whenDear = Math.Clamp(settings.ChanceWhenDear, 0, 1);
        var chance = r <= safe ? 0.01
            : r <= 1 ? 0.01 + (atNormal - 0.01) * (r - safe) / Math.Max(0.001, 1 - safe)
            : r <= 1 + expensive ? atNormal + (whenDear - atNormal) * (r - 1) / Math.Max(0.001, expensive)
            : whenDear;
        if (Random.Shared.NextDouble() >= chance)
        {
            return;
        }

        var low = Math.Max(0.1, settings.MinMinutes);
        var wait = TimeSpan.FromMinutes(low + Random.Shared.NextDouble() * Math.Max(0, settings.MaxMinutes - low));
        DebugLog.Write(() => $"Undercut: {Name(offer)} listed at x{r:0.00} of normal, someone lists under it in {wait.TotalMinutes:0.0} min");
        _ = Task.Delay(wait).ContinueWith(_ => Undercut(profileOffers, offer), TaskScheduler.Default);
    }

    private static void Undercut(List<RagfairOffer> profileOffers, RagfairOffer offer)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool listed;
            lock (profileOffers)
            {
                listed = profileOffers.Contains(offer);
            }

            if (!listed || offer.EndTime <= now || offer.Items is not { Count: > 0 } items || offer.RequirementsCost is not { } each
                || offer.Requirements?.All(requirement => _itemHelper!.IsOfBaseclass(requirement.TemplateId, BaseClasses.MONEY)) != true)
            {
                return;
            }

            var root = items[0];
            var quality = UsedListingsGate.PriceQuality(root);
            var target = Math.Min(each, items.Count == 1 ? Cheapest(root.Template, quality) ?? each : each);
            var price = Under(target);

            var copy = _json!.Deserialize<List<Item>>(_json.Serialize(items))!.ReplaceIDs().ToList();
            var copyRoot = copy[0];
            copyRoot.ParentId = "hideout";
            copyRoot.SlotId = "hideout";
            copyRoot.Location = null;
            var stack = Math.Max(1, (int)(root.Upd?.StackObjectsCount ?? 1));
            var quantity = stack > 1 ? Random.Shared.Next(1, stack + 1) : 1;
            if (copyRoot.Upd is { } upd)
            {
                upd.StackObjectsCount = quantity;
            }

            if (price < PriceGate.FloorForItems([copyRoot], Money.ROUBLES))
            {
                return;
            }

            Creating = true;
            ListingCountGate.NoCap = true;
            RagfairOffer made;
            try
            {
                made = _generator!.CreateAndAddFleaOffer(new CreateFleaOfferDetails
                {
                    UserId = new MongoId(),
                    Time = now,
                    Items = copy,
                    BarterScheme = [new BarterScheme { Count = price, Template = Money.ROUBLES }],
                    LoyalLevel = 1,
                    Quantity = quantity,
                    Creator = OfferCreator.FakePlayer,
                    SellInOnePiece = false
                });
            }
            finally
            {
                Creating = false;
                ListingCountGate.NoCap = false;
            }

            OnDemandOffers.Keep(made.Id);

            // The cheaper offer gets most of the buyers. A sale due now goes through.
            var keep = Math.Clamp(Settings?.BuyersLeft ?? 0.15, 0, 1);
            offer.SellResults = offer.SellResults?.Where(sale => sale.SellTime <= now + 5 || Random.Shared.NextDouble() < keep).ToList();
            var count = Interlocked.Increment(ref _made);
            DebugLog.Summary(() => $"Undercut: {made.User?.Nickname} listed {Name(offer)} at {price:0} RUB, under a player's {each:0} ({count} so far)");
        }
        catch (Exception exception)
        {
            DebugLog.Summary(() => $"Undercut failed: {exception.GetBaseException().Message}");
        }
    }

    // Mostly a rouble under; sometimes down to a round number, the way people price.
    private static double Under(double price)
    {
        var whole = Math.Floor(price);
        if (whole < 1000 || Random.Shared.NextDouble() < 0.65)
        {
            return Math.Max(1, whole - 1);
        }

        var unit = Math.Pow(10, Math.Floor(Math.Log10(whole)) - 2);
        return Math.Max(1, Math.Floor((whole - 1) / unit) * unit);
    }

    private static string Name(RagfairOffer offer) =>
        offer.Items?.FirstOrDefault() is { } root ? _itemHelper?.GetItem(root.Template).Value?.Name ?? root.Template.ToString() : "?";
}
