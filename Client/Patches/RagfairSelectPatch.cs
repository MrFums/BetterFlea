using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI.Ragfair;
using SPT.Reflection.Patching;

namespace BetterFlea.Client.Patches;

// The flea's own "can this be listed" check, which greys items out in the add-offer screen and shows
// the reason on hover. The game already says no for these (CanSellOnRagfairPatch), but with its own
// generic reason; this swaps in ours so the player knows why.
internal class RagfairSelectPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagFair).GetMethod(nameof(RagFair.CanBeSelectedAtRagfair), BindingFlags.Static | BindingFlags.Public);
    }

    [PatchPostfix]
    public static void Postfix(Item item, ref string error, ref bool __result)
    {
        if (FleaStatus.IsFleaBanned(item))
        {
            error = Tags.FleaBannedText;
            __result = false;
        }
        else if (FleaStatus.IsBoughtFromTrader(item))
        {
            error = Tags.BoughtFromTraderText;
            __result = false;
        }
    }
}
