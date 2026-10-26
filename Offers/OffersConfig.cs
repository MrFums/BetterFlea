using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterFlea.Offers;

public record GeneratedOffersConfig
{
    [JsonPropertyName("listings")]
    public ListingsConfig Listings { get; set; } = new();

    [JsonPropertyName("offerCurrency")]
    public OfferCurrencyConfig OfferCurrency { get; set; } = new();

    [JsonPropertyName("presets")]
    public PresetOffersConfig Presets { get; set; } = new();

    [JsonPropertyName("offerExpiry")]
    public OfferExpiryConfig OfferExpiry { get; set; } = new();

    [JsonPropertyName("itemsPerOffer")]
    public OfferQuantityConfig ItemsPerOffer { get; set; } = new();

    [JsonPropertyName("usedItems")]
    public UsedListingsConfig UsedItems { get; set; } = new();

    [JsonPropertyName("armor")]
    public ArmorOffersConfig Armor { get; set; } = new();

    [JsonPropertyName("barterOffers")]
    public BarterOffersConfig BarterOffers { get; set; } = new();

    [JsonPropertyName("packs")]
    public PackOffersConfig Packs { get; set; } = new();

    [JsonPropertyName("sellerRating")]
    public SellerRatingConfig SellerRating { get; set; } = new();

    [JsonPropertyName("sellerBadges")]
    public SellerBadgesConfig SellerBadges { get; set; } = new();

    [JsonPropertyName("simulatedSales")]
    public SimulatedSalesConfig SimulatedSales { get; set; } = new();

    [JsonPropertyName("playerGuns")]
    public PlayerGunsConfig PlayerGuns { get; set; } = new();

    [JsonPropertyName("soldBeforeYouChance")]
    public double SoldBeforeYouChance { get; set; } = 0.05;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

public record ListingsConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("multiplier")]
    public double Multiplier { get; set; } = 3.0;

    [JsonPropertyName("currencyMultiplier")]
    public double CurrencyMultiplier { get; set; } = 0.02;

    [JsonPropertyName("categoryMultipliers")]
    public Dictionary<string, double> CategoryMultipliers { get; set; } = new()
    {
        ["gear.armorPlates"] = 3.0,
        ["medical.medkits"] = 3.0,
        ["medical.bandagesAndSplints"] = 3.0,
        ["medical.painkillers"] = 3.0,
        ["medical.stimulants"] = 3.0
    };

    [JsonPropertyName("minimumOffers")]
    public Dictionary<string, int> MinimumOffers { get; set; } = new()
    {
        ["all"] = 30,
        ["keys.markedKeys"] = 20,
        ["keys.keycards"] = 20,
        ["keys.mechanicalKeys"] = 20,
        ["weapons.guns"] = 60
    };

    [JsonPropertyName("superrareMinimumOffers")]
    public Dictionary<string, int> SuperrareMinimumOffers { get; set; } = new()
    {
        ["weapons.guns"] = 30
    };

    [JsonPropertyName("rarityMultipliers")]
    public Dictionary<string, double> RarityMultipliers { get; set; } = new()
    {
        ["Common"] = 1.5,
        ["Rare"] = 1.0,
        ["Superrare"] = 0.6
    };

    [JsonPropertyName("onDemand")]
    public OnDemandOffersConfig OnDemand { get; set; } = new();
}

public record PlayerGunsConfig
{
    [JsonPropertyName("savedBuildChance")]
    public double SavedBuildChance { get; set; } = 0.05;

    [JsonPropertyName("lostGunChance")]
    public double LostGunChance { get; set; } = 0.35;

    [JsonPropertyName("lostGunMinMinutes")]
    public double LostGunMinMinutes { get; set; } = 5;

    [JsonPropertyName("lostGunMaxMinutes")]
    public double LostGunMaxMinutes { get; set; } = 30;

    [JsonPropertyName("killerAsSeller")]
    public bool KillerAsSeller { get; set; } = true;

