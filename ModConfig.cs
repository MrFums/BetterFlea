using System.Text.Json;
using System.Text.Json.Serialization;
using BetterFlea.Flea;
using BetterFlea.Offers;
using BetterFlea.Prices;
using BetterFlea.Selling;

namespace BetterFlea;

public record ModConfig
{
    [JsonPropertyName("flea")]
    public FleaConfig Flea { get; set; } = new();

    [JsonPropertyName("generatedOffers")]
    public GeneratedOffersConfig GeneratedOffers { get; set; } = new();

    [JsonPropertyName("prices")]
    public PricesConfig Prices { get; set; } = new();

    [JsonPropertyName("playerSelling")]
    public PlayerSellingConfig PlayerSelling { get; set; } = new();

    [JsonPropertyName("economy")]
    public EconomyFile Economy { get; set; } = new();

    [JsonPropertyName("debugLogging")]
    public bool DebugLogging { get; set; } = false;

    [JsonPropertyName("web")]
    public WebConfig Web { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

// Files in configs/ holding more than one section. The rest map straight onto one section.

public record ListingsFile
{
    [JsonPropertyName("listings")]
    public ListingsConfig Listings { get; set; } = new();

    [JsonPropertyName("itemsPerOffer")]
    public OfferQuantityConfig ItemsPerOffer { get; set; } = new();

    [JsonPropertyName("offerCurrency")]
    public OfferCurrencyConfig OfferCurrency { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

public record WeaponsFile
{
    [JsonPropertyName("presets")]
    public PresetOffersConfig Presets { get; set; } = new();

    [JsonPropertyName("playerGuns")]
    public PlayerGunsConfig PlayerGuns { get; set; } = new();

    [JsonPropertyName("moddedParts")]
    public ModdedPartsConfig ModdedParts { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

public record MarketFile
{
    [JsonPropertyName("offerExpiry")]
    public OfferExpiryConfig OfferExpiry { get; set; } = new();

    [JsonPropertyName("simulatedSales")]
    public SimulatedSalesConfig SimulatedSales { get; set; } = new();

    [JsonPropertyName("soldBeforeYouChance")]
    public double SoldBeforeYouChance { get; set; } = 0.05;

    [JsonPropertyName("barterOffers")]
    public BarterOffersConfig BarterOffers { get; set; } = new();

    [JsonPropertyName("sellerRating")]
    public SellerRatingConfig SellerRating { get; set; } = new();

    [JsonPropertyName("sellerBadges")]
    public SellerBadgesConfig SellerBadges { get; set; } = new();

    [JsonPropertyName("packs")]
    public PackOffersConfig Packs { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

public record DebugFile
{
    [JsonPropertyName("debugLogging")]
    public bool DebugLogging { get; set; } = false;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

// configs/web.jsonc: the settings page at /betterflea.
public record WebConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}
