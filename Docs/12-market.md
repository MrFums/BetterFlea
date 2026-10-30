# How the market works

[Back to the guide](README.md)

What happens to the flea over time: how offers are made, how long they last, who buys them, and the sellers behind
them. Settings are on the [Listings](02-settings-reference.md#listings-tab) and [Market](02-settings-reference.md#market-tab)
tabs.

## Where offers come from

There are three kinds of offer on the flea:

| Kind | Who makes it | Notes |
|---|---|---|
| Trader offers | SPT, from each trader's assort | Unchanged by Better Flea |
| Generated offers | SPT's offer generator, with Better Flea's patches | The NPC sellers. Everything on this page is about these |
| Player offers | Real players | Measured against the same normal price; see [Selling](07-selling.md) |

**How many.** SPT makes 7 to 30 offers per item. Better Flea makes 3 times that by default, more for common items and
fewer for rare ones (1.5x and 0.6x), with extra for plates and meds (3x) and marked keys (2x), and a minimum per kind
(30 for most items, 60 for guns, 20 for keys). Money itself (dollars, euros) almost never has an offer, like live.

**Made when someone looks.** Making all of those up front would take far more memory. So at startup each item gets 15%
of its offers (at least 3), and the rest are made when a player opens its category or searches for it. Players see the
same flea either way:
- The last 4 lists you opened stay filled; opening another trims the oldest.
- An item nobody has looked at for 2 minutes goes back to its start share.
- At most 15,000 extra offers exist at once. Past that, the items looked at longest ago go back first.
- A search over more than 400 items (a whole top category like Weapon parts) shows what's there without filling every
  item. Open a smaller category to see everything.

This is why Better Flea runs at about the same memory as SPT without it, 390 to 450 MB against vanilla's 457 MB in
our tests.

## How long offers last

Live, people list for 12 or 24 hours (30% and 70% here), and a big stack is often up for days. A stack of 10 or more of
a non-stacking item is listed for 1.5 days up to 5 days 35% of the time.

Each offer is found partway through its time, and mostly soon after it went up (`freshness`), so few offers show the
red "under 10 hours". Good deals are fresher still, because they don't last; overpriced ones are older, because nobody
bought them.

## Other players buying

Every 5 minutes, buyers come round the generated offers:

- An offer at the normal price sells within an hour 4% of the time.
- A deal 20% under normal sells 20 times as fast; 20% over, 20 times slower.
- **Deals don't last:** an offer 10% or more under normal is bought by someone else 1 to 6 minutes after it appears. If
  you see one, buy it now.
- **The top of each list keeps moving:** the cheapest few offers of every item sell and are relisted fresh, about every
  2 hours for a typical item, even items nobody has open. Busy items turn over faster (wires about every hour), rare
  ones hardly ever (a LEDX).
- A sold offer is replaced by a newly listed one a while later.

## Sold before you

Buying a gun or a barter item from another seller fails 5% of the time: the game's own "offer is already sold"
message, and the offer is gone. Never on trader offers.

## Barter offers

About 1 offer in 200 wants items instead of money: one or two kinds of barter item people actually carry (common or
rare, no quest items), worth 85 to 97% of the offer's normal price. Offers under 20,000 roubles always want money.

## Packs

3% of offers of the item types SPT allows in packs are packs: 2 to 10 of an item you must buy together.
- 60% are scams, priced 2 to 25% over buying the items one by one, so they sit mid-list looking like any other offer.
  Divide the price by the count.
- The rest are a fair bundle a little under the single offers, near the top.
- 2% are silly big (50 to 80: 60 golden eggs someone buys without reading the count).

## Sellers

**Ratings.** Generated sellers have flea ratings from 2 to 300 (SPT's own go -2 to 20):
- Everyday players run up to 100, most in double digits.
- Regulars run 20 to 300, the high end rarer. A quarter of single-item sellers are regulars.
- Someone selling several of one item is usually a flipper: 65% are regulars, more for bigger stacks (90% at most).
- 5% of single-item sellers are new to the flea, at -2 to 1.

**Badges.** 25% of sellers show an Edge of Darkness badge and 20% an Unheard badge, like live. 1 in 1000 has a special
one (Developer, Sherpa or Emissary). Never a trader's or system badge.

**The same seller everywhere.** Each seller name keeps the same rating and badge on every offer.

**Your own guns.** A gun you die with can be listed 5 to 30 minutes after the raid under your killer's name, 35% of
the time. Saved builds turn up very rarely. See [Guns](04-guns-and-attachments.md#how-a-gun-offer-is-built).

## Prices over time

Each item's price drifts up to 20% either way, a few percent at a time, about every 45 minutes. Buying up an item's
offers raises its next ones (2% per offer, 30% at most), and a player selling a lot of one item now and then lowers it a
little; half of either wears off in an hour. Only new offers get the new price. See
[How prices work](03-how-prices-work.md#what-better-flea-does-instead).
