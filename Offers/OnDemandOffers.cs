using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Services.Ragfair;

namespace BetterFlea.Offers;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class OnDemandOffersLoad(
    ConfigManager configManager,
    RagfairOfferGenerator ragfairOfferGenerator,
    RagfairOfferService ragfairOfferService,
    SPTarkov.Server.Core.Helpers.Commerce.PaymentHelper paymentHelper,
    RagfairConfig ragfairConfig) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        OnDemandOffers.Bind(configManager, ragfairOfferGenerator, ragfairOfferService, ragfairConfig, paymentHelper);
        new FillOnSearchPatch().Enable();
        new FullCategoryCountsPatch().Enable();

        return Task.CompletedTask;
    }
}

// Most items are never looked at, so each starts with a share of its offers and fills up when someone opens
// its category or searches for it. Left alone for a while, expired offers stop being replaced.
internal static class OnDemandOffers
{
    private static ConfigManager? _config;
    private static RagfairOfferGenerator? _generator;
    private static RagfairOfferService? _offers;
    private static RagfairConfig? _ragfairConfig;
    private static SPTarkov.Server.Core.Helpers.Commerce.PaymentHelper? _payments;

    // Same path as startup, so all our patches apply.
    private static readonly MethodInfo CreateOffersFromAssort = typeof(RagfairOfferGenerator).GetMethod(
        "CreateOffersFromAssort", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly ConcurrentDictionary<MongoId, ConcurrentDictionary<MongoId, List<Item>>> Assorts = new();
    private static readonly ConcurrentDictionary<MongoId, int> FullCounts = new();
    private static readonly ConcurrentDictionary<MongoId, long> LastLooked = new();
    private static readonly ConcurrentDictionary<MongoId, byte> Filling = new();

    [ThreadStatic]
    private static int? _fillCount;

    private static int _logged;

    public static void Bind(ConfigManager configManager, RagfairOfferGenerator generator, RagfairOfferService offers, RagfairConfig ragfairConfig,
        SPTarkov.Server.Core.Helpers.Commerce.PaymentHelper payments)
    {
        _payments = payments;
        _config = configManager;
        _generator = generator;
        _offers = offers;
        _ragfairConfig = ragfairConfig;
        if (Enabled)
        {
            var every = TimeSpan.FromSeconds(Math.Clamp(Settings.KeepSeconds / 4, 5, 60));
            _trimTimer ??= new Timer(_ => TrimUnwatched(), null, every, every);
        }
    }

    private static Timer? _trimTimer;

    // Full counts are kept: forgetting them without remaking the offers stopped every item filling up. Saving a
    // file that changes them (listings.jsonc) remakes the flea, which forgets them anyway (FleaRefresh, Forget).
    public static void SettingsChanged()
    {
        if (_trimTimer is not null)
        {
            var every = TimeSpan.FromSeconds(Math.Clamp(Settings.KeepSeconds / 4, 5, 60));
            _trimTimer.Change(every, every);
        }
    }

    private static readonly object TrimLock = new();

    // Never trimmed (lost guns).
    private static readonly ConcurrentDictionary<MongoId, byte> Kept = new();

    public static void Keep(MongoId offerId) => Kept[offerId] = 0;

    public static bool IsKept(MongoId offerId) => Kept.ContainsKey(offerId);

    // For the status page: offers every item would have in full, and items with their full list made right now.
    public static (int Full, int Items, int Open) Stats() => (FullCounts.Values.Sum(), FullCounts.Count, LastLooked.Keys.Count(IsLookedAt));

    // Before the whole flea is made again: forget everything about the old offers.
    public static void Forget()
    {
        lock (TrimLock)
        {
            FullCounts.Clear();
            Assorts.Clear();
            LastLooked.Clear();
            RecentPages.Clear();
        }
    }

    private static List<RagfairOffer> Trimmable(MongoId tpl) =>
        _offers!.GetOffersOfType(tpl)?.Where(offer => offer.IsFakePlayerOffer() && !Kept.ContainsKey(offer.Id)).ToList() ?? [];

    private static int ExtraOffers(MongoId tpl) =>
        FullCounts.TryGetValue(tpl, out var full) ? Math.Max(0, Trimmable(tpl).Count - StartCount(full)) : 0;

    // Back to the start share. Returns how many offers went.
    private static int Trim(MongoId tpl)
    {
        var removed = 0;
        if (FullCounts.TryGetValue(tpl, out var full))
        {
            foreach (var offer in Trimmable(tpl).Skip(StartCount(full)))
            {
                _offers!.RemoveOfferById(offer.Id);
                removed++;
            }
        }

        LastLooked.TryRemove(tpl, out _);
        return removed;
    }

    private static void TrimUnwatched()
    {
        try
        {
            var removed = 0;
            lock (TrimLock)
            {
                // Never the list on screen now, however long it's been open.
                var current = RecentPages.First?.Value;
                foreach (var tpl in LastLooked.Keys)
                {
                    if (!IsLookedAt(tpl) && !Filling.ContainsKey(tpl) && current?.Contains(tpl) != true)
                    {
                        removed += Trim(tpl);
                    }
                }
            }

            if (removed > 0)
            {
                DebugLog.Summary(() => $"On-demand offers: removed {removed} extra offer(s) of items nobody has looked at lately.");
                TidyMemorySoon();
            }
        }
        catch (Exception exception)
        {
            DebugLog.Summary(() => $"On-demand offers: trim failed: {exception.Message}");
        }
    }

    private static OnDemandOffersConfig Settings => _config!.Mod.GeneratedOffers.Listings.OnDemand;

    public static bool Enabled => _config?.Mod.GeneratedOffers.Listings.OnDemand.Enabled == true;

    private static int StartCount(int full) =>
        Math.Min(full, Math.Max(Settings.MinStartOffers, (int)Math.Ceiling(full * Settings.StartShare)));

    private static bool IsLookedAt(MongoId tpl) =>
        LastLooked.TryGetValue(tpl, out var looked)
        && Stopwatch.GetElapsedTime(looked).TotalSeconds < Settings.KeepSeconds;

    public static bool TieredFleaOn => _ragfairConfig?.TieredFlea.Enabled == true;

    public static IEnumerable<RagfairOffer> OffersOf(HashSet<MongoId> items) =>
        items.SelectMany(tpl => _offers!.GetOffersOfType(tpl) ?? []);

    private static int FakeOffers(MongoId tpl) => _offers!.GetOffersOfType(tpl)?.Count(offer => offer.IsFakePlayerOffer()) ?? 0;

    // SPT rerolls the count every time it asks; keep the first so the fill and AddOffer's cap agree.
    public static int FullCount(MongoId tpl, int rolled) => Enabled ? FullCounts.GetOrAdd(tpl, rolled) : rolled;

    public static int CountToMake(MongoId tpl, int full)
    {
        if (!Enabled)
        {
            return full;
        }

        if (_fillCount is { } fill)
        {
            return Math.Min(fill, full);
        }

        return IsLookedAt(tpl) ? full : StartCount(full);
    }

    public static void Remember(MongoId tpl, List<Item> assort)
    {
        if (Enabled && assort.FirstOrDefault()?.Id is { } rootId)
        {
            Assorts.GetOrAdd(tpl, _ => new ConcurrentDictionary<MongoId, List<Item>>()).TryAdd(rootId, assort);
        }
    }

    public static bool ShouldReplaceExpired(MongoId tpl)
    {
        if (!Enabled || IsLookedAt(tpl) || !FullCounts.TryGetValue(tpl, out var full))
        {
            return true;
        }

        return FakeOffers(tpl) < StartCount(full);
    }

    // Waits up to FillWait, then shows what's there while the rest builds in the background.
    // Otherwise the game greys the list out while guns are being built.
    public static void FillUp(HashSet<MongoId> items)
    {
        if (!Enabled || items.Count == 0 || items.Count > Settings.MaxItemsPerSearch)
        {
            return;
        }

        RememberPage(items);

        var now = Stopwatch.GetTimestamp();
        var work = new List<(MongoId Tpl, List<Item> Assort, int Count)>();
        var filling = new List<MongoId>();
        foreach (var tpl in items)
        {
            LastLooked[tpl] = now;
            if (!FullCounts.TryGetValue(tpl, out var full) || !Assorts.TryGetValue(tpl, out var byRoot) || byRoot.IsEmpty)
            {
                continue;
            }

            var missing = full - FakeOffers(tpl);
            if (missing <= 0 || !Filling.TryAdd(tpl, 0))
            {
                continue;
            }

            // A gun has one assort entry per preset.
            filling.Add(tpl);
            var assorts = byRoot.Values.ToList();
            for (var i = 0; i < assorts.Count; i++)
            {
                var count = missing / assorts.Count + (i < missing % assorts.Count ? 1 : 0);
                if (count > 0)
                {
                    work.Add((tpl, assorts[i], count));
                }
            }
        }

        if (work.Count == 0)
        {
            return;
        }

        MakeRoom(work.Sum(job => job.Count), items);
        var fill = Task.Run(() => Fill(work, filling));
        fill.Wait(FillWait);
    }

    // Newest first. Only touched under TrimLock.
    private static readonly LinkedList<HashSet<MongoId>> RecentPages = new();

    private static bool OnRecentPage(MongoId tpl)
    {
        if (Settings.KeepPages <= 0)
        {
            return true;
        }

        lock (TrimLock)
        {
            return RecentPages.Any(page => page.Contains(tpl));
        }
    }

    // Only the last keepPages lists stay filled; opening another trims the oldest (items still on a newer
    // list stay). Paging through the same list doesn't count.
    private static void RememberPage(HashSet<MongoId> items)
    {
        if (Settings.KeepPages <= 0)
        {
            return;
        }

        var removed = 0;
        lock (TrimLock)
        {
            if (RecentPages.First?.Value.SetEquals(items) == true)
            {
                return;
            }

            RecentPages.AddFirst(new HashSet<MongoId>(items));
            while (RecentPages.Count > Settings.KeepPages)
            {
                var oldest = RecentPages.Last!.Value;
                RecentPages.RemoveLast();
                foreach (var tpl in oldest)
                {
                    if (!Filling.ContainsKey(tpl) && !RecentPages.Any(page => page.Contains(tpl)))
                    {
                        removed += Trim(tpl);
                    }
                }
            }
        }

        if (removed > 0)
        {
            DebugLog.Summary(() => $"On-demand offers: removed {removed} offer(s) of the oldest list opened.");
            TidyMemorySoon();
        }
    }

    // Caps the on-demand offers at maxExtraOffers, trimming whatever was looked at longest ago.
    private static void MakeRoom(int adding, HashSet<MongoId> searched)
    {
        var removed = 0;
        lock (TrimLock)
        {
            var extra = LastLooked.Keys.Sum(ExtraOffers);
            if (extra + adding <= Settings.MaxExtraOffers)
            {
                return;
            }

            foreach (var tpl in LastLooked.OrderBy(entry => entry.Value).Select(entry => entry.Key).ToList())
            {
                if (searched.Contains(tpl) || Filling.ContainsKey(tpl))
                {
                    continue;
                }

                var gone = Trim(tpl);
                removed += gone;
                extra -= gone;
                if (extra + adding <= Settings.MaxExtraOffers)
                {
                    break;
                }
            }
        }

        if (removed > 0)
        {
            DebugLog.Summary(() => $"On-demand offers: over maxExtraOffers, removed {removed} offer(s) of the items looked at longest ago.");
            TidyMemorySoon();
        }
    }

    private static readonly TimeSpan FillWait = TimeSpan.FromMilliseconds(60);

    private static void Fill(List<(MongoId Tpl, List<Item> Assort, int Count)> work, List<MongoId> filling)
    {
        // A few threads and one fill at a time: every thread makes the runtime reserve more memory.
        FillGate.Wait();
        var timer = Stopwatch.StartNew();
        try
        {
            // Skip lists that dropped out while this was waiting.
            work = work.Where(job => OnRecentPage(job.Tpl)).ToList();
            Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = FillThreads }, job =>
            {
                _fillCount = job.Count;
                try
                {
                    CreateOffersFromAssort.Invoke(_generator, [job.Assort, false, _ragfairConfig!.Dynamic]);
                }
                finally
                {
                    _fillCount = null;
                }
            });
        }
        catch (Exception exception)
        {
            DebugLog.Summary(() => $"On-demand offers: fill failed: {exception.GetBaseException().Message}");
        }
        finally
        {
            FillGate.Release();
            foreach (var tpl in filling)
            {
                Filling.TryRemove(tpl, out _);
            }

            // Dropped out mid-fill: trim it now.
            lock (TrimLock)
            {
                foreach (var tpl in filling.Where(tpl => !OnRecentPage(tpl)))
                {
                    Trim(tpl);
                }
            }
        }

