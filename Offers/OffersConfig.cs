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

    [JsonPropertyName("moddedParts")]
    public ModdedPartsConfig ModdedParts { get; set; } = new();

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
        ["keys.markedKeys"] = 2.0,
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
    public double SavedBuildChance { get; set; } = 0.003;

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

    // A helmet comes with an attachment on it (a Bastion's armor plate, a shroud), as its last owner had it.
    [JsonPropertyName("helmetAttachmentChance")]
    public double HelmetAttachmentChance { get; set; } = 0.2;

    // Helmets are rarely badly damaged.
    // 1 = like the rest, 0 = never badly damaged.
    [JsonPropertyName("helmetDamage")]
    public double HelmetDamage { get; set; } = 0.5;

    // A helmet a boss or guard can wear sometimes comes off one: good max, low current (they were shot to death).
    [JsonPropertyName("bossHelmetChance")]
    public double BossHelmetChance { get; set; } = 0.25;

    // A piece repaired down (lower max) now and then has a repair kit bonus (the gold background). Very rare.
    [JsonPropertyName("repairBuffChance")]
    public double RepairBuffChance { get; set; } = 0.02;

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
    public double BuildDealChance { get; set; } = 0.05;

    [JsonPropertyName("buildDealMinDiscount")]
    public double BuildDealMinDiscount { get; set; } = 0.2;

    [JsonPropertyName("buildDealMaxDiscount")]
    public double BuildDealMaxDiscount { get; set; } = 0.35;

    // A rarer, deeper deal than a build deal.
    [JsonPropertyName("buildStealChance")]
    public double BuildStealChance { get; set; } = 0.01;

    [JsonPropertyName("buildStealMinDiscount")]
    public double BuildStealMinDiscount { get; set; } = 0.4;

    [JsonPropertyName("buildStealMaxDiscount")]
    public double BuildStealMaxDiscount { get; set; } = 0.6;

    // Near-stock offers on top of a gun's others: the factory gun, now and then with a cheap sight or no magazine.
    [JsonPropertyName("nearStockMin")]
    public int NearStockMin { get; set; } = 5;

    [JsonPropertyName("nearStockMax")]
    public int NearStockMax { get; set; } = 15;

    // A seller who forgot the scope was on it: the build priced as if it weren't.
    [JsonPropertyName("forgotScopeChance")]
    public double ForgotScopeChance { get; set; } = 0.007;

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
    public int MaxMinutes { get; set; } = 7200;

    // How long people list for, in hours, by weight. Live, nearly everyone picks 12 or 24. Empty = any time from
    // minMinutes to maxMinutes.
    [JsonPropertyName("hours")]
    public Dictionary<string, double> Hours { get; set; } = new() { ["12"] = 0.3, ["24"] = 0.7 };

    // A big stack (this many or more of an item that doesn't stack) is often listed for days, up to maxMinutes.
    [JsonPropertyName("longStackMin")]
    public int LongStackMin { get; set; } = 10;

    [JsonPropertyName("longChance")]
    public double LongChance { get; set; } = 0.35;

    // How fresh offers are when found. 1 = anywhere in their time, higher = most just listed, so few show the red
    // "under 10 hours" time. Deals are fresher still, overpriced offers older.
    [JsonPropertyName("freshness")]
    public double Freshness { get; set; } = 20;
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
    public double Min { get; set; } = 2;

    [JsonPropertyName("max")]
    public double Max { get; set; } = 300;

    [JsonPropertyName("everydayMax")]
    public double EverydayMax { get; set; } = 100;

    [JsonPropertyName("regularChance")]
    public double RegularChance { get; set; } = 0.25;

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
    public double EdgeOfDarknessChance { get; set; } = 0.25;

    [JsonPropertyName("unheardChance")]
    public double UnheardChance { get; set; } = 0.20;

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

    // The cheapest few offers of every item sell at this chance per hour for an everyday item (less for dear or rare
    // ones, more for busy ones) and are relisted fresh, so the market keeps moving while nobody looks. 0 = off.
    [JsonPropertyName("topSellChancePerHour")]
    public double TopSellChancePerHour { get; set; } = 0.5;

    [JsonPropertyName("dealSpeedup")]
    public double DealSpeedup { get; set; } = 20;

    // An offer 10% or more under normal is bought by someone else this long after it appears (random between).
    [JsonPropertyName("dealGoneMinMinutes")]
    public double DealGoneMinMinutes { get; set; } = 1;

    [JsonPropertyName("dealGoneMaxMinutes")]
    public double DealGoneMaxMinutes { get; set; } = 6;
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

    // Rarely someone sells off a whole batch of one stim they had stored (a dozen Propitals).
    [JsonPropertyName("stimBatch")]
    public BulkOfferConfig StimBatch { get; set; } = new() { Chance = 0.01, Min = 8, Max = 12 };

    [JsonPropertyName("fullMedWeights")]
    public List<double> FullMedWeights { get; set; } = new() { 70, 16, 8, 3.5, 1.5, 1 };

    // Now and then someone dumps a big stack of full meds or drinks (an IFAK x30, water x31).
    [JsonPropertyName("fullStacks")]
    public BulkOfferConfig FullStacks { get; set; } = new() { Chance = 0.03, Min = 10, Max = 30 };

    // A flipper's stack of used medkits, all at the same amount (four AFAK stacks of 9 to 27 at 324-336/400).
    [JsonPropertyName("usedMedStacks")]
    public BulkOfferConfig UsedMedStacks { get; set; } = new() { Chance = 0.03, Min = 5, Max = 27 };

    // Factory guns as found (ground loot) now and then several to an offer: people farming a boss list their haul.
    [JsonPropertyName("stockGunStacks")]
    public BulkOfferConfig StockGunStacks { get; set; } = new() { Chance = 0.12, Min = 2, Max = 6 };

    // A trader's gun bought to flip: the exact factory gun at full durability, several to an offer. Chance per offer of
    // a gun a trader sells.
    // Helmets that are easy to come by (not superrare, or a trader sells them) now and then several to an offer, only
    // at max/max durability. Chance per such offer.
    [JsonPropertyName("helmetStacks")]
    public BulkOfferConfig HelmetStacks { get; set; } = new() { Chance = 0.05, Min = 2, Max = 5 };

    [JsonPropertyName("traderGunStacks")]
    public BulkOfferConfig TraderGunStacks { get; set; } = new() { Chance = 0.02, Min = 2, Max = 5 };
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
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public KeyListingSettings Keys { get; set; } = new()
    {
        UsedChance = 0.12, UsesLeftFalloff = 1.05, OneUsePricePercent = 20,
        UsedChanceByRarity = new(StringComparer.OrdinalIgnoreCase) { ["Common"] = 0.5, ["Rare"] = 1, ["Superrare"] = 2 }
    };

    [JsonPropertyName("markedKeys")]
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public KeyListingSettings MarkedKeys { get; set; } = new() { UsedChance = 0.88, UsesLeftFalloff = 0.6, OneUsePricePercent = 35, OneDownChance = 0.1 };

    [JsonPropertyName("keycards")]
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public KeyListingSettings Keycards { get; set; } = new() { UsedChance = 0.4, UsesLeftFalloff = 0.9, OneUsePricePercent = 20 };

    [JsonPropertyName("meds")]
    public ResourceListingSettings Meds { get; set; } = new() { UsedChance = 0.12, LowAmountBias = 1.3, UsedPricePercent = 90, AmountPriceWeight = 0.5 };

    [JsonPropertyName("foodAndDrink")]
    public ResourceListingSettings FoodAndDrink { get; set; } = new() { UsedChance = 0.08, LowAmountBias = 1.3, UsedPricePercent = 68, AmountPriceWeight = 0.15 };

    [JsonPropertyName("fuel")]
    public ResourceListingSettings Fuel { get; set; } = new() { UsedChance = 0.5, LowAmountBias = 1.5, EmptyChance = 0.06, UsedPricePercent = 85, AmountPriceWeight = 0.2 };

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

    // Share of worn guns whose max is lowered. A gun nobody has used has never been repaired.
    [JsonPropertyName("lowMaxChance")]
    public double LowMaxChance { get; set; } = 0.85;

    // Mostly near lowMaxMax. Around 93 a gun starts to jam, so most are sold before that.
    [JsonPropertyName("lowMaxMin")]
    public double LowMaxMin { get; set; } = 93;

    [JsonPropertyName("lowMaxMax")]
    public double LowMaxMax { get; set; } = 99;

    // A gun repaired down (lower max) now and then has a repair kit bonus (the gold background). Very rare.
    [JsonPropertyName("repairBuffChance")]
    public double RepairBuffChance { get; set; } = 0.03;

    // Guns only bosses and their guards carry (M60E6, PKP, Mk 43, AK-50): people farm the boss and list them as
    // found. This share is listed as ground loot, and the rest are worn this much as often (0.15 = 15%).
    [JsonPropertyName("bossGunGroundShare")]
    public double BossGunGroundShare { get; set; } = 0.6;

    [JsonPropertyName("bossGunWear")]
    public double BossGunWear { get; set; } = 0.15;

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

    // Share of used keys only one use down (9/10): used once and sold on.
    [JsonPropertyName("oneDownChance")]
    public double OneDownChance { get; set; } = 0;

    // usedChance times this, by rarity. Cheap common keys are bought for one quest and sold on new.
    [JsonPropertyName("usedChanceByRarity")]
    public Dictionary<string, double> UsedChanceByRarity { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

    // Price of a used one, in % of a full one, and how much the amount left counts on top (0 = not at all,
    // 1 = all of it). 100 and 1 = SPT's own: price follows the amount left.
    [JsonPropertyName("usedPricePercent")]
    public double UsedPricePercent { get; set; } = 100;

    [JsonPropertyName("amountPriceWeight")]
    public double AmountPriceWeight { get; set; } = 1;
}

// Attachments from server mods on other sellers' gun builds.
public record ModdedPartsConfig
{
    // How often a modded part is picked, in % of a base-game part that fits as well. 0 = never.
    [JsonPropertyName("percent")]
    public double Percent { get; set; } = 100;

    // Mods whose parts are left off builds (by the name on the Bans tab). A part one is still used where nothing else
    // fits a part the gun needs, so a mod that depends on it (JSOC needs Epic's All in One) still gets built.
    [JsonPropertyName("disabledMods")]
    public List<string> DisabledMods { get; set; } = new();

    // Modded parts their mod marks as not sellable on the flea can still go on builds. Some mods mark a part a gun
    // needs (WTT's F2000 top rail, without which it can't take a scope), so this gives those guns more variety.
    [JsonPropertyName("allowFleaBannedParts")]
    public bool AllowFleaBannedParts { get; set; } = false;

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Unknown { get; set; }
}
