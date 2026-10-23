using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Services.Ragfair;

namespace BetterFlea.Offers;

// Offers keep the settings they were made with, so after a save the generated offers are made again, the same
// way as at startup. Trader offers, real players' offers and lost guns stay.
internal static class FleaRefresh
{
    // Files whose settings change how offers are made.
    private static readonly HashSet<string> OfferFiles =
        ["listings.jsonc", "condition.jsonc", "weapons.jsonc", "armor.jsonc", "market.jsonc", "prices.jsonc", "economy.jsonc"];

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    private static RagfairOfferService? _offers;
    private static RagfairOfferGenerator? _generator;
    private static Action<string>? _log;
    private static Timer? _timer;
    private static readonly object RunLock = new();

    public static void Bind(RagfairOfferService offers, RagfairOfferGenerator generator, Action<string> log)
    {
        _log = log;
        _offers = offers;
        _generator = generator;
    }

    public static bool Remakes(string file) => OfferFiles.Contains(file) && _offers is not null;

    // The page saves one file at a time, so wait for the last one.
    public static void Saved(string file)
    {
        if (!Remakes(file))
        {
            return;
        }

        _timer ??= new Timer(_ => Run());
        _timer.Change(Wait, Timeout.InfiniteTimeSpan);
    }

    private static void Run()
    {
        lock (RunLock)
        {
            try
            {
                var removed = 0;
                foreach (var offer in _offers!.GetOffers())
                {
                    if (offer.IsFakePlayerOffer() && !OnDemandOffers.IsKept(offer.Id))
                    {
                        _offers.RemoveOfferById(offer.Id);
                        removed++;
                    }
                }

                OnDemandOffers.Forget();
                ListingCountGate.ForgetCaches();
                _generator!.GenerateDynamicOffers();
                OnDemandOffers.TidyMemorySoon();
                _log?.Invoke($"[BetterFlea] Remade the flea with the saved settings: {removed} old offers replaced, {_offers.GetOffers().Count} listed now.");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[BetterFlea] Couldn't remake the flea after a save: {ex.Message}");
            }
        }
    }
}
