using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Services.Ragfair;
using SPTarkov.Server.Core.Utils;

namespace BetterFlea.Offers;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class OfferLifecycleLoad(
    ConfigManager configManager,
    ItemHelper itemHelper,
    RagfairPriceService ragfairPriceService,
    RagfairOfferService ragfairOfferService,
    RagfairOfferHolder ragfairOfferHolder) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        OfferLifecycle.Bind(configManager, itemHelper, ragfairPriceService, ragfairOfferService, ragfairOfferHolder);
        new OfferListedTimePatch().Enable();
        return Task.CompletedTask;
    }
}

// Offers last up to 24h, are found partway through, and come off early when bought.
// Good deals are mostly fresh and sell fast; overpriced ones have been sitting there.
internal static class OfferLifecycle
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static RagfairPriceService? _prices;
    private static RagfairOfferService? _offers;
    private static RagfairOfferHolder? _holder;
    private static Timer? _buyers;
    private static int _sold, _dealsSold;

    // Offers other buyers have bought since the server started, for the status page.
    public static int Sold => _sold;
    public static int DealsSold => _dealsSold;

    private const double DealRange = 0.2;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, RagfairPriceService prices, RagfairOfferService offers, RagfairOfferHolder holder)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _prices = prices;
        _offers = offers;
        _holder = holder;
        var every = BuyerInterval();
        _buyers ??= new Timer(_ => BuyersComeRound(), null, every, every);
    }

    private static TimeSpan BuyerInterval() => TimeSpan.FromMinutes(Math.Max(1, _config!.Mod.GeneratedOffers.SimulatedSales.EveryMinutes));

    public static void SettingsChanged()
    {
        _buyers?.Change(BuyerInterval(), BuyerInterval());
        ForgetSellers();
    }

    // Under 1 = good deal, over 1 = overpriced, against the normal price for its condition.
    public static double DealRatio(RagfairOffer offer)
    {
        if (offer.Items is not { Count: > 0 } items || offer.RequirementsCost is not { } price || price <= 0)
        {
            return 1;
        }

        var normal = BetterFlea.Prices.GunPricing.Applies(items) ? BetterFlea.Prices.GunPricing.Value(items) : items.Sum(item => _prices!.GetFleaPriceForItem(item.Template));
        var quality = items.Min(item => UsedListingsGate.IgnoresCondition(item.Template) ? 1.0 : Math.Clamp(_itemHelper!.GetItemQualityModifier(item), 0.01, 1));

        // Where the wipe has moved an item's price, that price is normal for buyers too.
        normal *= BetterFlea.Prices.WipeEconomy.Factor(items[0].Template);
        return normal <= 0 ? 1 : price / (normal * quality);
    }

    // -1 best deal, 0 normal, 1 most overpriced.
    private static double DealLean(RagfairOffer offer) => Math.Clamp((DealRatio(offer) - 1) / DealRange, -1, 1);

    public static void SetListedTime(RagfairOffer offer, bool justListed)
    {
        var expiry = _config!.Mod.GeneratedOffers.OfferExpiry;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var low = Math.Max(10, expiry.MinMinutes) * 60.0;
        var duration = (long)(low + Random.Shared.NextDouble() * Math.Max(0, Math.Max(expiry.MaxMinutes * 60.0, low) - low));

        var lean = DealLean(offer);
        var roll = Random.Shared.NextDouble();
        var through = justListed ? 0 : lean < 0 ? Math.Pow(roll, 1 + 3 * -lean) : 1 - Math.Pow(roll, 1 + 3 * lean);

        // At least a minute old ("now" shows as not available yet), at least five minutes left.
        var age = (long)Math.Clamp(through * duration, 60 + Random.Shared.Next(240), Math.Max(60, duration - 300));
        offer.StartTime = now - age;
        offer.EndTime = offer.StartTime + duration;
    }

    // One seller per name: the same name shows the same rating and badge on every offer. Rolled from the name, so
    // a restart gives them the same again; rolled anew only when the wipe level (which ratings grow with) moves.
    private sealed record Seller(int WipeLevel, double? Rating, MemberCategory Badge);

    private static readonly ConcurrentDictionary<string, Seller> Sellers = new(StringComparer.Ordinal);

    public static void ForgetSellers() => Sellers.Clear();

    public static void SetSeller(RagfairOffer offer)
    {
        if (offer.User is not { } user)
        {
            return;
        }

        var wipeLevel = BetterFlea.Prices.WipeEconomy.Level > 0 ? BetterFlea.Prices.WipeEconomy.Level : 15;
        var name = user.Nickname ?? "";
        if (!Sellers.TryGetValue(name, out var seller) || seller.WipeLevel != wipeLevel)
        {
            var roll = new Random(StableHash(name));
            var badge = RollBadge(roll);
            seller = new Seller(wipeLevel, RollRating(roll, wipeLevel, Math.Max(1, offer.Quantity)), badge);
            Sellers[name] = seller;
        }

        if (seller.Rating is { } rating)
        {
            user.Rating = rating;
        }

        // SPT's own flea level gate only hides Default sellers; LevelGatePatch hides badged ones too.
        if (seller.Badge != MemberCategory.Default)
        {
            user.MemberType = seller.Badge;
            user.SelectedMemberCategory = seller.Badge;
        }
    }

    // Never Trader, System, Group or UnitTest: the game treats those offers differently.
    private static readonly MemberCategory[] SpecialBadges = [MemberCategory.Developer, MemberCategory.Sherpa, MemberCategory.Emissary];

    private static MemberCategory RollBadge(Random roll)
    {
        var settings = _config!.Mod.GeneratedOffers.SellerBadges;
        var dice = roll.NextDouble();
        var pick = roll.Next(SpecialBadges.Length);
        if (!settings.Enabled)
        {
            return MemberCategory.Default;
        }

        var special = Math.Clamp(settings.SpecialChance, 0, 1);
        var eod = Math.Clamp(settings.EdgeOfDarknessChance, 0, 1);
        var unheard = Math.Clamp(settings.UnheardChance, 0, 1);
        return dice < special ? SpecialBadges[pick]
            : dice < special + eod ? MemberCategory.UniqueId
            : dice < special + eod + unheard ? MemberCategory.Unheard
            : MemberCategory.Default;
    }

    // Mostly everyday players with low ratings, some regulars up to max. Bulk sellers are mostly regulars.
    // Null leaves SPT's.
    private static double? RollRating(Random roll, int wipeLevel, int quantity)
    {
        var settings = _config!.Mod.GeneratedOffers.SellerRating;
        if (!settings.Enabled)
        {
            return null;
        }

        // Sellers' ratings grow with the wipe: up to level 15 most are new to the flea, by level 50 few are. Goes by
        // the highest level of the players online (15 when nobody is).
        var t = Math.Clamp((wipeLevel - 15) / 35.0, 0, 1);
        var grown = t * t * (3 - 2 * t);

        // Always the same number of rolls, so a name's place in each range stays put as the wipe goes on.
        var (newRoll, regularRoll, shareRoll) = (roll.NextDouble(), roll.NextDouble(), roll.NextDouble());
        if (quantity == 1 && newRoll < settings.NewSellerChance * (1 - 0.85 * grown))
        {
            return Math.Round(-2 + shareRoll * (Math.Max(settings.Min, -2) + 2), 2);
        }

        // Someone with several of one item is usually a flipper with a long flea history. Not always.
        var regularChance = quantity == 1 ? settings.RegularChance : Math.Min(0.9, settings.FlipperChance + 0.03 * (quantity - 2));
        regularChance += (0.5 - regularChance) * 0.6 * grown;
        var regular = regularRoll < regularChance;
        var everydayMax = Math.Max(settings.Min, Math.Min(settings.EverydayMax * (1 + grown), settings.Max));
        var everydayMin = settings.Min + (Math.Min(5, everydayMax) - settings.Min) * grown;
        var (low, high, bias) = regular
            ? (Math.Min(20, settings.Max), settings.Max, 1.5 - 0.4 * grown)
            : (everydayMin, everydayMax, 1.3 - 0.5 * grown);
        low = Math.Max(0.01, low);
        high = Math.Max(low, high);
        var share = Math.Pow(shareRoll, bias);
        return Math.Round(Math.Exp(Math.Log(low) + share * (Math.Log(high) - Math.Log(low))), 2);
    }

    // string.GetHashCode changes every run.
    private static int StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)hash;
    }

    private static void BuyersComeRound()
    {
        try
        {
            var settings = _config!.Mod.GeneratedOffers.SimulatedSales;
            if (!settings.Enabled)
            {
                return;
            }

            var hours = Math.Max(1, settings.EveryMinutes) / 60.0;
            var onDemand = OnDemandOffers.Enabled;
            int sold = 0, deals = 0;
            foreach (var offer in _offers!.GetOffers())
            {
                if (!offer.IsFakePlayerOffer())
                {
                    continue;
                }

                var lean = DealLean(offer);
                var perHour = Math.Clamp(settings.NormalSellChancePerHour * Math.Pow(Math.Max(1, settings.DealSpeedup), -lean), 0, 0.95);
                if (Random.Shared.NextDouble() >= 1 - Math.Pow(1 - perHour, hours))
                {
                    continue;
                }

                // On demand: comes back next time someone looks. Otherwise SPT replaces it like an expired one.
                if (onDemand)
                {
                    _offers.RemoveOfferById(offer.Id);
                }
                else
                {
                    _holder!.FlagOfferAsExpired(offer.Id);
                }

                sold++;
                deals += lean < -0.25 ? 1 : 0;
            }

            Interlocked.Add(ref _sold, sold);
            Interlocked.Add(ref _dealsSold, deals);
            if (sold > 0)
            {
                DebugLog.Summary(() => $"Buyers: {sold} offer(s) sold in the last {settings.EveryMinutes:0.#} min ({deals} of them good deals).");
            }
        }
        catch (Exception exception)
        {
            DebugLog.Summary(() => $"Buyers: pass failed: {exception.GetBaseException().Message}");
        }
    }
}

