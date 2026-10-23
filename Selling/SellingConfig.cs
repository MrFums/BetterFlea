using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterFlea.Selling;

public record PlayerSellingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("foundInRaidOnly")]
    public bool FoundInRaidOnly { get; set; } = false;

    [JsonPropertyName("saleDelay")]
    public SaleDelayConfig SaleDelay { get; set; } = new();

    [JsonPropertyName("flipPenalty")]
    public FlipPenaltyConfig FlipPenalty { get; set; } = new();

    [JsonPropertyName("purchaseLock")]
    public PurchaseLockConfig PurchaseLock { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

public record SaleDelayConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("minMinutes")]
    public double MinMinutes { get; set; } = 2;

    [JsonPropertyName("maxMinutes")]
    public double MaxMinutes { get; set; } = 8;

    [JsonPropertyName("noDelayChance")]
    public double NoDelayChance { get; set; } = 0.1;

    [JsonPropertyName("expensivePercent")]
    public double ExpensivePercent { get; set; } = 30;
}

public record FlipPenaltyConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("windowMinutes")]
    public double WindowMinutes { get; set; } = 60;

    [JsonPropertyName("freePurchases")]
    public double FreePurchases { get; set; } = 2;

    [JsonPropertyName("purchasesForMaxPenalty")]
    public double PurchasesForMaxPenalty { get; set; } = 10;

    [JsonPropertyName("maxExtraMinutes")]
    public double MaxExtraMinutes { get; set; } = 90;

    [JsonPropertyName("countFleaPurchases")]
    public bool CountFleaPurchases { get; set; } = true;

    [JsonPropertyName("fenceMultiplier")]
    public double FenceMultiplier { get; set; } = 3;
}

public record PurchaseLockConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("includeFleaPurchases")]
    public bool IncludeFleaPurchases { get; set; } = false;
}
