# Settings reference

[Back to the guide](README.md)

Every setting, tab by tab. Each tab is one file in `configs/`, and the names in `code` are the keys in that file, so
you can find a setting by searching for its key on the settings page or in the file. Defaults are what ships.

Chances run from 0 (never) to 1 (always): 0.25 is 25%. Percents are percents: 10 is 10%.

**Contents:** [Bans](#bans-tab) · [Listings](#listings-tab) · [Condition](#condition-tab) · [Weapons](#weapons-tab) ·
[Armour](#armour-tab) · [Market](#market-tab) · [Prices](#prices-tab) · [Selling](#selling-tab) ·
[Economy](#economy-tab) · [Debug](#debug-tab) · [Web](#web)

---

## Bans tab

`configs/flea.jsonc`. What can be bought and sold on the flea. **Bans need a server restart, and a game restart for
players**, because the game reads which items can be sold only when it starts.

| Setting | Default | What it does |
|---|---|---|
| `enableBsgBlacklist` | on | Keep BSG's own flea bans (M995, Bitcoins, LEDX and so on). Off lifts them so they can be listed and bought. Some things stay off whatever you pick: quest items, money, dogtags, secure containers, armbands, SPT's dev and test items, and anything the real game can't trade at all. Mods' own bans stay too, unless you allow them under Mod items. |
| `bannedCategories` | all off | Take a whole kind of item off the flea: guns, throwables, melee, rounds, ammo boxes, magazines, sights, suppressors, other weapon parts, body armour, armoured rigs, chest rigs, backpacks, plates, headwear, helmet parts, face covers, eyewear, headphones, armbands, containers, mechanical keys, marked keys, keycards, medkits, bandages and splints, painkillers, stims, food and drink, barter items, info items, maps, posters, special equipment. Each item is in exactly one of these. |
| `bannedItems` | none | Single items (or SPT category ids) that are never on the flea. Search by name on the page. |
| `allowedItems` | none | Single items that are always on the flea, even when their kind is banned above. "Ban all guns except the Mosin" is a banned category plus one allowed item. |
| `bannedModItems` | none | Ban what another server mod adds: everything (`["all"]`) or some kinds (`["weapons.guns"]`). The page shows one card per mod that adds items. |
| `allowedModItems` | none | The other way round: let on the flea items a mod itself marks as not sellable there. Everything, or some kinds. |
| `levelLock.enabled` | off | Item unlock levels, like live: some items can only be bought and sold from a set level (the M4A1 from 25, a graphics card from 40). The real game's levels. With the plugin, items show their level when inspected and flea categories show a lock. |
| `levelLock.allowSellingBelowLevel` | off | Let players list items they can't buy yet. |
| `levelLock.moddedItems` | on | Modded items unlock with the base-game items of their kind worth about the same. A strong modded stim needs the level the strongest real stims need. |
| `levelLock.levels` | none | Your own levels by item or category id. `0` = no level. An item's own level beats its category's. Advanced. |

**Recipes**
- *Hardcore, no guns or armour on the flea:* ban `weapons.guns`, `gear.bodyArmor`, `gear.armoredRigs`.
- *Only one gun allowed:* ban `weapons.guns`, then add that gun to allowed items.
- *A mod's items are too strong to buy:* Mod items, that mod's card, Ban.
- *A mod keeps its own items off the flea and you want them on:* Mod items, Allow.

---

## Listings tab

`configs/listings.jsonc`. How many offers the server makes, how many items each holds, and which currency it asks
for. Only generated offers; players' own are untouched.

### How many offers

| Setting | Default | What it does |
|---|---|---|
| `listings.enabled` | on | Off = SPT's own counts (7 to 30 offers per item). |
| `listings.multiplier` | 3.0 | Offers per item against SPT's. 1 = SPT's, 0 = no generated offers (traders only), 10 at most. |
| `listings.currencyMultiplier` | 0.02 | Offers selling money itself (dollars, euros). Live almost never has these. 0 = none. |
| `listings.categoryMultipliers` | plates 3, marked keys 2, meds 3 | Extra for a kind of item, on top of everything, minimums included. |
| `listings.minimumOffers` | all 30, keys 20, guns 60 | The fewest offers a kind gets, whatever the multipliers say. |
| `listings.superrareMinimumOffers` | guns 30 | A lower minimum for superrare items. |
| `listings.rarityMultipliers` | common 1.5, rare 1, superrare 0.6 | More offers for common items, fewer for rare ones. |

### Offers made on demand (memory)

Making three times SPT's offers up front would take far more memory. Better Flea makes a share at startup and the rest
when a player opens a category or searches. Players see the same flea either way.

| Setting | Default | What it does |
|---|---|---|
| `listings.onDemand.enabled` | on | Off = every offer made at startup. Needs a restart. |
| `startShare` | 0.15 | Share of each item's offers made at startup. |
| `minStartOffers` | 3 | Every item starts with at least this many. |
| `keepPages` | 4 | Only the last this many lists you opened stay filled. |
| `keepSeconds` | 120 | An item nobody has looked at for this long goes back to its start share. |
| `maxExtraOffers` | 15000 | The most on-demand offers at once. Past it, the longest-unseen items go back first. |
| `maxItemsPerSearch` | 400 | A search over more items than this (a whole top category) shows what's there without filling it all. |

### Items per offer

How many of an item one offer holds, for items that don't stack (ammo and money use SPT's).

| Setting | Default | What it does |
|---|---|---|
| `itemsPerOffer.enabled` | on | Off = SPT's own amounts. |
| `armoredRigsSingle` | on | Armoured rigs always one per offer. |
| `bulk` | up to 35, falloff 0.8 | Common small barter items: bulbs, wires, bolts. Falloff: lower = more single-item offers. |
| `stock` | up to 6, falloff 0.55 | Rare small barter items, common meds, food, weapon parts. |
| `few` | up to 3, falloff 0.3 | Very rare or big items and all gear: GPUs, LEDX, rigs. |
| `signalFlares` | up to 2 | Signal flares and toy gun disks. |
| `ammoBoxes` | up to 3 | Ammo boxes. |
| `grenadeWeights` | 1, 4, 3, 1, ... | Chance weights for 1, 2, 3... grenades per offer. |
| `grenadeBulk`, `stimBulk`, `stimBatch` | rare | Now and then a pile of grenades, a handful of one stim, or a whole stored batch. |
| `fullStacks` | 3% | Now and then a big stack of full meds, food or drinks (an IFAK x30). |
| `usedMedStacks` | 3% | A flipper's stack of used medkits, all with the same amount left. |
| `stockGunStacks` | 12% | Factory guns as found, several to an offer (a boss farmer's haul). |
| `helmetStacks` | 5% | Easy-to-get helmets at full durability, several to an offer. |
| `traderGunStacks` | 2% | A trader's gun bought to flip, at 100/100, several to an offer. |

### Currency

`offerCurrency`: roubles 100, dollars 2, euros 2. Only the ratios matter. SPT's own is 78 / 14 / 8. 0 = never.

---

## Condition tab

`configs/condition.jsonc`. Partly used keys, meds, food, fuel, repair kits and guns. (Armour is on the Armour tab.)
Less left means cheaper. See [Armour and condition](05-armour-and-condition.md) for how the price follows condition.

### Keys

| Setting | Mechanical | Marked | Keycards | What it does |
|---|---|---|---|---|
| `usedChance` | 0.12 | 0.88 | 0.4 | Share of offers that are partly used. Live, mechanical keys are nearly all new and marked keys nearly all used. |
| `usesLeftFalloff` | 1.05 | 0.6 | 0.9 | Under 1 = more keys with few uses left. 1 = any number left. Over 1 = most only a few uses down. |
| `oneUsePricePercent` | 20 | 35 | 20 | A key with one use left costs this % of a new one. |
| `usedChanceByRarity` | common 0.5, rare 1, superrare 2 | | | Mechanical keys: used chance by rarity. Cheap keys are bought for one quest and sold on unused. |
| `oneDownChance` | | 0.1 | | Marked keys used once and sold on (9/10). |

### Meds, food, fuel and repair kits

| Setting | Meds | Food | Fuel | Repair kits | What it does |
|---|---|---|---|---|---|
| `usedChance` | 0.12 | 0.08 | 0.5 | 0.55 | Share of offers that are partly used. |
| `lowAmountBias` | 1.3 | 1.3 | 1.5 | 2.0 | 1 = any amount left. Higher = more that are low. Never under about 15% left. |
| `usedPricePercent` | 90 | 68 | 85 | 100 | A used one's price, in % of a full one. |
| `amountPriceWeight` | 0.5 | 0.15 | 0.2 | 1 | How much the amount left counts on top. 0 = a used one costs the same whatever is left; 1 = the price follows the amount. `100` and `1` together are SPT's own way. |
| `emptyChance` | | | 0.06 | | Fuel cans listed empty... |
| `emptyScamChance` | | | 0.3 | | ...and the share of those priced like a full can. |

Live, a used water costs about a third less than a full one whatever is left, and a part-full fuel can nearly as much
as a full one. That's what `usedPricePercent` and `amountPriceWeight` copy.

### Guns

| Setting | Default | What it does |
|---|---|---|
| `wornChanceByRarity` | common 0.9, rare 0.65, superrare 0.35 | Share of guns a little below max. |
| `maxWear` | 40 | How far below max a worn gun can be. Most are only a few points down. |
| `lowMaxChance` | 0.85 | Share of worn guns whose max was lowered by repairs. |
| `lowMaxMin`, `lowMaxMax` | 93 to 99 | The lowered max, mostly near the top. Around 93 a gun starts to jam, so most are sold by then. |
| `repairBuffChance` | 0.03 | A repaired gun now and then has a repair kit bonus (the gold background). |
| `bossGunGroundShare` | 0.6 | Boss-only guns (M60E6, PKP, Mk 43, AK-50): share listed as found, factory gun at 97-100/100. |
| `bossGunWear` | 0.15 | How worn the rest are. |
| `wearByBuild` | meta 0.5 ... broken 2.0 | How worn each build tier is. Broken-down and stripped guns had a hard life; meta builds were looked after. |

---

## Weapons tab

`configs/weapons.jsonc`. How gun offers are built. The full story is in [Guns and attachments](04-guns-and-attachments.md).

### Builds

| Setting | Default | What it does |
|---|---|---|
| `presets.showCustomBuilds` | on | Custom builds can be listed, not only SPT's default builds. |
| `customBuildChance` | 0.15 | Share of custom builds kept, so they stay rare. |
| `randomBuildChance` | 1.0 | Share of guns listed as one of Better Flea's build tiers. The rest are SPT's factory builds. |
| Tiers: `stripped`, `stock`, `found`, `modded`, `broken`, `gutted`, `budget`, `ground` | see file | Each gun offer is one tier, picked by `weight` for cheap guns and `dearWeight` for dear ones. Each tier has chances for each kind of part (`scope`, `suppressor`, `muzzle`, `grip`, `stock`, `magazine`, `other`), `factory` (chance a part is the factory one), `barrelSwap`, `stripMin`/`stripMax` (parts sold off before listing), `statFocus` (how much the build cares about good parts), `colourMatch`, `bestOf` (builds tried, best kept) and `goodMagazineChance`. |
| `loadedMagazineChance` | 0.3 | Chance a listed gun has some rounds in its mag. |
| `buildStyles` | ergo 0.45, hybrid 0.4, recoil 0.15 | What builds aim for. |
| `metaBuildChance` | 0.3 | Share of modded listings that are the gun's meta build, copied part for part. Save a build in the game's Builds screen with a name starting "Meta" and that exact build is used. |
| `lootedBuildChance` | 0.2 | Share of found listings that are a gun as a scav, boss or guard carries it. |
| `buildDealChance` | 0.05 | Share of built guns (and plated armour) listed as a deal, 20-35% under their parts. Never under what traders pay for the parts plus 10%. |
| `buildStealChance` | 0.01 | A rarer, deeper deal, 40-60% off. |
| `nearStockMin`, `nearStockMax` | 5 to 15 | Extra near-stock offers per gun: the factory gun, now and then with a cheap red dot. |
| `forgotScopeChance` | 0.007 | A scoped build listed as if the scope weren't on it. |
| `brokenMaxParts`, `brokenPriceBias` | 3, 2 | Broken-down guns: most main parts off, and how much they lean to cheap guns. |
| `maxTacticalDevices` ... `emptyRingsChance` | see file | Lights and lasers, empty mounts left after a scope was sold, scope rings on rifles with no optic. |
| `backupSightChance` | 0.25 | A 3x+ scope also gets a small backup red dot. |
| `tanBuildChance` | 0.15 | Chance a build is tan. Otherwise the first coloured part sets the colour. |
| `maxAttachmentDepth` | 5 | How deep parts nest. An AR needs 5 for a scope on a handguard rail. |

### Players' own guns

| Setting | Default | What it does |
|---|---|---|
| `playerGuns.savedBuildChance` | 0.003 | Share of a gun's offers that are one of the players' saved builds of it. An easter egg. |
| `lostGunChance` | 0.35 | Chance a gun you die with turns up on the flea... |
| `lostGunMinMinutes`, `lostGunMaxMinutes` | 5 to 30 | ...this long after the raid... |
| `killerAsSeller` | on | ...listed under your killer's name. |
| `scopeStripChance`, `suppressorStripChance` | 0.3, 0.25 | Chance the scope or suppressor was sold off first. |

### Modded attachments

| Setting | Default | What it does |
|---|---|---|
| `moddedParts.percent` | 100 | How often a modded part is picked against a base-game part that fits as well. 0 = never. |
| `disabledMods` | none | Mods whose parts stay off builds. Still used where nothing else fits a part the gun needs. |
| `allowFleaBannedParts` | off | Let modded parts their mod bans from the flea go on builds. Some mods ban parts a gun needs (WTT's F2000 top rail: no scope without it). |

---

## Armour tab

`configs/armor.jsonc`. Plates and condition for armour, rigs, helmets and their parts. Replaces SPT's own random
plate removal.

| Setting | Default | What it does |
|---|---|---|
| `enabled` | on | Off = SPT's own plates and condition. |
| `plates` | both 0.6, front 0.12, back 0.08, none 0.2 | Which front and back plates armour comes with. |
| `mixedClassChance` | 0.2 | One class 3 and one class 4 plate. |
| `class3Chance` | 0.15 | Both class 3 (old or budget kit). |
| `sidePlateChance` | 0.3 | Armour that takes side plates has them. |
| `bestKitChance` | 0.05 | Class 4 front and back plus class 6 sides, all mint. The dearest there is. |
| `flippedDownChance` | 0.3 | A face shield, visor or night vision listed flipped down. |
| `helmetAttachmentChance` | 0.2 | A helmet comes with something on it. |
| `helmetDamage` | 0.5 | Helmets are rarely wrecked. 1 = as damaged as other armour. |
| `bossHelmetChance` | 0.25 | A helmet bosses wear sometimes comes off one: good max, low current. |
| `repairBuffChance` | 0.02 | A repaired piece now and then has a repair kit bonus. |
| `conditionByRarity` | see file | Mint, worn, destroyed (15-35% left with a good max) and obliterated (max worn down too), by rarity. About 1 in 8 is badly damaged. |

Class 5 and 6 front and back plates are flea-banned in this game version, so front and back plates on offers are class
3 or 4. Side plates can be class 5 or 6.

---

## Market tab

`configs/market.jsonc`. How the market behaves over time.

| Setting | Default | What it does |
|---|---|---|
| `offerExpiry.minMinutes`, `maxMinutes` | 360 to 7200 | Shortest and longest an offer is up. |
| `offerExpiry.hours` | 12 h 0.3, 24 h 0.7 | How long people list for, by weight. |
| `longStackMin`, `longChance` | 10, 0.35 | A stack of 10 or more is often listed for days. |
| `freshness` | 20 | 1 = offers found anywhere in their time; higher = most only just listed, so few show the red "under 10 hours". |
| `simulatedSales.enabled` | on | Other players buy offers, which come off the market and are replaced. |
| `everyMinutes` | 5 | How often those buyers come round. |
| `normalSellChancePerHour` | 0.04 | An offer at the normal price sells within an hour this often. |
| `topSellChancePerHour` | 0.5 | The cheapest few offers of every item sell and are relisted this often, so the top of a list stays fresh. |
| `dealSpeedup` | 20 | A deal 20% under normal sells this many times as fast; 20% over, this many times slower. |
| `dealGoneMinMinutes`, `dealGoneMaxMinutes` | 1 to 6 | A deal 10% or more under normal is bought by someone else this soon. Spot it quickly. |
| `soldBeforeYouChance` | 0.05 | Buying a gun or barter item from another seller fails now and then: "sold before you". Never on trader offers. |
| `barterOffers.chance` | 0.005 | A seller wants items instead of money, items people actually carry. Offers under 20,000 stay money. |
| `barterOffers.valueMin`, `valueMax` | 0.85 to 0.97 | What the asked-for items are worth against the normal price. |
| `sellerRating` | 2 to 300 | Ratings next to generated sellers. Everyday players up to 100, regulars 20 to 300, flippers mostly regulars, a few new sellers at -2 to 1. |
| `sellerBadges` | EoD 0.25, Unheard 0.2, special 0.001 | Edition badges next to sellers' names. Each seller keeps its badge and rating on every offer. |
| `packs.chancePercent` | 3 | Several of an item you must buy together. |
| `packs.min`, `max` | 2 to 10 | Items in a pack. |
| `packs.scamChance` | 0.6 | Share of packs priced 2-25% over buying them one by one. Do the maths. |
| `packs.hugeChance` | 0.02 | A silly-sized pack (50 to 80). |

---

## Prices tab

`configs/prices.jsonc`. The prices of generated offers. Trader prices never change. The full pipeline is in
[How prices work](03-how-prices-work.md).

| Setting | Default | What it does |
|---|---|---|
| `priceMultiplier` | 1.0 | Every generated offer. 1.5 = 50% dearer. The simplest way to make the flea harder or easier. |
| `categoryMultipliers` | all 1 | **Prices by category**: every kind of item from the Bans tab with its own multiplier, on top of the price multiplier: `ammo.rounds` 0.8, `weapons.guns` 1.5, `other.barterItems` 2. Changes other sellers' offers and the normal price your listing is measured against. Traders unchanged. An item or category ID works in the file too. |
| `floor.enabled` | on | No generated offer under the most a trader pays for the item, so nobody buys on the flea to sell to a trader. |
| `floor.marginPercent` | 10 | The cheapest offer is this % over that trader price. |
| `floor.logTraderProfitLoops` | off | Log the items one trader sells for less than another pays. |
| `floor.ignoreForTraderLoops` | off | **Not supported.** No floor for those items (and their parts), so their offers can go under what a trader pays. Worn ones get cheaper than a new one from the trader, and buying on the flea to sell to a trader then makes money. Only items where traders already make that possible are affected. |
| `drift.enabled` | on | Prices wander a little over time, per item. Only new offers get the new price. |
| `drift.maxDriftPercent`, `stepPercent`, `updateMinutes` | 20, 4, 45 | How far, how big a step, how often. |
| `sellers.overpriceChance` | 0.1 | An offer asks too much... |
| `sellers.wornOverpriceChance` | 0.3 | ...worn items more often, priced as if in better shape. |
| `sellers.overpriceMinPercent`, `MaxPercent` | 10 to 50 | How much too much. |
| `sellers.quickSellChance` | 0.004 | Someone sells one cheap to be rid of it, 10-25% under. Never under the floor. |
| `sellers.roundPriceChance` | 0.75 | Sellers type easy numbers (45,000, 44,900, 44,444, 49,999) instead of exact prices. |
| `sellers.trollChance`, `trollGunChance` | 0.004, 0.04 | A silly price nobody pays, 5 to 15 times normal, at the end of the list. Guns get the most. |
| `spread.minPercent`, `maxPercent` | -4 to +15 | How far an ordinary offer strays from normal. SPT's own is -20 to +20, which puts a 20% deal on top of nearly every list. |
| `demand.enabled` | on | Buying up an item's offers makes its next ones dearer; selling a lot of one item now and then makes it a little cheaper. Only real sales count. |
| `demand.buyRisePercent`, `maxRisePercent` | 2, 30 | Rise per offer bought, and the most. |
| `demand.floodChance`, `floodDropPercent`, `maxDropPercent` | 0.15, 3, 15 | Drops from selling a lot. |
| `demand.recoverMinutes` | 60 | Half the change wears off in this long. |
| `guns.enabled` | on | Price guns against what traders sell them for, not the sum of their parts' flea prices. [Details](04-guns-and-attachments.md#how-a-gun-is-priced) |
| `guns.traderPricePercent` | 95 | A part a trader sells is worth this % of the trader's price. |
| `guns.partsAtFleaPrice` | on | A gun's parts are worth their own flea price; only the gun itself goes by trader prices. So stripping a gun makes no money. |
| `guns.fleaShareByLoyalty` | LL3 0.2, LL4 0.5 | Share of the flea price kept over the trader price, by the loyalty the part needs. |
| `guns.fleaShareByDifficulty` | medium 0.2, hard 0.5, very hard 0.9 | The same by how hard its quest unlock is. [The quest tree](06-quest-tree.md) |
| `guns.priceAtHalfDurabilityPercent` | 50 | A gun at half durability is worth this % of a new one. 50 = straight line. SPT's own is about 71. |
| `guns.offerMinPercent`, `offerMaxPercent` | -12 to 0 | Where other sellers list guns against their value: a little under, as a rule. |
| `guns.stockOfferMinPercent`, `stockOfferMaxPercent` | -15 to -3 | Near-stock guns, a bit cheaper again. |

---

## Selling tab

`configs/selling.jsonc`. Players selling their own items. The full story is in [Selling on the flea](07-selling.md).

| Setting | Default | What it does |
|---|---|---|
| `enabled` | on | Off = players can't sell on the flea at all. |
| `foundInRaidOnly` | off | Only found-in-raid items can be sold. Off, like live. |
| `saleDelay.enabled` | on | How fast a listing sells depends on its price against normal. Off = SPT's own sale rolls. |
| `saleDelay.minMinutes`, `maxMinutes` | 2 to 8 | Wait at the normal price. |
| `saleDelay.noDelayChance` | 0.1 | Chance of no wait for an offer at or under normal. Not for guns or armour. |
| `saleDelay.uniqueMinMinutes`, `uniqueMaxMinutes` | 5 to 20 | Wait for a gun, armour or anything with durability at about 100,000 roubles; dearer ones longer. |
| `saleDelay.slowSaleMaxHours` | 4 | A low-chance sale that happens finds its buyer any time up to this long after listing. |
| `saleDelay.expensivePercent` | 15 | This far over normal, 1 buyer in 20 is still interested. Dearer still sells now and then, later. |
| `saleDelay.neverSellsPercent` | 100 | This far over normal it never sells. 100 = twice the normal price. |
| `saleDelay.cheaperOfferShare` | 0.35 | Another seller has it cheaper: this share of buyers is left for you. |
| `buyers.enabled` | on | Buyers go down the list, cheapest first. |
| `buyers.minMinutes`, `maxMinutes` | 0.05 to 0.2 | Time between buyers for an item of about 20,000 roubles; dearer, rarer and less-wanted items wait longer. |
| `buyers.slowdown`, `recoverMinutes` | 1.1, 10 | Each sale from your offer makes the next buyer a little slower, and it wears off. A pile sells a bit at a time. |
| `buyers.wholeOfferChance` | 0.08 | A buyer takes another seller's whole offer. |
| `buyers.relistMinMinutes`, `relistMaxMinutes` | 1 to 5 | A bought offer is replaced this long later. |
| `undercut.enabled` | on | When yours is the cheapest offer, now and then another seller lists just under it and takes most of your buyers. |
| `undercut.chanceAtNormal`, `chanceWhenDear` | 0.35, 0.8 | How often, at the normal price and at `expensivePercent` over. |
| `undercut.safeBelowPercent` | 10 | This far under normal you're hardly ever undercut. |
| `undercut.minMinutes`, `maxMinutes`, `buyersLeft` | 1 to 10, 0.15 | When it happens, and the share of buyers you keep. |
| `flipPenalty.enabled` | on | Buy many of one item and list them straight away: buyers take longer. |
| `flipPenalty.windowMinutes`, `freePurchases`, `purchasesForMaxPenalty`, `maxExtraMinutes` | 30, 2, 10, 10 | Purchases within 30 minutes count; 2 are free; at 10 the extra wait is the full 10 minutes. |
| `flipPenalty.countFleaPurchases`, `fenceMultiplier` | on, 3 | Flea purchases count too; one item from Fence counts as three. |
| `purchaseLock.enabled` | off | Items bought from a trader (Fence too) can never be sold on the flea. Parts on a bought gun too. Stackable items aren't locked. Purchases are remembered while it's off, so turning it on mid-wipe locks what was bought before. |
| `purchaseLock.includeFleaPurchases` | off | Lock items bought from other players on the flea too. |

---

## Economy tab

`configs/economy.jsonc`, plus the price index and Wipe Cycle status. The tools are on their own **Lookup** tab, right
after Home: **Item lookup** (any item's prices, its sale odds, its price history as an hourly stock chart with players'
listings and sales, and its price through a wipe) and **Offers on the flea** (every player's offer that's up, with its
odds, and a Refresh button).

| Setting | Default | What it does |
|---|---|---|
| `betterFleaPricing.enabled` | on | Every base-game item starts from Better Flea's own fixed price instead of SPT's. Built from a year of real flea prices; doesn't move. [How it's built](../Research/price-index.md) |
| `betterFleaPricing.estimateModdedItems` | on | Modded items get a price on the same scale, from similar base-game items. |
| `betterFleaPricing.contentBackport` | on | The base-game items WTT ContentBackport adds get Better Flea's price too. |
| `betterFleaPricing.liveTraderPrices` | on | Traders pay what they do live (since patch 1.1): Prapor 40%, Therapist 51%, Ragman 50%, Ref 40-50% by loyalty level. Off = SPT 4.1's own, which are the 0.16.9 numbers (Therapist 63%). A trader another mod changed keeps that mod's prices. [Details](03-how-prices-work.md#what-traders-pay) |
| `livePrices.enabled` | off | Every item starts from today's real flea price (tarkov.dev's 24-hour average) instead. Not used while Better Flea pricing is on. |
| `livePrices.gameMode` | live | `live` (PvP), `pve` or `season`. |
| `livePrices.refreshHours` | 24 | How often new prices are fetched. They're saved, so a tarkov.dev outage uses the last ones. |
| `livePrices.estimateModdedItems` | on | As above. |
| `wipeCycle.enabled` | off | Prices follow how far into the wipe the players online are, shaped by how real flea prices moved through a wipe: everything dear at the start, hideout and early quest items dear before people get to them, the best gear dear late on. |
| `wipeCycle.strength` | 1.0 | Size of the swings. |
| `wipeCycle.levelStep` | 3 | Prices move every this many levels. |
| `wipeCycle.cooldownMinutes` | 30 | After the flea's level changes it stays put this long, so swapping profiles can't be used to flip. |
| `wipeCycle.endLevel` | 60 | About where a wipe "ends". |
| `wipeCycle.prestige` | add | A prestiged player counts as late wipe (`add`) or a fresh wipe (`reset`). |
| `wipeCycle.level` | 0 | For testing: pretend the players are this level. 0 = the highest level online. |
| `priceOverrides` | none | **Your prices**: your own base price for an item, in roubles. Replaces Better Flea's, the live or SPT's; multipliers, drift and the Wipe Cycle still go on top. Mainly for a modded item its mod prices oddly. Search to add, or paste a list of ids and prices. |

Better Flea pricing and live prices aren't meant to run together; with Better Flea pricing on, live prices are ignored.

---

## Debug tab

`configs/debug.jsonc`. `debugLogging` (off): write a lot of extra lines to the server log, including every sale
roll. Only for testing or a bug report.

## Web

`configs/web.jsonc`. `enabled` (on): the settings page. Off = no page; edit the files and restart instead. Not shown
on the page itself.
