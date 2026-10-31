using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BetterFlea.Client.Patches;
using SPT.Reflection.Patching;

namespace BetterFlea.Client;

[BepInPlugin("com.fums.betterflea", "Fums-BetterFleaClient", "2.4.1")]
[BepInDependency("com.SPT.core", BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;

    internal static ConfigEntry<bool> ShowFleaBannedIcon;
    internal static ConfigEntry<bool> ShowTraderIcon;

    private void Awake()
    {
        Log = Logger;
        ShowFleaBannedIcon = Config.Bind("Icons", "ShowFleaBannedIcon", false, "Red icon on items banned at the Flea Market, with its tooltip text.");
        ShowTraderIcon = Config.Bind("Icons", "ShowTraderIcon", true, "Orange icon on items bought from a trader, with its tooltip text. Only shows when the server has the trader item lock on.");
        ShowFleaBannedIcon.SettingChanged += (_, __) => FleaMarker.RefreshAll();
        ShowTraderIcon.SettingChanged += (_, __) => FleaMarker.RefreshAll();

        // One at a time so a game update breaking one patch doesn't take the rest down.
        Enable(() => new ItemCellMarkerPatch());
        Enable(() => new InspectMarkerPatch());
        Enable(() => new ItemCanSellOnRagfairPatch());
        Enable(() => new CompoundItemCanSellOnRagfairPatch());
        Enable(() => new AmmoBoxCanSellOnRagfairPatch());
        Enable(() => new RagfairSelectPatch());
        Enable(() => new AddOfferPatch());
        Enable(() => new InventoryChangedPatch());
        Enable(() => new InspectUnlockPrefixPatch());
        Enable(() => new CategoryLevelPatch());
        Enable(() => new CategorySelectPatch());
        Enable(() => new CategoryDeselectPatch());
    }

    private void Update()
    {
        FleaMarker.RefreshIfChanged();
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
