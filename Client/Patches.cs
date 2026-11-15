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
        if (FleaStatus.SellingOff)
        {
            error = Tags.SellingOffWarning;
            __result = false;
        }
        else if (FleaStatus.IsFleaBanned(item))
        {
            error = Tags.FleaBannedWarning;
            __result = false;
        }
        else if (FleaStatus.IsBoughtFromTrader(item))
        {
            error = Tags.BoughtFromTraderWarning;
            __result = false;
        }
        else if (FleaStatus.SellLockLevel(item) is var level && level > 0)
        {
            error = Tags.LevelLockWarning(level);
            __result = false;
        }
        // The game's own "can't be sold" for items its data bans, worded like ours.
        else if (!__result && error == CannotBePlaced)
        {
            error = Tags.FleaBannedWarning;
        }
        else if (!__result && !item.CanSellOnRagfair && item is CompoundItem compound && compound.TryGetLastForbiddenItem(out var inside))
        {
            error = Tags.ContainsWarning(inside.Name.Localized());
        }
    }

    private static string CannotBePlaced => "ragfair/This item cannot be placed at ragfair".Localized();
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
    public static bool Prefix(string[] items, ref Action callback)
    {
        if (FleaStatus.SellingOff)
        {
            NotificationManager.DisplayWarningNotification(Tags.SellingOffNotice);
            callback?.Invoke();
            return false;
        }

        if (items == null || !items.Any(FleaStatus.IsBoughtFromTrader))
        {
            // Once listed, say if it waits longer for buyers (the flip penalty).
            callback = FlipNotice.After(callback);
            return true;
        }

        if (!FleaStatus.NotificationsOff)
        {
            NotificationManager.DisplayWarningNotification(ClientText.Get("Trader items can't be sold at the Flea Market."));
        }

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
        FleaMarker.UpdateCell(__instance);
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
        if (result && (FleaStatus.SellingOff || FleaStatus.IsFleaBanned(item) || FleaStatus.IsBoughtFromTrader(item) || FleaStatus.SellLockLevel(item) > 0))
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

// Meds are priced by what the game shows, not the hidden decimals: two CALOKs both at 1/3 get the same trader price,
// and so do AFAKs at 399 and 399.1. The game pays a trader sale at this price. Whole uses or HP, rounded down as the
// inspect window shows them. The amount is put back as soon as the price is worked out. The server does the same for
// flea prices.
internal class MedPricePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        typeof(EFT.Trading.PriceCalculator).GetMethod(nameof(EFT.Trading.PriceCalculator.CalculateBasePriceForSingleItem), BindingFlags.Public | BindingFlags.Static);

    [PatchPrefix]
    public static void Prefix(Item item, out float? __state)
    {
        __state = null;
        if (item == null || !item.TryGetItemComponent<MedKitComponent>(out var medKit))
        {
            return;
        }

        var usable = (float)Math.Floor(medKit.HpResource + 1e-3f);
        if (usable != medKit.HpResource)
        {
            __state = medKit.HpResource;
            medKit.HpResource = usable;
        }
    }

    [PatchFinalizer]
    public static void Finalizer(Item item, float? __state)
    {
        if (__state is { } original && item.TryGetItemComponent<MedKitComponent>(out var medKit))
        {
            medKit.HpResource = original;
        }
    }
}
