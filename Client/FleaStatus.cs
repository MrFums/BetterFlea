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

    private const string LevelsRoute = "/betterflea/item-levels";

    private static Snapshot _current = new Snapshot(new HashSet<string>(), new HashSet<string>(), false, false, new ClientData());
    private static DateTime _fetchedAt = DateTime.MinValue;
    private static bool _fetching;
    private static bool _warned;

    // The level each item unlocks at, fetched again only when the server's LevelsVersion changes.
    private static Dictionary<string, int> _levels = new Dictionary<string, int>();
    private static int _levelsVersion = -1;

    // Bumped whenever the level data or the player's level changes, so the UI knows to redraw.
    public static int LevelsStamp { get; private set; }

    // Bumped whenever the bans or trader purchases change, so cells drawn before the answer get their icon.
    public static int MarkersStamp { get; private set; }

    private sealed class Snapshot
    {
        public readonly HashSet<string> FleaBanned;
        public readonly HashSet<string> BoughtFromTrader;
        public readonly bool PurchaseLockOn;
        public readonly bool SellingOff;
        public readonly bool LevelLockOn;
        public readonly bool LevelLockSelling;
        public readonly int PlayerLevel;

        public Snapshot(HashSet<string> fleaBanned, HashSet<string> boughtFromTrader, bool purchaseLockOn, bool sellingOff, ClientData data)
        {
            FleaBanned = fleaBanned;
            BoughtFromTrader = boughtFromTrader;
            PurchaseLockOn = purchaseLockOn;
            SellingOff = sellingOff;
            LevelLockOn = data.LevelLockOn;
            LevelLockSelling = data.LevelLockSelling;
            PlayerLevel = data.PlayerLevel;
        }
    }

    private sealed class ClientData
    {
        public List<string> FleaBanned { get; set; }
        public List<string> BoughtFromTrader { get; set; }
        public bool PurchaseLockOn { get; set; }
        public bool SellingOff { get; set; }
        public bool LevelLockOn { get; set; }
        public bool LevelLockSelling { get; set; }
        public int LevelsVersion { get; set; }
        public int PlayerLevel { get; set; }
    }

    public static bool LevelLockOn
    {
        get
        {
            RefreshIfOld();
            return _current.LevelLockOn;
        }
    }

    public static int PlayerLevel => _current.PlayerLevel;

    // 0 when the item needs no level, or level locks are off.
    public static int UnlockLevel(string templateId)
    {
        RefreshIfOld();
        return _current.LevelLockOn && templateId != null && _levels.TryGetValue(templateId, out var level) ? level : 0;
    }

    // Only the item listed counts, not its parts (the server checks the same way).
    public static int SellLockLevel(Item item)
    {
        if (item == null || !LevelLockOn || !_current.LevelLockSelling)
        {
            return 0;
        }

        var level = UnlockLevel(item.TemplateId.ToString());
        return level > _current.PlayerLevel ? level : 0;
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
            var data = JsonConvert.DeserializeObject<ClientData>(RequestHandler.GetJson(Route)) ?? new ClientData();
            var levelsChanged = false;
            if (data.LevelsVersion != _levelsVersion)
            {
                _levels = JsonConvert.DeserializeObject<Dictionary<string, int>>(RequestHandler.GetJson(LevelsRoute)) ?? new Dictionary<string, int>();
                _levelsVersion = data.LevelsVersion;
                levelsChanged = true;
            }

            var before = _current;
            _current = new Snapshot(
                new HashSet<string>(data.FleaBanned ?? new List<string>()),
                new HashSet<string>(data.BoughtFromTrader ?? new List<string>()),
                data.PurchaseLockOn,
                data.SellingOff,
                data);
            if (levelsChanged || before.LevelLockOn != data.LevelLockOn || before.PlayerLevel != data.PlayerLevel)
            {
                LevelsStamp++;
            }

            if (before.PurchaseLockOn != _current.PurchaseLockOn
                || !before.FleaBanned.SetEquals(_current.FleaBanned)
                || !before.BoughtFromTrader.SetEquals(_current.BoughtFromTrader))
            {
                MarkersStamp++;
            }
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

    public const string FleaBannedWarning = "Flea banned - cannot be listed";
    public const string SellingOffWarning = "Player listings are turned off";
    public const string SellingOffNotice = "Player listings are turned off at the Flea Market.";
    public const string BoughtFromTraderWarning = "Bought from trader - cannot be listed";

    public static string LevelLockWarning(int level) => "Unlocks on the Flea Market at level " + level;
    public static string ContainsWarning(string inside) => "Contains " + inside + " - cannot be listed";
    public static string LevelLockNotice(int level) => "This item can only be sold at the Flea Market from level " + level + ".";

    // Tooltips: the heading in bold, the line under it plain, both in the icon's colour.
    public const string FleaBanned = "<color=#c84b4b><b>Flea banned</b>\nCannot be listed on the Flea Market</color>";
    public const string BoughtFromTrader = "<color=#d9a441><b>Bought from trader</b>\nCannot be listed on the Flea Market</color>";
}
