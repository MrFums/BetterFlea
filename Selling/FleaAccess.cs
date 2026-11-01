using System.Reflection;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Selling;

// The flea's unlock level and offer slots are in the globals the game gets at login: players see a change after
// logging in again. SPT reads the same unlock level on the server. Each value is only written while its setting is
// changed, and put back once it isn't, so another mod's (SVM's) stays when these are left alone.
public static class FleaAccess
{
    private static ConfigManager? _config;
    // SPT's own, the default: left alone, so another mod's level stays.
    private const int SptUnlockLevel = 15;
    private static int? _sptLevel;
    private static IEnumerable<MaxActiveOfferCount>? _sptSlots;

    public static void Apply(ConfigManager configManager, GlobalTable globalTable)
    {
        _config = configManager;
        var ragfair = globalTable.Configuration.RagFair;
        var set = configManager.Mod.PlayerSelling.FleaAccess;

        if (set.UnlockLevel != SptUnlockLevel)
        {
            _sptLevel ??= ragfair.MinUserLevel;
            ragfair.MinUserLevel = set.UnlockLevel;
        }
        else if (_sptLevel is { } level)
        {
            ragfair.MinUserLevel = level;
            _sptLevel = null;
        }

        if (set.OfferSlotsMultiplier != 1 || set.ExtraOfferSlots != 0)
        {
            _sptSlots ??= ragfair.MaxActiveOfferCount;
            ragfair.MaxActiveOfferCount = _sptSlots.Select(tier => tier with
            {
                Count = Slots(tier.Count, set),
                CountForSpecialEditions = Slots(tier.CountForSpecialEditions, set)
            }).ToList();
        }
        else if (_sptSlots is { } slots)
        {
            ragfair.MaxActiveOfferCount = slots;
            _sptSlots = null;
        }
    }

    // Rounded half up, like the settings page's table.
    private static double Slots(double count, FleaAccessConfig set) =>
        Math.Max(1, Math.Round(count * set.OfferSlotsMultiplier, MidpointRounding.AwayFromZero) + set.ExtraOfferSlots);

    // The offer slots by flea rep before these settings (SPT's, or another mod's), for the settings page.
    public static IEnumerable<MaxActiveOfferCount> BaseSlots(GlobalTable globalTable) =>
        _sptSlots ?? globalTable.Configuration.RagFair.MaxActiveOfferCount;

    internal static double RepGain => _config?.Mod.PlayerSelling.FleaAccess.RepGainMultiplier ?? 1;

    private static NotifierHelper? _notifier;
    private static NotificationSendHelper? _sender;

    public static void Bind(NotifierHelper notifier, NotificationSendHelper sender)
    {
        _notifier = notifier;
        _sender = sender;
    }

    // SPT adds the rep but never tells the game, so the flea's rating only changed after logging in again. This sends
    // the game's own new-rating message, as the live game does.
    internal static void SendRating(SptProfile profile)
    {
        if (_notifier is null || _sender is null || profile.ProfileInfo?.ProfileId is not { } sessionId
            || profile.CharacterData?.PmcData?.RagfairInfo is not { Rating: { } rating } info)
        {
            return;
        }

        try
        {
            _ = _sender.SendMessageAsync(sessionId, _notifier.CreateRagfairNewRatingNotification(rating, info.IsRatingGrowing ?? true));
        }
        catch (Exception)
        {
        }
    }
}

// Flea rep from a player's sale x repGainMultiplier, and the new rep sent to the game.
public class RepGainPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() =>
        typeof(RagfairOfferHelper).GetMethod(nameof(RagfairOfferHelper.IncreaseProfileRagfairRating))!;

    [PatchPrefix]
    public static void Prefix(ref double? amountToIncrementBy)
    {
        if (FleaAccess.RepGain != 1)
        {
            amountToIncrementBy *= FleaAccess.RepGain;
        }
    }

    [PatchPostfix]
    public static void Postfix(SptProfile profile)
    {
        FleaAccess.SendRating(profile);
    }
}
