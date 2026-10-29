using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;
using SPTarkov.Server.Core.Utils;
using Microsoft.AspNetCore.Http;

namespace BetterFlea.Selling;

// GET /betterflea/flip-notice for the client plugin, right after a listing: the flip penalty on it, if it has one,
// so the game can show it on screen. Each notice is given out once.
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload)]
public class FlipNoticeRoute(JsonUtil jsonUtil) : IHttpListener
{
    public const string Path = "/betterflea/flip-notice";

    public bool CanHandle(HttpContext context) =>
        context.Request.Path.Equals(Path, StringComparison.OrdinalIgnoreCase);

    public async Task HandleAsync(MongoId sessionId, HttpContext context, CancellationToken cancellationToken)
    {
        var notice = FlipPenaltyGate.TakeNotice(sessionId) ?? new FlipNotice();
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(jsonUtil.Serialize(notice) ?? "{}", cancellationToken);
    }
}

// Minutes 0 = no penalty. Tpl: the item listed. Bought: how many of it counted (Fence's count extra). ClearsIn: minutes
// until listing it would carry no wait.
public sealed class FlipNotice
{
    public string Tpl { get; set; } = "";

    public double Minutes { get; set; }

    public double Bought { get; set; }

    public int ClearsIn { get; set; }
}
