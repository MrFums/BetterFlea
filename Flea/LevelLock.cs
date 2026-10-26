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
        LevelGatePatch.Globals = globals;
        new LevelLockPatch().Enable();
        new LevelGatePatch().Enable();
        return Task.CompletedTask;
    }
}

// Like the real flea: some items can only be bought from a certain level (the M4A1 at 25, a graphics card at 40).
// The levels are the real game's (tarkov.dev), shipped with the mod and updated whenever live prices are fetched.
// A modded item gets the level most base-game items of its kind have.
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
    private static readonly Dictionary<MongoId, int> KindLevels = new();

    private static LevelLockConfig? Settings => _config?.Mod.Flea.LevelLock is { Enabled: true } settings ? settings : null;

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
        lock (KindLevels)
        {
            KindLevels.Clear();
        }

        _levels = new Lazy<Dictionary<MongoId, int>>(Build);
    }

    // 0 = no level needed beyond the flea's own.
    public static int LevelFor(MongoId tpl)
    {
        if (_levels.Value.TryGetValue(tpl, out var level))
        {
            return level;
        }

        if (Settings is not { ModdedItems: true } || ModItems.IsBaseItem(tpl) ||_templates?.Items.GetValueOrDefault(tpl)?.Parent is not { } kind)
        {
            return 0;
        }

        lock (KindLevels)
        {
            if (!KindLevels.TryGetValue(kind, out level))
            {
                KindLevels[kind] = level = UsualLevel(kind);
            }

            return level;
        }
    }

    // The level most base-game items of this kind have (0 counts too).
    private static int UsualLevel(MongoId kind)
    {
        var levels = _levels.Value;
        var counts = _templates!.Items.Where(entry => entry.Value.Parent == kind && ModItems.IsBaseItem(entry.Key))
            .GroupBy(entry => levels.GetValueOrDefault(entry.Key))
            .Select(group => (Level: group.Key, Count: group.Count()))
            .ToList();
        return counts.Count == 0 ? 0 : counts.MaxBy(entry => (entry.Count, entry.Level)).Level;
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

        DebugLog.Summary(() => $"Level lock: {levels.Count} items need a level to buy ({(_live is null ? "shipped list" : "tarkov.dev")}).");
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