    [JsonPropertyName("scopeStripChance")]
    public double ScopeStripChance { get; set; } = 0.3;

    [JsonPropertyName("suppressorStripChance")]
    public double SuppressorStripChance { get; set; } = 0.25;
}

public record OnDemandOffersConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("startShare")]
    public double StartShare { get; set; } = 0.15;

    [JsonPropertyName("minStartOffers")]
    public int MinStartOffers { get; set; } = 3;

    [JsonPropertyName("keepPages")]
    public int KeepPages { get; set; } = 4;

    [JsonPropertyName("keepSeconds")]
    public double KeepSeconds { get; set; } = 120;

    // Older configs had keepMinutes.
    [JsonPropertyName("keepMinutes")]
    public double? KeepMinutes
    {
        get => null;
        set
        {
            if (value is { } minutes)
            {
                KeepSeconds = minutes * 60;
            }
        }
    }

    [JsonPropertyName("maxExtraOffers")]
    public int MaxExtraOffers { get; set; } = 15000;

    [JsonPropertyName("maxItemsPerSearch")]
    public int MaxItemsPerSearch { get; set; } = 400;
}

public record WeaponTierConfig
{
    [JsonPropertyName("weight")]
    public double Weight { get; set; } = 1;

    [JsonPropertyName("dearWeight")]
    public double? DearWeight { get; set; }

    [JsonPropertyName("magazine")]
    public double Magazine { get; set; } = 0.5;

    [JsonPropertyName("scope")]
    public double Scope { get; set; } = 0.5;

    [JsonPropertyName("muzzle")]
    public double Muzzle { get; set; } = 0.5;

    [JsonPropertyName("suppressor")]
    public double Suppressor { get; set; } = 0.3;

    [JsonPropertyName("stock")]
    public double Stock { get; set; } = 0.5;

    [JsonPropertyName("grip")]
    public double Grip { get; set; } = 0.5;

    [JsonPropertyName("other")]
    public double Other { get; set; } = 0.5;

    [JsonPropertyName("factory")]
    public double Factory { get; set; } = 0.5;

    [JsonPropertyName("barrelSwap")]
    public double BarrelSwap { get; set; } = 0.3;

    [JsonPropertyName("stripMin")]
    public int StripMin { get; set; }

    [JsonPropertyName("stripMax")]
    public int StripMax { get; set; } = 2;

    [JsonPropertyName("statFocus")]
    public double StatFocus { get; set; } = 1;

    [JsonPropertyName("colourMatch")]
    public double ColourMatch { get; set; } = 0.5;

    [JsonPropertyName("bestOf")]
    public int BestOf { get; set; } = 1;

    [JsonPropertyName("goodMagazineChance")]
    public double GoodMagazineChance { get; set; } = 0.02;
}

public record ArmorOffersConfig
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("plates")]
    public ArmorPlateMixConfig Plates { get; set; } = new();

    [JsonPropertyName("mixedClassChance")]
    public double MixedClassChance { get; set; } = 0.2;

    [JsonPropertyName("class3Chance")]
    public double Class3Chance { get; set; } = 0.15;

    [JsonPropertyName("sidePlateChance")]
    public double SidePlateChance { get; set; } = 0.3;

    [JsonPropertyName("bestKitChance")]
    public double BestKitChance { get; set; } = 0.05;

    [JsonPropertyName("flippedDownChance")]
    public double FlippedDownChance { get; set; } = 0.3;

    [JsonPropertyName("conditionByRarity")]
    public Dictionary<string, ArmorConditionWeights> ConditionByRarity { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Common"] = new() { Mint = 0.3, Worn = 0.55, Destroyed = 0.1, Obliterated = 0.05 },
        ["Rare"] = new() { Mint = 0.4, Worn = 0.48, Destroyed = 0.08, Obliterated = 0.04 },
        ["Superrare"] = new() { Mint = 0.55, Worn = 0.37, Destroyed = 0.05, Obliterated = 0.03 }
    };
}

