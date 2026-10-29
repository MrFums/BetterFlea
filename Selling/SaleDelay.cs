using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;
using Item = SPTarkov.Server.Core.Models.Eft.Common.Tables.Item;

namespace BetterFlea.Selling;

[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public class SaleDelayPatchesLoad(
    ConfigManager configManager,
    GlobalTable globalTable,
    ItemHelper itemHelper,
    RagfairPriceService ragfairPriceService,
    SPTarkov.Server.Core.Models.Spt.Config.RagfairConfig ragfairConfig) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        SaleDelayGate.Bind(configManager, globalTable, itemHelper, ragfairPriceService, ragfairConfig);
        new SaleDelayPatch().Enable();
        new SellChancePatch().Enable();
        return Task.CompletedTask;
    }
}

// Sale speed goes by price against normal: undercut sells in under a minute, normal in a few minutes,
// past expensivePercent over never. SPT's buyer timeline is pushed
// back by one wait, so its spacing stays.
//
// "Normal" is worked out from what's actually listed, priced the way the flea's own offers are. SPT's normal is
// the average of every offer of that item, so a 1-plate armour was weighed against full-plate ones and sold at a
// full-plate price.
internal static class SaleDelayGate
{
    private static ConfigManager? _config;
    private static GlobalTable? _globals;
    private static ItemHelper? _itemHelper;
    private static RagfairPriceService? _prices;
    private static SPTarkov.Server.Core.Models.Spt.Config.RagfairConfig? _ragfairConfig;

    [ThreadStatic] private static double? _priceRatio;
    [ThreadStatic] private static double? _gunValue;
    [ThreadStatic] private static double? _contentsValue;
    [ThreadStatic] private static int _packUnits;
    [ThreadStatic] private static double? _cheapest;
    [ThreadStatic] private static bool _lowest;
    [ThreadStatic] private static MongoId? _listedTpl;
    [ThreadStatic] private static bool _plain;
    [ThreadStatic] private static double? _normalUsed;

    // How the listing went: its price against normal, and whether no other seller was cheaper. Null ratio = not judged.
    public static (double? Ratio, bool Lowest) Outcome => (_priceRatio, _lowest);

    // packUnits: how many of the item a pack offer sells at once (its price is for all of them). 1 otherwise.
    public static void BeginListing(List<Item>? listed, ItemHelper itemHelper, int packUnits)
    {
        EndListing();
        _packUnits = Math.Max(1, packUnits);
        if (listed is not { Count: > 0 })
        {
            return;
        }

        _listedTpl = listed[0].Template;
        // Only what the order book's buyers take (OrderBook.Handles): a lone gun or thing with durability (glasses,
        // a bare helmet) gets the roll, or nothing would ever buy it.
        _plain = listed.Count == 1 && _packUnits == 1 && listed[0].Upd?.Repairable is null
            && !itemHelper.IsOfBaseclass(listed[0].Template, BaseClasses.WEAPON);

        // One plain item (not a gun, not a pack) is up against the same item from other sellers.
        if (listed.Count == 1 && _packUnits == 1 && !itemHelper.IsOfBaseclass(listed[0].Template, BaseClasses.WEAPON))
        {
            var quality = BetterFlea.Offers.UsedListingsGate.PriceQuality(listed[0]);
            _cheapest = Undercuts.Cheapest(listed[0].Template, quality);
        }

        // A gun goes by the trader-based value the flea's guns use; with that off, by its parts like anything else.
        if (BetterFlea.Prices.GunPricing.Applies(listed))
        {
            _gunValue = BetterFlea.Prices.GunPricing.Value(listed) * BetterFlea.Prices.GunPricing.Condition(listed);
            return;
        }

        _contentsValue = ContentsValue(listed);
    }

    // What one of this item (with everything in it) normally sells for in roubles: the price SellChance measures a
    // listing against. Price Check shows it. Null when there's nothing to go by.
    public static double? NormalPrice(List<Item> listed)
    {
        if (listed is not { Count: > 0 } || _itemHelper is null)
        {
            return null;
        }

        if (BetterFlea.Prices.GunPricing.Applies(listed))
        {
            return BetterFlea.Prices.GunPricing.Value(listed) * BetterFlea.Prices.GunPricing.Condition(listed);
        }

        return ContentsValue(listed);
    }

