using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Flea;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class LevelLockLoad(ConfigManager configManager, ProfileHelper profileHelper, TemplateTable templates, GlobalTable globals) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        LevelLock.Bind(configManager, profileHelper, templates);
        HandbookTree.Bind(templates);
        LevelGatePatch.Globals = globals;
        new LevelLockPatch().Enable();
        new LevelGatePatch().Enable();
        return Task.CompletedTask;
    }
}

// Like the real flea: some items can only be bought from a certain level (the M4A1 at 25, a graphics card at 40).
// The levels are the real game's (tarkov.dev), shipped with the mod and updated whenever live prices are fetched.
// A modded item gets the level of the base-game items of its kind worth about the same. Locked items can't be
// listed for sale either (optional).
//
// SPT has this built in (ragfair "tieredFlea"), but that copies every offer on the flea for every search. Here only
// the page of offers a player is shown is looked at, and only a locked offer is copied (it gets marked locked and
// named "Unlock level: X" for that player alone, so other players in co-op see their own).
internal static class LevelLock
{
    private static ConfigManager? _config;
    private static ProfileHelper? _profiles;
    private static TemplateTable? _templates;
    private static Dictionary<string, int>? _live;
    private static Lazy<Dictionary<MongoId, int>> _levels = new(Build);
    private static readonly Dictionary<MongoId, int> ModdedLevels = new();
    private static readonly Dictionary<MongoId, List<(double LogPrice, int Level)>> KindPeers = new();

    private static LevelLockConfig? Settings => _config?.Mod.Flea.LevelLock is { Enabled: true } settings ? settings : null;

    public static bool On => Settings is not null;

    public static bool LockSelling => Settings is { AllowSellingBelowLevel: false };

    // Changes whenever the levels the client was sent could have changed, so it only fetches them again then.
    private static int _forgets;
    public static int Version => HashCode.Combine(_forgets, On, Settings is { ModdedItems: true }, Custom().Stamp);

    // The levels the server owner set on the settings page, by item or category ID.
    private static (Dictionary<string, int>? Source, Dictionary<MongoId, int> Levels, int Stamp) _custom = (null, new(), 0);

    private static (Dictionary<string, int>? Source, Dictionary<MongoId, int> Levels, int Stamp) Custom()
    {
        var source = Settings?.Levels;
        var custom = _custom;
        if (ReferenceEquals(custom.Source, source))
        {
            return custom;
        }

        var levels = new Dictionary<MongoId, int>();
        var stamp = 0;
        foreach (var (id, level) in source ?? [])
        {
            if (MongoId.IsValidMongoId(id))
            {
                levels[new MongoId(id)] = Math.Max(0, level);
                stamp ^= HashCode.Combine(id.ToLowerInvariant(), level);
            }
        }

        return _custom = (source, levels, stamp);
    }

    // An item's own setting beats its nearest category's, and a handbook category beats an item type.
    private static int? CustomLevel(MongoId tpl)
    {
        var levels = Custom().Levels;
        if (levels.Count == 0)
        {
            return null;
        }

        if (levels.TryGetValue(tpl, out var level))
        {
            return level;
        }

        foreach (var category in HandbookTree.CategoriesOf(tpl))
        {
            if (levels.TryGetValue(category, out level))
            {
                return level;
            }
        }

        MongoId? kind = _templates?.Items.GetValueOrDefault(tpl)?.Parent;
        for (var depth = 0; depth < 10 && kind is { } current; depth++)
        {
            if (levels.TryGetValue(current, out level))
            {
                return level;
            }

            kind = _templates!.Items.GetValueOrDefault(current)?.Parent;
        }

        return null;
    }

    private static (int Version, Dictionary<string, int> Levels)? _forClient;

    public static void Bind(ConfigManager configManager, ProfileHelper profileHelper, TemplateTable templates)
    {
        _config = configManager;
        _profiles = profileHelper;
        _templates = templates;
    }

    // Newer levels from tarkov.dev (LivePrices).
    public static void UseLive(Dictionary<string, int>? levels)
    {
        if (levels is not { Count: > 500 })
        {
            return;
        }

        _live = levels;
        Forget();
    }

    public static void Forget()
    {
        lock (ModdedLevels)
        {
            ModdedLevels.Clear();
            KindPeers.Clear();
        }

        _levels = new Lazy<Dictionary<MongoId, int>>(Build);
        HandbookPrices = new Lazy<Dictionary<MongoId, double>>(ReadHandbook);
        Interlocked.Increment(ref _forgets);
    }