public record ArmorPlateMixConfig
{
    [JsonPropertyName("both")]
    public double Both { get; set; } = 0.6;

    [JsonPropertyName("frontOnly")]
    public double FrontOnly { get; set; } = 0.12;

    [JsonPropertyName("backOnly")]
    public double BackOnly { get; set; } = 0.08;

    [JsonPropertyName("none")]
    public double None { get; set; } = 0.2;
}

public record ArmorConditionWeights
{
    [JsonPropertyName("mint")]
    public double Mint { get; set; } = 0.35;

    [JsonPropertyName("worn")]
    public double Worn { get; set; } = 0.45;

    [JsonPropertyName("destroyed")]
    public double Destroyed { get; set; } = 0.12;

    [JsonPropertyName("obliterated")]
    public double Obliterated { get; set; } = 0.08;
}

public record BuildStylesConfig
{
    [JsonPropertyName("ergo")]
    public double Ergo { get; set; } = 0.45;

    [JsonPropertyName("hybrid")]
    public double Hybrid { get; set; } = 0.4;

    [JsonPropertyName("recoil")]
    public double Recoil { get; set; } = 0.15;
}

public record PresetOffersConfig
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    [JsonPropertyName("showCustomBuilds")]
    public bool ShowCustomBuilds { get; set; } = true;

    [JsonPropertyName("customBuildChance")]
    public double CustomBuildChance { get; set; } = 0.15;

    [JsonPropertyName("randomBuildChance")]
    public double RandomBuildChance { get; set; } = 1.0;

    [JsonPropertyName("stripped")]
    public WeaponTierConfig Stripped { get; set; } = new() { Weight = 0.3, DearWeight = 0.06, Magazine = 0.2, Scope = 0, Muzzle = 0.05, Suppressor = 0.02, Stock = 0.3, Grip = 0.3, Other = 0.2, Factory = 0.7, BarrelSwap = 0.1, StripMin = 4, StripMax = 5, StatFocus = 0, ColourMatch = 0, BestOf = 1 };

    [JsonPropertyName("stock")]
    public WeaponTierConfig Stock { get; set; } = new() { Weight = 0.35, DearWeight = 0.15, Magazine = 0.5, Scope = 0.1, Muzzle = 0.2, Suppressor = 0.1, Stock = 0.9, Grip = 0.8, Other = 0.6, Factory = 0.85, BarrelSwap = 0.05, StripMin = 0, StripMax = 1, StatFocus = 0.5, ColourMatch = 0.2, BestOf = 1, GoodMagazineChance = 0.03 };

    [JsonPropertyName("found")]
    public WeaponTierConfig Found { get; set; } = new() { Weight = 0.3, Magazine = 0.6, Scope = 0.85, Muzzle = 0.45, Suppressor = 0.4, Stock = 0.8, Grip = 0.75, Other = 0.8, Factory = 0.2, BarrelSwap = 0.4, StripMin = 0, StripMax = 2, StatFocus = 1.5, ColourMatch = 0.6, BestOf = 1, GoodMagazineChance = 0.15 };

    [JsonPropertyName("modded")]
    public WeaponTierConfig Modded { get; set; } = new() { Weight = 0.12, DearWeight = 0.35, Magazine = 0.85, Scope = 0.9, Muzzle = 0.6, Suppressor = 0.8, Stock = 0.9, Grip = 0.9, Other = 0.8, Factory = 0.1, BarrelSwap = 0.9, StripMin = 0, StripMax = 0, StatFocus = 4, ColourMatch = 0.95, BestOf = 3, GoodMagazineChance = 0.4 };

    [JsonPropertyName("broken")]
    public WeaponTierConfig Broken { get; set; } = new() { Weight = 0.25, Magazine = 0, Scope = 0, Muzzle = 0.1, Suppressor = 0.05, Stock = 0.5, Grip = 0.5, Other = 0.4, Factory = 0.7, BarrelSwap = 0.15, StripMin = 1, StripMax = 4, StatFocus = 0, ColourMatch = 0, BestOf = 1 };

    [JsonPropertyName("gutted")]
    public WeaponTierConfig Gutted { get; set; } = new() { Weight = 0.06, Magazine = 0, Scope = 0, Muzzle = 0, Suppressor = 0, Stock = 0.5, Grip = 0.3, Other = 0.2, Factory = 0.8, BarrelSwap = 0.1, StripMin = 0, StripMax = 0, StatFocus = 0, ColourMatch = 0, BestOf = 1 };

    [JsonPropertyName("budget")]
    public WeaponTierConfig Budget { get; set; } = new() { Weight = 0.3, DearWeight = 0.25, Magazine = 0.7, Scope = 0.9, Muzzle = 0.4, Suppressor = 0.25, Stock = 0.9, Grip = 0.8, Other = 0.7, Factory = 0.15, BarrelSwap = 0.3, StripMin = 0, StripMax = 0, StatFocus = 2.5, ColourMatch = 0.3, BestOf = 2, GoodMagazineChance = 0 };

    [JsonPropertyName("ground")]
    public WeaponTierConfig Ground { get; set; } = new() { Weight = 0.45, DearWeight = 0.25, Magazine = 0.85 };

    [JsonPropertyName("loadedMagazineChance")]
    public double LoadedMagazineChance { get; set; } = 0.3;

    [JsonPropertyName("metaBuildChance")]
    public double MetaBuildChance { get; set; } = 0.3;

    [JsonPropertyName("lootedBuildChance")]
    public double LootedBuildChance { get; set; } = 0.2;

    [JsonPropertyName("buildDealChance")]
    public double BuildDealChance { get; set; } = 0.1;

    [JsonPropertyName("buildDealMinDiscount")]
    public double BuildDealMinDiscount { get; set; } = 0.15;

    [JsonPropertyName("buildDealMaxDiscount")]
    public double BuildDealMaxDiscount { get; set; } = 0.3;

    [JsonPropertyName("buildStyles")]
    public BuildStylesConfig BuildStyles { get; set; } = new();

    [JsonPropertyName("brokenMaxParts")]
    public int BrokenMaxParts { get; set; } = 3;

    [JsonPropertyName("brokenPriceBias")]
    public double BrokenPriceBias { get; set; } = 2;

    [JsonPropertyName("maxTacticalDevices")]
    public int MaxTacticalDevices { get; set; } = 2;

    [JsonPropertyName("comboTacticalWeight")]
    public double ComboTacticalWeight { get; set; } = 3;

    [JsonPropertyName("laserLightChance")]
    public double LaserLightChance { get; set; } = 0.35;

    [JsonPropertyName("extraTacticalChance")]
    public double ExtraTacticalChance { get; set; } = 0.05;

    [JsonPropertyName("emptyMountChance")]
    public double EmptyMountChance { get; set; } = 0.3;

    [JsonPropertyName("emptyRingsChance")]
    public double EmptyRingsChance { get; set; } = 0.2;

    [JsonPropertyName("maxAttachmentDepth")]
    public int MaxAttachmentDepth { get; set; } = 5;

    [JsonPropertyName("backupSightChance")]
    public double BackupSightChance { get; set; } = 0.25;

    [JsonPropertyName("tanBuildChance")]
    public double TanBuildChance { get; set; } = 0.15;
}

