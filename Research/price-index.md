# Better Flea price index

One fixed price for every base-game item, used as its base price when **Use Better Flea pricing** is on (Economy tab,
`configs/economy.jsonc` → `betterFleaPricing`). Think of it as a more reliable handbook: it doesn't move. Better Flea's
own pricing goes on top as it does with SPT's prices: unlock levels, the price multiplier, demand, drift, the Wipe
Cycle and the price floor.

Built by `Research/tools/price_index.py` from tarkov.dev's PvP and PvE data of 27 October 2026, using a year of flea
price history ([price-history.md](price-history.md)), and shipped inside the
DLL as `Prices/price-index.json`. It covers SPT 4.1.6's items and the newer base-game items WTT ContentBackport adds
(see below). Every item and where its price came from: [price-index.tsv](price-index.tsv). Items
the sources disagree on: [price-index-review.md](price-index-review.md). How the sources compare:
[price-sources-compared.md](price-sources-compared.md).

## Why

SPT's prices are the real flea's averages from some point in time, passed through SPT's own calculations. Most are
fine, but 12% are more than 3x off what the item sells for today (a 275k FN SCAR-H X-17 that sells for 17k, 44k Glock
sights that sell for under 1k, a 503k jar of pickles that sells for 53k). SPT 4.1.7 is expected to fix some of it.
The handbook is worse: the median item sells for twice its handbook price, and 32% are more than 3x off.

Live prices (tarkov.dev's 24 hour average, refreshed daily) fix the scale but move every day, follow one game mode's
quirks (the PvE flea pays 1M for a repair kit, PvP 250k), and had to keep SPT's price wherever the two disagreed too
much. The index settles each item once and stays put.

## How each item is priced

**PvP's flea is the price**: its median over the last year ([price-history.md](price-history.md)), not one day's
average. PvE's runs hotter (a 925k repair kit that's 322k on PvP), so it's only used where PvP has no price. **Keys
and keycards** are the exception: in PvE a key's room can be looted without getting killed for it, so its value is
safer to count on there, and a key gets the geometric mean of the two.
**SPT's own prices are never used**, and the handbook only as a last nudge for review items (below). Both are shown in
the review and the table for comparison.

**Prices from one or two listings don't count.** Only history readings backed by 3 or more listings make a solid
price. An item without enough of those gets a thin price from all its readings, which counts only if it's not more
than 3x what traders say the item is worth (their cash price, or their sell price times the markup below): bait like
an NSPU-M eyecup listed at 10,000 that Prapor sells for 355. A cheap one stands, since bait is never cheap. With
nothing from traders, it counts only if the other mode's price agrees within 2x. Rounds are left alone.

**A thin day counts near its cheapest listing.** A reading's price is the average of what's listed, and with only a
few listings that's mostly the ones nobody buys: an AK-545 Mk.3 handguard had one listing at 99,999 for weeks while the
few at 40-50k sold, so its year averaged 80,700. Buyers take the cheapest. Below 10 listings, a reading counts for no
more than its cheapest listing times 4/3, which is how far the average sits over the cheapest where there are plenty
of listings, so thin and well-listed items come out on the same scale. Items with 10 or more listings, and ammo, are
unchanged. A single odd listing in a year was never a problem: the price is a median of days, so one 200k day among
3k days doesn't move it.

**Weapons and weapon parts are held to what traders say.** Their worth follows trader prices, unlike keys and
collectibles. Without 10 or more listings on a typical day:

- One with solid days still gets checked, and counts only up to 2x what traders say it's worth (2.5x is where nine in
  ten well-listed parts sit). Over that it goes by the trader estimate: a 30,000 HK USP trigger is 1,210.
- One a trader sells counts up to 2x the trader's price; the trader-price rule below takes most of what's over.
- The markup for weapon kinds comes from well-listed items only. Counted from all of them, bait gave triggers and
  hammers 34x the trader sell price, which let the 30,000 trigger pass.
- A part that comes on a build a trader sells is worth at most twice its share (by handbook price) of the cheapest
  such build, since anyone can buy the build and take it off: a 10,000 Vityaz metal skeleton stock is 3,640.

Against the index built the old way from the same data: 1,083 items moved more than 10%, 1,047 of them down (median
-24%). The median item didn't move. Well-listed items, ammo and flea-banned items are unchanged. Guns can rise: a gun is
its default build less the parts, and the parts got cheaper.

**Flea-banned items ignore their flea history.** If tarkov.dev marks an item banned in a mode, that mode's old prices
are from before the ban and don't count (a THICC item case at 14.3M on PvE). They go by traders and barters instead.

