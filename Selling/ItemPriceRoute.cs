using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;
using SPTarkov.Server.Core.Utils;
using Item = SPTarkov.Server.Core.Models.Eft.Common.Tables.Item;
using Microsoft.AspNetCore.Http;

namespace BetterFlea.Selling;

// POST /betterflea/item-price for the Price Check plugin: the normal flea price of one item with its parts, the same
// number a listing of it is measured against. The body is the item and its children as the game has them (plain
// JSON, not zlib), so it also works for items that aren't in the stash yet, like loot in a raid.
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload)]
public class ItemPriceRoute(JsonUtil jsonUtil, ItemHelper itemHelper) : IHttpListener
{
    public const string Path = "/betterflea/item-price";

    // A loaded gun is well under this; anything bigger isn't one item.
    private const int MostItems = 300;

    public bool CanHandle(HttpContext context)
    {
        return HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.Equals(Path, StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(MongoId sessionId, HttpContext context, CancellationToken cancellationToken)
    {
        double? price = null;
        try
        {
            using var reader = new StreamReader(context.Request.Body);
            var items = jsonUtil.Deserialize<List<Item>>(await reader.ReadToEndAsync(cancellationToken));
            if (items is { Count: > 0 and <= MostItems } && items.All(item => itemHelper.GetItem(item.Template).Key))
            {
                price = SaleDelayGate.NormalPrice(items);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write(() => $"Item price: could not read the request ({ex.Message})");
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(jsonUtil.Serialize(new ItemPrice { Price = Math.Round(price ?? 0) }) ?? "{}", cancellationToken);
    }

    public sealed class ItemPrice
    {
        // Roubles for one of the item. 0 = no price.
        public double Price { get; set; }
    }
}