        // Building offers leaves a lot of short-lived garbage, and SPT's garbage collector lets it pile up for a
        // while. A quick young-generation collection after each fill keeps the peak down.
        GC.Collect(1, GCCollectionMode.Forced, blocking: false);
        TidyMemorySoon();

        if (Interlocked.Increment(ref _logged) <= 40)
        {
            DebugLog.Summary(() => $"On-demand offers: filled {filling.Count} item(s) ({work.Sum(job => job.Count)} offers) in {timer.ElapsedMilliseconds} ms.");
        }
    }

    private static readonly int FillThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    private static readonly SemaphoreSlim FillGate = new(1, 1);

    private static int _tidyQueued;

    // Compact once things go quiet after a fill, at most once per TidyDelay.
    public static void TidyMemorySoon()
    {
        if (Interlocked.Exchange(ref _tidyQueued, 1) == 1)
        {
            return;
        }

        _ = Task.Delay(TidyDelay).ContinueWith(_ =>
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            Interlocked.Exchange(ref _tidyQueued, 0);
        });
    }

    private static readonly TimeSpan TidyDelay = TimeSpan.FromSeconds(10);

    private static bool IsMoneyOnly(RagfairOffer offer)
    {
        using var requirements = offer.Requirements?.GetEnumerator();
        if (requirements is null || !requirements.MoveNext())
        {
            return false;
        }

        var first = requirements.Current;
        return !requirements.MoveNext() && _payments!.IsMoneyTpl(first.TemplateId);
    }

    // Full counts, not just what's been made so far.
    public static Dictionary<MongoId, int> CountByItem(IEnumerable<RagfairOffer> offers, SearchRequestData search, bool fleaUnlocked)
    {
        var counts = new Dictionary<MongoId, int>();
        var made = new Dictionary<MongoId, int>();
        var neededSearch = search.NeededSearchId is { IsEmpty: false };
        var moneyOnly = !neededSearch && search.RemoveBartering == true;
        foreach (var offer in offers)
        {
            var isTrader = offer.IsTraderOffer();
            if (!fleaUnlocked && !isTrader
                || moneyOnly && !IsMoneyOnly(offer)
                || search.OfferOwnerType == OfferOwnerType.PlayerOwnerType && isTrader
                || search.OfferOwnerType == OfferOwnerType.TraderOwnerType && !isTrader
                || offer.Items?.FirstOrDefault()?.Template is not { } tpl)
            {
                continue;
            }

            counts[tpl] = counts.GetValueOrDefault(tpl) + 1;
            if (offer.IsFakePlayerOffer())
            {
                made[tpl] = made.GetValueOrDefault(tpl) + 1;
            }
        }

        if (Enabled && fleaUnlocked && search.OfferOwnerType != OfferOwnerType.TraderOwnerType)
        {
            foreach (var (tpl, count) in counts.ToList())
            {
                if (FullCounts.TryGetValue(tpl, out var full))
                {
                    counts[tpl] = count + Math.Max(0, full - made.GetValueOrDefault(tpl));
                }
            }
        }

        return counts;
    }
}

