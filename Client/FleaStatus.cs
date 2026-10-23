using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using SPT.Common.Http;
using EFT.InventoryLogic;
using Newtonsoft.Json;

namespace BetterFlea.Client;

// Flea bans and trader-bought items, fetched from the server in the background so hovering never waits.
// Bans don't change while the server runs, so with the purchase lock off one answer is enough. With it on,
// ask again after an inventory change or once a minute.
internal static class FleaStatus
{
    private const string Route = "/betterflea/client-data";
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    private static Snapshot _current = new Snapshot(new HashSet<string>(), new HashSet<string>(), false);
    private static DateTime _fetchedAt = DateTime.MinValue;
    private static bool _fetching;
    private static bool _done;
    private static bool _warned;

    private sealed class Snapshot
    {
        public readonly HashSet<string> FleaBanned;
        public readonly HashSet<string> BoughtFromTrader;
        public readonly bool PurchaseLockOn;

        public Snapshot(HashSet<string> fleaBanned, HashSet<string> boughtFromTrader, bool purchaseLockOn)
        {
            FleaBanned = fleaBanned;
            BoughtFromTrader = boughtFromTrader;
            PurchaseLockOn = purchaseLockOn;
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
                new HashSet<string>(data?.BoughtFromTrader ?? new List<string>()),
                data?.PurchaseLockOn ?? false);
            _done = data != null && !data.PurchaseLockOn;
        }
        catch (Exception ex)
        {
            // Usually means the server mod isn't installed.
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

// A ban wins over a trader purchase.
internal static class Tags
{
    public const string FleaBannedText = "Flea banned\nCannot be listed on the Flea Market";
    public const string BoughtFromTraderText = "Bought from trader\nCannot be listed on the Flea Market";

    public const string FleaBannedWarning = "Flea banned - cannot be listed";
    public const string BoughtFromTraderWarning = "Bought from trader - cannot be listed";

    public const string FleaBanned = "<color=#c84b4b>" + FleaBannedText + "</color>";
    public const string BoughtFromTrader = "<color=#d9a441>" + BoughtFromTraderText + "</color>";
}
