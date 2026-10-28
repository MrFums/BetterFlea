# Price sources compared

What we found comparing every base-game item's price from four places, for the [price index](price-index.md). The
index itself uses only the market (PvP and PvE flea, traders); SPT's prices and the handbook are compared here, not
used:
tarkov.dev's PvP flea and PvE flea (24 hour averages, 27 October 2026), SPT 4.1.6's own prices
(`templates/prices.json`), and the handbook (`templates/handbook.json`, what traders base their prices on).

## How far apart they are

Ratios over every item that has both prices. "Index" is the price index.

| | Items | 10% | 25% | Median | 75% | 90% | More than 3x apart |
|---|--:|--:|--:|--:|--:|--:|--:|
| PvE flea / PvP flea | 2,908 | 0.52 | 0.84 | 1.20 | 1.92 | 3.82 | 544 (19%) |
| Index / SPT | 2,613 | 0.35 | 0.65 | 0.99 | 1.35 | 1.94 | 301 (12%) |
| Index / handbook | 3,692 | 0.90 | 1.31 | 2.05 | 3.74 | 8.10 | 1,174 (32%) |
| SPT / handbook | 2,613 | 0.80 | 1.26 | 2.36 | 5.22 | 13.80 | 1,070 (41%) |

- **SPT's prices are right on the whole and badly wrong for a few.** The median item is where the index puts it,
  but one in eight is more than 3x off. Iron sights (index 0.29x SPT), mounts (0.51x), assault rifles (0.52x),
  muzzle devices (0.52x) and pistols (0.54x) are the most overpriced kinds. Electronics (1.57x), foregrips (1.30x)
  and ammo packs (1.25x) are the most underpriced.
- **The handbook is no guide to flea prices.** The median item sells for twice its handbook price, and the spread is
  huge (a Vulkan-5 helmet: handbook 8k, flea 400k to 900k).
- **PvP and PvE disagree more than expected.** One item in five is more than 3x apart. Valuable loot mostly agrees
  (LEDX, GPUs, Salewas within 2x); the disagreement is in thinly traded items.

## Where PvP and PvE disagree

By kind, PvE price / PvP price. The middle half of most kinds is close; the 10% and 90% columns are where it goes
wrong.

| Kind | Items | Median | 10% | 90% |
|---|--:|--:|--:|--:|
| Mount | 228 | 1.39 | 0.37 | 5.64 |
| Stock | 220 | 1.18 | 0.44 | 5.03 |
| Magazine | 194 | 1.26 | 0.50 | 3.73 |
| Mechanical key | 185 | 1.13 | 0.44 | 4.64 |
| Handguard | 183 | 1.33 | 0.68 | 3.83 |
| Ammo pack | 169 | 1.59 | 0.37 | 4.44 |
| Flash hider | 138 | 1.36 | 0.26 | 5.00 |
| Barrel | 134 | 1.32 | 0.52 | 6.00 |
| Iron sight | 66 | 1.08 | 0.14 | 11.93 |
| Muzzle combo | 40 | 1.21 | 0.30 | 11.43 |
| Auxiliary part | 19 | 2.15 | 0.13 | 33.33 |
| Silencer | 74 | 0.93 | 0.62 | 1.42 |
| Assault rifle | 41 | 1.03 | 0.75 | 1.54 |
| Stimulant | 21 | 1.21 | 0.92 | 2.60 |
| Food | 22 | 1.31 | 1.05 | 1.58 |

- **Cheap gun parts are the noisy ones.** Mounts, stocks, iron sights and muzzle devices are listed a handful of
  times a day, and one joke listing (a 2 ₽ handguard, a 30,000 ₽ muzzle brake that traders sell for 600) moves the
  average. The trader's cash price is the best check: the median part sells on the flea for 1.31x its trader price,
  but one in ten for 5x or more.
- **Marked keys, Labs keycards, collector items and boss gear are 5 to 15x dearer on PvE** (a Mysterious room
  marked key: 1.8M PvP, 21.4M PvE). These went to the review.
- **Repair kits:** a body armour repair kit is 253k on PvP and 1.02M on PvE. With live prices on PvE, a kit costs
  over 1M on Better Flea, and the Wipe Cycle can lift it to 2M early in a wipe. The index follows PvP: 253k.

## Guns: whole guns are cheaper than their parts

For most guns, the default build sells on the flea for less than its parts do one by one:

| Gun | Default build on the flea | Its parts, loose |
|---|--:|--:|
| FN SCAR-L | 59k | 205k |
| HK 416A5 | 129k | 201k |
| AK-12 | 155k | 183k |
| SVDS | 112k | 319k |
| Glock 18C | 72k | 300k |

Loose stock parts are thinly traded and listed high, while whole guns sell cheap: a default SCAR-L build sells for
59k, about what its barrel alone costs from the trader. So a gun's value can't be "the gun item's
flea price plus its parts' flea prices"; Better Flea's gun pricing (trader prices for parts, by how hard they are to
unlock) already accounts for this. For the index:
- Where the build sells for more than its parts (60 guns), the gun item gets the difference.
- Otherwise (79 guns) it gets the gun's cheapest listing, which is about a stripped gun.

Either way the gun item is now 10k to 60k instead of SPT's 100k to 300k. A default M4A1 build on Better Flea came
out at 145k with PvE live prices (the M4A1 kept SPT's 160k) and 120k with the index; the real flea's average default M4A1 is 100k, and player
listings of worn ones run 79k to 119k.

**Still to look at:** Better Flea's random builds carry more parts than a default build (12 to 16 on an M4A1 or
X-17), and their loose parts are priced by the index. An X-17 build still lists for about 337k (median of 36
offers), down from about 450k, but a lot of that is parts. Part prices inside a build may want a discount of their
own.

## SPT prices the live price check threw away

Live prices skip any live price below 15% or above 40x of SPT's own, to keep out junk averages. Against today's
data, that threw away good prices for 40 PvE items and 50 PvP items where SPT's own price was the wrong one: the
X-17 (SPT 275k, flea 17k), the M4A1 and AKM on PvE (SPT 133k and 135k, flea 15k and 10k), Glock sights (SPT 44k,
flea 664), a jar of pickles (SPT 503k, flea 54k), several keys and gun parts. Those kept SPT's price, which is how
an X-17 build reached 400k.

Fixed: the live price is now also kept if it's within 0.2x to 10x of the handbook price. That recovers 36 of the 40
PvE items and 42 of the 50 PvP items. What's still thrown away is junk (a 5M pumpkin, 69 ₽ Glock sights on PvP, a
2 ₽ handguard, ammo packs at 130k to 340k).

## What tarkov.dev has

| Feed | What's in it |
|---|---|
| `json.tarkov.dev/{regular,pve}/items` | 5,480 items (497 of them gun presets): 24 hour average, low and high, last low price, offer count, trader cash prices with loyalty level and quest lock, trader sell prices, the level an item unlocks on the flea, default build contents. No history. |
| `json.tarkov.dev/{regular,pve}/barters` | 855 trader barters with their required items. |
| `json.tarkov.dev/{regular,pve}/crafts` | Hideout crafts. Not used yet. |
| GraphQL `historicalItemPrices`, `archivedItemPrices` | Price history, and prices of items no longer on the flea. Down all day on 27 October 2026 ("GraphQL server unavailable"). |

1,751 items have no PvE flea price today, nearly all of them flea-banned items (1,525) or presets (276). Of the banned items that SPT has,
trader cash prices cover 198, barters 40 and trader sell prices the rest, apart from items that can't be traded at
all.