// SPT checks every offer on the flea against the filters; this only checks the searched items' offers.
public class FillOnSearchPatch : AbstractPatch
{
    private delegate bool PassesFilters(RagfairOfferHelper helper, SearchRequestData search, RagfairOffer offer, Item root, PmcData pmc);

    private delegate bool Displayable(RagfairOfferHelper helper, SearchRequestData search, HashSet<MongoId> items,
        Dictionary<MongoId, TraderAssort> traderAssorts, RagfairOffer offer, Item root, PmcData pmc, bool fleaBanned);

    private static readonly PassesFilters? Passes = Bind<PassesFilters>("PassesSearchFilterCriteria");
    private static readonly Displayable? IsDisplayable = Bind<Displayable>("IsDisplayableOffer");

    private static T? Bind<T>(string name) where T : Delegate =>
        typeof(RagfairOfferHelper).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) is { } method
            ? (T?)Delegate.CreateDelegate(typeof(T), method, throwOnBindFailure: false)
            : null;

    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairOfferHelper).GetMethod(nameof(RagfairOfferHelper.GetValidOffers), BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static bool Prefix(RagfairOfferHelper __instance, SearchRequestData searchRequest, HashSet<MongoId> itemsToAdd,
        Dictionary<MongoId, TraderAssort> traderAssorts, PmcData pmcData, ref List<RagfairOffer> __result)
    {
        OnDemandOffers.FillUp(itemsToAdd);

        // Tiered flea copies offers per player, leave it to SPT.
        if (Passes is null || IsDisplayable is null || OnDemandOffers.TieredFleaOn)
        {
            return true;
        }

        var fleaBanned = pmcData.PlayerIsFleaBanned(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var result = new List<RagfairOffer>();
        foreach (var offer in OnDemandOffers.OffersOf(itemsToAdd))
        {
            var root = offer.Items?.FirstOrDefault();
            if (root is not null && Passes(__instance, searchRequest, offer, root, pmcData)
                && IsDisplayable(__instance, searchRequest, itemsToAdd, traderAssorts, offer, root, pmcData, fleaBanned))
            {
                result.Add(offer);
            }
        }

        __result = result;
        return false;
    }
}

// One pass instead of SPT's grouping plus a second pass for offers not made yet.
public class FullCategoryCountsPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairCategoriesService).GetMethod(nameof(RagfairCategoriesService.GetCategoriesFromOffers), BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static bool Prefix(IEnumerable<RagfairOffer> offers, SearchRequestData searchRequestData, bool fleaUnlocked, ref Dictionary<MongoId, int> __result)
    {
        __result = OnDemandOffers.CountByItem(offers, searchRequestData, fleaUnlocked);
        return false;
    }
}
