using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace BetterFlea.Client;

// What the server says about the flea: item types that can't go on it, and which of our items
// came from a trader. Fetched in the background, so hovering items never waits on the server.
// Until the first answer arrives nothing is tagged.
//
// The server logs every request, so this asks as little as it can: bans don't change while the
// server runs, so one answer is enough unless the purchase lock is on. Then it asks again on the
// first hover after the inventory changes (a purchase may have locked something), or once a minute.
internal static class FleaStatus
{
    private const string Route = "/betterflea/client-data";
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    private static Snapshot _current = new Snapshot(new HashSet<string>(), new HashSet<string>());
    private static DateTime _fetchedAt = DateTime.MinValue;
    private static bool _fetching;
    private static bool _done;
    private static bool _warned;

    private sealed class Snapshot
    {
        public readonly HashSet<string> FleaBanned;
        public readonly HashSet<string> BoughtFromTrader;

        public Snapshot(HashSet<string> fleaBanned, HashSet<string> boughtFromTrader)
        {
            FleaBanned = fleaBanned;
            BoughtFromTrader = boughtFromTrader;
        }
    }

    private sealed class ClientData
    {
        public List<string> FleaBanned { get; set; }
        public List<string> BoughtFromTrader { get; set; }
        public bool PurchaseLockOn { get; set; }
    }

    public static bool IsFleaBanned(Item item)
    {
        RefreshIfOld();
        return item != null && _current.FleaBanned.Contains(item.TemplateId.ToString());
    }

    // The item itself or anything attached to or inside it. Stacks and ammo boxes never count: a stack
    // can be merged into another and a box opened into loose rounds, so the server doesn't lock them either.
    public static bool IsBoughtFromTrader(Item item)
    {
        RefreshIfOld();
        var bought = _current.BoughtFromTrader;
        return item != null && bought.Count > 0
            && item.GetAllItems().Any(part => part.StackMaxSize <= 1 && !(part is AmmoBox) && bought.Contains(part.Id));
    }

    public static bool IsBoughtFromTrader(string itemId)
    {
        RefreshIfOld();
        return itemId != null && _current.BoughtFromTrader.Contains(itemId);
    }

    // After an inventory change: ask again on the next hover, as long as anything can be locked at all.
    public static void MarkStale()
    {
        _fetchedAt = DateTime.MinValue;
    }

    private static void RefreshIfOld()
    {
        if (_done || _fetching || DateTime.UtcNow - _fetchedAt < MaxAge)
        {
            return;
        }

        _fetching = true;
        Task.Run(Fetch);
    }

    private static void Fetch()
    {
        try
        {
            var data = JsonConvert.DeserializeObject<ClientData>(RequestHandler.GetJson(Route));
            _current = new Snapshot(
                new HashSet<string>(data?.FleaBanned ?? new List<string>()),
                new HashSet<string>(data?.BoughtFromTrader ?? new List<string>()));
            _done = data != null && !data.PurchaseLockOn;
        }
        catch (Exception ex)
        {
            // Usually the server side of BetterFlea isn't installed. Say so once, then stop asking.
            if (!_warned)
            {
                _warned = true;
                Plugin.Log.LogWarning($"Could not get flea data from the server ({ex.Message}). Is BetterFlea installed on the server?");
            }

            _done = true;
        }
        finally
        {
            _fetchedAt = DateTime.UtcNow;
            _fetching = false;
        }
    }
}
