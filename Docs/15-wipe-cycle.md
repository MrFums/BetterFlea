# The Wipe Cycle

[Back to the guide](README.md)

With the Wipe Cycle on, prices follow how far into the wipe the players online are, the way the live flea moves
through a wipe. Off by default; the **Live progression** flea style turns it on, or `wipeCycle.enabled`
on the [Economy tab](02-settings-reference.md#economy-tab).

## The idea

On the live flea, prices move through a wipe in a pattern:
- At the very start everything is scarce and dear. Prices settle by about level 25, then climb again late in the wipe.
- What people need for the hideout and quests is dear just before most people get there, and cheap once they're past it.
- The best guns, ammo, armour and stims are dear late on, and early-wipe gear gets cheap.

The Wipe Cycle copies that, from how real flea prices moved through whole wipes.

## Where the numbers come from

tarkov.dev's PvP flea price history, back to March 2022, for 3,400 items. Four wipes have data from their first
weeks: June 2022, December 2022, December 2023 and December 2024 (the last only from week 3). For each item, its
price each week is measured against its own price mid-wipe (weeks 4 to 16), and the median over the wipes is kept.
Weeks become levels with the game's XP table: 25 raids a week, 45% survived, up to 12 quests a week.

The whole market, against mid-wipe:

| Level | 4 | 10 | 19 | 25 | 29 | 33 | 41 | 46 | 51 | 55 | 60 | 64 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Prices | 1.6x | 1.1x | 1.05x | 1.05x | 1.0x | 1.0x | 1.0x | 1.0x | 1.1x | 1.15x | 1.3x | 1.3x |

Guns, ammo, armour, meds, keys, barter items and gear each have their own curve, and 2,500 items have one of their own.
One item's history is thin early in a wipe (the flea is locked to most players then), so its curve is pulled halfway
to its group's and kept within 0.67x to 1.5x of it. A week only counts where at least two wipes have it. The full
table is in [Research: wipe cycle](../Research/wipe-cycle.md), and `Research/tools/wipe_curves.py` rebuilds it.

## Which level the flea follows

- **The highest level among the players online.** Anyone making requests counts, so a game that kept running through
  a server restart is still seen. Nothing is saved, so a test profile that isn't playing doesn't count.
- **Prestige:** a prestiged player counts as late wipe (`prestige: "add"`) or as a fresh wipe (`"reset"`).
- **It moves in steps** of 3 levels (`levelStep`), on a wipe that "ends" at about level 60 (`endLevel`).
- **After it moves, it stays put for 30 minutes** (`cooldownMinutes`), so swapping to a low or high profile can't be
  used to buy low and sell high.
- **Testing:** `wipeCycle.level` pretends the players are at that level. 0 = follow the real players.

## What decides an item's price

Each new offer gets a multiplier for where the wipe is now:

1. **Real price history** is the base: the item's own move through a wipe, or, for items without one (modded items),
   its group's.
2. **The hideout and quests:** an item needed for a hideout stage or a quest is dearer just before the level most
   people build or start it, and cheap once they're past it. Hideout stages are placed at the level people usually
   build them (their trader levels and the areas they need first); quest items at the level their quest opens.
3. **Guns:** the good ones are wanted late. Assault rifles always, marksman rifles a lot, SMGs all wipe; shotguns,
   snipers and pistols drop off once people have better.
4. **Ammo by penetration:** good rounds are scarce at the start and wanted late, weak rounds drop off. Flesh rounds are
   used all wipe, most of all late. Ammo goes by the whole market at half strength, plus these rules.
5. **Armour by class:** high classes late, low classes early. Armour goes by its own class or the best plate it comes
   with.
6. **Expensive stims and GPUs:** nobody needs them early, everyone late.
7. **Before a trader sells it for money**, the flea is the only place to get it, so it climbs towards that level; after,
   the trader's price caps it and it eases down over a few levels. Not guns (good ones stay wanted), and wanted ammo keeps
   a premium because traders only sell so much per restock. Only vanilla traders count; mod traders say nothing about
   the wipe.

Where an item's own history already shows a rule, the rule goes on at half weight. `strength` scales every swing (0.5 =
half, 2 = double).

## Seeing it

The Economy tab shows where the wipe is, the whole market's multiplier right now, and the items moving most against
the market, with a preview at any level. **Item lookup** (Lookup tab) draws any item's price through levels 1 to 70.

## Things to know

- It changes only new offers' prices, like drift.
- It's not meant to run with live prices.
- Each item's factor stays between 0.4x and 2.5x. In practice, items range from about 0.7x to 2.4x.
- If something moves oddly, report it with the item and the level.
