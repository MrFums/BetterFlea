using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterFlea.Prices;

// configs/economy.jsonc
public record EconomyFile
{
    [JsonPropertyName("betterFleaPricing")]
    public BetterFleaPricingConfig BetterFleaPricing { get; set; } = new();

    [JsonPropertyName("livePrices")]
    public LivePricesConfig LivePrices { get; set; } = new();

    [JsonPropertyName("wipeCycle")]
    public WipeCycleConfig WipeCycle { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

// Better Flea's own fixed price for every base-game item (built from real PvP and PvE flea and trader prices) as the
// base price. Takes the place of live prices while it's on.
public record BetterFleaPricingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    // Items the real game doesn't have (modded items) get a price on the same scale, from similar base-game items.
    [JsonPropertyName("estimateModdedItems")]
    public bool EstimateModdedItems { get; set; } = true;

    // Base-game items that WTT ContentBackport adds (SPT 4.1.6 doesn't have them yet) get Better Flea's price too.
    // Off: they keep the price ContentBackport gives them.
    [JsonPropertyName("contentBackport")]
    public bool ContentBackport { get; set; } = true;

    // Traders pay what they do in the live game: each takes its live cut (Therapist pays 51% of an item's base price,
    // not SPT's 63%). Off: SPT's own.
    [JsonPropertyName("liveTraderPrices")]
    public bool LiveTraderPrices { get; set; } = true;
}

// Real flea prices (the 24 hour average from tarkov.dev) as the base price of every item.
public record LivePricesConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    // Which game's prices to use: "live" (PvP), "pve" or "season".
    [JsonPropertyName("gameMode")]
    public string GameMode { get; set; } = "live";

    [JsonPropertyName("refreshHours")]
    public double RefreshHours { get; set; } = 24;

    // Items the real game doesn't have (modded items) get a price on the same scale, from similar base-game
    // items. Base-game items added by mods (content backports) aren't counted as modded.
    [JsonPropertyName("estimateModdedItems")]
    public bool EstimateModdedItems { get; set; } = true;

    // The tarkov.dev name of the game mode.
    public string Endpoint() => GameMode.ToLowerInvariant() switch
    {
        "pve" => "pve",
        "season" => "pvp-season",
        _ => "regular"
    };
}

// Prices follow how far into the wipe the players on the server are.
public record WipeCycleConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    // 1 = normal size of the swings, 0.5 = half, 2 = double.
    [JsonPropertyName("strength")]
    public double Strength { get; set; } = 1.0;

    // Prices move every this many levels.
    [JsonPropertyName("levelStep")]
    public int LevelStep { get; set; } = 3;

    // After the flea's level changes, it stays put this long.
    [JsonPropertyName("cooldownMinutes")]
    public double CooldownMinutes { get; set; } = 30;

    // About the level where a wipe "ends".
    [JsonPropertyName("endLevel")]
    public int EndLevel { get; set; } = 60;

    // What a prestiged player counts as: "reset" = a fresh wipe from their new level, "add" = late wipe (55 levels
    // per prestige on top of their level).
    [JsonPropertyName("prestige")]
    public string Prestige { get; set; } = "add";

    // For testing: pretend the players are this level. 0 = the highest level of the players online.
    [JsonPropertyName("level")]
    public int Level { get; set; }
}
