using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterFlea.Prices;

public record PricesConfig
{
    [JsonPropertyName("priceMultiplier")]
    public double PriceMultiplier { get; set; } = 1.0;

    [JsonPropertyName("markedKeyMultiplier")]
    public double MarkedKeyMultiplier { get; set; } = 1.0;

    [JsonPropertyName("floor")]
    public PriceFloorConfig Floor { get; set; } = new();

    [JsonPropertyName("drift")]
    public PriceDriftConfig Drift { get; set; } = new();

    [JsonPropertyName("sellers")]
    public SellerPricingConfig Sellers { get; set; } = new();

    [JsonPropertyName("guns")]
    public GunPricingConfig Guns { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

public record PriceFloorConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("marginPercent")]
    public double MarginPercent { get; set; } = 10;

    [JsonPropertyName("logTraderProfitLoops")]
    public bool LogTraderProfitLoops { get; set; } = false;
}

public record GunPricingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("traderPricePercent")]
    public double TraderPricePercent { get; set; } = 95;
}

public record PriceDriftConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("maxDriftPercent")]
    public double MaxDriftPercent { get; set; } = 20;

    [JsonPropertyName("stepPercent")]
    public double StepPercent { get; set; } = 4;

    [JsonPropertyName("updateMinutes")]
    public double UpdateMinutes { get; set; } = 45;
}

public record SellerPricingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("overpriceChance")]
    public double OverpriceChance { get; set; } = 0.1;

    [JsonPropertyName("wornOverpriceChance")]
    public double WornOverpriceChance { get; set; } = 0.3;

    [JsonPropertyName("overpriceMinPercent")]
    public double OverpriceMinPercent { get; set; } = 10;

    [JsonPropertyName("overpriceMaxPercent")]
    public double OverpriceMaxPercent { get; set; } = 30;

    [JsonPropertyName("quickSellChance")]
    public double QuickSellChance { get; set; } = 0.03;

    [JsonPropertyName("quickSellMinPercent")]
    public double QuickSellMinPercent { get; set; } = 15;

    [JsonPropertyName("quickSellMaxPercent")]
    public double QuickSellMaxPercent { get; set; } = 35;
}