public class OfferListedTimePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairOfferGenerator).GetMethod(nameof(RagfairOfferGenerator.CreateAndAddFleaOffer), BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(CreateFleaOfferDetails details, RagfairOffer __result)
    {
        if (details.Creator == OfferCreator.FakePlayer && __result is not null)
        {
            OfferLifecycle.SetListedTime(__result, justListed: ListingCountGate.NoCap);
            OfferLifecycle.SetSeller(__result);
        }
    }
}

[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public class BarterOffersLoad(
    ConfigManager configManager,
    ItemHelper itemHelper,
    RagfairServerHelper ragfairServerHelper,
    RagfairPriceService ragfairPriceService,
    TemplateTable templateTable,
    RagfairConfig ragfairConfig) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        BarterOffers.Bind(configManager, itemHelper, ragfairServerHelper, ragfairPriceService, templateTable, ragfairConfig);

        // SPT's barter chance is a whole percent, so under 1% never fires. We roll it ourselves in
        // MaybeInsteadOfMoney, and replace SPT's barter pick in case its own switch is on.
        new BarterSchemePatch().Enable();
        return Task.CompletedTask;
    }
}

// SPT picks any item near the value, guns and helmets included. People trade barter junk instead.
internal static class BarterOffers
{
    private static ItemHelper? _itemHelper;
    private static RagfairServerHelper? _ragfair;
    private static RagfairPriceService? _prices;
    private static TemplateTable? _templates;
    private static ConfigManager? _config;
    private static int _logged;

