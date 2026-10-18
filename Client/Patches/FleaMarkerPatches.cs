using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using SPT.Reflection.Patching;

namespace BetterFlea.Client.Patches;

// Item cells in the stash and inventory. Runs every time a cell sets up its found-in-raid icon,
// including when it hides it, so the marker always matches the item currently in the cell.
internal class ItemCellMarkerPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(ItemView).GetMethod(nameof(ItemView.SetQuestItemViewPanel), BindingFlags.Instance | BindingFlags.Public);
    }

    [PatchPostfix]
    public static void Postfix(ItemView __instance)
    {
        // Same places the game shows its found-in-raid icon: the player's own items, not trader or flea lists.
        var shown = __instance.ItemContext != null && __instance.ItemContext.HasViewComponent(EItemViewComponent.QuestItem);
        FleaMarker.Update(__instance.QuestsItemViewPanel, __instance, shown ? __instance.Item : null, ItemUiContext.Instance?.Tooltip);
    }
}

// The inspect window, which sets up its own found-in-raid icon directly.
internal class InspectMarkerPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(QuestItemViewPanel).GetMethod(nameof(QuestItemViewPanel.Show), BindingFlags.Instance | BindingFlags.Public);
    }

    [PatchPostfix]
    public static void Postfix(QuestItemViewPanel __instance, Item item, SimpleTooltip tooltip)
    {
        // Item cells are handled above, after the game has finished with them. Checked against the
        // cell itself: the checkmark isn't always inside the cell's bottom panel object.
        if (__instance.GetComponentInParent<ItemView>(true) != null)
        {
            return;
        }

        FleaMarker.Update(__instance, null, item, tooltip);
    }
}