    public static void EndListing()
    {
        _priceRatio = null;
        _gunValue = null;
        _contentsValue = null;
        _packUnits = 1;
        _cheapest = null;
        _lowest = false;
        _listedTpl = null;
        _plain = false;
        _normalUsed = null;
    }

    // Chance a buyer takes an offer at this price against normal: certain up to normal, falling to 1 in 20 at
    // expensivePercent over, never past that.
    public static double BuyerTakes(double ratio)
    {
        var over = ratio - 1;
        var limit = Math.Max(0.01, (_config?.Mod.PlayerSelling.SaleDelay.ExpensivePercent ?? 15) / 100.0);
        return over <= 0 ? 1 : over > limit ? 0 : Math.Exp(-Math.Log(20) / limit * over);
    }

    public static int LotFor(MongoId tpl, int left)
    {
        var ammo = _itemHelper?.IsOfBaseclass(tpl, BaseClasses.AMMO) == true;
        var stackMax = Math.Max(1, _itemHelper?.GetItem(tpl).Value?.Properties?.StackMaxSize ?? 1);
        return Lot(Math.Max(1, left), ammo, stackMax);
    }

    // Buyers come one at a time, each taking what people buy at once: a round lot of ammo, one or two of anything
    // else. Every buyer after the first is less likely, so a pile of one item often only partly sells. The wait
    // between buyers is SPT's. Null leaves it to SPT (packs sell in one go). A plain offer of one item gets no roll
    // at all: the order book's buyers sell it when they get to it.
    public static List<SellResult>? RollForSale(double? sellChancePercent, int count, bool oneGo)
    {
        var settings = _config?.Mod.PlayerSelling.SaleDelay;
        if (settings is null || !settings.Enabled || oneGo || count < 1 || _listedTpl is not { } tpl
            || _itemHelper is null || _ragfairConfig is null)
        {
            return null;
        }

        if (_plain && OrderBook.On)
        {
            return [];
        }

        var chance = Math.Clamp((sellChancePercent ?? 0) / 100, 0, 1);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var end = OfferEndsAt() ?? now + 24 * 3600;
        var ammo = _itemHelper.IsOfBaseclass(tpl, BaseClasses.AMMO);
        var stackMax = Math.Max(1, _itemHelper.GetItem(tpl).Value?.Properties?.StackMaxSize ?? 1);
        var shortest = _ragfairConfig.Sell.Time.Min * 60;
        var longest = (1 - chance) * _ragfairConfig.Sell.Time.Max * 60;
        if (longest < shortest)
        {
            longest = shortest + 5;
        }

        var sales = new List<SellResult>();
        var at = now + SlowSaleSeconds(chance, settings.SlowSaleMaxHours);
        var left = count;
        var interest = chance;
        while (left > 0 && at < end && Random.Shared.NextDouble() < interest)
        {
            var lot = Lot(left, ammo, stackMax);
            at += Math.Max(1, (long)(shortest + Random.Shared.NextDouble() * (longest - shortest)));
            sales.Add(new SellResult { SellTime = at, Amount = lot });
            left -= lot;
            interest *= ammo || stackMax > 1 ? 0.85 : 0.45;
        }

        DebugLog.Write(() => $"Sale roll: {count} listed, buyers for {count - left} ({string.Join(", ", sales.Select(sale => sale.Amount))})");
        return sales;
    }

    // A buyer who might come at any time over slowSaleMaxHours, as often as the chance says: a sure sale comes now,
    // a 50% one mostly in the first part, a 15% one about anywhere in it.
    private static long SlowSaleSeconds(double chance, double maxHours)
    {
        if (chance >= 0.999 || chance <= 0 || maxHours <= 0)
        {
            return 0;
        }

        var hours = -Math.Log(1 - Random.Shared.NextDouble() * chance) / -Math.Log(1 - chance) * maxHours;
        return (long)Math.Round(hours * 3600);
    }

