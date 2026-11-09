# Selling on the flea

[Back to the guide](README.md)

How fast your own listings sell, why, and what stops the flea being a money printer. Settings are on the
[Selling tab](02-settings-reference.md#selling-tab).

## The short version

- **Price it at its normal price** and it sells in a few minutes (gear and guns a bit longer).
- **Under normal** sells in under a minute.
- **Over normal** still sells, but later, and the dearer it is the more likely the offer expires first. At **twice the
  normal price** it never sells.
- **With the flip penalty on** (off by default), something you bought in the last 48 hours never sells more than 15%
  over normal, so you can't buy at normal and relist far over for profit.
- **The average the game shows when you list is a new one's price.** For a worn or stripped item it's too high. Use
  Price Index or the settings page's [sale odds](#sale-odds) for your exact item.

## Your item's normal price

Everything is measured against the **normal price**: what one of your item, exactly as it is, is worth on the flea
right now. It's worked out the same way as other sellers' offers, so "normal" means the same thing to buyers and
sellers:

- **Each part priced on its own:** the item, its plates, inserts, attachments, the rounds in a mag, each at its base
  price and its own condition.
- **Then the same steps as generated offers:** drift, the price multiplier, prices by category, the Wipe Cycle.
- **Guns** use the [gun value](04-guns-and-attachments.md#how-a-gun-is-priced).
- **Never under what traders pay for all of it**, built-in inserts included.
- A **pack** (several items sold together) is the normal price times the count.

A rise in price from players buying an item up never raises your normal price, so buying an item up and selling at
the raised price doesn't work.

## How likely it sells

Over normal, whether your offer sells at all is decided once, so leaving it up longer doesn't help. It goes by the
roubles over, against some room over normal: 12% of the normal price (0.8 x `expensivePercent`) plus 8,000 roubles
(`overpriceRoubles`, never more than 40% of the normal price: `overpriceRoublesMaxPercent`). So a few thousand over on
a cheap item hardly counts, and the same share over on a dear one does.

Gear gets more room (`gearOverpricePercent`, 20%): guns, armour, helmets, rigs with plates, and anything with
durability or with parts in it. Its value hangs on its parts, plates and wear, so nobody knows it to the rouble. Single
items (wires, a LEDX, a key) keep the 12%.

What that means at the defaults (tested on 936 gear offers: 785 sold, 782 expected):

| Item | Normal | Always sells up to | Half the time at | Never sells from about |
|---|---|---|---|---|
| PACA | 30k | 40k | 44k | 48k |
| Slick, class 4 plates | 208k | 242k | 258k | 270k |
| Slick, no plates | 178k | 207k | 221k | 236k |
| Slick, half worn | 161k | 188k | 202k | 216k |
| AVS rig | 87k | 104k | 112k | 120k |
| Killa helmet with face shield | 151k | 176k | 189k | 202k |
| Altyn with face shield | 312k | 360k | 382k | 401k |
| AK-74N, stock | 33k | 43k | 48k | 52k |
| M4A1, stock | 90k | 108k | 116k | 124k |
| M4A1, SpecOps build | 227k | 262k | 280k | 294k |
| AKM, Kreb thermal build | 982k | 1,088k | 1,148k | 1,194k |

The normal price is for the item as it is, so stripping plates or parts, or wear, lowers it. A bare or worn Slick
priced like a full new one is overpriced.

```
share = 0.12, and over 100,000: 0.12 x (100,000 / normal)^0.2     (8% on a LEDX, 5% on an 8.25M marked key)
gear:   0.20, and over 300,000: 0.20 x (300,000 / normal)^0.2     (18% on a 500k build, 16% on a 1M one)
room = share x normal + min(8,000, 0.4 x normal)        x = (your price - normal) / room

sells at all:           always up to x 0.5, then 0.5^((2x - 1)^3)    96% at x 0.7, half at x 1, 1 in 7 at x 1.2, never from x 1.5
extra wait (median):    30 minutes x x^1.5                           3 at x 0.2, 17 at x 0.7, 30 at x 1, 50 at x 1.4
                        over 100,000: times (normal / 100,000)^0.4   twice as long on a LEDX, 6 times on a marked key
```

So the dearer the item, the less it can go over in roubles, and the longer that takes: a marked key at 8.25M listed
at 8.66M sells about half the time, after about 3.4 hours; from about 8.88M it never sells. A LEDX 10% over sells about
a third of the time, after about an hour and a half (`overpriceDearShrink`, `overpriceDearSlowdown`).

The extra wait comes on top of the wait at the normal price, and it's random but never straight away: most land
between half and twice the usual wait, about 1 in 100 under a quarter of it. So an offer that usually takes 3 hours
doesn't sell in 10 minutes. The ones that don't sell were decided at listing and run until the offer ends.
The normal price is for the offer as it is, so a key with 1 use left, worn armour or a gun with parts on is judged
against its own normal price. `neverSellsPercent` (100) is only a safety cap: at the defaults nothing sells past 78%
over anyway.

It's the same for every listing: plain items, gear, guns, keys. One thing with durability listed on its own (glasses, a
bare helmet, a plate) that another seller has cheaper, when yours isn't well under normal, waits twice as long for its
buyer. Guns and gear with parts or plates in them aren't compared with other sellers' offers.

**Old overpricing** (`classicOverpricing`, off): 2.5.1's rule, for anyone who preferred it. There's little reason to
turn it on. Over normal, one buyer takes an offer 1 in 20 times at `expensivePercent` over, less further over, none
at `neverSellsPercent`, and a new buyer looks every few minutes until the offer ends: 6 minutes or so on a 100k item,
longer the dearer. The same share of the price for every item, with no rouble room, no gear room and no less room on
dear items. Whether it sells is still decided at listing, from all those looks together, so a 12 hour offer 40% over
still sells most of the time (about 95% on a 100k item). Nothing else changes.

## How buyers come

There are two kinds of listing.

**Gear, guns and anything with durability or contents** (armour, helmets, guns, a filled rig): when you list it,
whether it sells is decided (above). If it does, the sale comes after a wait. At the normal price that's about a
minute at 20,000 roubles, longer the dearer and rarer: about 1.3 minutes at 33,000, 2.3 at 110,000, 4 at 340,000, 9
for a superrare LEDX, 30 for a superrare marked key (`uniqueMinMinutes` x the square root of how many times dearer than
20,000, x 1.25 if rare or 1.5 if superrare: `rareWaitMultiplier`, `superrareWaitMultiplier`; at most
`uniqueMaxMinutes`). Each wait is random around that, most between half and twice it. It's less when the offer is
cheaper than normal, plus the extra wait for how far over normal it is. Dear plain items that seldom trade (a GPU, a
LEDX) wait the same before buyers get to them; busy ones (wires) only seconds.

**Plain single items** (no durability, alone, not a gun: a GPU, meds, an empty rig): these go to the order book. For the
item you have open and every item a player has listed, a buyer turns up every 15 seconds to a minute (longer for dear,
rare or unwanted items) and goes down the list cheapest first. Your offer sells when the buyers reach it. Over normal,
other sellers' offers sit ahead of yours, so whether it sells is decided once instead (above), after buyers first get
to it.

Each buyer takes one or two of most things, or a round lot of ammo (30, 60, 90, 120, 150, 180, 200 or the lot), so a
pile sells a bit at a time, and buyers come a little slower after each of your sales.

## Sale odds

How often an offer sells before it ends (12 hours), and the usual wait, with the default settings:

| Item, normal | At normal | 5% over | 10% over | 20% over | 30% over | 50% over |
|---|---|---|---|---|---|---|
| Wires, 23k | always, 35 s | always, 2 min | always, 4 min | always, 9 min | 98%, 17 min | 33%, 35 min |
| CPU fan, 21k | always, 35 s | always, 2 min | always, 3 min | always, 9 min | always, 15 min | 51%, 31 min |
| AK-74N, 34k | always, 1 min | always, 3 min | always, 6 min | always, 14 min | 80%, 25 min | 2%, 53 min |
| PACA, 31k | always, 1 min | always, 3 min | always, 6 min | always, 13 min | 86%, 23 min | 4%, 47 min |
| Diary, 41k | always, 2 min | always, 4 min | always, 8 min | 99%, 17 min | 62%, 30 min | never |
| HK 416A5, 112k | always, 4 min | always, 8 min | always, 16 min | 38%, 36 min | never | never |
| Defibrillator, 153k | always, 4 min | always, 11 min | always, 22 min | 11%, 52 min | never | never |
| GPU, 309k | always, 6 min | always, 19 min | 84%, 41 min | never | never | never |
| Slick, 342k | always, 6 min | always, 21 min | 78%, 44 min | never | never | never |
| LEDX, 734k | always, 9 min | always, 38 min | 30%, 84 min | never | never | never |
| Marked key (314), 8.41M | always, 31 min | 52%, 3.5 h | never | never | never | never |

**On the settings page:** the Lookup tab's **Offers on the flea** lists every player's offer that's up, when it ends,
and roughly how it's priced: underpriced (should sell quickly), priced well (should sell in decent time), a bit dear
(may not sell) or overpriced (likely not to sell). The exact odds above stay hidden, since a player who can see them
prices right up to the limit. An admin can show them with **Show exact sale odds** (Debug tab, `showSaleOdds`): then
**Item lookup** shows this table for any item as found, and each offer the table for that exact item (worn and plated
as it is) with its own price marked. Neither ever says whether or when an offer will sell: that's decided when it's
listed, and a player who could see it would take the offer down and list it again until it sells.

## Wrecked gear

Wrecked gear (under half a new one's worth, all parts together: [what counts](05-armour-and-condition.md#wrecked-gear))
is a slow sell: its buyer takes three times as long to come, even at the normal price. Over normal it sells as often as
anything else at that price. Players do buy wrecked armour to repair it, but they won't pay over the odds for it.

## What stops flipping

| Rule | What it does | Default |
|---|---|---|
| Sale speed by price | Overpriced offers sell late or not at all | On |
| Banned plates | Armor with a front or back plate the flea doesn't take (class 5 and 6, like the Kiba Arms steel plates in SPT's default Slick) can't be listed until it's taken out, as on live | Always |
| 48-hour rule | Part of the flip penalty. Anything you bought (trader or flea, its parts too, stacks by item type) in the last 48 hours gets no long tail: past 15% over it never sells. Its offers show a Flip penalty tag on the Lookup tab | Off (with the flip penalty) |
| Flip penalty | Buy more than 2 of one item and list it within 30 minutes: buyers take up to 10 minutes longer. Fence purchases count three times. The game says so in a message, and with a pop-up if you have the plugin | Off |
| Trader item lock | Items bought from traders (and Fence) can never be listed; parts on a bought gun too | Off |
| Undercuts | When yours is the cheapest offer, another seller now and then lists just under it a few minutes later and takes most of your buyers. 35% of the time at normal, 80% at 15% over, hardly ever 10% under | On |
| Floor | No generated offer is under what a trader pays, so there's nothing to buy on the flea and sell to a trader | On |
| Deals | An offer 10% or more under normal is bought by someone else within 1 to 6 minutes | On |
| Demand | Buying up an item raises its next offers, but never your normal price | On |

**Flip penalty and trader item lock compared:**

| | Flip penalty | Trader item lock |
|---|---|---|
| What happens | The item can be sold; buyers take longer | The item can never be sold on the flea |
| Which purchases | Traders, Fence and the flea | Traders and Fence (flea too if you choose) |
| When it starts | After buying several of one item | From the first item |
| What the player sees | A pop-up and a message | A grey item and a warning |

## Set sale times

**Sale speed or Set sale times?** Sale speed and Buyers are the market: how long an offer takes is worked out from
its price against normal, the other sellers' offers, undercuts and the buyers coming along, so a cheap offer goes in
seconds and an overpriced one may never sell. Set sale times skips all of that: every offer just waits a random time
between the shortest and longest you set for its rarity. Use Sale speed for a living market, Set sale times for
predictable waits.

To decide the sale times yourself, turn on **Set sale times** (Selling tab, with Advanced settings on;
`selling.jsonc: saleTime`). A player's offer then sells after a wait between the shortest and longest you set for its
rarity (common, rare, very rare), in minutes. Buyers, other sellers' offers and undercuts no longer change it. The
flip penalty still adds its wait, and an offer that would sell after it ends just expires.

The waits it starts with are about what an item at the normal price takes with it off: common under a minute, rare
1 to 3 minutes, very rare 3 to 8. Guns and armour normally take a minute to half an hour, longer the dearer; with set
sale times on they go by their rarity like everything else.

| You want | Set |
|---|---|
| Everything sells straight away | All six waits 0, **Sells whatever the price** on |
| Everything takes 60 minutes or more | Every shortest wait 60, every longest wait 90 (or more) |
| Common items at once, the rest in a few hours | Common 0 and 0, rare and very rare 120 and 240 |

With **Sells whatever the price** off, the price still counts: over the normal price an offer sells less often and
takes longer, and far enough over it never sells (as [above](#how-likely-it-sells)). On, any price sells.

To keep the normal sale speed and only make rare items slower or quicker, leave this off and change
**Slower buyers by rarity** (`buyers.rarityMultipliers`, also advanced): 1 = as quick as a common item.

## Flea access

**Flea access** (Selling tab, `selling.jsonc: fleaAccess`) sets when players get the Flea Market, how many offers they
can have up, and how fast their flea rep grows. Left at its defaults it changes nothing, and SPT's values (or another
mod's, like SVM's) are kept.

- **Flea unlock level** (`unlockLevel`): the player level the Flea Market opens at. SPT's is 15.
- **Flea rep gain multiplier** (`repGainMultiplier`): flea rep from each sale x this. SPT gives +0.02 for every
  100,000 roubles sold, so reaching the 5-slot step at rep 7 takes about 34 million roubles of sales. At 2, half that.
  0 = sales never raise rep.
- **Offer slots** go up with flea rep. SPT gives 4 at the start (rep 0.2), 5 from rep 7, 6 from 30, and so on up to
  15 from rep 1000. **Offer slots multiplier** (`offerSlotsMultiplier`) multiplies every step, then **Extra offer
  slots** (`extraOfferSlots`) adds to every step, so more rep still means more slots. A player always has at least 1.
  Under the settings, a table shows every rep step's slots now and with what you've typed, before you save.

| Multiplier, extra | At the start | At rep 1000 |
|---|--:|--:|
| 1, 0 (SPT) | 4 | 15 |
| 2, 0 | 8 | 30 |
| 1, 5 | 9 | 20 |
| 2, 1 | 9 | 31 |

The unlock level and slots are part of what the game loads at login: players see a change after logging in again.
Editions with more slots (`countForSpecialEditions`, usually one more) are changed the same way.

Your flea rating on the flea screen changes as soon as an offer sells, whatever these settings. SPT adds the rep but
never tells the game, so without Better Flea it only shows after logging in again.

## Fees

SPT charges the game's own listing fee: see [The flea fee](03-how-prices-work.md#the-flea-fee). It grows fast when you
ask more than an item is worth, so overpricing costs real money even when it sells.

## Turning things off

- No selling at all: `enabled` off.
- Found in raid only: `foundInRaidOnly` on.
- SPT's own sale rolls instead of Better Flea's: `saleDelay.enabled` off. (The 48-hour rule and the long tail go with it.)
- Gentler or harsher on overpricing: raise or lower `expensivePercent` and `overpriceRoubles`.
