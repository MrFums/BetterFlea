using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services.Ragfair;
using SPTarkov.Server.Core.Utils;
using BetterFlea.Offers;

namespace BetterFlea.Selling;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class OrderBookLoad(
    ConfigManager configManager,
    ItemHelper itemHelper,
    ProfileHelper profileHelper,
    RagfairOfferService ragfairOfferService,
    RagfairOfferHolder ragfairOfferHolder,
    HideoutTable hideoutTable) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        OrderBook.Bind(configManager, itemHelper, profileHelper, ragfairOfferService, ragfairOfferHolder, hideoutTable);
        new AveragePricePatch().Enable();
        return Task.CompletedTask;
    }
}

// Buyers go down the list. For the item on screen and for every item a player has listed, a buyer turns up every
// few minutes and takes the cheapest single offer (nobody picks a pack or a barter first), if the price is one people
// pay. A player's offer sells when the buyers get to it, so cheaper offers above it go first. Each buyer takes what
// people buy at once, and buyers come less often after each sale, so a pile of one item sells a bit at a time.
internal static class OrderBook
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static ProfileHelper? _profiles;
    private static RagfairOfferService? _offers;
    private static RagfairOfferHolder? _holder;
    private static HideoutTable? _hideout;
    private static Timer? _timer;
    private static long _lastReport;

    private sealed record Pace(long NextAt, double Slow, long At);

    private static readonly ConcurrentDictionary<MongoId, Pace> Paces = new();

    // Player offers that can't sell yet (the flip penalty), by offer id: from when they can.
    private static readonly ConcurrentDictionary<MongoId, long> NotBefore = new();

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, ProfileHelper profiles, RagfairOfferService offers, RagfairOfferHolder holder, HideoutTable hideout)
    {
        _hideout = hideout;
        _config = configManager;
        _itemHelper = itemHelper;
        _profiles = profiles;
        _offers = offers;
        _holder = holder;
        _timer ??= new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private static BuyersConfig? Settings => _config?.Mod.PlayerSelling is { Enabled: true } selling && selling.Buyers.Enabled
        && selling.SaleDelay.Enabled ? selling.Buyers : null;

    public static bool On => Settings is not null;

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // A player's plain offer of one item: the buyers here sell it, not a roll made when it went up. Not a gun or
    // anything with durability (armour, helmets): each of those is its own thing, judged on its own.
    public static bool Handles(RagfairOffer offer) =>
        offer.SellInOnePiece != true && offer.Items is { Count: 1 } items && MoneyOnly(offer)
        && items[0].Upd?.Repairable is null && !_itemHelper!.IsOfBaseclass(items[0].Template, BaseClasses.WEAPON);

    // ratio: its price against normal. One at or under normal is up for buyers within seconds; a dearer one waits a
    // minute. waitMinutes: the flip penalty.
    public static void Listed(RagfairOffer offer, double? ratio, double waitMinutes)
    {
        // A rare item takes a few minutes to find a buyer even at a fair price, longer with a slower buyer timer; at a
        // quick-sell price (safeBelowPercent under normal) it goes at once.
        var rarity = offer.Items?.FirstOrDefault() is { } root ? _itemHelper!.GetItem(root.Template).Value?.Properties?.RarityPvE : null;
        var timer = Settings is { } settings ? TimerScale(settings) : 1;
        var first = ratio is { } r && r <= QuickBelow() ? 5
            : rarity == "Superrare" ? (long)((180 + Random.Shared.Next(300)) * timer)
            : rarity == "Rare" ? (long)((60 + Random.Shared.Next(120)) * timer)
            : ratio is <= 1 ? 5 : 60;
        NotBefore[offer.Id] = Now() + first + (long)(Math.Max(0, waitMinutes) * 60);
    }

    // At or under this share of normal an offer is a quick sell: buyers come at the set timer, whatever the item.
    private static double QuickBelow() =>
        1 - Math.Clamp(_config?.Mod.PlayerSelling.Undercut.SafeBelowPercent ?? 10, 0, 90) / 100;

    // The buyer timer against its default (6 to 12 s): 1 at the default, 2 at twice as slow.
    private static double TimerScale(BuyersConfig settings)
    {
        var low = Math.Max(0.1, settings.MinMinutes);
        return Math.Clamp((low + Math.Max(low, settings.MaxMinutes)) / 2 / 0.15, 0.2, 10);
    }

    // A player's offer buyers can take now at a quick-sell price.
    private static bool Quick(RagfairOffer offer, double normal, long now) =>
        offer.RequirementsCost is { } price && price > 0 && now >= NotBefore.GetValueOrDefault(offer.Id)
        && price / Condition(offer.Items![0]) <= normal * QuickBelow();

    // Guns, armour and anything else these buyers don't take get one roll when listed. A fair one (at most 5% over
    // normal) that missed gets another every unique wait, so in the end it sells; a dearer one stays as it rolled.
    // Never over normal by more than that, so relisting can't make money the market wouldn't pay.
    private const double FairOver = 1.05;

    private static readonly ConcurrentDictionary<MongoId, long> RecheckAt = new();

    // waitMinutes: the flip penalty, so a quick flip waits for its next roll too.
    public static void Rolled(RagfairOffer offer, double waitMinutes) =>
        RecheckAt[offer.Id] = Now() + (long)(Math.Max(0, waitMinutes) * 60) + Recheck(offer);

    private static long Recheck(RagfairOffer offer) =>
        (long)(SaleDelayGate.UniqueWaitMinutes(SaleDelayGate.NormalPrice(offer.Items ?? []) ?? 100000) * 60);

    private static void Rerolls(long now)
    {
        var delay = _config?.Mod.PlayerSelling is { Enabled: true } selling && selling.SaleDelay.Enabled ? selling.SaleDelay : null;
        if (delay is null || _profiles is null || _itemHelper is null)
        {
            return;
        }

        var live = new HashSet<MongoId>();
        foreach (var (_, profile) in _profiles.GetProfiles())
        {
            foreach (var offer in (profile.CharacterData?.PmcData?.RagfairInfo?.Offers ?? []).ToList())
            {
                if (offer.EndTime <= now || offer.SellInOnePiece == true || offer.Items is not { Count: > 0 } items
                    || !MoneyOnly(offer) || (On && Handles(offer)))
                {
                    continue;
                }

                live.Add(offer.Id);
                // Seen for the first time since the server started: its roll is long done.
                if (!RecheckAt.TryGetValue(offer.Id, out var due))
                {
                    RecheckAt[offer.Id] = now + Recheck(offer);
                    continue;
                }

                if (now < due)
                {
                    continue;
                }

                RecheckAt[offer.Id] = now + Recheck(offer);
                var pending = offer.SellResults?.ToList().Sum(sale => sale.Amount ?? 0) ?? 0;
                var left = offer.Quantity - pending;
                if (pending > 0 || left <= 0 || offer.RequirementsCost is not { } price || price <= 0
                    || SaleDelayGate.NormalPrice(items) is not { } normal || normal <= 0 || price > normal * FairOver)
                {
                    continue;
                }

                // As on listing: a cheaper offer of the same item goes first, unless this one is well under normal.
                var tpl = items[0].Template;
                var ratio = price / normal;
                var chance = SaleDelayGate.BuyerTakes(ratio);
                if (items.Count == 1 && !_itemHelper.IsOfBaseclass(tpl, BaseClasses.WEAPON) && ratio > QuickBelow()
                    && Undercuts.Cheapest(tpl, Condition(items[0])) is { } cheapest && cheapest < price)
                {
                    chance *= Math.Clamp(delay.CheaperOfferShare, 0, 1);
                }

                if (Random.Shared.NextDouble() >= chance)
                {
                    DebugLog.Write(() => $"Re-roll: {Name(tpl)} at {price:0} RUB (x{ratio:0.00}) found no buyer, next in {RecheckAt[offer.Id] - now} s");
                    continue;
                }

                var lot = items.Count > 1 ? 1 : Math.Min(left, SaleDelayGate.LotFor(tpl, left));
                offer.SellResults ??= [];
                offer.SellResults.Add(new SellResult { SellTime = now + 5 + Random.Shared.Next(55), Amount = lot });
                DebugLog.Write(() => $"Re-roll: {Name(tpl)} found a buyer for {lot} at {price:0} RUB (x{ratio:0.00})");
            }
        }

        foreach (var id in RecheckAt.Keys)
        {
            if (!live.Contains(id))
            {
                RecheckAt.TryRemove(id, out _);
            }
        }
    }

    // Someone bought an offer of the item: a new one goes up a while later.
    public static void Gone(MongoId tpl)
    {
        if (Settings is { } settings)
        {
            OnDemandOffers.Sold(tpl, settings.RelistMinMinutes, settings.RelistMaxMinutes);
        }
    }

    private static bool MoneyOnly(RagfairOffer offer) =>
        offer.Requirements?.All(requirement => _itemHelper!.IsOfBaseclass(requirement.TemplateId, BaseClasses.MONEY)) == true;

    private static double Condition(Item item) =>
        UsedListingsGate.PriceQuality(item);

    private static void Tick()
    {
        try
        {
            Rerolls(Now());
            if (Settings is not { } settings || _offers is null || _itemHelper is null)
            {
                return;
            }

            var mine = PlayerOffers();
            var items = new HashSet<MongoId>(OnDemandOffers.Watched(5, 600));
            items.UnionWith(mine.Select(entry => entry.Offer.Items![0].Template));
            var now = Now();
            if (now - _lastReport >= 60)
            {
                _lastReport = now;
                DebugLog.Write(() => $"Buyers: watching {items.Count} item(s) ({string.Join(", ", items.Select(tpl => $"{Name(tpl)} volume x{Volume(tpl):0.00} ({BetterFlea.Prices.WipeEconomy.Uses(tpl) + Crafts.Value.GetValueOrDefault(tpl)} uses), next in {(Paces.TryGetValue(tpl, out var p) ? p.NextAt - now : -1)} s, slow x{(Paces.TryGetValue(tpl, out var q) ? q.Slow : 0):0.00}"))}), {mine.Count} player offer(s), on the market list: {string.Join(", ", items.Select(tpl => (_offers.GetOffersOfType(tpl) ?? []).Count(offer => offer.CreatedBy == OfferCreator.Player)))}");
            }
            foreach (var tpl in items)
            {
                // A player's quick sell draws buyers at the set timer, however rare or dear the item.
                var quick = SaleDelayGate.NormalPrice([new Item { Id = new MongoId(), Template = tpl }]) is { } normal && normal > 0
                    && mine.Any(entry => entry.Offer.Items![0].Template == tpl && Quick(entry.Offer, normal, now));
                var volume = quick ? Math.Min(1, Volume(tpl)) : Volume(tpl);
                var pace = Paces.GetOrAdd(tpl, _ => new Pace(now + Wait(settings, volume), 1, now));
                if (quick && pace.NextAt - now > Math.Max(0.1, settings.MaxMinutes) * 60 * pace.Slow)
                {
                    pace = pace with { NextAt = now + Wait(settings, pace.Slow) };
                }

                // A busy item can see several buyers between two passes.
                for (var buyers = 0; buyers < 6 && now >= pace.NextAt; buyers++)
                {
                    // Buyers come back to their usual pace over time.
                    var slow = 1 + (pace.Slow - 1) * Math.Pow(0.5, (now - pace.At) / 60.0 / Math.Max(1, settings.RecoverMinutes));
                    // Only a player's own sale slows the next buyer, so a pile of one item sells a bit at a time.
                    var bought = Buyer(tpl, mine.Where(entry => entry.Offer.Items![0].Template == tpl).ToList(), now);
                    slow = Math.Min(3, slow * (bought == Sold.Player ? Math.Max(1, settings.Slowdown) : bought == Sold.Nothing ? 1.2 : 1));
                    pace = new Pace(pace.NextAt + Wait(settings, slow * volume), slow, now);
                }

                Paces[tpl] = pace.NextAt < now ? pace with { NextAt = now + Wait(settings, pace.Slow * volume) } : pace;
                TopSells(tpl, mine.Where(entry => entry.Offer.Items![0].Template == tpl).Select(entry => entry.Offer).ToList(), volume, now);
            }
        }
        catch (Exception exception)
        {
            DebugLog.Summary(() => $"Buyers: order book pass failed: {exception.GetBaseException().Message}");
        }
    }

    // How much longer than usual buyers take for an item, from what it is. Cheap things trade all the time and dear
    // ones now and then; rare things less again; and the more hideout builds, crafts and quests want it, the busier
    // it is. Wires (about 50,000, common, wanted all over the hideout) trade every few seconds, a LEDX every few minutes.
    internal static double Volume(MongoId tpl)
    {
        var normal = SaleDelayGate.NormalPrice([new Item { Id = new MongoId(), Template = tpl }]) ?? 20000;
        var price = Math.Clamp(Math.Sqrt(Math.Max(1, normal) / 20000), 0.15, 8);
        var rarity = _itemHelper!.GetItem(tpl).Value?.Properties?.RarityPvE switch
        {
            "Superrare" => 3,
            "Rare" => 1.5,
            _ => 1.0
        };
        var uses = BetterFlea.Prices.WipeEconomy.Uses(tpl) + Crafts.Value.GetValueOrDefault(tpl);
        return price * rarity / (1 + 0.2 * Math.Min(uses, 20));
    }

    // How many hideout crafts take each item.
    private static readonly Lazy<Dictionary<MongoId, int>> Crafts = new(() =>
    {
        var counts = new Dictionary<MongoId, int>();
        foreach (var recipe in _hideout?.Production.Recipes ?? [])
        {
            foreach (var requirement in recipe.Requirements ?? [])
            {
                if (requirement.Type == "Item" && requirement.TemplateId is { } tpl)
                {
                    counts[tpl] = counts.GetValueOrDefault(tpl) + 1;
                }
            }
        }

        return counts;
    });
    private static long Wait(BuyersConfig settings, double slow)
    {
        var low = Math.Max(0.1, settings.MinMinutes);
        return (long)((low + Random.Shared.NextDouble() * Math.Max(0, settings.MaxMinutes - low)) * 60 * slow);
    }

    private static List<(RagfairOffer Offer, List<RagfairOffer> Owner, MongoId Player)> PlayerOffers()
    {
        var found = new List<(RagfairOffer, List<RagfairOffer>, MongoId)>();
        var now = Now();
        foreach (var (id, profile) in _profiles!.GetProfiles())
        {
            if (profile.CharacterData?.PmcData?.RagfairInfo?.Offers is not { } offers)
            {
                continue;
            }

            foreach (var offer in offers.ToList())
            {
                if (offer.EndTime > now && Handles(offer))
                {
                    found.Add((offer, offers, id));
                }
            }
        }

        return found;
    }

    // A player's offer at the top of the list always sells soon, however quiet the item: within two minutes (a busy
    // item well under that), if its price is fair (at most 5% over normal: being top of a list that buying has pushed
    // up doesn't count). An item with uses or a resource (keys, meds, fuel) goes against offers in the same condition;
    // a worse one only counts if it's at least 15% cheaper. Top of those, it waits a bit longer (2 to 10 minutes),
    // unless it's at least 15% under the others in its condition.
    private static readonly ConcurrentDictionary<MongoId, long> TopDue = new();

    private static void TopSells(MongoId tpl, List<RagfairOffer> mine, double volume, long now)
    {
        if (mine.Count == 0 || SaleDelayGate.NormalPrice([new Item { Id = new MongoId(), Template = tpl }]) is not { } normal || normal <= 0)
        {
            return;
        }

        var props = _itemHelper!.GetItem(tpl).Value?.Properties;
        var worn = (props?.MaximumNumberOfUsage ?? 0) > 0 || (props?.MaxHpResource ?? 0) > 0 || (props?.MaxResource ?? 0) > 0
            || (props?.MaxRepairResource ?? 0) > 0;
        var others = (_offers!.GetOffersOfType(tpl) ?? [])
            .Where(offer => offer.IsFakePlayerOffer() && offer.SellInOnePiece != true && offer.Items is { Count: 1 }
                && offer.RequirementsCost is > 0 && MoneyOnly(offer))
            .Select(offer => (Price: offer.RequirementsCost!.Value, Condition: Condition(offer.Items![0])))
            .ToList();

        foreach (var offer in mine.OrderBy(offer => offer.RequirementsCost ?? double.MaxValue))
        {
            var pending = offer.SellResults?.ToList().Sum(sale => sale.Amount ?? 0) ?? 0;
            var left = offer.Quantity - pending;
            var condition = Condition(offer.Items![0]);
            if (left <= 0 || offer.RequirementsCost is not { } price || price <= 0 || now < NotBefore.GetValueOrDefault(offer.Id)
                || price / condition > normal * 1.05)
            {
                TopDue.TryRemove(offer.Id, out _);
                continue;
            }

            var rivals = worn
                ? others.Where(other => other.Condition >= condition - 0.05 || other.Price < price * 0.85).Select(other => other.Price).ToList()
                : others.Select(other => other.Price).ToList();
            var cheapestRival = rivals.Count > 0 ? rivals.Min() : double.MaxValue;
            if (cheapestRival < price)
            {
                TopDue.TryRemove(offer.Id, out _);
                continue;
            }

            if (!TopDue.TryGetValue(offer.Id, out var due))
            {
                var wait = worn && price > cheapestRival * 0.85
                    ? Math.Min(600, 120 + 60 * volume) * (0.6 + 0.4 * Random.Shared.NextDouble())
                    : Math.Min(120, 20 + 25 * volume) * (0.6 + 0.4 * Random.Shared.NextDouble());
                TopDue[offer.Id] = now + (long)wait;
                continue;
            }

            if (now < due)
            {
                continue;
            }

            var lot = Math.Min(left, Need(tpl));
            offer.SellResults ??= [];
            offer.SellResults.Add(new SellResult { SellTime = now, Amount = lot });
            TopDue.TryRemove(offer.Id, out _);
            DebugLog.Write(() => $"Buyers: {Name(tpl)} bought {lot} from a player's offer at the top of the list, {price:0} RUB (x{price / condition / normal:0.00})");
        }
    }

    private enum Sold { Nothing, Seller, Player }

    // One buyer: one of the cheapest single offers, bought if the price is right.
    private static Sold Buyer(MongoId tpl, List<(RagfairOffer Offer, List<RagfairOffer> Owner, MongoId Player)> mine, long now)
    {
        if (SaleDelayGate.NormalPrice([new Item { Id = new MongoId(), Template = tpl }]) is not { } normal || normal <= 0)
        {
            return Sold.Nothing;
        }

        var candidates = new List<(RagfairOffer Offer, double Each, bool Player, int Left)>();
        foreach (var offer in _offers!.GetOffersOfType(tpl) ?? [])
        {
            if (offer.IsFakePlayerOffer() && offer.SellInOnePiece != true && offer.Items is { Count: 1 } items
                && offer.RequirementsCost is { } cost && cost > 0 && MoneyOnly(offer))
            {
                candidates.Add((offer, cost / Condition(items[0]), false, Math.Max(1, offer.Quantity)));
            }
        }

        foreach (var (offer, _, _) in mine)
        {
            // SPT counts a sale off the offer's quantity, not the item's stack.
            var root = offer.Items![0];
            var pending = offer.SellResults?.ToList().Sum(sale => sale.Amount ?? 0) ?? 0;
            var left = offer.Quantity - pending;
            if (left > 0 && offer.RequirementsCost is { } cost && cost > 0 && now >= NotBefore.GetValueOrDefault(offer.Id))
            {
                candidates.Add((offer, cost / Condition(root), true, left));
            }
        }

        if (candidates.Count == 0)
        {
            return Sold.Nothing;
        }

        // Each buyer wants some (mostly one to three, now and then a lot) and pays up to a price of their own: most
        // near the normal price, some well over it (about 1 in 4 go to 6% over, a few to 15%). They start near the
        // top of the list, mostly the cheapest but sometimes one of the next few for the seller or the amount, then
        // work down it cheapest first until they have enough or the next offer is more than they'll pay. So a busy
        // item's list clears from the top and an offer a few percent over still sells, just later.
        var expensive = Math.Max(0.01, (_config!.Mod.PlayerSelling.SaleDelay.ExpensivePercent) / 100.0);
        var spread = Math.Sqrt(-2 * Math.Log(1 - Random.Shared.NextDouble())) * Math.Cos(2 * Math.PI * Random.Shared.NextDouble());
        var most = normal * (1 + Math.Min(expensive, Math.Abs(spread) * 0.06));
        var need = Need(tpl);

        var sorted = candidates.OrderBy(entry => entry.Each).ThenBy(entry => entry.Player).ToList();
        var top = sorted.TakeWhile((entry, index) => index < 5 && entry.Each <= sorted[0].Each * 1.03).ToList();
        double[] weights = [0.4, 0.25, 0.15, 0.12, 0.08];
        var roll = Random.Shared.NextDouble() * weights.Take(top.Count).Sum();
        var choice = 0;
        while (choice < top.Count - 1 && (roll -= weights[choice]) > 0)
        {
            choice++;
        }

        var order = new List<(RagfairOffer Offer, double Each, bool Player, int Left)> { top[choice] };
        order.AddRange(sorted.Where(entry => entry.Offer != top[choice].Offer));

        var sold = Sold.Nothing;
        foreach (var (pick, each, player, available) in order)
        {
            if (need <= 0 || each > most)
            {
                break;
            }

            // Three or fewer left go in one go; another seller's whole offer now and then too.
            var lot = available <= 3 || (!player && Random.Shared.NextDouble() < Math.Clamp(Settings!.WholeOfferChance, 0, 1))
                ? available : Math.Min(need, available);
            need -= lot;
            var ratio = each / normal;
            if (player)
            {
                // Added in place: SPT takes sales off this list on its own thread, and a copy put back could bring
                // back one it had just paid out.
                pick.SellResults ??= [];
                pick.SellResults.Add(new SellResult { SellTime = now, Amount = lot });
                DebugLog.Write(() => $"Buyers: {Name(tpl)} bought {lot} from a player's offer at {each:0} RUB (x{ratio:0.00})");

                // Only a sale over normal slows the next buyer: a fair price sells like any other offer.
                sold = ratio > 1 ? Sold.Player : sold == Sold.Nothing ? Sold.Seller : sold;
                continue;
            }

            if (lot < available && pick.Items![0].Upd is { } upd)
            {
                pick.Quantity = available - lot;
                upd.StackObjectsCount = available - lot;
            }
            else
            {
                if (OnDemandOffers.Enabled)
                {
                    _offers.RemoveOfferById(pick.Id);
                }
                else
                {
                    _holder?.FlagOfferAsExpired(pick.Id);
                }

                Gone(tpl);
            }

            DebugLog.Write(() => $"Buyers: {Name(tpl)} bought {lot} from {pick.User?.Nickname} at {each:0} RUB (x{ratio:0.00})");
            sold = sold == Sold.Nothing ? Sold.Seller : sold;
        }

        if (sold == Sold.Nothing)
        {
            DebugLog.Write(() => $"Buyers: {Name(tpl)} cheapest at x{sorted[0].Each / normal:0.00} of normal, more than the buyer pays");
        }

        return sold;
    }

    // How many a buyer wants: mostly a few (a round lot of ammo), one in seven a lot.
    private static int Need(MongoId tpl)
    {
        var stackMax = Math.Max(1, _itemHelper!.GetItem(tpl).Value?.Properties?.StackMaxSize ?? 1);
        var bulk = Random.Shared.NextDouble() < 0.15;
        if (stackMax > 1)
        {
            return bulk ? stackMax * Random.Shared.Next(2, 7) : SaleDelayGate.LotFor(tpl, stackMax);
        }

        return bulk ? Random.Shared.Next(5, 31) : SaleDelayGate.LotFor(tpl, 3);
    }
    private static string Name(MongoId tpl) => _itemHelper?.GetItem(tpl).Value?.Name ?? tpl.ToString();
}

// The average the game shows when listing is the average of the offers up, which sit a little over normal (they go
// from a few percent under to 15% over). Shown as Better Flea's normal price instead, so "the average" is a fair price.
public class AveragePricePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairController).GetMethod(nameof(RagfairController.GetItemMinAvgMaxFleaPriceValues), BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(GetMarketPriceRequestData getPriceRequest, GetItemPriceResult __result)
    {
        if (__result is null || !OrderBook.On
            || SaleDelayGate.NormalPrice([new Item { Id = new MongoId(), Template = getPriceRequest.TemplateId }]) is not { } normal || normal <= 0)
        {
            return;
        }

        __result.Avg = Math.Round(normal);
        __result.Min = Math.Min(__result.Min, normal);
        __result.Max = Math.Max(__result.Max, normal);
    }
}