public record OfferExpiryConfig
{
    [JsonPropertyName("minMinutes")]
    public int MinMinutes { get; set; } = 360;

    [JsonPropertyName("maxMinutes")]
    public int MaxMinutes { get; set; } = 1440;
}

public record PackOffersConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("chancePercent")]
    public int ChancePercent { get; set; } = 3;

    [JsonPropertyName("min")]
    public int Min { get; set; } = 2;

    [JsonPropertyName("max")]
    public int Max { get; set; } = 10;

    [JsonPropertyName("scamChance")]
    public double ScamChance { get; set; } = 0.6;

    [JsonPropertyName("hugeChance")]
    public double HugeChance { get; set; } = 0.02;

    [JsonPropertyName("hugeMin")]
    public int HugeMin { get; set; } = 50;

    [JsonPropertyName("hugeMax")]
    public int HugeMax { get; set; } = 80;
}

public record BarterOffersConfig
{
    [JsonPropertyName("chance")]
    public double Chance { get; set; } = 0.005;

    [JsonPropertyName("valueMin")]
    public double ValueMin { get; set; } = 0.85;

    [JsonPropertyName("valueMax")]
    public double ValueMax { get; set; } = 0.97;
}

public record SellerRatingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("min")]
    public double Min { get; set; } = 0.2;

    [JsonPropertyName("max")]
    public double Max { get; set; } = 300;

    [JsonPropertyName("everydayMax")]
    public double EverydayMax { get; set; } = 50;

    [JsonPropertyName("regularChance")]
    public double RegularChance { get; set; } = 0.08;

    [JsonPropertyName("flipperChance")]
    public double FlipperChance { get; set; } = 0.65;

    [JsonPropertyName("newSellerChance")]
    public double NewSellerChance { get; set; } = 0.05;
}