    private static int Lot(int left, bool ammo, int stackMax)
    {
        (int Size, double Weight)[] options = ammo
            ? left <= 30 ? [(left, 1)] : [(30, 3), (60, 3), (90, 1), (120, 2), (150, 1), (180, 2), (200, 1), (left, 3)]
            : stackMax > 1 ? [(stackMax, 2), (Math.Max(1, stackMax / 2), 2), (Math.Min(5, stackMax), 1), (left, 1)]
            : [(1, 7), (2, 2), (3, 1)];
        var fitting = options.Where(option => option.Size <= left).ToList();
        if (fitting.Count == 0)
        {
            return left;
        }

        var roll = Random.Shared.NextDouble() * fitting.Sum(option => option.Weight);
        foreach (var (size, weight) in fitting)
        {
            roll -= weight;
            if (roll <= 0)
            {
                return size;
            }
        }

        return fitting[^1].Size;
    }

    // One of the listed item with everything in it (plates, inserts, rounds in a mag), each part priced like on a
    // generated offer: base price and SPT's multiplier for it, its own condition, then key wear, drift and never under
    // what a trader pays. Then the whole market's multiplier and the Wipe Cycle. Null leaves it to SPT.
    private static double? ContentsValue(List<Item> listed)
    {
        if (_prices is null || _itemHelper is null || _ragfairConfig is null)
        {
            return null;
        }

        var root = listed[0];
        // An ammo box's price already covers the rounds in it.
        var parts = _itemHelper.IsOfBaseclass(root.Template, BaseClasses.AMMO_BOX) ? [root] : listed;
        var dynamic = _ragfairConfig.Dynamic;
        var total = 0.0;
        foreach (var item in parts)
        {
            // Built-in soft armour comes with the armour; SPT doesn't price it either.
            if (_itemHelper.IsOfBaseclass(item.Template, BaseClasses.BUILT_IN_INSERTS))
            {
                continue;
            }

            var price = BetterFlea.Prices.BookPrice.Of(item.Template);
            if (dynamic.IgnoreQualityPriceVarianceBlacklist?.Contains(item.Template) != true)
            {
                price *= _itemHelper.GetItemQualityModifier(item);
            }

            price = BetterFlea.Prices.PriceGate.Adjust(item.Template, Money.ROUBLES, item, price, counted: false);
            var rounds = item != root && item.SlotId == "cartridges" ? Math.Max(1, item.Upd?.StackObjectsCount ?? 1) : 1;
            total += price * rounds;
        }

        total *= (_config?.Mod.Prices.PriceMultiplier ?? 1.0) * BetterFlea.Prices.WipeEconomy.Factor(root.Template);
        return total > 0 ? total : null;
    }

    // Null leaves it to SPT.
    public static double? SellChance(double normalPrice, double listedPrice, double quality)
    {
        var settings = _config?.Mod.PlayerSelling.SaleDelay;
        if (settings is null || !settings.Enabled || listedPrice <= 0)
        {
            return null;
        }

        // SPT sums a gun's flea part prices; use the same trader-based value the flea's guns use. Anything else goes
        // by what's in it (condition included already).
        var normal = _gunValue is { } gun ? gun : _contentsValue is { } contents ? contents * _packUnits : normalPrice;
        if (normal <= 0)
        {
            return null;
        }

        var ratio = listedPrice / normal;
        _priceRatio = ratio;
        _normalUsed = normal / Math.Max(1, _packUnits);
        DebugLog.Write(() => $"Sale check: listed {listedPrice:0} RUB against a normal {normal:0} (SPT's average says {normalPrice:0}), x{ratio:0.00}");

        var chance = 100 * BuyerTakes(ratio);
        if (chance <= 0)
        {
            return 0;
        }

        // Buyers take a cheaper offer of the same item first, unless this one is well under normal.
        _lowest = _cheapest is not { } cheapest || listedPrice <= cheapest;
        var safe = 1 - Math.Clamp(_config!.Mod.PlayerSelling.Undercut.SafeBelowPercent, 0, 90) / 100;
        if (!_lowest && ratio > safe)
        {
            chance *= Math.Clamp(settings.CheaperOfferShare, 0, 1);
            DebugLog.Write(() => $"Sale check: another seller has it for {_cheapest:0} RUB, cheaper than {listedPrice:0}");
        }

        return chance < 1 ? 0 : Math.Round(chance);
    }

