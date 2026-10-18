using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using Item = SPTarkov.Server.Core.Models.Eft.Common.Tables.Item;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Utils;

namespace BetterFlea.Selling;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class PurchaseLockPatchesLoad(
    ISptLogger<PurchaseLockPatchesLoad> logger,
    ConfigManager configManager,
    ItemHelper itemHelper,
    JsonUtil jsonUtil) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        PurchaseLock.Bind(configManager, itemHelper, jsonUtil, message => logger.Warning(message));
        new LockPurchasePatch().Enable();
        return Task.CompletedTask;
    }
}

// Remembers the ids of items players buy from traders, so those exact items can never be listed.
// The item itself isn't marked: an unknown field in an item's data would be sent to every game client
// and through co-op syncing, which isn't worth the risk.
//
// Stackable items (ammo, money, ...) and ammo boxes are never locked: a bought stack can be merged into
// one the player already had, and a box opened into loose rounds, so a lock would never be effective.
//
// Saved to data/purchase-locks.json. Locks for items that are no longer in the player's inventory
// (sold, used up, lost in raid) are dropped whenever that player buys from a trader, and for every
// player at server start, so the file only holds what players actually still own.
internal static class PurchaseLock
{
    private const string FileName = "purchase-locks.json";

    // Safety net only; pruning against the inventory keeps real counts far below this.
    private const int MaxLocksPerPlayer = 5000;

    private static readonly object Sync = new();

    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static JsonUtil? _json;
    private static Action<string>? _warn;
    private static string _path = string.Empty;

    private static LockFile _data = new();

    // Purchases are handled one request at a time per thread; carries the inventory as it was
    // before the purchase into the postfix.
    [ThreadStatic] private static HashSet<string>? _ownedBefore;

    public sealed class LockFile
    {
        /// <summary>Player id -> ids of items they bought from traders.</summary>
        public Dictionary<string, HashSet<string>> Players { get; set; } = new();
    }

    private static bool Enabled => _config?.Mod.PlayerSelling.PurchaseLock.Enabled == true;

