using System.Reflection;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;

namespace BetterFlea.Prices;

// SPT 4.1.6 and older add the handbook-based flea price on top of prices.json's instead of replacing it (SPT issue
// #135), so most base prices are about 2.5 times too high. SPT 4.1.7 replaces them. Clearing those prices before SPT
// sets them gives every SPT version the fixed prices, so the flea doesn't change when SPT is updated.
public class BasePriceFixPatch : AbstractPatch
{
    private static TemplateTable? _templates;
    private static readonly Dictionary<MongoId, double> Cleared = new();

    public static void Bind(TemplateTable templates) => _templates = templates;

    protected override MethodBase GetTargetMethod() =>
        typeof(RagfairPriceService).GetMethod(nameof(RagfairPriceService.ReplaceFleaBasePrices), BindingFlags.Instance | BindingFlags.Public)!;

    [PatchPrefix]
    public static void Prefix(RagfairPriceService __instance)
    {
        Cleared.Clear();
        var prices = _templates!.Prices;
        foreach (var tpl in __instance.GetAllStaticPrices().Keys)
        {
            if (prices.Remove(tpl, out var price))
            {
                Cleared[tpl] = price;
            }
        }
    }

    // Items SPT gives no handbook-based price keep their prices.json one.
    [PatchPostfix]
    public static void Postfix()
    {
        var prices = _templates!.Prices;
        var replaced = Cleared.Count(entry => !prices.TryAdd(entry.Key, entry.Value));
        DebugLog.Summary(() => $"Base prices: {replaced} prices.json price(s) replaced by SPT's handbook-based price, as in SPT 4.1.7.");
        Cleared.Clear();
    }
}
