using System.Reflection;
using EFT;
using SPT.Reflection.Patching;

namespace BetterFlea.Client.Patches;

// Runs whenever the server sends back inventory changes (buying, selling, moving items...).
// A purchase may have just locked something, so the next hover asks the server again
// instead of showing what it knew a minute ago.
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
