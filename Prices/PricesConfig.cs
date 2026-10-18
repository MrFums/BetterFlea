using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterFlea.Prices;

public record PricesConfig
{
    /// <summary>Price multiplier for marked key offers. 1 = vanilla.</summary>
    [JsonPropertyName("markedKeyMultiplier")]
    public double MarkedKeyMultiplier { get; set; } = 1.0;

    /// <summary>Offers never undercut trader buy prices.</summary>
    [JsonPropertyName("floor")]
    public PriceFloorConfig Floor { get; set; } = new();

    /// <summary>Prices slowly wander over time.</summary>
    [JsonPropertyName("drift")]
    public PriceDriftConfig Drift { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

// Keeps generated offers above what the best trader would pay for the same item,
// so nothing can be bought off the flea and sold straight to a trader for profit.
public record PriceFloorConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>How far above the best trader buy price the cheapest offer sits, in %.</summary>
    [JsonPropertyName("marginPercent")]
    public double MarginPercent { get; set; } = 10;

    /// <summary>At startup, warn about items one trader sells for less than another buys them.</summary>
    [JsonPropertyName("logTraderProfitLoops")]
    public bool LogTraderProfitLoops { get; set; } = false;
}

// Each item's flea price wanders a little over time. Only new offers use the new price,
// so the market shifts as old offers sell or expire.
public record PriceDriftConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Furthest an item's price can move from normal, in % either way.</summary>
    [JsonPropertyName("maxDriftPercent")]
    public double MaxDriftPercent { get; set; } = 10;

    /// <summary>Biggest single move, in %.</summary>
    [JsonPropertyName("stepPercent")]
    public double StepPercent { get; set; } = 2;

    /// <summary>How often an item's price can move.</summary>
    [JsonPropertyName("updateMinutes")]
    public double UpdateMinutes { get; set; } = 45;
}