public record SellerBadgesConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("edgeOfDarknessChance")]
    public double EdgeOfDarknessChance { get; set; } = 0.20;

    [JsonPropertyName("unheardChance")]
    public double UnheardChance { get; set; } = 0.15;

    [JsonPropertyName("specialChance")]
    public double SpecialChance { get; set; } = 0.001;
}

public record SimulatedSalesConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("everyMinutes")]
    public double EveryMinutes { get; set; } = 5;

    [JsonPropertyName("normalSellChancePerHour")]
    public double NormalSellChancePerHour { get; set; } = 0.04;

    [JsonPropertyName("dealSpeedup")]
    public double DealSpeedup { get; set; } = 8;
}

public record OfferCurrencyConfig
{
    [JsonPropertyName("roubles")]
    public double Roubles { get; set; } = 100;

    [JsonPropertyName("dollars")]
    public double Dollars { get; set; } = 2;

    [JsonPropertyName("euros")]
    public double Euros { get; set; } = 2;
}

public record OfferQuantityConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("armoredRigsSingle")]
    public bool ArmoredRigsSingle { get; set; } = true;

    [JsonPropertyName("bulk")]
    public QuantityProfile Bulk { get; set; } = new() { Max = 35, Falloff = 0.8 };

    [JsonPropertyName("stock")]
    public QuantityProfile Stock { get; set; } = new() { Max = 6, Falloff = 0.55 };

    [JsonPropertyName("few")]
    public QuantityProfile Few { get; set; } = new() { Max = 3, Falloff = 0.3 };

    [JsonPropertyName("signalFlares")]
    public QuantityProfile SignalFlares { get; set; } = new() { Max = 2, Falloff = 0.1 };

    [JsonPropertyName("ammoBoxes")]
    public QuantityProfile AmmoBoxes { get; set; } = new() { Max = 3, Falloff = 0.5 };

    [JsonPropertyName("grenadeWeights")]
    public List<double> GrenadeWeights { get; set; } = new() { 1, 4, 3, 1, 0.5, 0.25, 0.1, 0.05 };

    [JsonPropertyName("grenadeBulk")]
    public BulkOfferConfig GrenadeBulk { get; set; } = new() { Chance = 0.04, Min = 6, Max = 12 };

    [JsonPropertyName("stimBulk")]
    public BulkOfferConfig StimBulk { get; set; } = new() { Chance = 0.03, Min = 3, Max = 6 };

    [JsonPropertyName("fullMedWeights")]
    public List<double> FullMedWeights { get; set; } = new() { 70, 16, 8, 3.5, 1.5, 1 };
}

