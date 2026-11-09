using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterFlea.Selling;

public record PlayerSellingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("foundInRaidOnly")]
    public bool FoundInRaidOnly { get; set; } = false;

    // false = no pop-ups or Messenger messages about the flip penalty or a trader item that can't be listed.
    [JsonPropertyName("notifications")]
    public bool Notifications { get; set; } = true;

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

    [JsonPropertyName("saleTime")]
    public SaleTimeConfig SaleTime { get; set; } = new();

    [JsonPropertyName("fleaAccess")]
    public FleaAccessConfig FleaAccess { get; set; } = new();

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

    // A gun, armour or anything with durability at the normal price usually waits this long at 20,000 roubles or
    // less, times the square root of how many times dearer it is, never over uniqueMaxMinutes (SaleDelayGate.PriceWait).
    // A dear plain item that seldom trades waits the same.
    [JsonPropertyName("uniqueMinMinutes")]
    public double UniqueMinMinutes { get; set; } = 1;

    [JsonPropertyName("uniqueMaxMinutes")]
    public double UniqueMaxMinutes { get; set; } = 60;

    // A rare or superrare item (the game's own rarity) waits this many times as long at the normal price.
    [JsonPropertyName("rareWaitMultiplier")]
    public double RareWaitMultiplier { get; set; } = 1.25;

    [JsonPropertyName("superrareWaitMultiplier")]
    public double SuperrareWaitMultiplier { get; set; } = 1.5;

    [JsonPropertyName("slowSaleMaxHours")]
    public double SlowSaleMaxHours { get; set; } = 4;

    [JsonPropertyName("expensivePercent")]
    public double ExpensivePercent { get; set; } = 15;

    // A safety cap: this far over normal nothing sells. At the defaults the rouble rule (OverpriceX) stops sales by
    // 78% over at most, so this only matters with gentler settings.
    [JsonPropertyName("neverSellsPercent")]
    public double NeverSellsPercent { get; set; } = 100;

    // 2.5.1's overpricing in place of the rouble room (SaleDelayGate.Classic): every item gets the same room as a share
    // of its price, with no rouble room, no gear room and no less room on dear items, and buyers keep coming over
    // the offer's hours. Off by default and not recommended: only for anyone who preferred how 2.5.1 felt.
    [JsonPropertyName("classicOverpricing")]
    public bool ClassicOverpricing { get; set; } = false;

    // How far over normal an offer can go is judged in roubles (SaleDelayGate.OverpriceX): the roubles over against
    // 0.8 x expensivePercent of the normal price plus this many roubles...
    [JsonPropertyName("overpriceRoubles")]
    public double OverpriceRoubles { get; set; } = 8000;

    // ...that part never more than this share of the normal price, so a cheap item doesn't get far more room.
    [JsonPropertyName("overpriceRoublesMaxPercent")]
    public double OverpriceRoublesMaxPercent { get; set; } = 40;

    // Over 100,000 roubles the share of the room shrinks by (100,000 / normal)^this, so a dear item can't go far
    // over in roubles. 0 = the same share for every price.
    [JsonPropertyName("overpriceDearShrink")]
    public double OverpriceDearShrink { get; set; } = 0.2;

    // Over 100,000 roubles the wait over normal is (normal / 100,000)^this times as long. 0 = no longer.
    [JsonPropertyName("overpriceDearSlowdown")]
    public double OverpriceDearSlowdown { get; set; } = 0.4;

    // The usual extra wait, in minutes, for an offer a whole room over normal (x 1): x^1.5 times this.
    [JsonPropertyName("overpriceWaitMinutes")]
    public double OverpriceWaitMinutes { get; set; } = 30;

    // From this normal price in roubles an item counts as dear (overpriceDearShrink, overpriceDearSlowdown).
    [JsonPropertyName("dearFromRoubles")]
    public double DearFromRoubles { get; set; } = 100000;

    // Gear (a gun, armour, a helmet, a rig with plates, anything with durability or with parts in it) gets this % of
    // its normal price as room over normal, instead of 0.8 x expensivePercent: its value hangs on its parts, plates
    // and wear, so nobody, buyer or seller, knows it to the rouble. Single items (wires, a LEDX, a key) don't.
    [JsonPropertyName("gearOverpricePercent")]
    public double GearOverpricePercent { get; set; } = 20;

    // Gear dearer than this many roubles gets less of that room, by (this / normal)^overpriceDearShrink.
    [JsonPropertyName("gearDearFromRoubles")]
    public double GearDearFromRoubles { get; set; } = 300000;

    // Wrecked armour or a wrecked helmet waits this many times as long for its buyer.
    [JsonPropertyName("wreckedWaitMultiplier")]
    public double WreckedWaitMultiplier { get; set; } = 3;

    // One thing with durability listed on its own (glasses, a bare helmet, a plate) that another seller has cheaper waits
    // this many times as long. Guns and gear with parts or plates in them aren't compared (SaleDelayGate.BeginListing).
    [JsonPropertyName("cheaperWaitMultiplier")]
    public double CheaperWaitMultiplier { get; set; } = 2;
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

    // How much longer buyers take for an item of each rarity than for a common one. 1 = no longer.
    [JsonPropertyName("rarityMultipliers")]
    public Dictionary<string, double> RarityMultipliers { get; set; } = new()
    {
        ["Common"] = 1.0,
        ["Rare"] = 1.5,
        ["Superrare"] = 3.0
    };
}

