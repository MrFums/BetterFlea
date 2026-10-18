using System.Reflection;
using EFT.UI;
using EFT.UI.DragAndDrop;
using SPT.Reflection.Patching;

namespace BetterFlea.Client.Patches;

// Adds one line under an item's hover tooltip. Runs after the game (and any other mod) has written
// the tooltip, so it only ever appends.
internal class ItemTooltipPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(GridItemView).GetMethod(nameof(GridItemView.ShowTooltip), BindingFlags.Instance | BindingFlags.Public);
    }

    [PatchPostfix]
    public static void Postfix(GridItemView __instance)
    {
        var tag = Tags.For(__instance.Item);
        var tooltip = ItemUiContext.Instance?.Tooltip;
        if (tag == null || tooltip == null || tooltip._label == null)
        {
            return;
        }

        tooltip.SetText(tooltip._label.text + "\n" + tag);
    }
}
