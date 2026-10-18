using System;
using System.Reflection;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;

namespace BetterFlea.Client.Patches;

// The game's own "can this go on the Flea Market?" answer for an item. Anything that asks it (the
// add-offer screen, the item menu, quick-sell mods choosing between the Flea Market and a trader)
// gets "no" for banned item types and trader purchases, so they go straight to a trader instead of
// trying the Flea Market and failing. The server enforces the same rules either way.
//
// Three item types answer this themselves (plain items, items holding other items, ammo boxes), so
// each gets its own patch. SPT only looks for the patch method on the patch class itself, so each
// declares its own postfix rather than inheriting one.
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
