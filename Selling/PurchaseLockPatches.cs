using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers.Http;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using Item = SPTarkov.Server.Core.Models.Eft.Common.Tables.Item;
using Microsoft.AspNetCore.Http;

namespace BetterFlea.Selling;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class PurchaseLockPatchesLoad(
    ConfigManager configManager,
    ItemHelper itemHelper) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        PurchaseLock.Bind(configManager, itemHelper);
        new LockPurchasePatch().Enable();
        return Task.CompletedTask;
    }
}

// Ids of items players bought from traders. The item itself isn't marked, since unknown fields get sent to
// every client and through co-op sync. Stacks and ammo boxes are never locked (they can be merged or opened).
// Saved to data/purchase-locks.json and pruned against the inventory.
internal static class PurchaseLock
{
    private const string FileName = "purchase-locks.json";

    // Safety net, pruning keeps it far below this.
    private const int MaxLocksPerPlayer = 5000;

    private static readonly object Sync = new();

    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;

    private static LockFile _data = new();

    [ThreadStatic] private static HashSet<string>? _ownedBefore;

    public sealed class LockFile
    {
        public Dictionary<string, HashSet<string>> Players { get; set; } = new();
    }

    private static bool Enabled => _config?.Mod.PlayerSelling.PurchaseLock.Enabled == true;

    public static bool IsOn => Enabled;

    private static bool IncludeFleaPurchases => _config?.Mod.PlayerSelling.PurchaseLock.IncludeFleaPurchases == true;

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _data = DataFiles.Read<LockFile>(FileName) ?? new LockFile();
    }

    public static void BeforePurchase(PmcData pmcData, ProcessBuyTradeRequestData request)
    {
        var fromTrader = request.Type is "buy_from_trader" or "buy_from_ragfair_trader"
            || (IncludeFleaPurchases && request.Type == "buy_from_ragfair");
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

        // Whatever's new in the inventory, attachments included, so a gun can't be stripped and sold in parts.
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

    private static bool CanSlipThrough(Item item)
    {
        if (_itemHelper is null)
        {
            return false;
        }

        return (_itemHelper.GetItem(item.Template).Value?.Properties?.StackMaxSize ?? 1) > 1
            || _itemHelper.IsOfBaseclass(item.Template, AmmoBox);
    }

    // Also drops stackable locks from older versions.
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
            // Profile unreadable: keep its locks.
            return null;
        }
    }

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

    // Only on a purchase, when the inventory is known to be complete.
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

    private static void Save() => DataFiles.Write(FileName, _data);
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

// GET /betterflea/client-data for the client plugin: flea-banned types and the caller's trader-bought items.
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload)]
public class ClientDataRoute(
    TemplateTable templates,
    ItemHelper itemHelper,
    RagfairServerHelper ragfairServerHelper,
    ConfigManager configManager,
    JsonUtil jsonUtil) : IHttpListener
{
    public const string Path = "/betterflea/client-data";

    // Currency, dogtags and quest items are kept off by the game itself, so they get no icon.
    private List<string>? _fleaBanned;
    private readonly object _sync = new();

    public bool CanHandle(HttpContext context)
    {
        return context.Request.Path.Equals(Path, StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(MongoId sessionId, HttpContext context, CancellationToken cancellationToken)
    {
        var body = jsonUtil.Serialize(new ClientData
        {
            FleaBanned = FleaBanned(),
            BoughtFromTrader = PurchaseLock.LockedIds(sessionId),
            PurchaseLockOn = PurchaseLock.IsOn,
            SellingOff = !configManager.Mod.PlayerSelling.Enabled
        });

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(body ?? "{}", cancellationToken);
    }

    private List<string> FleaBanned()
    {
        lock (_sync)
        {
            return _fleaBanned ??= templates.Handbook.Items
                .Select(entry => itemHelper.GetItem(entry.Id))
                .Where(item => item.Key
                    && item.Value?.Properties?.CanSellOnRagfair == true
                    && item.Value?.Properties?.QuestItem != true
                    && !ragfairServerHelper.IsItemValidRagfairItem(item))
                .Select(item => item.Value!.Id.ToString())
                .ToList();
        }
    }

    public sealed class ClientData
    {
        public List<string> FleaBanned { get; set; } = new();
        public List<string> BoughtFromTrader { get; set; } = new();

        public bool PurchaseLockOn { get; set; }

        public bool SellingOff { get; set; }
    }
}
