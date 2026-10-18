using System;
using System.Linq;
using System.Reflection;
using EFT.Communications;
using EFT.UI.Ragfair;
using SPT.Reflection.Patching;

namespace BetterFlea.Client.Patches;

// Every flea listing goes through here, from the game's own screen and from quick-sell mods alike.
// A bought item is stopped before anything is sent, with a small warning instead of a server error.
// The callback still runs so the mod or screen that asked can tidy up as if the server said no.
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
