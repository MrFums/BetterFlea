using System.Collections.Concurrent;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Ragfair;

namespace BetterFlea.Selling;

// A gun, armour or anything else the order book doesn't take sells or not as rolled when it's listed
// (SaleDelayGate.RollForSale), and that roll lives on the offer. If it's lost (the server stopped before SPT saved
// the profile, or anything else), the offer comes back with no sale and would never sell. So each such offer's roll is
// noted here (data/gear-rolls.json) as soon as it's made. An offer that comes back from a restart with no sale and no
// note gets its roll then, for the time it has left (SaleDelayGate.RollLoaded). One with a note keeps what it rolled,
// so restarting the server never gives an offer another go.
internal static class GearRolls
{
    private const string FileName = "gear-rolls.json";

    public sealed class RollFile
    {
        // Offer id -> when the offer ends, so ended ones can be dropped.
        public Dictionary<string, long> Offers { get; set; } = new();
    }

    private static readonly ConcurrentDictionary<string, long> Rolled = new();

    // Offers already looked at since the server started.
    private static readonly ConcurrentDictionary<string, byte> Checked = new();

    private static long _serverStart;

    public static void Load()
    {
        _serverStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var (id, end) in DataFiles.Read<RollFile>(FileName)?.Offers ?? [])
        {
            if (end > _serverStart)
            {
                Rolled[id] = end;
            }
        }
    }

    // The offer just listed was rolled (ListingContextPatch), whether it sells or not.
    public static void Mark(RagfairOffer offer)
    {
        var id = offer.Id.ToString();
        Checked[id] = 0;
        Rolled[id] = offer.EndTime ?? 0;
        Save();
    }

    private static void Save()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var (id, end) in Rolled)
        {
            if (end <= now)
            {
                Rolled.TryRemove(id, out _);
            }
        }

        DataFiles.Write(FileName, new RollFile { Offers = new Dictionary<string, long>(Rolled) });
    }

    // Each player offer the order book doesn't take, once per server start: listed before this start, still up, with no
    // sale on it and no note that it was rolled, it's rolled now. Packs are SPT's to roll.
    public static void Check(ProfileHelper? profiles, long now)
    {
        if (profiles is null || SaleDelayGate.Settings is null || SaleDelayGate.Fixed is not null)
        {
            return;
        }

        var changed = false;
        foreach (var (player, profile) in profiles.GetProfiles())
        {
            foreach (var offer in (profile.CharacterData?.PmcData?.RagfairInfo?.Offers ?? []).ToList())
            {
                var id = offer.Id.ToString();
                if (offer.EndTime is not { } end || end <= now || offer.SellInOnePiece == true || offer.Items is not { Count: > 0 }
                    || OrderBook.Handles(offer) || !Checked.TryAdd(id, 0))
                {
                    continue;
                }

                // Listed since the server started: rolled then, and noted by Mark.
                if (Rolled.ContainsKey(id) || (offer.StartTime ?? 0) >= _serverStart)
                {
                    continue;
                }

                if (offer.SellResults is not { Count: > 0 })
                {
                    SaleDelayGate.RollLoaded(offer, player, now);
                }

                Rolled[id] = end;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }
}