In order; the first that applies wins.

1. **PvP flea price:** 2,720 items.
2. **PvE flea price, where PvP has none:** 404 items.
3. **Keys and keycards with both:** the geometric mean of PvP and PvE. 202 keys.
4. **Ammo packs:** their rounds at the index price. Packs are rarely listed, so their averages are mostly a few odd
   listings (an M80 pack of 20 averaged 119k on PvP). 229 packs.
5. **No usable flea price:** the trader's cash price (145), then the cheapest barter, with its items at their index
   prices (8), then the best trader sell price times what the flea pays over the trader sell price for the same
   kind of item (median over that kind, from well-listed PvP items; 5.9x over all items) (502).
6. **Guns:** the gun item is priced so that its default build costs what that build sells for on PvP's flea: the
   build's price less its parts (44 guns). Where the default build's loose parts add up to more than the build sells
   for, which is most guns, the gun item gets its cheapest listing instead, as live prices do (119). Flea-banned guns
   get the trader-based estimate (9).
7. **Armour and helmets with removable parts:** the flea and traders price the whole thing, but in SPT its plates,
   visors and the like are items of their own, priced on top. They come off at their own price (16 items). If they'd
   take off three quarters or more, the item is sold without them (most plate carriers are), so its price stands.
   Built-in soft armour stays in: SPT and Better Flea don't price it separately.

Then, for every item:
- **Over a trader's cash price, only a share of the difference is kept, by how hard the item is to unlock**: anyone
  who can buy it from the trader does. The shares are the ones Better Flea's gun pricing uses: loyalty 3 keeps a
  fifth, loyalty 4 half; a quest lock keeps what its difficulty says ([quest-unlock-difficulty.md](quest-unlock-difficulty.md):
  medium a fifth, hard half, very hard 90%); the BTR Driver counts as hard, Lightkeeper as very hard. An item several
  traders sell goes by the easiest. 1,570 items: 727 keep nothing (an easy Mosin Tacfire grip, 17.1k on the flea, is
  3.1k), 528 a fifth, 312 half (an AS VAL dust cover, 32.4k on the flea, is 17.3k), 3 nine tenths. Guns are left
  to Better Flea's gun pricing, which does the same itself.
- **Not under the best trader sell price,** or players would sell to the trader instead.
- **Rounded to 3 significant figures.**

## Items to review

210 items the rules can't settle on their own. They're priced from the market only: the PvP flea price (or PvE's
where PvP has none) and the trader's cash price count in full, the trader sell price times its markup at half. With
fewer than three it's their geometric mean, with the handbook in at a fifth of a market price's weight, so it can
nudge but never decide; an item with no flea price never goes under its trader-based estimate. They're listed in
[price-index-review.md](price-index-review.md) under these reasons:
- **PvE price only, far from what traders say** (71). PvP has no price and PvE's is over 5x off.
- **PvE price only, nothing from traders to check against** (3): Vengeful Zryachiy's balaclavas (64M and 57M on
  PvE) and the Pumpkin with sweets (7.1M). Pulled toward the handbook until reviewed.
- **Flea-banned guns** (24).
- **One or two listings in each mode, both far above what traders say** (112). Possibly rare items, possibly bait in
  both modes; priced at the trader-based estimate until reviewed (mostly gun parts traders sell cheap, like an ORSIS
  T-5000M stock or a KS-23M pistol grip).

**Set by hand:** the Compartment C-3 keycard, at what its room holds (4 LEDX, a medbag, meds and stims), 3M. It's a
one-time story keycard listed once or twice at 19.5M to 55M.

To set a price by hand, add it to `Research/tools/price-index-overrides.json` (`{"<item id>": price}`) and run the
tool again.

## Live trader prices

What a trader pays a player is a cut of the item's base price (SPT's handbook, which matches the live game's base
price for 88% of items). SPT's cuts are out of date: its traders pay 10 to 16 points more than live ones. Worked out
from tarkov.dev's trader sell prices over base prices (median over every item; the same in PvP and PvE, and at every
loyalty level except Ref's):

| Trader | Live pays | SPT pays |
|---|--:|--:|
| Therapist | 51% | 63% |
| Ragman | 50% | 62% |
| Jaeger | 48% | 60% |
| Mechanic | 45% | 56% |
| Prapor | 40% | 50% |
| Arena (Ref) | 40%, 45%, 45%, 50% | 50%, 56%, 56%, 62% |
| Skier | 39% | 49% |
| Peacekeeper | 36% | 45% |
| Fence | 24% | 40% |