    public static void Bind(ConfigManager configManager, GlobalTable globalTable, ItemHelper itemHelper, RagfairPriceService prices,
        SPTarkov.Server.Core.Models.Spt.Config.RagfairConfig ragfairConfig)
    {
        _config = configManager;
        _globals = globalTable;
        _itemHelper = itemHelper;
        _prices = prices;
        _ragfairConfig = ragfairConfig;
    }

    // How much longer a dear unique item takes than a cheap one: 1 at 100,000 roubles, with the root of the price.
    private static double Dearness(double? normal = null) =>
        Math.Clamp(Math.Sqrt(Math.Max(1, normal ?? _normalUsed ?? 100000) / 100000), 0.5, 3);

    // One wait at the normal price for a unique item worth this much: uniqueMinMinutes to uniqueMaxMinutes, longer
    // the dearer. At least a minute.
    public static double UniqueWaitMinutes(double normal)
    {
        var settings = _config?.Mod.PlayerSelling.SaleDelay;
        var low = settings?.UniqueMinMinutes ?? 5;
        var high = Math.Max(low, settings?.UniqueMaxMinutes ?? 20);
        return Math.Max(1, (low + Random.Shared.NextDouble() * (high - low)) * Dearness(normal));
    }

    public static void Delay(List<SellResult> sales)
    {
        var settings = _config?.Mod.PlayerSelling.SaleDelay;
        if (settings is null || !settings.Enabled || sales.Count == 0)
        {
            return;
        }

        var flipMinutes = FlipPenaltyGate.ExtraWaitMinutes();
        var ratio = _priceRatio ?? 1.0;

        // A gun, armour or anything with durability is its own thing: at the normal price someone takes a while to
        // want that one, longer the dearer it is. Never straight away.
        var unique = !_plain;
        if (!unique && flipMinutes <= 0 && ratio <= 1.0 && Random.Shared.NextDouble() < settings.NoDelayChance)
        {
            return;
        }

        // 1 at the normal price; about a tenth of that 10% under, and 4x at 15% over.
        var past = (ratio - 0.85) / 0.15;
        var scale = past <= 0 ? 0.03 : Math.Max(0.03, past * past);
        var (low, high) = unique
            ? (settings.UniqueMinMinutes * Dearness(), settings.UniqueMaxMinutes * Dearness())
            : (settings.MinMinutes, settings.MaxMinutes);
        var waitMinutes = (low + Random.Shared.NextDouble() * Math.Max(0, high - low)) * scale + flipMinutes;
        var waitSeconds = (long)Math.Round(waitMinutes * 60.0);
        if (waitSeconds <= 0)
        {
            return;
        }

        foreach (var sale in sales)
        {
            sale.SellTime += waitSeconds;
        }

        // Past the end of the listing: the offer just expires.
        if (OfferEndsAt() is { } end)
        {
            sales.RemoveAll(sale => sale.SellTime > end);
        }
    }

    private static long? OfferEndsAt()
    {
        var hours = _globals?.Configuration?.RagFair?.OfferDurationTimeInHour;
        if (hours is not { } duration || duration <= 0)
        {
            return null;
        }

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)TimeSpan.FromHours((int)duration).TotalSeconds;
    }
}

public class SellChancePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairSellHelper).GetMethod(
            nameof(RagfairSellHelper.CalculateSellChance),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static bool Prefix(double averageOfferPriceRub, double playerListedPriceRub, double qualityMultiplier, ref double __result)
    {
        if (SaleDelayGate.SellChance(averageOfferPriceRub, playerListedPriceRub, qualityMultiplier) is not { } chance)
        {
            return true;
        }

        __result = chance;
        return false;
    }
}