    private static readonly MongoId BarterItemBase = new("5448eb774bdc2d0a728b4567");

    private static RagfairConfig? _ragfairConfig;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, RagfairServerHelper ragfair, RagfairPriceService prices, TemplateTable templates,
        RagfairConfig ragfairConfig)
    {
        _config = configManager;
        _ragfairConfig = ragfairConfig;
        _itemHelper = itemHelper;
        _ragfair = ragfair;
        _prices = prices;
        _templates = templates;
    }

    private static BarterOffersConfig Settings => _config!.Mod.GeneratedOffers.BarterOffers;

    // Every barter item has CanRequireOnRagfair off, so that flag is ignored (SPT ignores it too).
    // Cheapest first.
    private static readonly Lazy<List<(MongoId Tpl, double Price)>> Wanted = new(() =>
        _templates!.Items.Values
            .Where(item => item.Type == "Item" && _itemHelper!.IsOfBaseclass(item.Id, BarterItemBase)
                && item.Properties is { } props && props.QuestItem != true
                && props.RarityPvE is "Common" or "Rare" && _ragfair!.IsItemValidRagfairItem(_itemHelper.GetItem(item.Id)))
            .Select(item => (item.Id, Price: _prices!.GetFleaPriceForItem(item.Id)))
            .Where(entry => entry.Price > 0)
            .OrderBy(entry => entry.Price)
            .ToList());

    // Null = leave it to SPT.
    public static List<BarterScheme>? SchemeFor(List<Item> offerItems, BarterDetails barterConfig)
    {
        var price = _prices!.GetDynamicOfferPriceForOffer(offerItems, Money.ROUBLES, false);

        if (price < barterConfig.MinRoubleCostToBecomeBarter || Wanted.Value.Count == 0)
        {
            return null;
        }

        var low = Math.Clamp(Settings.ValueMin, 0.1, 3);
        var target = price * (low + Random.Shared.NextDouble() * Math.Max(0, Settings.ValueMax - low));
        var kinds = Random.Shared.NextDouble() < 0.65 ? 1 : 2;
        var root = offerItems.FirstOrDefault()?.Template;
        var scheme = new List<BarterScheme>();
        foreach (var share in kinds == 1 ? new[] { target } : [target * 0.6, target * 0.4])
        {
            // 1 to 5 of something, otherwise several of the dearest thing under the share.
            var options = Wanted.Value.Where(entry => entry.Tpl != root && scheme.All(barter => barter.Template != entry.Tpl)
                && entry.Price >= share / 5 && entry.Price <= share * 1.1).ToList();
            if (options.Count == 0)
            {
                options = Wanted.Value.Where(entry => entry.Tpl != root && entry.Price <= share).TakeLast(5).ToList();
            }

            if (options.Count == 0)
            {
                continue;
            }

            var (tpl, each) = options[Random.Shared.Next(options.Count)];
            scheme.Add(new BarterScheme { Template = tpl, Count = Math.Clamp(Math.Round(share / each), 1, 20) });
        }

        if (scheme.Count == 0)
        {
            return null;
        }

        if (Interlocked.Increment(ref _logged) <= 10)
        {
            DebugLog.Summary(() => $"Barter offer: {_itemHelper!.GetItem(root ?? default).Value?.Name} ({price:0} RUB) for "
                + string.Join(" + ", scheme.Select(barter => $"{barter.Count:0} x {_itemHelper.GetItem(barter.Template).Value?.Name}"))
                + $" (worth {scheme.Sum(barter => barter.Count!.Value * _prices.GetFleaPriceForItem(barter.Template)):0} RUB)");
        }

        return scheme;
    }

    public static bool MaybeInsteadOfMoney(CreateFleaOfferDetails details)
    {
        if (details.Creator != OfferCreator.FakePlayer || details.SellInOnePiece || details.Items is not { Count: > 0 } items
            || details.BarterScheme is not { Count: 1 } current || !_itemHelper!.IsOfBaseclass(current[0].Template, BaseClasses.MONEY)
            || Settings.Chance <= 0 || Random.Shared.NextDouble() >= Settings.Chance
            || SchemeFor(items, _ragfairConfig!.Dynamic.Barter) is not { } scheme)
        {
            return false;
        }

        details.BarterScheme = scheme;
        return true;
    }
}