A portable defibrillator: live Therapist pays 114,546, SPT's 141,498. With **Live trader prices** on (part of Better
Flea pricing, on by default), traders take their live cut. Shipped in the index as `TraderCuts`. Modded traders keep
their own.

Ref is the one trader whose cut changes with loyalty level, live and in SPT. tarkov.dev only has his level 1 price, so
his steps (5, 5 and 10 points over level 1) come from the wiki's Trading page, shipped as `TraderLevelCuts`.

These are the live cuts since patch 1.1 (August 2026), when BSG cut every trader's price by about 20% (Fence's by
more). SPT's cuts are the 0.16.9 ones, the game version SPT 4.1 is built on: the wiki's Trading page had exactly
SPT's numbers until then. About 10% of base prices also changed in live after 0.16.9 (meds 20% cheaper, cosmetic
masks 5,000, some armour reworked), and SPT has the 0.16.9 ones, so for those items a trader takes the live cut of
the old base price.

## Crafts and barters

Checked with the index prices, so nothing can be bought, crafted or bartered into something worth far more:
- **Hideout crafts (209):** the best turns its inputs into 5x their value, for 89k profit (a M.U.L.E. from Christmas
  tree ornaments, event only). Ten reach 3x; most are small sums, as in the real game. The biggest profit is a
  Far-forward GPS into 4 microcontroller boards on the workbench, 3.3x.
- **Trader barters (399):** the dogtag barters look like 20x (40 USEC dogtags for a Lucky Scav Junk box), but
  dogtags can't be bought on SPT's flea and the barters ask for tags of a set level and side, so only kills supply
  them. The rest top out at about 4x (a UHF RFID reader for GPNVG-18s at loyalty 4).
- **Barter prices:** a flea-banned item priced by its barter is capped at 5x what traders pay for it times the
  markup, since some barters are deliberately bad deals: Lightkeeper's microcontroller board (a GPU and 3 Virtex
  processors, 1.26M) is 160k, the Far-forward GPS 192k, the advanced current converter 480k. A THICC item case keeps
  its barter's 10.6M; that barter is how people really get one.

## Coverage

| | Items |
|---|--:|
| Base-game items tarkov.dev knows (presets left out) | 4,983 |
| Priced by the index | 4,390 |
| ... of them SPT 4.1.6's own | 3,714 |
| ... of them only in WTT ContentBackport | 676 |
| Not priced: can't be traded, or only ever listed as bait (secure containers, sealed weapon cases, quest items) | 176 + 49 in ContentBackport |
| Not priced: not in SPT 4.1.6 or ContentBackport (quest notes, audio tapes, documents) | 376 |

**WTT ContentBackport.** SPT 4.1.6 is missing 1,100 of the real game's items; ContentBackport adds 724 of them, and
the index prices the 675 that can be traded. They're read from ContentBackport's own item files, so a backported
gun's default build and a backported ammo pack's rounds work like any other. On the Economy tab, **WTT
ContentBackport items** (on by default) gives them Better Flea's price; off, they keep the price ContentBackport
gives them. Without ContentBackport installed it does nothing. In the review, ContentBackport's own flea and
handbook prices stand in for SPT's and the handbook.

Items the index has no price for keep SPT's (or their mod's). Items the real game doesn't have (modded items) are moved onto the
same scale from the base-game items of the same kind, as with live prices: **Estimate modded items** on the Economy
tab.

## Older prices

tarkov.dev's static JSON feed (`json.tarkov.dev/{regular,pve}/items`) only has the last 24 hours. Older prices are
only in its GraphQL API (`historicalItemPrices`, and `archivedItemPrices` for items no longer on the flea), which
answered "GraphQL server unavailable" all day on 27 October 2026. When it's back, the tool can take a multi-day
median instead of one day's average, which would smooth out the rest of the one-day noise. The index is rebuilt by
hand, so this doesn't affect anyone's server.

## Rebuilding

```
python -I Research/tools/quest_difficulty.py [path-to-SPT_Data/database] [output.md] [download-folder]/quest-difficulty.tsv
python -I Research/tools/price_index.py [path-to-SPT_Data/database] [download-folder] [WTT-ContentBackport/db]
```

It downloads tarkov.dev's files once into the download folder (a temp folder by default; delete it to fetch again),
then writes `Prices/price-index.json`, `Research/price-index.tsv` and `Research/price-index-review.md`. SPT's
database is only read for item kinds, default gun builds, ammo pack contents, and the SPT and handbook prices shown
in the review.