    public static int PlayerLevel(MongoId sessionId) => _profiles?.GetPmcProfile(sessionId)?.Info?.Level ?? 0;

    // Every flea item that needs a level, modded ones included, for the client plugin to show on items and categories.
    public static Dictionary<string, int> ForClient()
    {
        var version = Version;
        if (_forClient is { } cached && cached.Version == version)
        {
            return cached.Levels;
        }

        var levels = new Dictionary<string, int>();
        if (On && _templates?.Handbook?.Items is { } handbook)
        {
            foreach (var entry in handbook)
            {
                if (LevelFor(entry.Id) is > 0 and var level)
                {
                    levels[entry.Id.ToString()] = level;
                }
            }
        }

        _forClient = (version, levels);
        var moddedItems = _templates?.Items.Where(entry => entry.Value.Type == "Item" && !ModItems.IsBaseItem(entry.Key)).Select(entry => entry.Key).ToList() ?? [];
        var modded = moddedItems.Count;
        var lockedModded = moddedItems.Count(tpl => levels.ContainsKey(tpl.ToString()));
        DebugLog.Summary(() => $"Level lock: {levels.Count} items need a level, {lockedModded} of the {modded} modded items among them.");
        return levels;
    }

    // 0 = no level needed beyond the flea's own.
    public static int LevelFor(MongoId tpl)
    {
        if (CustomLevel(tpl) is { } custom)
        {
            return custom;
        }

        if (_levels.Value.TryGetValue(tpl, out var level))
        {
            return level;
        }

        if (Settings is not { ModdedItems: true } || ModItems.IsBaseItem(tpl) || _templates?.Items.GetValueOrDefault(tpl) is not { } item)
        {
            return 0;
        }

        lock (ModdedLevels)
        {
            if (!ModdedLevels.TryGetValue(tpl, out level))
            {
                ModdedLevels[tpl] = level = SimilarLevel(item);
                var name = item.Name;
                DebugLog.Summary(() => $"Level lock: modded {name} ({tpl}) unlocks at level {level}, like the base-game items priced closest to it.");
            }

            return level;
        }
    }

    // A modded item takes the level of the base-game items of its kind that are worth about the same, so a strong
    // modded stim unlocks with the strong real ones and a weak one with the weak ones. Price stands in for how good
    // an item is: the real flea's levels follow it closely within a kind. Too few base items of the exact kind
    // (a mod's own category) and the wider kind above it is used. Works for any mod's items: only the item's type
    // and price are looked at. An item with no price at all gets the level most items of its kind need.
    private static int SimilarLevel(TemplateItem item)
    {
        var price = PriceOf(item.Id);
        MongoId? kind = item.Parent;
        for (var depth = 0; depth < 3 && kind is { } current; depth++)
        {
            var peers = PeersOf(current);
            if (peers.Count >= 3)
            {
                if (price <= 0)
                {
                    return peers.GroupBy(peer => peer.Level).MaxBy(group => (group.Count(), group.Key))!.Key;
                }

                var target = Math.Log(price);
                var nearest = peers.OrderBy(peer => Math.Abs(peer.LogPrice - target)).Take(3).Select(peer => peer.Level).Order().ToList();
                return nearest[nearest.Count / 2];
            }

            kind = _templates!.Items.GetValueOrDefault(current)?.Parent;
        }

        return 0;
    }

    // Base-game items that can be sold on the flea, anywhere under this kind: (log price, level).
    private static List<(double LogPrice, int Level)> PeersOf(MongoId kind)
    {
        if (KindPeers.TryGetValue(kind, out var peers))
        {
            return peers;
        }

        var levels = _levels.Value;
        var items = _templates!.Items;
        peers = new List<(double, int)>();
        foreach (var (tpl, item) in items)
        {
            if (item.Type != "Item" || item.Properties?.CanSellOnRagfair == false || !ModItems.IsBaseItem(tpl) || PriceOf(tpl) is var price && price <= 0)
            {
                continue;
            }

            MongoId? parent = item.Parent;
            for (var depth = 0; depth < 4 && parent is { } current; depth++)
            {
                if (current == kind)
                {
                    peers.Add((Math.Log(price), levels.GetValueOrDefault(tpl)));
                    break;
                }

                parent = items.GetValueOrDefault(current)?.Parent;
            }
        }

        return KindPeers[kind] = peers;
    }

    // The flea price, or the handbook (trader) price for a mod that only set that.
    private static double PriceOf(MongoId tpl) =>
        _templates!.Prices.GetValueOrDefault(tpl) is > 0 and var price ? price : HandbookPrices.Value.GetValueOrDefault(tpl);

