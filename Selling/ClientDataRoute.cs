using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers.Http;
using SPTarkov.Server.Core.Utils;

namespace BetterFlea.Selling;

// GET /betterflea/client-data - what the BepInEx plugin shows on items:
// which item types can't be bought or sold on the flea, and which of the caller's items came from a trader.
// The server works the same with or without the plugin; this only feeds the display.
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload)]
public class ClientDataRoute(
    TemplateTable templates,
    ItemHelper itemHelper,
    RagfairServerHelper ragfairServerHelper,
    JsonUtil jsonUtil) : IHttpListener
{
    public const string Path = "/betterflea/client-data";

    // Bans are settled once the server has loaded, so this is worked out on the first request and kept.
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
            PurchaseLockOn = PurchaseLock.IsOn
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
                .Where(item => item.Key && !ragfairServerHelper.IsItemValidRagfairItem(item))
                .Select(item => item.Value!.Id.ToString())
                .ToList();
        }
    }

    public sealed class ClientData
    {
        public List<string> FleaBanned { get; set; } = new();
        public List<string> BoughtFromTrader { get; set; } = new();

        /// <summary>false = nothing will ever be locked, so the client doesn't need to ask again.</summary>
        public bool PurchaseLockOn { get; set; }
    }
}