public class SaleDelayPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairSellHelper).GetMethod(
            nameof(RagfairSellHelper.RollForSale),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static bool Prefix(double? sellChancePercent, int itemSellCount, bool sellInOneGo, ref List<SellResult> __result)
    {
        if (SaleDelayGate.RollForSale(sellChancePercent, itemSellCount, sellInOneGo) is not { } sales)
        {
            return true;
        }

        __result = sales;
        return false;
    }

    [PatchPostfix]
    public static void Postfix(List<SellResult> __result)
    {
        SaleDelayGate.Delay(__result);
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class FlipPenaltyPatchesLoad(
    ConfigManager configManager,
    ItemHelper itemHelper,
    TraderHelper traderHelper,
    RagfairOfferService ragfairOfferService,
    SPTarkov.Server.Core.Services.Commerce.MailSendService mailSendService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        FlipPenaltyGate.Bind(configManager, itemHelper, traderHelper, ragfairOfferService, mailSendService);
        new RecordPurchasePatch().Enable();
        new ListingContextPatch().Enable();
        return Task.CompletedTask;
    }
}

// Recent buys per player, so a quick flip waits longer for buyers. Saved in data/recent-purchases.json;
// anything older than the window is dropped.
internal static class FlipPenaltyGate
{
    private const string FleaPurchase = "buy_from_ragfair_pmc";
    private const string FileName = "recent-purchases.json";
    private const int MaxPerItem = 200;

    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static TraderHelper? _traderHelper;
    private static RagfairOfferService? _offers;

    private static readonly ConcurrentDictionary<MongoId, ConcurrentDictionary<MongoId, List<Purchase>>> History = new();

    // Player -> item id -> when it was bought, so only the bought items wait, not ones of the same kind the player
    // already had. Items that stack aren't kept: a stack can be split or merged, so those go by item type.
    private static readonly ConcurrentDictionary<MongoId, ConcurrentDictionary<string, long>> BoughtItems = new();

    private static readonly MongoId AmmoBox = new("543be5cb4bdc2deb348b4568");

    [ThreadStatic] private static Purchase? _pendingPurchase;
    [ThreadStatic] private static HashSet<string>? _ownedBefore;
    [ThreadStatic] private static (MongoId Player, MongoId Tpl)? _listing;

    private sealed record Purchase(MongoId Tpl, double Count, long At);

    // Player -> item -> [count, when] per purchase. Items: player -> bought item id -> when.
    public sealed class PurchaseFile
    {
        public Dictionary<string, Dictionary<string, List<double[]>>> Players { get; set; } = new();

        public Dictionary<string, Dictionary<string, long>> Items { get; set; } = new();
    }

    private static int _dirty;
    private static Timer? _saveTimer;

    private static SPTarkov.Server.Core.Services.Commerce.MailSendService? _mail;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, TraderHelper traderHelper, RagfairOfferService offers,
        SPTarkov.Server.Core.Services.Commerce.MailSendService mail)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _traderHelper = traderHelper;
        _offers = offers;
        _mail = mail;

        foreach (var (player, items) in DataFiles.Read<PurchaseFile>(FileName)?.Players ?? [])
        {
            foreach (var (tpl, purchases) in items)
            {
                var list = History.GetOrAdd(new MongoId(player), _ => new()).GetOrAdd(new MongoId(tpl), _ => new());
                list.AddRange(purchases.Where(entry => entry.Length == 2).Select(entry => new Purchase(new MongoId(tpl), entry[0], (long)entry[1])));
            }
        }

        foreach (var (player, items) in DataFiles.Read<PurchaseFile>(FileName)?.Items ?? [])
        {
            var bought = BoughtItems.GetOrAdd(new MongoId(player), _ => new());
            foreach (var (id, at) in items)
            {
                bought[id] = at;
            }
        }

        _saveTimer ??= new Timer(_ => SaveIfChanged(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private static void SaveIfChanged()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0)
        {
            return;
        }

        var window = (Settings?.WindowMinutes ?? 0) * 60.0;
        var now = Now();
        var file = new PurchaseFile();
        foreach (var (player, items) in History)
        {
            foreach (var (tpl, list) in items)
            {
                List<double[]> kept;
                lock (list)
                {
                    list.RemoveAll(p => now - p.At >= window);
                    if (list.Count > MaxPerItem)
                    {
                        list.RemoveRange(0, list.Count - MaxPerItem);
                    }

                    kept = list.Select(p => new[] { p.Count, p.At }).ToList();
                }

                if (kept.Count == 0)
                {
                    continue;
                }

                if (!file.Players.TryGetValue(player.ToString(), out var byItem))
                {
                    file.Players[player.ToString()] = byItem = new();
                }

                byItem[tpl.ToString()] = kept;
            }
        }

        foreach (var (player, bought) in BoughtItems)
        {
            foreach (var (id, at) in bought)
            {
                if (now - at >= window)
                {
                    bought.TryRemove(id, out _);
                }
            }

            if (!bought.IsEmpty)
            {
                file.Items[player.ToString()] = new Dictionary<string, long>(bought);
            }
        }

        DataFiles.Write(FileName, file);
    }

    private static FlipPenaltyConfig? Settings =>
        _config?.Mod.PlayerSelling.FlipPenalty is { Enabled: true } settings ? settings : null;

    // Before SPT runs the trade: a sold-out offer is gone afterwards.
    public static void BeforePurchase(PmcData pmcData, ProcessBuyTradeRequestData request)
    {
        _pendingPurchase = null;
        _ownedBefore = null;
        if (Settings is not { } settings)
        {
            return;
        }

        var fromFlea = string.Equals(request.Type, FleaPurchase, StringComparison.OrdinalIgnoreCase);
        if (fromFlea && !settings.CountFleaPurchases)
        {
            return;
        }

        MongoId? tpl = fromFlea
            ? _offers?.GetOfferByOfferId(request.ItemId)?.Items?.FirstOrDefault()?.Template
            : _traderHelper?.GetTraderAssortsByTraderId(request.TransactionId)?.Items?
                .FirstOrDefault(item => item.Id == request.ItemId)?.Template;

        if (tpl is { } found && request.Count is > 0)
        {
            var weight = !fromFlea && request.TransactionId == Traders.FENCE ? settings.FenceMultiplier : 1.0;
            _pendingPurchase = new Purchase(found, request.Count.Value * weight, Now());
            _ownedBefore = pmcData.Inventory?.Items?.Select(item => item.Id.ToString()).ToHashSet();
        }
    }

    public static void AfterPurchase(PmcData pmcData, MongoId player, ItemEventRouterResponse output)
    {
        var purchase = _pendingPurchase;
        var before = _ownedBefore;
        _pendingPurchase = null;
        _ownedBefore = null;
        if (purchase is null || output.Warnings is { Count: > 0 })
        {
            return;
        }

        var list = History.GetOrAdd(player, _ => new()).GetOrAdd(purchase.Tpl, _ => new());
        lock (list)
        {
            list.Add(purchase);
        }

        // Attachments too, so parts stripped off a bought gun wait like the gun.
        if (before is not null)
        {
            var bought = BoughtItems.GetOrAdd(player, _ => new());
            foreach (var item in pmcData.Inventory?.Items ?? [])
            {
                if (!before.Contains(item.Id.ToString()) && !Stacks(item.Template))
                {
                    bought[item.Id.ToString()] = purchase.At;
                }
            }
        }

        Interlocked.Exchange(ref _dirty, 1);

        DebugLog.Write(() => $"Flip penalty: {player} bought {purchase.Count} x {Name(purchase.Tpl)}, recent weight now {RecentWeight(player, purchase.Tpl):0.0}");
    }

    public static void BeginListing(PmcData pmcData, AddOfferRequestData offerRequest, MongoId player)
    {
        _listing = null;
        var firstId = offerRequest.Items?.FirstOrDefault();
        var item = firstId is { } id ? pmcData.Inventory?.Items?.FirstOrDefault(i => i.Id == id) : null;
        var listed = (offerRequest.Items ?? []).Select(listedId => pmcData.Inventory?.Items?.FirstOrDefault(i => i.Id == listedId)).OfType<Item>();
        if (item is not null && listed.Any(listedItem => RecentlyBought(player, listedItem)))
        {
            _listing = (player, item.Template);
        }

        // A pack's price is for every item in it; SPT counts them the same way.
        var packUnits = offerRequest.SellInOnePiece == true
            ? (int)(offerRequest.Items ?? []).Sum(id => pmcData.Inventory!.Items!.FirstOrDefault(i => i.Id == id)?.Upd?.StackObjectsCount ?? 1)
            : 1;
        SaleDelayGate.BeginListing(item is null ? null : pmcData.Inventory!.Items!.GetItemWithChildren(item.Id), _itemHelper!, packUnits);
    }

    public static void EndListing()
    {
        _listing = null;
        SaleDelayGate.EndListing();
    }

    // A listing that waits longer because its item was just bought says so, so the wait isn't a mystery.
    public static void TellPlayer(MongoId player)
    {
        // A notice the client never picked up isn't this listing's.
        Notices.TryRemove(player, out _);
        if (_listing is null || _mail is null || _config?.Mod.PlayerSelling.SaleDelay.Enabled != true)
        {
            return;
        }

        var extra = Math.Ceiling(ExtraWaitMinutes());
        if (extra < 1 || Settings is not { } settings || _listing is not { } listing)
        {
            return;
        }

        var (bought, clearsIn) = Recent(player, listing.Tpl, settings);
        // For the client plugin to show on screen, every listing.
        Notices[player] = new FlipNotice { Tpl = listing.Tpl.ToString(), Minutes = extra, Bought = Math.Round(bought), ClearsIn = clearsIn };

        // Listing several of it one after another: one message.
        var key = $"{player}:{listing.Tpl}";
        var now = Now();
        if (Told.TryGetValue(key, out var at) && now - at < 120)
        {
            return;
        }

        Told[key] = now;
        string Minutes(double count) => $"{count:0} minute{(Math.Round(count) == 1 ? "" : "s")}";
        try
        {
            var name = SafeName(listing.Tpl);
            var stacks = Stacks(listing.Tpl) ? $" (it stacks, so any {name} you list counts, not only the ones you bought)" : "";
            _mail.SendSystemMessageToPlayer(player,
                $"Better Flea flip penalty: you bought {bought:0} {name} in the last {Minutes(settings.WindowMinutes)}{stacks}. "
                + $"Buyers will take about {Minutes(extra)} longer to get to this offer. "
                + $"The first {settings.FreePurchases:0} you buy are free; from {settings.PurchasesForMaxPenalty:0} the wait is the longest, {Minutes(settings.MaxExtraMinutes)}. "
                + (clearsIn > 0 ? $"Listed in about {Minutes(clearsIn)}, there'd be no wait. " : "")
                + "It's there to put off buying to resell straight away.", null);
        }
        catch (Exception ex)
        {
            DebugLog.Write(() => $"Flip penalty: couldn't send the message ({ex.Message})");
        }
    }

    // Player and item -> when they were last told about the penalty.
    private static readonly ConcurrentDictionary<string, long> Told = new();

    // The penalty on each player's last listing, until the client plugin asks for it.
    private static readonly ConcurrentDictionary<MongoId, FlipNotice> Notices = new();

    public static FlipNotice? TakeNotice(MongoId player) => Notices.TryRemove(player, out var notice) ? notice : null;

    // The game's own name for the item (English), or its database name.
    private static string SafeName(MongoId tpl)
    {
        try
        {
            return _itemHelper?.GetItemName(tpl) is { Length: > 0 } name ? name : Name(tpl);
        }
        catch (Exception)
        {
            return Name(tpl);
        }
    }

    // How many of the item the player bought within the window (Fence's counted as fenceMultiplier each), and in how
    // many minutes the penalty would be gone.
    private static (double Bought, int ClearsIn) Recent(MongoId player, MongoId tpl, FlipPenaltyConfig settings)
    {
        if (!History.TryGetValue(player, out var byItem) || !byItem.TryGetValue(tpl, out var list))
        {
            return (0, 0);
        }

        var window = settings.WindowMinutes * 60.0;
        var now = Now();
        List<Purchase> recent;
        lock (list)
        {
            recent = list.Where(p => now - p.At < window).ToList();
        }

        double WeightIn(int minutes) => recent.Sum(p => p.Count * Math.Max(0, 1.0 - (now + minutes * 60 - p.At) / window));
        var clears = 0;
        while (clears < settings.WindowMinutes && WeightIn(clears) > settings.FreePurchases)
        {
            clears++;
        }

        return (recent.Sum(p => p.Count), clears);
    }

    public static double ExtraWaitMinutes()
    {
        if (_listing is not { } listing || Settings is not { } settings)
        {
            return 0;
        }

        var weight = RecentWeight(listing.Player, listing.Tpl);
        var extra = ShareOfMax(settings, weight) * settings.MaxExtraMinutes;
        if (extra > 0)
        {
            DebugLog.Write(() => $"Flip penalty: {Name(listing.Tpl)} listed after buying {weight:0.0} recently, +{extra:0} min before the first buyer");
        }

        return extra;
    }

    // Fades from full to nothing over the window.
    private static double RecentWeight(MongoId player, MongoId tpl)
    {
        if (Settings is not { } settings
            || !History.TryGetValue(player, out var byItem)
            || !byItem.TryGetValue(tpl, out var list))
        {
            return 0;
        }

        var window = settings.WindowMinutes * 60.0;
        var now = Now();
        lock (list)
        {
            list.RemoveAll(p => now - p.At >= window);
            return list.Sum(p => p.Count * (1.0 - (now - p.At) / window));
        }
    }

    // A stack can't be told apart from one bought, so it counts as bought.
    private static bool RecentlyBought(MongoId player, Item item)
    {
        if (Stacks(item.Template))
        {
            return true;
        }

        var window = (Settings?.WindowMinutes ?? 0) * 60.0;
        return BoughtItems.TryGetValue(player, out var bought)
            && bought.TryGetValue(item.Id.ToString(), out var at)
            && Now() - at < window;
    }

    private static bool Stacks(MongoId tpl) =>
        _itemHelper is not null
        && ((_itemHelper.GetItem(tpl).Value?.Properties?.StackMaxSize ?? 1) > 1 || _itemHelper.IsOfBaseclass(tpl, AmmoBox));

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string Name(MongoId tpl) => _itemHelper?.GetItem(tpl).Value?.Name ?? tpl.ToString();

    internal static double ShareOfMax(FlipPenaltyConfig settings, double weight)
    {
        var over = weight - settings.FreePurchases;
        return over <= 0 ? 0 : Math.Min(1.0, over / (settings.PurchasesForMaxPenalty - settings.FreePurchases));
    }
}

