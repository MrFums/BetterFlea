using System.Linq;
using System.Reflection;
using System;
using SPT.Reflection.Patching;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using EFT.UI.Ragfair;
using EFT.UI;
using EFT;

namespace BetterFlea.Client.Patches;

// Greys items out in the add-offer screen with our reason instead of the game's generic one.
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
            error = Tags.FleaBannedWarning;
            __result = false;
        }
        else if (FleaStatus.IsBoughtFromTrader(item))
        {
            error = Tags.BoughtFromTraderWarning;
            __result = false;
        }
    }
}

// Every listing goes through here, quick-sell mods included. A bought item is stopped with a warning
// instead of a server error; the callback still runs so the caller can tidy up.
internal class AddOfferPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagFair).GetMethod(nameof(RagFair.AddOffer), BindingFlags.Instance | BindingFlags.Public);
    }

    [PatchPrefix]
    public static bool Prefix(string[] items, Action callback)
    {
        if (items == null || !items.Any(FleaStatus.IsBoughtFromTrader))
        {
            return true;
        }

        NotificationManager.DisplayWarningNotification("Trader items can't be sold at the Flea Market.");
        callback?.Invoke();
        return false;
    }
}

// A purchase may have locked something, so ask the server again on the next hover.
internal class InventoryChangedPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(ClientBackendSession).GetMethod(nameof(ClientBackendSession.ApplyProfileChanges), BindingFlags.Instance | BindingFlags.Public);
    }

    [PatchPostfix]
    public static void Postfix()
    {
        FleaStatus.MarkStale();
    }
}

// Runs every time a cell sets up its found-in-raid icon, including when it hides it.
internal class ItemCellMarkerPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(ItemView).GetMethod(nameof(ItemView.SetQuestItemViewPanel), BindingFlags.Instance | BindingFlags.Public);
    }

    [PatchPostfix]
    public static void Postfix(ItemView __instance)
    {
        // Only the player's own items, not trader or flea lists.
        var shown = __instance.ItemContext != null && __instance.ItemContext.HasViewComponent(EItemViewComponent.QuestItem);
        FleaMarker.Update(__instance.QuestsItemViewPanel, __instance, shown ? __instance.Item : null, ItemUiContext.Instance?.Tooltip);
    }
}

internal class InspectMarkerPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(QuestItemViewPanel).GetMethod(nameof(QuestItemViewPanel.Show), BindingFlags.Instance | BindingFlags.Public);
    }

    [PatchPostfix]
    public static void Postfix(QuestItemViewPanel __instance, Item item, SimpleTooltip tooltip)
    {
        // Item cells are handled above.
        if (__instance.GetComponentInParent<ItemView>(true) != null)
        {
            return;
        }

        FleaMarker.Update(__instance, null, item, tooltip);
    }
}

// The game's own "can this go on the flea" check, so anything that asks (including quick-sell mods)
// gets no for banned types and trader purchases. Three item classes override it, so three patches,
// each with its own postfix (SPT only looks for the method on the patch class itself).
internal static class CanSellOnRagfair
{
    public static MethodBase Getter(Type itemType)
    {
        return itemType.GetProperty(nameof(Item.CanSellOnRagfair), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!.GetMethod;
    }

    public static void Apply(Item item, ref bool result)
    {
        if (result && (FleaStatus.IsFleaBanned(item) || FleaStatus.IsBoughtFromTrader(item)))
        {
            result = false;
        }
    }
}

internal class ItemCanSellOnRagfairPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() => CanSellOnRagfair.Getter(typeof(Item));

    [PatchPostfix]
    public static void Postfix(Item __instance, ref bool __result) => CanSellOnRagfair.Apply(__instance, ref __result);
}

internal class CompoundItemCanSellOnRagfairPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() => CanSellOnRagfair.Getter(typeof(CompoundItem));

    [PatchPostfix]
    public static void Postfix(Item __instance, ref bool __result) => CanSellOnRagfair.Apply(__instance, ref __result);
}

internal class AmmoBoxCanSellOnRagfairPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() => CanSellOnRagfair.Getter(typeof(AmmoBox));

    [PatchPostfix]
    public static void Postfix(Item __instance, ref bool __result) => CanSellOnRagfair.Apply(__instance, ref __result);
}
