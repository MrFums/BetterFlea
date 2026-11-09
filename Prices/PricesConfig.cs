using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterFlea.Prices;

public record PricesConfig
{
    [JsonPropertyName("priceMultiplier")]
    public double PriceMultiplier { get; set; } = 1.0;

    // Item or category ID -> multiplier, on top of priceMultiplier.
    [JsonPropertyName("categoryMultipliers")]
    public Dictionary<string, double> CategoryMultipliers { get; set; } = new();

    [JsonPropertyName("floor")]
    public PriceFloorConfig Floor { get; set; } = new();

    [JsonPropertyName("traderSellFloor")]
    public TraderSellFloorConfig TraderSellFloor { get; set; } = new();

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

    [JsonPropertyName("unusableKeys")]
    public UnusableKeysConfig UnusableKeys { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

// Keys that open nothing: no lock on any map takes them and no quest needs them. Nobody can have used one, so they're never listed used, and with no use
// they're worth little: pricePercent of the usual price, never under what a trader pays (the floor).
public record UnusableKeysConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("pricePercent")]
    public double PricePercent { get; set; } = 10;

    [JsonPropertyName("keys")]
    public List<string> Keys { get; set; } =
    [
        "590de4a286f77423d9312a32", // Folding car key
        "59136f6f86f774447a1ed173", // Folding car key
        "5a0f045e86f7745b0f0d0e42", // Gas station safe key
        "5a0ea69f86f7741cd5406619", // Health Resort east wing office room 108 key
        "5a0ee62286f774369454a7ac", // Health Resort east wing room 209 key
        "5a0ee72c86f77436955d3435", // Health Resort east wing room 213 key
        "5a0ee76686f7743698200d5c", // Health Resort east wing room 216 key
        "5a0eedb386f77403506300be", // Health Resort east wing room 322 key
        "5a0ec70e86f7742c0b518fba", // Health Resort west wing room 207 key
        "5a0eeb1a86f774688b70aa5c", // Health Resort west wing room 303 key
        "5a0eeb8e86f77461257ed71a", // Health Resort west wing room 309 key
        "5a13ee1986f774794d4c14cd", // Health Resort west wing room 323 key
        "5a0eebed86f77461230ddb3d", // Health Resort west wing room 325 key
        "62a09ec84f842e1bd12da3f2", // Missam forklift key
        "63a39f18c2d53c2c6839c1d3", // Pinewood hotel room 206 key
        "593858c486f774253a24cb52", // Pumping station back door key
        "57a349b2245977762b199ec7", // Pumping station front door key
        "5d80cb8786f774405611c7d9", // RB-PP key
        "5a0f075686f7745bcc42ee12", // Store safe key
        "6582dc63cafcd9485374dbc5", // Unity Credit Bank archive room key
        "5913651986f774432f15d132", // VAZ car key
        "59148f8286f7741b951ea113", // Weapon safe key
        "5a0f006986f7741ffd2fe484", // Weather station safe key
        "591ae8f986f77406f854be45", // Yotota car key
    ];

    public bool Has(string tpl) => Enabled && Keys.Contains(tpl);
}

public record PriceFloorConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("marginPercent")]
    public double MarginPercent { get; set; } = 10;

    [JsonPropertyName("logTraderProfitLoops")]
    public bool LogTraderProfitLoops { get; set; } = false;

    // Not supported. No floor for an item one trader sells for less than another pays (and its parts): the profit is
    // there through the traders anyway, so the flea may go under it. Selling flea buys to a trader then pays too.
    [JsonPropertyName("ignoreForTraderLoops")]
    public bool IgnoreForTraderLoops { get; set; } = false;
}

// Offers never under what a trader sells the item for (plus the margin), on top of the floor above. Off by default.
public record TraderSellFloorConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("marginPercent")]
    public double MarginPercent { get; set; } = 0;
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

    // With partsAtFleaPrice: a part on a gun is worth this % of its own flea price (a 100k gun with a 50k grip is
    // about 135k, not 150k). The gun's default parts, sights, scopes, suppressors, tactical devices and loaded rounds
    // keep their full price. 100 = parts at their full price.
    [JsonPropertyName("attachedPartPercent")]
    public double AttachedPartPercent { get; set; } = 70;

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