    private static Lazy<Dictionary<MongoId, double>> HandbookPrices = new(ReadHandbook);

    private static Dictionary<MongoId, double> ReadHandbook()
    {
        var prices = new Dictionary<MongoId, double>();
        foreach (var entry in _templates?.Handbook?.Items ?? [])
        {
            if (entry.Price is > 0 and var price)
            {
                prices[entry.Id] = price;
            }
        }

        return prices;
    }

    private static Dictionary<MongoId, int> Build()
    {
        var source = _live ?? Shipped();
        var levels = new Dictionary<MongoId, int>();
        foreach (var (id, level) in source)
        {
            if (level > 0 && MongoId.IsValidMongoId(id))
            {
                levels[new MongoId(id)] = level;
            }
        }

        DebugLog.Summary(() => $"Level lock: {levels.Count} items need a level to buy or sell ({(_live is null ? "shipped list" : "tarkov.dev")}).");
        return levels;
    }

    private static Dictionary<string, int> Shipped()
    {
        try
        {
            using var stream = typeof(LevelLock).Assembly.GetManifestResourceStream("BetterFlea.data.flea-levels.json");
            return stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, int>>(stream) ?? [];
        }
        catch (Exception ex)
        {
            DebugLog.Summary(() => $"Level lock: couldn't read the item levels: {ex.Message}");
            return [];
        }
    }

    // Like live, a locked item can't be listed either. Only the items listed count, not what's in or on them.
    public static string? SellRefusal(PmcData pmcData, IEnumerable<Item> listed)
    {
        if (!LockSelling || pmcData.Info?.Level is not { } playerLevel)
        {
            return null;
        }

        var level = listed.Select(item => LevelFor(item.Template)).DefaultIfEmpty().Max();
        return playerLevel >= level ? null : $"This item can only be sold at the Flea Market from level {level}.";
    }

    // The client won't buy a locked offer, but the server checks too.
    public static string? BuyRefusal(MongoId sessionId, RagfairOffer offer)
    {
        if (Settings is null || offer.IsTraderOffer() || offer.Items is not { Count: > 0 } items
            || _profiles?.GetPmcProfile(sessionId)?.Info?.Level is not { } playerLevel || LevelFor(items[0].Template) is var level && playerLevel >= level)
        {
            return null;
        }

        return $"This item can only be bought at the Flea Market from level {level}.";
    }

    public static void Apply(MongoId sessionId, SearchRequestData request, GetOffersResult result)
    {
        if (Settings is null || result.Offers is not { Count: > 0 } offers
            || _profiles?.GetPmcProfile(sessionId)?.Info?.Level is not { } playerLevel)
        {
            return;
        }

        // A build search picks one offer per part to buy at once, so a locked one is left out instead.
        var building = request.BuildCount > 0;
        for (var i = offers.Count - 1; i >= 0; i--)
        {
            var offer = offers[i];
            if (offer.IsTraderOffer() || offer.Items is not { Count: > 0 } items || LevelFor(items[0].Template) is var level && playerLevel >= level)
            {
                continue;
            }

            if (building)
            {
                offers.RemoveAt(i);
                continue;
            }

            offers[i] = offer with { Locked = true, User = offer.User with { Nickname = $"Unlock level: {level}" } };
        }
    }
}

// SPT hides player offers from anyone under the flea's level only when the seller has no badge (MemberType
// Default). Badged sellers (ours, or real players with Edge of Darkness) stay hidden too.
public class LevelGatePatch : AbstractPatch
{
    internal static GlobalTable? Globals;

    protected override MethodBase GetTargetMethod() =>
        typeof(RagfairOfferHelper).GetMethod("PassesSearchFilterCriteria", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [PatchPostfix]
    public static void Postfix(RagfairOffer offer, PmcData pmcData, ref bool __result)
    {
        if (__result && !offer.IsTraderOffer() && pmcData.Info?.Level < Globals?.Configuration?.RagFair?.MinUserLevel)
        {
            __result = false;
        }
    }
}

public class LevelLockPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() =>
        typeof(RagfairController).GetMethod(nameof(RagfairController.GetOffers), BindingFlags.Instance | BindingFlags.Public)!;

    [PatchPostfix]
    public static void Postfix(MongoId sessionID, SearchRequestData searchRequest, GetOffersResult __result)
    {
        LevelLock.Apply(sessionID, searchRequest, __result);
    }
}