    public static bool IsOn => Enabled;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, JsonUtil jsonUtil, Action<string> warn)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _json = jsonUtil;
        _warn = warn;
        _path = Path.Combine(configManager.ModPath, "data", FileName);
        Load();
    }

    public static void BeforePurchase(PmcData pmcData, ProcessBuyTradeRequestData request)
    {
        // Straight from a trader (Fence included), or a trader's offer on the flea. Other players' offers aren't locked.
        var fromTrader = request.Type is "buy_from_trader" or "buy_from_ragfair_trader";
        _ownedBefore = Enabled && fromTrader ? OwnedIds(pmcData) : null;
    }

    public static void AfterPurchase(PmcData pmcData, MongoId player, ItemEventRouterResponse output)
    {
        var before = _ownedBefore;
        _ownedBefore = null;
        if (before is null || !Enabled || output.Warnings is { Count: > 0 })
        {
            DebugLog.Write(() => $"Purchase lock: skipped a purchase by {player} (lock on: {Enabled}, trader purchase: {before is not null}, warnings: {output.Warnings?.Count ?? 0})");
            return;
        }

        // Whatever is in the inventory now that wasn't before: the bought item and everything attached
        // to it, so a gun can't be stripped and sold in parts. Read straight from the inventory, so it
        // doesn't depend on how SPT reports the change back to the game. Stackables are left out.
        var added = (pmcData.Inventory?.Items ?? [])
            .Where(item => !before.Contains(item.Id.ToString()) && !CanSlipThrough(item))
            .Select(item => item.Id.ToString())
            .ToList();
        if (added.Count == 0)
        {
            DebugLog.Write(() => $"Purchase lock: purchase by {player} added nothing lockable (stackable items aren't locked)");
            return;
        }

        lock (Sync)
        {
            var locks = LocksFor(player.ToString(), create: true)!;
            Prune(locks, pmcData);
            locks.UnionWith(added);
            if (locks.Count > MaxLocksPerPlayer)
            {
                locks.ExceptWith(locks.Take(locks.Count - MaxLocksPerPlayer).ToList());
            }

            Save();
        }

        DebugLog.Write(() => $"Purchase lock: {player} bought {added.Count} item(s) from a trader, locked: {string.Join(", ", added)}");
    }

    public static bool AnyLocked(MongoId player, List<Item> listed)
    {
        if (!Enabled)
        {
            return false;
        }

        lock (Sync)
        {
            var locks = LocksFor(player.ToString(), create: false);
            var locked = locks is not null && listed.Any(item => !CanSlipThrough(item) && locks.Contains(item.Id.ToString()));
            DebugLog.Write(() => $"Purchase lock: {player} listing {string.Join(", ", listed.Select(item => item.Id))} - {(locked ? "refused, bought from a trader" : "allowed")}");
            return locked;
        }
    }

    private static HashSet<string> OwnedIds(PmcData pmcData)
    {
        return pmcData.Inventory?.Items?.Select(item => item.Id.ToString()).ToHashSet() ?? new HashSet<string>();
    }

    private static readonly MongoId AmmoBox = new("543be5cb4bdc2deb348b4568");

    // Stacks can be merged into other stacks, and an ammo box can be opened into loose rounds (which stack),
    // so locking either would be pointless.
    private static bool CanSlipThrough(Item item)
    {
        if (_itemHelper is null)
        {
            return false;
        }

        return (_itemHelper.GetItem(item.Template).Value?.Properties?.StackMaxSize ?? 1) > 1
            || _itemHelper.IsOfBaseclass(item.Template, AmmoBox);
    }

    // At server start, once profiles are loaded: drop locks for items players no longer have, and for
    // stackables (older versions locked those too).
    public static void CleanUp(SaveServer saveServer)
    {
        lock (Sync)
        {
            var removed = 0;
            foreach (var (player, locks) in _data.Players.ToList())
            {
                var inventory = TryInventory(saveServer, player);
                if (inventory is null)
                {
                    continue;
                }

                var keep = inventory.Where(item => !CanSlipThrough(item)).Select(item => item.Id.ToString()).ToHashSet();
                removed += locks.RemoveWhere(id => !keep.Contains(id));
                if (locks.Count == 0)
                {
                    _data.Players.Remove(player);
                }
            }

            if (removed > 0)
            {
                Save();
                DebugLog.Summary(() => $"Purchase lock: cleared {removed} lock(s) for items players no longer have or that stack.");
            }
        }
    }

    private static List<Item>? TryInventory(SaveServer saveServer, string player)
    {
        try
        {
            return saveServer.GetProfile(new MongoId(player))?.CharacterData?.PmcData?.Inventory?.Items;
        }
        catch
        {
            // Profile deleted or unreadable: keep its locks rather than guess.
            return null;
        }
    }

    // For the client plugin's "bought from trader" tag. Empty while the lock is off.
    public static List<string> LockedIds(MongoId player)
    {
        if (!Enabled)
        {
            return new List<string>();
        }

        lock (Sync)
        {
            return LocksFor(player.ToString(), create: false)?.ToList() ?? new List<string>();
        }
    }

    // Drops locks for items the player no longer has (sold, used up, lost in raid).
    // Only run on a purchase, when the inventory is known to be complete and a new lock is added straight after.
    private static void Prune(HashSet<string> locks, PmcData pmcData)
    {
        var owned = OwnedIds(pmcData);
        if (owned.Count > 0)
        {
            locks.RemoveWhere(id => !owned.Contains(id));
        }
    }

    private static HashSet<string>? LocksFor(string player, bool create)
    {
        if (_data.Players.TryGetValue(player, out var locks))
        {
            return locks;
        }

        if (!create)
        {
            return null;
        }

        locks = new HashSet<string>();
        _data.Players[player] = locks;
        return locks;
    }

    private static void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            _data = _json!.Deserialize<LockFile>(File.ReadAllText(_path)) ?? new LockFile();
        }
        catch (Exception ex)
        {
            _warn?.Invoke($"[BetterFlea] Could not read {FileName}, starting with no purchase locks. ({ex.Message})");
            _data = new LockFile();
        }
    }

    // Purchases are rare enough to write straight away; written to a temp file first so a crash can't leave half a file.
    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, _json!.Serialize(_data));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _warn?.Invoke($"[BetterFlea] Could not save {FileName}. ({ex.Message})");
        }
    }
}

public class LockPurchasePatch : AbstractPatch
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
        PurchaseLock.BeforePurchase(pmcData, buyRequestData);
    }

    [PatchPostfix]
    public static void Postfix(PmcData pmcData, MongoId sessionId, ItemEventRouterResponse output)
    {
        PurchaseLock.AfterPurchase(pmcData, sessionId, output);
    }
}
