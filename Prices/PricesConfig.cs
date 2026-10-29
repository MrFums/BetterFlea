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

    [JsonPropertyName("spread")]
    public PriceSpreadConfig Spread { get; set; } = new();

    [JsonPropertyName("demand")]
    public DemandConfig Demand { get; set; } = new();

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

    // A gun's parts are worth their own flea price; only the gun itself goes by trader prices.
    [JsonPropertyName("partsAtFleaPrice")]
    public bool PartsAtFleaPrice { get; set; } = true;

    // 0 = the trader's price, 1 = the full flea price.
    [JsonPropertyName("fleaShareByLoyalty")]
    public Dictionary<string, double> FleaShareByLoyalty { get; set; } = new()
    {
        ["1"] = 0,
        ["2"] = 0,
        ["3"] = 0.2,
        ["4"] = 0.5
    };

    // By how hard the quest (or hidden trader) the part is locked behind is: Prices/QuestDifficulty.cs.
    [JsonPropertyName("fleaShareByDifficulty")]
    public Dictionary<string, double> FleaShareByDifficulty { get; set; } = new()
    {
        ["easy"] = 0,
        ["medium"] = 0.2,
        ["hard"] = 0.5,
        ["veryHard"] = 0.9
    };

    // What a gun at half durability is worth, in percent of a new one. 50 = straight line, SPT's own is about 71.
    [JsonPropertyName("priceAtHalfDurabilityPercent")]
    public double PriceAtHalfDurabilityPercent { get; set; } = 50;

    // Where other sellers' guns are listed against what they're worth, in percent: a little under, as a rule.
    [JsonPropertyName("offerMinPercent")]
    public double OfferMinPercent { get; set; } = -12;

    [JsonPropertyName("offerMaxPercent")]
    public double OfferMaxPercent { get; set; } = 0;

    // Near-stock guns (the factory gun), a bit cheaper again.
    [JsonPropertyName("stockOfferMinPercent")]
    public double StockOfferMinPercent { get; set; } = -15;

    [JsonPropertyName("stockOfferMaxPercent")]
    public double StockOfferMaxPercent { get; set; } = -3;
}

// How far an ordinary offer's price strays from normal, in percent. SPT's own is -20 to +20, which puts an offer 20%
// under normal at the top of nearly every item's list.
public record PriceSpreadConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("minPercent")]
    public double MinPercent { get; set; } = -4;

    [JsonPropertyName("maxPercent")]
    public double MaxPercent { get; set; } = 15;
}

// Buying up an item makes it dearer for a while; players listing a lot of one item now and then make it cheaper.
public record DemandConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("buyRisePercent")]
    public double BuyRisePercent { get; set; } = 2;

    [JsonPropertyName("maxRisePercent")]
    public double MaxRisePercent { get; set; } = 30;

    [JsonPropertyName("floodChance")]
    public double FloodChance { get; set; } = 0.15;

    [JsonPropertyName("floodDropPercent")]
    public double FloodDropPercent { get; set; } = 3;

    [JsonPropertyName("maxDropPercent")]
    public double MaxDropPercent { get; set; } = 15;

    [JsonPropertyName("recoverMinutes")]
    public double RecoverMinutes { get; set; } = 60;
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
    public double OverpriceMaxPercent { get; set; } = 50;

    [JsonPropertyName("quickSellChance")]
    public double QuickSellChance { get; set; } = 0.004;

    [JsonPropertyName("quickSellMinPercent")]
    public double QuickSellMinPercent { get; set; } = 10;

    [JsonPropertyName("quickSellMaxPercent")]
    public double QuickSellMaxPercent { get; set; } = 25;

    [JsonPropertyName("roundPriceChance")]
    public double RoundPriceChance { get; set; } = 0.75;

    // Troll prices: 5 to 15 times normal, at the end of the list. Guns get more of them.
    [JsonPropertyName("trollChance")]
    public double TrollChance { get; set; } = 0.004;

    [JsonPropertyName("trollGunChance")]
    public double TrollGunChance { get; set; } = 0.04;

    [JsonPropertyName("trollMinMultiplier")]
    public double TrollMinMultiplier { get; set; } = 5;

    [JsonPropertyName("trollMaxMultiplier")]
    public double TrollMaxMultiplier { get; set; } = 15;
}