public class RecordPurchasePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(TradeHelper).GetMethod(
            nameof(TradeHelper.BuyItem),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix(PmcData pmcData, ProcessBuyTradeRequestData buyRequestData)
    {
        FlipPenaltyGate.BeforePurchase(pmcData, buyRequestData);
    }

    [PatchPostfix]
    public static void Postfix(PmcData pmcData, MongoId sessionId, ItemEventRouterResponse output)
    {
        FlipPenaltyGate.AfterPurchase(pmcData, sessionId, output);
    }
}

public class ListingContextPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairController).GetMethod(
            nameof(RagfairController.AddPlayerOffer),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix(PmcData pmcData, AddOfferRequestData offerRequest, MongoId sessionID)
    {
        FlipPenaltyGate.BeginListing(pmcData, offerRequest, sessionID);
    }

    [PatchPostfix]
    public static void Postfix(PmcData pmcData, MongoId sessionID, ItemEventRouterResponse __result)
    {
        try
        {
            var (ratio, lowest) = SaleDelayGate.Outcome;
            Undercuts.AfterListing(sessionID, pmcData.RagfairInfo?.Offers, __result, ratio, lowest);
            if (__result?.Warnings is not { Count: > 0 })
            {
                FlipPenaltyGate.TellPlayer(sessionID);
            }
        }
        finally
        {
            FlipPenaltyGate.EndListing();
        }
    }
}
