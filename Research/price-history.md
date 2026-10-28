# Flea price history

A reliable PvP and PvE flea price for every base-game item, from a year of price history, for the
[price index](price-index.md). Every item: [price-history.tsv](price-history.tsv).

Data from tarkov.dev: `json.tarkov.dev/{regular,pve}/prices/{itemId}`, which their endpoint list describes as "all flea
market price history for an item". The history goes back to 2022, with a reading every couple of hours (less often
for items that rarely trade). Each reading has the average and lowest price listed and, in recent readings, how many
listings there were. Downloaded 27 October 2026 by `Research/tools/fetch_price_history.py` for 5,480 items in each
mode (items, ammo, keys and gun builds); 2,340 of the 10,960 have no history at all.

## How a price is worked out

For each item, separately for PvP and PvE, over the last 365 days:
1. Only readings backed by **3 or more listings** count. A price from one or two listings is somebody's joke or
   bait (an NSPU-M eyecup listed at 10,000 that Prapor sells for 355).
2. Each day's readings become **that day's median**, so busy days (wipe launch, events) don't outweigh quiet ones.
3. The item's price is **the median of those days**. A one-day spike (a 4M VSS dust cover listing) can't move it.

An item needs 14 such days for a **solid** price. With fewer, all its readings are used, as long as there are 5 days
of them, and the price is marked **thin**. The table also has each item's 25% and 75% marks over the year (where it
sits on a cheap or a dear day) and its median over the last 30 days.

| | PvP | PvE |
|---|--:|--:|
| Solid price | 2,894 | 3,319 |
| Thin price | 587 | 580 |
| No history | 1,126 (mostly flea-banned on PvP) | 708 |

## What a year of history changes

**PvP and PvE agree.** One day's averages put one item in five more than 3x apart between the modes; a year's
medians put only 53 of the 2,868 items with a solid price in both more than 3x apart. The median item is 1.16x
dearer on PvE, and nine in ten are within 0.86x to 1.83x. Most of what looked like a PvP/PvE split was one day's thin
market.

**Where they still part, it's the PvE economy, not noise:**
- **Keys and keycards** are far dearer on PvE (a Labs Black keycard: 656k PvP, 4.86M PvE; a Dorm 314 marked key:
  4.39M PvP, 14.5M PvE). In PvE a key's room can be looted without getting killed for it, so the key's value is
  safer to count on there.
- **Repair kits, GPUs and LEDX** are 1.5 to 3x dearer on PvE (a body armour repair kit: 322k PvP, 925k PvE).
- **Items banned from the PvP flea** (421) have a PvE price only: a THICC item case, Physical Bitcoin, the REAP-IR.

**Today's price can be a wipe high or low.** A Dorm 314 marked key was 1.7M on PvP on 27 October; its year median
is 4.39M. A Labs Black keycard was 512k; its year median is 656k.

## How the index uses it

- **PvP's year median is the price**; PvE's only where PvP has none. PvE's economy runs hotter (no risk, more
  money in it), so its prices sit higher for no reason that applies to an SPT flea.
- **Keys and keycards get the geometric mean of PvP and PvE**: their PvE value reflects what a key is worth when
  using it can't get you killed, which suits SPT.
- **Guns** use their default build's year median, less its parts.
- **Thin prices** only count if they're not more than 3x what traders say the item is worth; bait is never cheap.

## Rebuilding

```
python -I Research/tools/fetch_price_history.py [download-folder]
python -I Research/tools/price_index.py [path-to-SPT_Data/database] [download-folder] [WTT-ContentBackport/db]
```

The download is about 570 MB, kept in the download folder (a temp folder by default) and not in the repo. Files
already downloaded are kept; delete the `history` folder to fetch everything again.