public class BarterSchemePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairOfferGenerator).GetMethod("CreateBarterBarterScheme", BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPrefix]
    public static bool Prefix(IEnumerable<Item> offerItems, BarterDetails barterConfig, ref List<BarterScheme> __result)
    {
        if (BarterOffers.SchemeFor(offerItems as List<Item> ?? offerItems.ToList(), barterConfig) is not { } scheme)
        {
            return true;
        }

        __result = scheme;
        return false;
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class SoldBeforeYouPatchesLoad(
    ConfigManager configManager,
    RagfairOfferService ragfairOfferService,
    EventOutputHolder eventOutputHolder,
    HttpResponseUtil httpResponseUtil,
    LocaleService localeService,
    ItemHelper itemHelper) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        SoldBeforeYou.Bind(configManager, ragfairOfferService, eventOutputHolder, httpResponseUtil, localeService, itemHelper);
        new SoldBeforeYouPatch().Enable();
        return Task.CompletedTask;
    }
}

// Now and then someone else gets there first. Guns and barter items from generated sellers only.
internal static class SoldBeforeYou
{
    private static ConfigManager? _config;
    private static RagfairOfferService? _offers;
    private static EventOutputHolder? _output;
    private static HttpResponseUtil? _responses;
    private static LocaleService? _locales;
    private static ItemHelper? _itemHelper;
    private static int _sniped;

