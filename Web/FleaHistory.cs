using System.Diagnostics;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services.Ragfair;
using BetterFlea.Offers;
using BetterFlea.Prices;

namespace BetterFlea.Web;

// What the flea looks like right now and over the last few hours, for the settings page's home tab.
// Starts once the flea has been built.
[Injectable(TypePriority = int.MaxValue)]
public class FleaHistory(RagfairOfferService ragfairOfferService) : IOnLoad
{
    private const int KeepMinutes = 6 * 60;

    private static RagfairOfferService? _offers;
    private static Timer? _timer;
    private static readonly Queue<Sample> Samples = new();
    private static readonly DateTime Started = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    private static readonly HashSet<MongoId> MoneyTpls = [Money.ROUBLES, Money.DOLLARS, Money.EUROS];

    private record Sample(long Time, int Offers, int MemoryMb, int Sold);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        _offers = ragfairOfferService;
        _timer ??= new Timer(_ => Take(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    private static void Take()
    {
        try
        {
            var sample = new Sample(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), _offers!.GetOffers().Count, MemoryMb(), OfferLifecycle.Sold);
            lock (Samples)
            {
                Samples.Enqueue(sample);
                while (Samples.Count > KeepMinutes)
                {
                    Samples.Dequeue();
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write(() => $"Flea history sample failed: {ex.Message}");
        }
    }

    private static int MemoryMb()
    {
        using var process = Process.GetCurrentProcess();
        return (int)(process.WorkingSet64 / (1024 * 1024));
    }

    private static JsonObject? MemorySaver()
    {
        if (!OnDemandOffers.Enabled)
        {
            return null;
        }

        var (full, items, open) = OnDemandOffers.Stats();
        return new JsonObject { ["full"] = full, ["items"] = items, ["open"] = open };
    }

    public static JsonObject Status(Func<MongoId, string> name)
    {
        int traders = 0, sellers = 0, players = 0, barters = 0, packs = 0;
        foreach (var offer in _offers?.GetOffers() ?? [])
        {
            switch (offer.CreatedBy)
            {
                case OfferCreator.Trader: traders++; continue;
                case OfferCreator.Player: players++; break;
                default: sellers++; break;
            }

            if (offer.SellInOnePiece == true)
            {
                packs++;
            }

            if (offer.Requirements?.Any(requirement => !MoneyTpls.Contains(requirement.TemplateId)) == true)
            {
                barters++;
            }
        }

        Sample[] history;
        lock (Samples)
        {
            history = Samples.ToArray();
        }

        var movers = new JsonArray();
        foreach (var (tpl, factor) in PriceGate.Movers(5))
        {
            movers.Add(new JsonObject { ["name"] = name(tpl), ["percent"] = Math.Round((factor - 1.0) * 100, 1) });
        }

        return new JsonObject
        {
            ["uptimeSeconds"] = (long)(DateTime.UtcNow - Started).TotalSeconds,
            ["memoryMb"] = MemoryMb(),
            ["offers"] = new JsonObject
            {
                ["total"] = traders + sellers + players, ["traders"] = traders, ["sellers"] = sellers, ["players"] = players,
                ["barters"] = barters, ["packs"] = packs
            },
            ["sold"] = OfferLifecycle.Sold,
            ["dealsSold"] = OfferLifecycle.DealsSold,
            ["soldBeforeYou"] = SoldBeforeYou.Sniped,
            ["raisedToFloor"] = PriceGate.RaisedToFloor,
            ["memorySaver"] = MemorySaver(),
            ["driftOn"] = PriceGate.DriftOn,
            ["driftingItems"] = PriceGate.DriftingItems,
            ["movers"] = movers,
            ["history"] = new JsonArray(history.Select(sample => (JsonNode)new JsonArray(sample.Time, sample.Offers, sample.MemoryMb, sample.Sold)).ToArray())
        };
    }
}
