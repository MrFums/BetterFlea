# Selling on the flea

[Back to the guide](README.md)

How fast your own listings sell, why, and what stops the flea being a money printer. Settings are on the
[Selling tab](02-settings-reference.md#selling-tab).

## The short version

- **Price it at its normal price** and it sells in a few minutes (gear and guns a bit longer).
- **Under normal** sells in under a minute.
- **Over normal** still sells, but later, and the dearer it is the more likely the offer expires first. At **twice the
  normal price** it never sells.
- **Something you bought in the last 48 hours** never sells more than 15% over normal. You can't buy at normal and
  relist far over for profit.
- **The average the game shows when you list is a new one's price.** For a worn or stripped item it's too high. Use
  Price Index or the settings page's [sale odds](#sale-odds) for your exact item.

## Your item's normal price

Everything is measured against the **normal price**: what one of your item, exactly as it is, is worth on the flea
right now. It's worked out the same way as other sellers' offers, so "normal" means the same thing to buyers and
sellers:

- **Each part priced on its own:** the item, its plates, inserts, attachments, the rounds in a mag, each at its base
  price and its own condition.
- **Then the same steps as generated offers:** drift, the price multiplier, the marked key multiplier, the Wipe Cycle.
- **Guns** use the [gun value](04-guns-and-attachments.md#how-a-gun-is-priced).
- **Never under what traders pay for all of it**, built-in inserts included.
- A **pack** (several items sold together) is the normal price times the count.

A rise in price from players buying an item up never raises your normal price, so buying an item up and selling at
the raised price doesn't work.

## How likely a buyer is

Each buyer takes your offer with a chance set by how far over normal it is:

```
over = your price / normal price - 1        L = expensivePercent / 100 (0.15)        N = neverSellsPercent / 100 (1.0)

at or under normal:     1
up to L over:           exp(-ln(20) / L x over)        1 in 20 at 15% over
from L to N over:       0.05 x ((N - over) / (N - L))^2
N over or more:         0, never
```

If another seller already has the item cheaper and yours isn't well under normal, only 35% of buyers are left for you
(`cheaperOfferShare`).

## How buyers come

There are two kinds of listing.

**Gear, guns and anything with durability or contents** (armour, helmets, guns, a filled rig): one buyer looks when
you list it. If they buy, the sale comes after a wait: 5 to 20 minutes at about 100,000 roubles (longer for dearer
items), times a factor that grows with the price (about a tenth of that 10% under, 4x at 15% over). If they don't, another
buyer comes along every half of that wait, each with the same chance, until one buys or the offer ends.

**Plain single items** (no durability, alone, not a gun: a GPU, meds, an empty rig): these go to the order book. For the
item you have open and every item a player has listed, a buyer turns up every 15 seconds to a minute (longer for dear,
rare or unwanted items) and goes down the list cheapest first. Each buyer pays up to a price of their own: most near
normal, about 1 in 4 up to 6% over, a few up to 15% over. Your offer sells when the buyers reach it. Past 15% over, the
order book's buyers never reach it, so it gets the same occasional buyers as gear instead.

Each buyer takes one or two of most things, or a round lot of ammo (30, 60, 90, 120, 150, 180, 200 or the lot), so a
pile sells a bit at a time, and buyers come a little slower after each of your sales.

## Sale odds

A worn Trooper armour with no plates, normal price 47k, on a 12-hour offer:

| Listed at | Over normal | Sells before it ends | Usual wait |
|---|---|---|---|
| 47k | 0% | always | a few minutes |
| 52k | 10% | always | about 30 minutes |
| 56k | 20% | always | about an hour |
| 66k | 40% | almost always | about 2 hours |
| 75k | 60% | about 5 in 6 | about 3.5 hours |
| 85k | 80% | about 1 in 3 | about 5 hours |
| 94k or more | 100% or more | never | |

Dearer items wait longer: a 200k item takes about 1.5 to 2 times as long. Plain items also lose most buyers to cheaper
offers of the same item, so they're slower than this.

**See it for your item:** the settings page's Economy tab. **Item lookup** shows this table for any item as found.
**Offers on the flea** lists every player's offer that's up, when its buyer comes, and the table for that exact item
(worn and plated as it is), with its own price marked. Both assume no one else has it cheaper.

## Wrecked gear

Wrecked gear (under half a new one's worth, all parts together: [what counts](05-armour-and-condition.md#wrecked-gear))
is a hard sell even at the normal price. Its one buyer at listing takes it 60% as often, the wait is three times as
long, and it gets no more buyers after that. So about 4 in 10 never sell at the normal price, and priced over normal
almost none do. Price it at or under normal and be patient. Players do buy wrecked armour to repair it, but they won't
pay over the odds for it.

## What stops flipping

| Rule | What it does | Default |
|---|---|---|
| Sale speed by price | Overpriced offers sell late or not at all | On |
| 48-hour rule | Anything you bought (trader or flea, its parts too, stacks by item type) in the last 48 hours gets no long tail: past 15% over it never sells | Always, with sale delays on |
| Flip penalty | Buy more than 2 of one item and list it within 30 minutes: buyers take up to 10 minutes longer. Fence purchases count three times. The game says so in a message, and with a pop-up if you have the plugin | On |
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

## Fees

SPT charges the game's own listing fee: see [The flea fee](03-how-prices-work.md#the-flea-fee). It grows fast when you
ask more than an item is worth, so overpricing costs real money even when it sells.

## Turning things off

- No selling at all: `enabled` off.
- Found in raid only: `foundInRaidOnly` on.
- SPT's own sale rolls instead of Better Flea's: `saleDelay.enabled` off. (The 48-hour rule and the long tail go with it.)
- Gentler or harsher on overpricing: raise or lower `expensivePercent` and `neverSellsPercent`.
