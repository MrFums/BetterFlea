using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace BetterFlea.Selling;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class DisableFleaSellingPatchesLoad(
    ConfigManager configManager,
    HttpResponseUtil httpResponseUtil,
    EventOutputHolder eventOutputHolder) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        FleaSellingGate.Bind(configManager, httpResponseUtil, eventOutputHolder);
        new DisableFleaSellingPatch().Enable();
        return Task.CompletedTask;
    }
}

internal static class FleaSellingGate
{
    public const string DisabledMessage = "Player listings are turned off at the Flea Market.";
    public const string NotFoundInRaidMessage = "Only found in raid items can be sold at the Flea Market.";

    private static ConfigManager? _config;
    private static HttpResponseUtil? _http;
    private static EventOutputHolder? _output;

    public static void Bind(
        ConfigManager configManager,
        HttpResponseUtil http,
        EventOutputHolder output)
    {
        _config = configManager;
        _http = http;
        _output = output;
    }

    public static bool Blocked => _config is not null && !_config.Mod.PlayerSelling.Enabled;

    public static bool FoundInRaidOnly => _config?.Mod.PlayerSelling.FoundInRaidOnly == true;

    // The client greys out non-FIR items when the globals flag is on; this catches anything that gets past it.
    // Attachments and other children count too, so a FIR gun with non-FIR parts is refused.
    public static bool AllFoundInRaid(List<Item> listed) => listed.All(item => item.Upd?.SpawnedInSession == true);

    // The listed items plus everything attached to or inside them.
    public static List<Item> WithChildren(PmcData pmcData, IEnumerable<MongoId> listedIds)
    {
        var found = new List<Item>();
        var inventory = pmcData.Inventory?.Items;
        if (inventory is null)
        {
            return found;
        }

        var byId = new Dictionary<string, Item>();
        foreach (var item in inventory)
        {
            byId.TryAdd(item.Id.ToString(), item);
        }

        var childrenOf = inventory.Where(item => item.ParentId is not null).ToLookup(item => item.ParentId);
        var pending = new Stack<string>(listedIds.Select(id => id.ToString()));
        while (pending.TryPop(out var id))
        {
            // Unknown IDs are left for SPT's own validation to reject.
            if (!byId.TryGetValue(id, out var item))
            {
                continue;
            }

            found.Add(item);
            foreach (var child in childrenOf[id])
            {
                pending.Push(child.Id.ToString());
            }
        }

        return found;
    }

    public const string BoughtFromTraderMessage = "Trader items can't be sold at the Flea Market.";

    // No error code on purpose: the game treats RagfairUnavailable as the flea going down and
    // throws the player out to the menu. Without one it just shows the message.
    public static ItemEventRouterResponse Fail(MongoId sessionID, string message)
    {
        var output = _output!.GetOutput(sessionID);
        return _http!.AppendErrorToOutput(output, message);
    }
}

public class DisableFleaSellingPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairController).GetMethod(
            nameof(RagfairController.AddPlayerOffer),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static bool Prefix(PmcData pmcData, AddOfferRequestData offerRequest, MongoId sessionID, ref ItemEventRouterResponse __result)
    {
        if (FleaSellingGate.Blocked)
        {
            __result = FleaSellingGate.Fail(sessionID, FleaSellingGate.DisabledMessage);
            return false;
        }

        if (offerRequest.Items is not { } listedIds)
        {
            return true;
        }

        var listed = FleaSellingGate.WithChildren(pmcData, listedIds);
        if (FleaSellingGate.FoundInRaidOnly && !FleaSellingGate.AllFoundInRaid(listed))
        {
            __result = FleaSellingGate.Fail(sessionID, FleaSellingGate.NotFoundInRaidMessage);
            return false;
        }

        if (PurchaseLock.AnyLocked(sessionID, listed))
        {
            __result = FleaSellingGate.Fail(sessionID, FleaSellingGate.BoughtFromTraderMessage);
            return false;
        }

        return true;
    }
}
