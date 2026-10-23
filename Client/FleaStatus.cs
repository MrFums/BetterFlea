using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using SPT.Common.Http;
using EFT.InventoryLogic;
using Newtonsoft.Json;

namespace BetterFlea.Client;

// Flea bans, trader-bought items and whether players can list at all, fetched from the server in the background so
// hovering never waits. Asked again after an inventory change or once a minute (the settings page can change them).
internal static class FleaStatus
{
    private const string Route = "/betterflea/client-data";
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    // After a failed check (server restarting, or no BetterFlea on it) wait this much longer before trying again.
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(3);

    private static Snapshot _current = new Snapshot(new HashSet<string>(), new HashSet<string>(), false, false);
    private static DateTime _fetchedAt = DateTime.MinValue;
    private static bool _fetching;
    private static bool _warned;

    private sealed class Snapshot
    {
        public readonly HashSet<string> FleaBanned;
        public readonly HashSet<string> BoughtFromTrader;
        public readonly bool PurchaseLockOn;
        public readonly bool SellingOff;

        public Snapshot(HashSet<string> fleaBanned, HashSet<string> boughtFromTrader, bool purchaseLockOn, bool sellingOff)
        {
            FleaBanned = fleaBanned;
            BoughtFromTrader = boughtFromTrader;
            PurchaseLockOn = purchaseLockOn;
            SellingOff = sellingOff;
        }
    }

    private sealed class ClientData
    {
        public List<string> FleaBanned { get; set; }
        public List<string> BoughtFromTrader { get; set; }
        public bool PurchaseLockOn { get; set; }
        public bool SellingOff { get; set; }
    }

    public static bool SellingOff
    {
        get
        {
            RefreshIfOld();
            return _current.SellingOff;
        }
    }

    public static bool IsFleaBanned(Item item)
    {
        RefreshIfOld();
        return item != null && _current.FleaBanned.Contains(item.TemplateId.ToString());
    }

    // Stacks and ammo boxes never count (the server doesn't lock them either).
    public static bool IsBoughtFromTrader(Item item)
    {
        RefreshIfOld();
        var bought = _current.BoughtFromTrader;
        return item != null && _current.PurchaseLockOn && bought.Count > 0
            && item.GetAllItems().Any(part => part.StackMaxSize <= 1 && !(part is AmmoBox) && bought.Contains(part.Id));
    }

    public static bool IsBoughtFromTrader(string itemId)
    {
        RefreshIfOld();
        return itemId != null && _current.PurchaseLockOn && _current.BoughtFromTrader.Contains(itemId);
    }

    public static void MarkStale()
    {
        _fetchedAt = DateTime.MinValue;
    }

    private static void RefreshIfOld()
    {
        if (_fetching || DateTime.UtcNow - _fetchedAt < MaxAge)
        {
            return;
        }

        _fetching = true;
        Task.Run(Fetch);
    }

    private static void Fetch()
    {
        var next = DateTime.UtcNow;
        try
        {
            var data = JsonConvert.DeserializeObject<ClientData>(RequestHandler.GetJson(Route));
            _current = new Snapshot(
                new HashSet<string>(data?.FleaBanned ?? new List<string>()),
                new HashSet<string>(data?.BoughtFromTrader ?? new List<string>()),
                data?.PurchaseLockOn ?? false,
                data?.SellingOff ?? false);
        }
        catch (Exception ex)
        {
            // The server restarting, or no BetterFlea on it.
            if (!_warned)
            {
                _warned = true;
                Plugin.Log.LogWarning($"Could not get flea data from the server ({ex.Message}). Is BetterFlea installed on the server?");
            }

            // Keep what we had and try again later, rather than giving up until the game restarts.
            next += RetryAfterFailure;
        }
        finally
        {
            _fetchedAt = next;
            _fetching = false;
        }
    }
}

// A ban wins over a trader purchase.
internal static class Tags
{
    public const string FleaBannedText = "Flea banned\nCannot be listed on the Flea Market";
    public const string BoughtFromTraderText = "Bought from trader\nCannot be listed on the Flea Market";

    public const string FleaBannedWarning = "Flea banned - cannot be listed";
    public const string SellingOffWarning = "Player listings are turned off";
    public const string SellingOffNotice = "Player listings are turned off at the Flea Market.";
    public const string BoughtFromTraderWarning = "Bought from trader - cannot be listed";

    public const string FleaBanned = "<color=#c84b4b>" + FleaBannedText + "</color>";
    public const string BoughtFromTrader = "<color=#d9a441>" + BoughtFromTraderText + "</color>";
}
