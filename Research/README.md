# Better Flea: what we learned

Better Flea started as a few flea tweaks and turned into a rebuild of how SPT makes, prices and runs the Flea Market.
Along the way we read a lot of SPT's server code, compared prices from four sources over a year of history, and
studied the live PvE flea. This page collects what we found, so anyone curious about why the mod does what it does
(or anyone writing their own flea mod) doesn't have to dig through the code to find out.

Written for Better Flea 2.4.0 on SPT 4.1.6. SPT changes between versions, so check anything here against your own
version before relying on it.

**Contents**
1. [Where to read more](#1-where-to-read-more)
2. [How the mod fits together](#2-how-the-mod-fits-together)
3. [What SPT 4.1 does that surprised us](#3-what-spt-41-does-that-surprised-us)
4. [Prices: what the data says](#4-prices-what-the-data-says)
5. [What the live flea looks like](#5-what-the-live-flea-looks-like)
6. [Armour and helmets](#6-armour-and-helmets)
7. [Guns](#7-guns)
8. [Modded items](#8-modded-items)
9. [Flea bans: who decides what can be listed](#9-flea-bans-who-decides-what-can-be-listed)
10. [Player selling and flipping](#10-player-selling-and-flipping)
11. [Open questions](#11-open-questions)
12. [Tools and rebuilding the data](#12-tools-and-rebuilding-the-data)

---

## 1. Where to read more

| Page | What's in it |
|---|---|
| [../README.md](../README.md) | What every setting does, install, routes for other mods |
| [../Docs](../Docs/README.md) | The full guide: every setting, prices, guns, armour, the quest tree, selling, mod authors, API |
| [../CHANGELOG.md](../CHANGELOG.md) | Every change, version by version, with the reason for it |
| [price-index.md](price-index.md) | How Better Flea's built-in price for every base-game item is worked out |
| [price-history.md](price-history.md) | A year of flea price history and how it's turned into one price |
| [price-sources-compared.md](price-sources-compared.md) | PvP flea, PvE flea, SPT's prices and the handbook side by side |
| [price-index-review.md](price-index-review.md) | Items the sources disagree on, and what they were given |
| [quest-unlock-difficulty.md](quest-unlock-difficulty.md) | Every quest that unlocks a trader item, scored by how hard it is |
| [price-index.tsv](price-index.tsv), [price-history.tsv](price-history.tsv) | The numbers behind the two pages above, one row per item |

---

## 2. How the mod fits together

Better Flea is two parts. The **server mod** makes every rule. The **BepInEx plugin** only shows those rules in game
(icons, greyed-out items, unlock levels) and stops a listing the server would refuse, so the player gets a warning
instead of an error that sends them back to the menu.

Almost everything works by patching SPT's own methods with Harmony, so other mods that use the same SPT code see Better
Flea's results. The main systems, and where they live in the code:

| System | What it does | Code |
|---|---|---|
| Bans | BSG's bans on or off, categories, single items, items from other mods, unlock levels | `Flea/` |
| Offer counts | How many offers each item gets; offers made when someone looks instead of all at startup | `Offers/ListingCountPatches.cs`, `Offers/OnDemandOffers.cs` |
| Items per offer | Realistic stack sizes per kind of item | `Offers/OfferQuantityPatches.cs` |
| Condition | Partly used keys, meds, food, fuel; worn guns and armour | `Offers/UsedListingsPatches.cs` |
| Gun builds | Each gun offer built part by part in one of several build tiers | `Offers/PresetOffersPatches.cs`, `Offers/BuildQuality.cs`, `Offers/PresetOfferSources.cs` |
| Armour | Plate mixes, side plates, condition by rarity | `Offers/ArmorOffers.cs` |
| Market life | Expiry, simulated buyers, "sold before you", barter offers, packs, seller ratings | `Offers/Market.cs` |
| Prices | Floor, drift, spread, demand, seller behaviour, gun pricing | `Prices/Pricing.cs`, `Prices/Demand.cs` |
| Economy | The built-in price index, live prices, Wipe Cycle, your own prices, modded item estimates | `Prices/LivePrices.cs`, `Prices/WipeEconomy.cs` |
| Player selling | How fast your listings sell, buyers, NPC undercuts, flip penalty, trader purchase lock | `Selling/` |
| Settings page | The `/betterflea` page in your browser | `Web/` |
| Client plugin | Icons, greyed items, unlock levels in game | `Client/` |

**One price pipeline.** Every generated offer goes through the same steps, in this order:

1. **Base price:** Better Flea's price index, live tarkov.dev prices, or SPT's own, then your own price if you set one.
2. **Condition:** key uses, resource left, durability (for helmets and armour, their inserts' durability).
3. **Drift and demand:** a slow random walk per item, and rises when people buy.
4. **Floor:** never under what the best trader pays, plus a margin.
5. **Spread:** ordinary offers sit a little under or over normal.
6. **Sellers:** overpricing, quick sales and odd prices for wrecked armour.
7. **Global multiplier.**
8. **Human-looking prices:** prices to the rouble, easy numbers like 44 444 and 49 999, and one-rouble undercuts.

The price a player's own listing is measured against (how fast it sells) is worked out the same way, so "normal"
means the same thing to buyers and to the generated sellers.

---

## 3. What SPT 4.1 does that surprised us

These come from reading SPT's server code (decompiled from the 4.1.6 DLLs) and testing on a clean install. Some
are bugs, some are design choices that don't suit a live-like flea. Better Flea works around each one.

**Condition rolls came out the same.** SPT rolled worn items' condition with the minimum as both ends of the range,
so every worn item of a kind had the same durability or resource. Better Flea rolls it properly.

**The "most a trader pays" price counted traders that pay nothing.** SPT's highest sell-to-trader price ignored which
items each trader buys. So the BTR Driver, Lightkeeper and the Storyteller, who take no cut, counted as paying the
full handbook price for everything. With SPT's "use the trader price if higher" setting, every item worth less than
its handbook price was listed at about its handbook price: an LEDX at 970k when it was worth about 660k. Only traders
that really buy an item count now.

**SPT's trader cuts are out of date.** SPT's traders pay 10 to 16 points more than live ones. Live Therapist pays 51%
of an item's base price, and SPT's pays 63%. The full table is in [price-index.md](price-index.md#live-trader-prices).

**Every list had a 20% deal on top.** SPT places each offer anywhere from 20% under to 20% over normal, in whole
percent steps. That left a 20% deal at the top of nearly every list, and a dozen offers at exactly the same price.
Better Flea uses 4% under to 15% over, and prices to the rouble.

**Packs were always a discount.** SPT prices pack offers (several items bought together) at 75 to 96% of normal. On
the live flea a pack is more often a way to sell junk at full price.

**A player's "normal" price averaged every offer.** SPT judged a listing against the average of all offers of that
item. A one-plate armour was judged against full-plate ones, and a worn gun against new ones. Better Flea prices what
is actually in the listing.

**SPT already caps silly prices.** A weapon part or electronic whose flea price is far above its handbook price is
capped, and nothing is priced under what a trader pays. This matters most for modded items. Better Flea's "normal"
price now does the same, so the two agree.

**SPT's tiered flea copies every offer on every search.** That's why Better Flea's unlock levels don't use it. They
copy only the locked offers on the page you're looking at, and only for you, which is also safe in co-op.

**Banning items the obvious way changes bot loot.** SPT has two blacklists. The item blacklist (`ItemConfig`) also
changes what bots carry. Better Flea only uses the flea's own blacklist.

**Each offer gets its own copy of a trader's assort entry.** That's what lets Better Flea build a different gun for
every offer.

**Memory.** Making every offer at startup at 3x SPT's count would use far more memory. Better Flea makes a share at
startup and the rest when someone opens a category or searches. It runs at about 390 to 450 MB, against vanilla
SPT's 457 MB.

**Helmet and armour durability is in the inserts.** Many helmets and armours have no durability of their own: 62 of
111 helmets, 39 of 42 armours and 26 of 66 rigs in vanilla. Their durability is in their built-in inserts, which is
what the game shows you. SPT prices offers by the outer shell only, so a helmet at 6/14 was priced as new. See
[section 6](#6-armour-and-helmets).

**Turning off BSG's bans doesn't change what the game client sees.** See [section 9](#9-flea-bans-who-decides-what-can-be-listed).

---

## 4. Prices: what the data says

The short version is below. The details, and every number behind them, are in [price-index.md](price-index.md),
[price-history.md](price-history.md) and [price-sources-compared.md](price-sources-compared.md).

**SPT's prices are right on the whole and badly wrong for a few.** The median item is where the real flea puts it,
but 12% are more than 3x off. Examples: a 275k FN SCAR-H X-17 that sells for 17k, 44k Glock sights that sell for under
1k, and a 503k jar of pickles that sells for 53k. Iron sights, mounts, assault rifles, muzzle devices and pistols are
the most overpriced kinds. Electronics, foregrips and ammo packs are the most underpriced.

**The handbook is no guide to flea prices.** The median item sells for twice its handbook price, and 32% are more
than 3x off. A Vulkan-5 helmet has an 8k handbook price and sells for 400k to 900k.

**One day's prices are noisy, a year's aren't.** On one day's averages, one item in five was more than 3x apart
between PvP and PvE. On a year of daily medians, only 53 of 2,868 items are. Most of what looked like a PvP/PvE split
was a thin market on one day.

**Where PvP and PvE really differ, it's the PvE economy:**
- **Keys and keycards** cost far more on PvE, because a key's room can be looted without getting killed for it. A
  Dorm 314 marked key is 4.39M on PvP and 14.5M on PvE.
- **Repair kits, GPUs and LEDX** are 1.5 to 3x dearer on PvE.

Better Flea follows PvP, except for keys, which get a value between the two.

**Few listings means a misleading average.** An average from one or two listings is usually a joke or bait: an
NSPU-M eyecup listed at 10,000 that Prapor sells for 355. With few listings, the average is mostly the offers nobody
buys. Better Flea only trusts readings with three or more listings, and counts a thin day near its cheapest listing.

**Cheap gun parts are the noisiest prices.** Mounts, stocks, iron sights and muzzle devices are listed a handful of
times a day. One joke listing moves the average (a 2 rouble handguard, a 30,000 rouble muzzle brake that traders sell
for 600). Trader prices are the best check.

**Anyone who can buy an item from a trader caps its flea price.** Over a trader's cash price, the flea only keeps part
of the difference, and how much depends on how hard the item is to unlock:
- An easy unlock keeps nothing: a Mosin Tacfire grip, 17.1k on the flea, is 3.1k.
- Loyalty 4 keeps half: an AS VAL dust cover, 32.4k on the flea, is 17.3k.
- A very hard quest unlock keeps 90%.

**No crafts or barters turn into easy money with these prices.** We checked all 209 hideout crafts and 399 trader
barters. The best craft turns its inputs into 5x their value, for 89k profit, and it's an event craft. Trader barters
top out at about 4x. The dogtag barters look like 20x, but dogtags only come from kills, so they can't be bought for
that.

---

## 5. What the live flea looks like

We studied screenshots of the live PvE flea (EFT 1.2.5) to see how real offers look, and compared them with Better
Flea. These are observations of what players do, not data that ships with the mod.

**How long offers stay up.** Most offers have 10 to 23 hours left. Big stacks from high-rated sellers often run
multi-day, up to about 5 days. Single items are almost always under a day.

**How sellers price.** Prices cluster in a few shapes:
- round thousands: 45 000, 50 000;
- repeated digits: 11 111, 33 333, 44 444, 211 111, about 15 to 25% of offers;
- just under a round number: 19 999, 49 999, 99 999;
- one rouble under someone else: 13 332 under 13 333.

Odd exact prices are a minority.

**How wide a list is.** From the cheapest offer to the 12th, prices usually rise 10 to 35%. Valuable, busy items
are tight (LEDX, screw nuts). A few items run 80% or more. Gun lists end with troll prices at 10 to 15x normal.

**Price against condition.** Live prices follow condition only loosely:
- A used water bottle is 30 to 35% off whatever's left.
- Used AFAKs are only a little cheaper than full ones.
- Partly full fuel cans cost about as much as a full one from Jaeger.
- **Wrecked armour is never listed at what it's worth.** It sits a bit under the cheapest decent offer. Better
  Flea copies this. A wrecked helmet or armour, under half a new one's worth, is listed a little under the cheapest
  decent offer. It's a poor deal at the top of the list, like on the live flea.

**Keys.** Mechanical keys are nearly all unused (about 97%). Marked keys are almost always sold with one use left.

**Guns.** A lowered max durability is common on guns people use, mostly 82 to 98. It's rare on guns nobody uses.
Clean guns sometimes sell under the trader's price.

**Sellers.**
- Ratings mostly run 0 to 500.
- The same seller often has several offers of one item at about the same price.
- Edition badges are far more common than Better Flea first assumed: about 35% crown and 30% lightning bolt.

---

## 6. Armour and helmets

**Durability lives in the inserts.** A helmet or armour with no durability of its own takes its condition from its
built-in inserts: their total max against new, and their total current. The max counts most, because it's what the
item can ever be repaired to. A 33/40 plate is about 80% of a new one, a 5/33 about half, an 8/14 under a quarter.
So a nearly empty plate with a good max can't be bought cheap, repaired and sold for a profit.

**What traders pay sets the floor.** A worn helmet can't go under what the best trader pays for it, inserts
included, because players could sell it to that trader instead. So when a modded trader sells a helmet new for
less than another trader buys it back for, the trader stays the cheapest. That price comes from the trader's mod,
and Better Flea leaves it alone. For example, Ragman pays more for Artem's SF High Cut than Artem sells it for new.
An unsupported setting (**No floor for trader loops**) drops the floor for items like this only. For them, the
traders already make the profit possible.

**Wrecked means the whole item.** An item counts as wrecked by the average condition of its parts, weighted by what
each is worth. One part far worse than the rest pulls that down. For example, an average of 80% with one part at
10% counts as 55%. So a good carrier with one bad plate isn't wrecked.

**Wrecked armour is a hard sell.** A player listing wrecked armour at its normal price has about a 4 in 10 chance
it never sells. The rest take three times as long.

**Dear offers sell late, not never.** The game's average price for an item is a new one's price (Better Flea shows
its normal price there), so a player going by it can list a worn plateless armour 40% over what it's worth without
knowing. A hard
"never sells past 15% over" left such offers sitting until they expired. Now buyers keep coming, each less likely
to take it the dearer it is (1 in 20 at 15% over, then falling faster to none at twice the normal price). For a 47k
armour: 40% over sells in about 2 hours, 60% over in about 3.5 hours (1 in 6 expire first), 80% over 1 in 3 sell.
The deterrent is time, as on the live flea. An item the player bought in the last 48 hours (trader or flea, its
parts too) doesn't get this: past 15% over it never sells, or buying at normal and relisting 40% over would pay
about a third every couple of hours. Plain items (no durability, alone) normally go to the order book, whose buyers
never pay past 15% over; past that they get the same re-rolls as gear, and still lose most buyers to cheaper offers.

**Plates.**
- Class 5 and 6 front and back plates are flea-banned, so front and back plates on offers are class 3 or 4. Side
  plates can be class 5 or 6.
- Light carriers tend to get heavier plates, and heavy carriers lighter ones.
- Which plates a carrier gets goes by how it ranks against other carriers, not by its price in roubles. A rouble
  rule stopped working whenever prices changed.

**What the live flea shows.**
- Some carriers are almost all badly worn with a lowered max and only some plates (the CQC Osprey).
- Others are mostly near new (the HighCom Trooper, the Bastion helmet).

**One banned insert blocks the whole helmet.** See [section 9](#9-flea-bans-who-decides-what-can-be-listed).

---

## 7. Guns

**A whole gun sells for less than its parts.** For most guns, the default build sells on the flea for less than its
parts do one by one:

| Gun | Default build on the flea | Its parts, loose |
|---|--:|--:|
| FN SCAR-L | 59k | 205k |
| HK 416A5 | 129k | 201k |
| SVDS | 112k | 319k |
| Glock 18C | 72k | 300k |

Loose parts are thinly traded and listed high, and whole guns sell cheap. So a gun can't be priced as its own flea
price plus its parts' flea prices. Better Flea prices the gun itself from trader prices, by how hard it is to
unlock. Each attachment is worth what it sells for on its own, so stripping a gun makes no money. A gun with a Vudu
on it costs about a Vudu more.

**How hard a gun is to unlock matters.** A gun anyone can buy from a trader at loyalty 1 isn't worth much more on
the flea. One behind a very hard quest keeps most of its flea price. [quest-unlock-difficulty.md](quest-unlock-difficulty.md)
scores every quest that unlocks a trader item, by:
- its level lock;
- the work in it and in every quest before it;
- its bosses and maps;
- its hand-ins;
- skill and loyalty requirements;
- whether a hidden trader is involved.

Event-only quests that SPT never gives unlock items no trader will ever sell, so those keep their full flea price.

**Building a believable gun.**
- **Barrel first.** The barrel decides the build: a 10.3" M4 is a close-quarters gun, a 20" one a marksman rifle.
- **SPT's bot order for the rest.** The other parts follow the order SPT's bot generator uses, so parts that block
  other slots go on early.
- **Optic before mounts.** The optic is picked first, then the mounts it needs to sit in the best spot.
- **Optics suit the gun.** Pistols get red dots, SMGs and shotguns up to 4x, rifles up to 8x.
- **Parts are scored from the game's own item data:** ergonomics and recoil, and whether a trader sells them.
- **Parts from mods you don't have, and flea-banned parts, come off with everything on them.** One exception: if
  every part that fits a required slot is banned, the slot still gets one.

---

## 8. Modded items

**tarkov.dev only knows the base game.** A modded item keeps whatever price its mod gives it, which is often a
guess. Better Flea's **Estimate modded items** moves each one onto the same scale as base-game items of the same
kind. It estimates the item twice, once from its flea price and once from its handbook price, and keeps whichever
is nearer what that kind usually costs. It's also capped at what 9 in 10 base-game items of that kind cost.

**A trader's price beats a guess.** A modded item a trader sells is real money in that trader's mod. So the item:
- is worth at least about the trader's price;
- is worth at most what base-game items of its kind sell for next to their trader price (1 to 2x).

A helmet a trader sells new for 65k isn't worth 164k from a player. About 200 modded items came down this way.

**WTT ContentBackport adds base-game items SPT doesn't have yet.** The price index covers the 675 of them that
can be traded, so they're priced like any other base-game item.

**Finding which mod added an item.** Better Flea reads each mod's files for item ids, and an item belongs to the mod
that defines it, not one that only mentions it. The scan has limits so a huge mod folder can't stall the server:
- 20,000 files and 512 MB;
- 10 seconds per mod and 30 seconds in total.

A mod that hits a limit shows as "Failed to load" on the Bans tab.

---

## 9. Flea bans: who decides what can be listed

Three places can stop an item going on the flea, and they don't always agree:

1. **The item's own flag** (`CanSellOnRagfair` in the item data). The game client reads this flag from the server
   when it starts. It decides whether you can list the item, and other client mods (Price Index, quick-sell mods)
   ask the game the same question.
2. **SPT's flea blacklist** (`ragfair.json`, plus anything Better Flea bans). This decides which items SPT makes
   offers for.
3. **Better Flea's own rules** (trader purchase lock, unlock levels, selling off). The server enforces these. The
   client plugin shows them in game.

What we learned:
- **Turning off BSG's bans in SPT's settings only covers the offers.** SPT's own switch, set in `ragfair.json`
  before the server starts, marks everything sellable. Turned off later (as a mod does), it only stops SPT keeping
  the game's banned items off its own offers. Each item still says it can't be sold, so the game shows it as banned
  and won't let you list one. Better Flea now clears the flag too.
- **A helmet can't be listed if any of its built-in inserts is banned,** even when the helmet itself isn't. The
  inserts aren't in the handbook, so a ban lift that only walks the handbook misses them (the Champion's helmet's
  top plate). Better Flea lifts an item's built-in inserts along with the item.
- **The game client loads item data once, at startup.** After changing bans on the server, restart the game too.
- **Quest items and mods' own bans** are left alone when BSG's bans are off. A mod's ban is lifted only if you
  allow that mod's items on the Bans tab.
- **Some things should never be sellable, whatever the setting.** SPT's own switch makes everything sellable,
  money and dogtags included, and dogtags on the flea would make the dogtag barters easy to buy. Better Flea keeps
  these off:
  - money, dogtags, secure containers and armbands;
  - anything the real game can't trade anywhere, such as prestige and Arena rewards.
- **SPT's test and unreleased items could always be listed.** SPT keeps the developer's balaclava, test items,
  shrapnel and the like out of the game with its item blacklist. Their own data still said "sellable", so a player
  who had one could list it. Better Flea marks them unsellable.

---

## 10. Player selling and flipping

What stops the flea being a money printer:

| Rule | What it does |
|---|---|
| Sale speed by price | A listing under normal sells in under a minute. At normal it takes a while. Over normal it sells less often, and past a point never. |
| Buyers | Buyers come at the item's own pace and take the cheapest offer, if its price is one people pay. |
| Undercuts | When you have the cheapest offer, an NPC seller sometimes lists just under you. |
| Flip penalty | Items you bought recently wait longer to sell. |
| Trader purchase lock | Items bought from traders can't be listed at all. Bought items are tracked on the server, so nothing about the item changes in your stash. |
| Deals | Deals are rare and short-lived: an offer well under normal is bought within minutes. |

We also found that a "deal" has to be judged against what's actually in the offer. If it isn't, a plate carrier
without plates looks like a steal next to full ones.

---

## 11. Open questions

- **Part prices inside a build.** Better Flea's random builds carry more parts than a default build, so a heavily
  built gun is mostly priced by its parts. It may want a build discount of its own.
- **Expiry and stack size.** On the live flea, big stacks stay up for days. Better Flea doesn't link the two yet.
- **Lowered max on guns.** On the live flea it's more common and narrower (82 to 98) than Better Flea makes it.
- **Price history.** tarkov.dev's history API was down when the index was built. The index uses a year of
  per-item history from its JSON files instead.
- **SPT 4.1.7** is expected to fix some of SPT's own prices, which may change what the index needs to correct.

---

## 12. Tools and rebuilding the data

Everything in this folder can be rebuilt from public data with the scripts in [tools/](tools/):

| Script | Makes |
|---|---|
| `fetch_price_history.py` | Downloads a year of flea price history per item from tarkov.dev (about 570 MB, kept out of the repo) |
| `quest_difficulty.py` | [quest-unlock-difficulty.md](quest-unlock-difficulty.md) from SPT's quest data |
| `price_index.py` | The price index shipped in the mod, [price-index.tsv](price-index.tsv) and [price-index-review.md](price-index-review.md) |
| `price-index-overrides.json` | Prices set by hand, `{"<item id>": price}` |
| `wipe_curves.py` | The Wipe Cycle's curves (`Prices/wipe-curves.json`) and [wipe-cycle.md](wipe-cycle.md) |

```
python -I Research/tools/fetch_price_history.py [download-folder]
python -I Research/tools/quest_difficulty.py [path-to-SPT_Data/database] [output.md] [download-folder]/quest-difficulty.tsv
python -I Research/tools/price_index.py [path-to-SPT_Data/database] [download-folder] [WTT-ContentBackport/db]
python -I Research/tools/wipe_curves.py [path-to-SPT_Data/database] [download-folder]
```

Price data comes from [tarkov.dev](https://tarkov.dev), which publishes it openly. Thanks to them. The index is
rebuilt by hand and shipped inside the mod, so your server never needs the internet for it.