public record BulkOfferConfig
{
    [JsonPropertyName("chance")]
    public double Chance { get; set; }

    [JsonPropertyName("min")]
    public int Min { get; set; } = 2;

    [JsonPropertyName("max")]
    public int Max { get; set; } = 2;
}

public record QuantityProfile
{
    [JsonPropertyName("max")]
    public int Max { get; set; } = 3;

    [JsonPropertyName("falloff")]
    public double Falloff { get; set; } = 0.3;
}

public record UsedListingsConfig
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    [JsonPropertyName("keys")]
    public KeyListingSettings Keys { get; set; } = new() { UsedChance = 0.4, UsesLeftFalloff = 0.97, OneUsePricePercent = 20 };

    [JsonPropertyName("markedKeys")]
    public KeyListingSettings MarkedKeys { get; set; } = new() { UsedChance = 0.45, UsesLeftFalloff = 0.9, OneUsePricePercent = 20 };

    [JsonPropertyName("keycards")]
    public KeyListingSettings Keycards { get; set; } = new() { UsedChance = 0.4, UsesLeftFalloff = 0.9, OneUsePricePercent = 20 };

    [JsonPropertyName("meds")]
    public ResourceListingSettings Meds { get; set; } = new() { UsedChance = 0.3, LowAmountBias = 1.3 };

    [JsonPropertyName("foodAndDrink")]
    public ResourceListingSettings FoodAndDrink { get; set; } = new() { UsedChance = 0.35, LowAmountBias = 1.3 };

    [JsonPropertyName("fuel")]
    public ResourceListingSettings Fuel { get; set; } = new() { UsedChance = 0.5, LowAmountBias = 1.5, EmptyChance = 0.06 };

    [JsonPropertyName("repairKits")]
    public ResourceListingSettings RepairKits { get; set; } = new() { UsedChance = 0.55, LowAmountBias = 2.0 };

    [JsonPropertyName("guns")]
    public GunDurabilityConfig Guns { get; set; } = new();
}

public record GunDurabilityConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("wornChanceByRarity")]
    public Dictionary<string, double> WornChanceByRarity { get; set; } = new()
    {
        ["Common"] = 0.9,
        ["Rare"] = 0.65,
        ["Superrare"] = 0.35
    };

    [JsonPropertyName("maxWear")]
    public double MaxWear { get; set; } = 40;

    [JsonPropertyName("lowMaxChance")]
    public double LowMaxChance { get; set; } = 0.25;

    [JsonPropertyName("lowMaxMin")]
    public double LowMaxMin { get; set; } = 55;

    [JsonPropertyName("lowMaxMax")]
    public double LowMaxMax { get; set; } = 95;

    [JsonPropertyName("wearByBuild")]
    public Dictionary<string, double> WearByBuild { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["meta"] = 0.5,
        ["player"] = 0.8,
        ["modded"] = 0.6,
        ["budget"] = 0.9,
        ["stock"] = 1.0,
        ["found"] = 1.1,
        ["looted"] = 1.3,
        ["stripped"] = 1.5,
        ["gutted"] = 1.8,
        ["broken"] = 2.0
    };
}

public record KeyListingSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("usedChance")]
    public double UsedChance { get; set; } = 0.5;

    [JsonPropertyName("usesLeftFalloff")]
    public double UsesLeftFalloff { get; set; } = 0.75;

    [JsonPropertyName("oneUsePricePercent")]
    public double OneUsePricePercent { get; set; } = 25;
}

public record ResourceListingSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("usedChance")]
    public double UsedChance { get; set; } = 0.5;

    [JsonPropertyName("lowAmountBias")]
    public double LowAmountBias { get; set; } = 1.0;

    // Fuel only: cans listed bone dry.
    [JsonPropertyName("emptyChance")]
    public double EmptyChance { get; set; }

    [JsonPropertyName("emptyScamChance")]
    public double EmptyScamChance { get; set; } = 0.3;
}