// Set sale times: a player's offer sells after a wait set per rarity, in place of the wait worked out from buyers,
// other sellers and undercuts.
public record SaleTimeConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    // true = sells whatever its price. false = over normal it sells less often and later, as with saleDelay.
    [JsonPropertyName("ignorePrice")]
    public bool IgnorePrice { get; set; } = false;

    // The defaults are about what an item at the normal price takes with this off.
    [JsonPropertyName("commonMinMinutes")]
    public double CommonMinMinutes { get; set; } = 0;

    [JsonPropertyName("commonMaxMinutes")]
    public double CommonMaxMinutes { get; set; } = 1;

    [JsonPropertyName("rareMinMinutes")]
    public double RareMinMinutes { get; set; } = 1;

    [JsonPropertyName("rareMaxMinutes")]
    public double RareMaxMinutes { get; set; } = 3;

    [JsonPropertyName("superrareMinMinutes")]
    public double SuperrareMinMinutes { get; set; } = 3;

    [JsonPropertyName("superrareMaxMinutes")]
    public double SuperrareMaxMinutes { get; set; } = 8;
}

// The level the Flea Market opens at, how many offers a player can have up, and how fast their flea rep grows. The
// defaults leave SPT's (or another mod's) values alone.
public record FleaAccessConfig
{
    // SPT's is 15.
    [JsonPropertyName("unlockLevel")]
    public int UnlockLevel { get; set; } = 15;

    // Flea rep from each sale x this.
    [JsonPropertyName("repGainMultiplier")]
    public double RepGainMultiplier { get; set; } = 1;

    // Offer slots go up with flea rep. Each rep tier's slots x offerSlotsMultiplier, then + this, never under 1.
    [JsonPropertyName("extraOfferSlots")]
    public int ExtraOfferSlots { get; set; } = 0;

    [JsonPropertyName("offerSlotsMultiplier")]
    public double OfferSlotsMultiplier { get; set; } = 1;
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
    // Off by default. On: buying many of one item and listing it straight away waits longer, and anything bought in the
    // last 48 hours (its parts too) never sells more than expensivePercent over normal (FlipPenaltyGate.BoughtLately).
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("windowMinutes")]
    public double WindowMinutes { get; set; } = 30;

    [JsonPropertyName("freePurchases")]
    public double FreePurchases { get; set; } = 2;

    [JsonPropertyName("purchasesForMaxPenalty")]
    public double PurchasesForMaxPenalty { get; set; } = 10;

    [JsonPropertyName("maxExtraMinutes")]
    public double MaxExtraMinutes { get; set; } = 10;

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