    public static int Sniped => _sniped;

    private static readonly MongoId GunBase = new("5422acb9af1c889c16000029");

    public static void Bind(ConfigManager configManager, RagfairOfferService ragfairOfferService, EventOutputHolder eventOutputHolder,
        HttpResponseUtil httpResponseUtil, LocaleService localeService, ItemHelper itemHelper)
    {
        _config = configManager;
        _offers = ragfairOfferService;
        _output = eventOutputHolder;
        _responses = httpResponseUtil;
        _locales = localeService;
        _itemHelper = itemHelper;
    }

    public static ItemEventRouterResponse? BeforePurchase(ProcessRagfairTradeRequestData request, MongoId sessionId)
    {
        if (request.Offers is null)
        {
            return null;
        }

        // Already gone (sold or expired while the list was open).
        if (request.Offers.Any(wanted => wanted.Id is { } id && _offers!.GetOfferByOfferId(id) is null))
        {
            return _responses!.AppendErrorToOutput(_output!.GetOutput(sessionId), SoldMessage(), BackendErrorCodes.OfferNotFound);
        }

        foreach (var wanted in request.Offers)
        {
            if (wanted.Id is { } id && _offers!.GetOfferByOfferId(id) is { } offer && Flea.LevelLock.BuyRefusal(sessionId, offer) is { } tooLow)
            {
                return _responses!.AppendErrorToOutput(_output!.GetOutput(sessionId), tooLow);
            }
        }

        var chance = _config?.Mod.GeneratedOffers.SoldBeforeYouChance ?? 0;
        if (chance <= 0)
        {
            return null;
        }

        foreach (var wanted in request.Offers)
        {
            if (wanted.Id is not { } offerId || _offers!.GetOfferByOfferId(offerId) is not { } offer
                || offer.User?.MemberType == MemberCategory.Trader || offer.Items?.FirstOrDefault() is not { } root
                || !_itemHelper!.IsOfBaseclasses(root.Template, [GunBase, BaseClasses.BARTER_ITEM])
                || Random.Shared.NextDouble() >= chance)
            {
                continue;
            }

            _offers.RemoveOfferById(offerId);
            var count = Interlocked.Increment(ref _sniped);
            DebugLog.Summary(() => $"Sold before you: {_itemHelper.GetItem(root.Template).Value?.Name} offer {offerId} went to someone else ({count} so far).");
            // Not the OfferSold code: the client kicks to the menu on that one.
            return _responses!.AppendErrorToOutput(_output!.GetOutput(sessionId), SoldMessage(), BackendErrorCodes.OfferNotFound);
        }

        return null;
    }

    private static string SoldMessage()
    {
        const string key = "EBackendErrorCode/OfferSold";
        try
        {
            if (_locales!.GetLocaleDb(_locales.GetDesiredGameLocale()) is { } locale && locale.TryGetValue(key, out var text))
            {
                return text;
            }

            if (_locales.GetLocaleDb("en") is { } english && english.TryGetValue(key, out var englishText))
            {
                return englishText;
            }
        }
        catch (Exception)
        {
        }

        return "Offer is already sold";
    }
}

public class SoldBeforeYouPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(TradeController).GetMethod(
            nameof(TradeController.ConfirmRagfairTrading),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    // By position so it doesn't depend on SPT's parameter names.
    [PatchPrefix]
    public static bool Prefix(ProcessRagfairTradeRequestData __1, MongoId __2, ref ItemEventRouterResponse __result)
    {
        if (SoldBeforeYou.BeforePurchase(__1, __2) is not { } refusal)
        {
            return true;
        }

        __result = refusal;
        return false;
    }
}
