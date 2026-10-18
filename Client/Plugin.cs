using System;
using BepInEx;
using BepInEx.Logging;
using BetterFlea.Client.Patches;
using SPT.Reflection.Patching;

namespace BetterFlea.Client;

[BepInPlugin("com.fums.betterflea.client", "BetterFlea Client", "1.0.0")]
[BepInDependency("com.SPT.core", BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;

        // Each on its own, so a game update that breaks one patch doesn't take the others down with it.
        // Created inside the guard too: SPT checks a patch when it's created, not only when it's enabled.
        Enable(() => new ItemTooltipPatch());
        Enable(() => new ItemCellMarkerPatch());
        Enable(() => new InspectMarkerPatch());
        Enable(() => new ItemCanSellOnRagfairPatch());
        Enable(() => new CompoundItemCanSellOnRagfairPatch());
        Enable(() => new AmmoBoxCanSellOnRagfairPatch());
        Enable(() => new RagfairSelectPatch());
        Enable(() => new AddOfferPatch());
        Enable(() => new InventoryChangedPatch());
    }

    private static void Enable(Func<ModulePatch> create)
    {
        try
        {
            create().Enable();
        }
        catch (Exception ex)
        {
            Log.LogError($"A patch could not be enabled: {ex.Message}");
        }
    }
}
