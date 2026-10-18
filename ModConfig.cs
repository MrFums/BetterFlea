using System.Text.Json;
using System.Text.Json.Serialization;
using BetterFlea.Flea;
using BetterFlea.Offers;
using BetterFlea.Prices;
using BetterFlea.Selling;

namespace BetterFlea;

// config.jsonc, one section per area. Each section's settings live next to that feature's code.
// Settings missing from the file keep their defaults.
public record ModConfig
{
    /// <summary>What can be bought on the flea.</summary>
    [JsonPropertyName("flea")]
    public FleaConfig Flea { get; set; } = new();

    /// <summary>The offers the server generates for fake sellers.</summary>
    [JsonPropertyName("generatedOffers")]
    public GeneratedOffersConfig GeneratedOffers { get; set; } = new();

    /// <summary>What generated offers cost.</summary>
    [JsonPropertyName("prices")]
    public PricesConfig Prices { get; set; } = new();

    /// <summary>Players listing their own items.</summary>
    [JsonPropertyName("playerSelling")]
    public PlayerSellingConfig PlayerSelling { get; set; } = new();

    /// <summary>Log every price floor, drift and flip penalty decision. For testing.</summary>
    [JsonPropertyName("debugLogging")]
    public bool DebugLogging { get; set; } = false;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}
