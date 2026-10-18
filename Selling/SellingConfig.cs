using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterFlea.Selling;

public record PlayerSellingConfig
{
    /// <summary>false = players can't list offers on the flea.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>true = players can only list found in raid items, like live.</summary>
    [JsonPropertyName("foundInRaidOnly")]
    public bool FoundInRaidOnly { get; set; } = false;

    /// <summary>Wait before buyers show up on a player's offers.</summary>
    [JsonPropertyName("saleDelay")]
    public SaleDelayConfig SaleDelay { get; set; } = new();

    /// <summary>Longer wait when a player lists items they just bought in bulk.</summary>
    [JsonPropertyName("flipPenalty")]
    public FlipPenaltyConfig FlipPenalty { get; set; } = new();

    /// <summary>Items bought from traders can never be listed.</summary>
    [JsonPropertyName("purchaseLock")]
    public PurchaseLockConfig PurchaseLock { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

// Holds back the buyers SPT rolls for a player's flea offer, so cheap listings don't sell seconds after posting.
public record SaleDelayConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Shortest wait before the first buyer, in minutes.</summary>
    [JsonPropertyName("minMinutes")]
    public double MinMinutes { get; set; } = 2;

    /// <summary>Longest wait before the first buyer, in minutes.</summary>
    [JsonPropertyName("maxMinutes")]
    public double MaxMinutes { get; set; } = 20;

    /// <summary>0-1 share of offers that skip the wait and sell on SPT's normal schedule.</summary>
    [JsonPropertyName("noDelayChance")]
    public double NoDelayChance { get; set; } = 0.1;
}

// Makes a player's flea listing take longer to sell when they recently bought a lot of that same item.
// Works through saleDelay, so it does nothing while saleDelay is off.
public record FlipPenaltyConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>How long a purchase counts against the player, in minutes. It counts less the older it gets.</summary>
    [JsonPropertyName("windowMinutes")]
    public double WindowMinutes { get; set; } = 60;

    /// <summary>Purchases of one item allowed before any penalty.</summary>
    [JsonPropertyName("freePurchases")]
    public double FreePurchases { get; set; } = 2;

    /// <summary>Purchases of one item at which the full penalty applies.</summary>
    [JsonPropertyName("purchasesForMaxPenalty")]
    public double PurchasesForMaxPenalty { get; set; } = 10;

    /// <summary>Extra wait before the first buyer at the full penalty, in minutes, on top of saleDelay's own wait.</summary>
    [JsonPropertyName("maxExtraMinutes")]
    public double MaxExtraMinutes { get; set; } = 90;

    /// <summary>Also count items bought from other players' flea offers, not just traders.</summary>
    [JsonPropertyName("countFleaPurchases")]
    public bool CountFleaPurchases { get; set; } = true;

    /// <summary>Each item bought from Fence counts as this many purchases. Fence is the cheapest place to flip from.</summary>
    [JsonPropertyName("fenceMultiplier")]
    public double FenceMultiplier { get; set; } = 3;
}

// Items a player buys from any trader (Fence included) can never be listed on the flea.
public record PurchaseLockConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
