using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterFlea.Offers;

public record GeneratedOffersConfig
{
    /// <summary>How many offers each item gets.</summary>
    [JsonPropertyName("listings")]
    public ListingsConfig Listings { get; set; } = new();

    /// <summary>How many non-stackable items go into one offer.</summary>
    [JsonPropertyName("itemsPerOffer")]
    public OfferQuantityConfig ItemsPerOffer { get; set; } = new();

    /// <summary>Partly used keys, meds, food, fuel and repair kits.</summary>
    [JsonPropertyName("usedItems")]
    public UsedListingsConfig UsedItems { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

// Scales how many startup listings SPT generates per item (ragfair offerItemCount, 7-30 by default).
// Expired listings are replaced one for one, so the count holds after startup.
public record ListingsConfig
{
    /// <summary>Applies to every item. 1 = vanilla, 2 = twice as many listings.</summary>
    [JsonPropertyName("multiplier")]
    public double Multiplier { get; set; } = 2.0;

    /// <summary>Per category, same names as banFromFlea (e.g. "keys.markedKeys"). Replaces the global multiplier.</summary>
    [JsonPropertyName("categoryMultipliers")]
    public Dictionary<string, double> CategoryMultipliers { get; set; } = new();

    /// <summary>By item rarity (Common, Rare, Superrare), on top of the multiplier above. Common goods have more sellers.</summary>
    [JsonPropertyName("rarityMultipliers")]
    public Dictionary<string, double> RarityMultipliers { get; set; } = new()
    {
        ["Common"] = 1.5,
        ["Rare"] = 1.0,
        ["Superrare"] = 0.6
    };
}

// How many of a non-stackable item (rigs, bulbs, tea, armor...) go into one generated offer.
// SPT rolls 1-10 evenly for everything. Each item is put in a profile instead, from its type,
// its rarity and how much stash space it takes (see OfferQuantityGate for the rules).
public record OfferQuantityConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Armored rigs always come one per offer, like backpacks and body armor.</summary>
    [JsonPropertyName("armoredRigsSingle")]
    public bool ArmoredRigsSingle { get; set; } = true;

    /// <summary>Common small barter goods people farm and dump: bulbs, wires, bolts, cigarettes.</summary>
    [JsonPropertyName("bulk")]
    public QuantityProfile Bulk { get; set; } = new() { Max = 20, Falloff = 0.85 };

    /// <summary>Everyday items people hold a few of: rarer small barter goods, common meds, food and mods.</summary>
    [JsonPropertyName("stock")]
    public QuantityProfile Stock { get; set; } = new() { Max = 6, Falloff = 0.55 };

    /// <summary>Rare, valuable or bulky items, and gear: sold as they're found.</summary>
    [JsonPropertyName("few")]
    public QuantityProfile Few { get; set; } = new() { Max = 3, Falloff = 0.3 };
}

public record QuantityProfile
{
    /// <summary>Most items one offer can hold.</summary>
    [JsonPropertyName("max")]
    public int Max { get; set; } = 3;

    /// <summary>0-1. 1 per offer is the most common; each extra item is this much as likely as one fewer.</summary>
    [JsonPropertyName("falloff")]
    public double Falloff { get; set; } = 0.3;
}

// Replaces SPT's dynamic condition roll for these types.
public record UsedListingsConfig
{
    /// <summary>Mechanical keys, not marked ones.</summary>
    [JsonPropertyName("keys")]
    public KeyListingSettings Keys { get; set; } = new() { UsedChance = 0.6, UsesLeftFalloff = 0.75, OneUsePricePercent = 20 };

    [JsonPropertyName("markedKeys")]
    public KeyListingSettings MarkedKeys { get; set; } = new() { UsedChance = 0.6, UsesLeftFalloff = 0.8, OneUsePricePercent = 20 };

    [JsonPropertyName("keycards")]
    public KeyListingSettings Keycards { get; set; } = new() { UsedChance = 0.5, UsesLeftFalloff = 0.7, OneUsePricePercent = 20 };

    /// <summary>Anything with an HP resource: medkits, surgical kits, multi-use painkillers etc.</summary>
    [JsonPropertyName("meds")]
    public ResourceListingSettings Meds { get; set; } = new() { UsedChance = 0.5, LowAmountBias = 2.5 };

    [JsonPropertyName("foodAndDrink")]
    public ResourceListingSettings FoodAndDrink { get; set; } = new() { UsedChance = 0.4, LowAmountBias = 2.0 };

    /// <summary>Fuel cans and tanks.</summary>
    [JsonPropertyName("fuel")]
    public ResourceListingSettings Fuel { get; set; } = new() { UsedChance = 0.6, LowAmountBias = 2.5 };

    [JsonPropertyName("repairKits")]
    public ResourceListingSettings RepairKits { get; set; } = new() { UsedChance = 0.4, LowAmountBias = 2.0 };
}

public record KeyListingSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>0-1 share of generated listings that are partly used.</summary>
    [JsonPropertyName("usedChance")]
    public double UsedChance { get; set; } = 0.5;

    /// <summary>0-1. 1 use left is the most common; each extra use left is this much as likely as the one before.</summary>
    [JsonPropertyName("usesLeftFalloff")]
    public double UsesLeftFalloff { get; set; } = 0.75;

    /// <summary>Price of a 1-use listing as % of a brand new one. Rises in a straight line to 100% at full uses.</summary>
    [JsonPropertyName("oneUsePricePercent")]
    public double OneUsePricePercent { get; set; } = 25;
}

// Priced by SPT (resource left / max).
public record ResourceListingSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>0-1 share of generated listings that are partly used.</summary>
    [JsonPropertyName("usedChance")]
    public double UsedChance { get; set; } = 0.5;

    /// <summary>1 = amount left spread evenly. Higher = more nearly empty listings; at 2.5, about half of used ones have 20% or less left.</summary>
    [JsonPropertyName("lowAmountBias")]
    public double LowAmountBias { get; set; } = 1.0;
}
