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

    [JsonPropertyName("undercut")]
    public UndercutConfig Undercut { get; set; } = new();

    [JsonPropertyName("buyers")]
    public BuyersConfig Buyers { get; set; } = new();

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

    // A gun, armour or anything with durability at the normal price: this many minutes (random between) for an item
    // of about 100,000 roubles, longer for dearer ones.
    [JsonPropertyName("uniqueMinMinutes")]
    public double UniqueMinMinutes { get; set; } = 5;

    [JsonPropertyName("uniqueMaxMinutes")]
    public double UniqueMaxMinutes { get; set; } = 20;

    [JsonPropertyName("slowSaleMaxHours")]
    public double SlowSaleMaxHours { get; set; } = 4;

    [JsonPropertyName("expensivePercent")]
    public double ExpensivePercent { get; set; } = 15;

    // Share of buyers left when a cheaper offer of the same item is already on the market.
    [JsonPropertyName("cheaperOfferShare")]
    public double CheaperOfferShare { get; set; } = 0.35;
}

// Buyers for the item on screen and for items players have listed: each takes the cheapest offer.
public record BuyersConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("minMinutes")]
    public double MinMinutes { get; set; } = 0.05;

    [JsonPropertyName("maxMinutes")]
    public double MaxMinutes { get; set; } = 0.2;

    [JsonPropertyName("slowdown")]
    public double Slowdown { get; set; } = 1.1;

    [JsonPropertyName("recoverMinutes")]
    public double RecoverMinutes { get; set; } = 10;

    [JsonPropertyName("wholeOfferChance")]
    public double WholeOfferChance { get; set; } = 0.08;

    [JsonPropertyName("relistMinMinutes")]
    public double RelistMinMinutes { get; set; } = 1;

    [JsonPropertyName("relistMaxMinutes")]
    public double RelistMaxMinutes { get; set; } = 5;
}

// Another seller listing just under a player's offer, taking its buyers.
public record UndercutConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("chanceAtNormal")]
    public double ChanceAtNormal { get; set; } = 0.35;

    [JsonPropertyName("chanceWhenDear")]
    public double ChanceWhenDear { get; set; } = 0.8;

    [JsonPropertyName("safeBelowPercent")]
    public double SafeBelowPercent { get; set; } = 10;

    [JsonPropertyName("minMinutes")]
    public double MinMinutes { get; set; } = 1;

    [JsonPropertyName("maxMinutes")]
    public double MaxMinutes { get; set; } = 10;

    // Share of the offer's buyers who still buy it after the undercut.
    [JsonPropertyName("buyersLeft")]
    public double BuyersLeft { get; set; } = 0.15;
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
