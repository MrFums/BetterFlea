# The SPT quest tree

[Back to the guide](README.md)

Many trader items are locked behind quests. How hard that quest is to reach decides how many players can buy the item
from a trader, and so how much it's worth on the flea. This page explains how SPT stores quests and trader unlocks,
and how Better Flea scores every unlock. The full scored list is in
[Research: quest unlock difficulty](../Research/quest-unlock-difficulty.md).

## How SPT stores quests

All quests are in `SPT_Data/database/templates/quests.json`, one entry per quest id. Each has three lists of
conditions:

| List | What it means | Kinds of condition (count in SPT 4.1.6) |
|---|---|---|
| `AvailableForStart` | What you need before the trader offers the quest | `Quest` 736, `Level` 224, `TraderStanding` 8, `TraderLoyalty` 4 |
| `AvailableForFinish` | What you have to do | `CounterCreator` 609 (kills, survivals, visits), `HandoverItem` 413, `FindItem` 269, `LeaveItemAtLocation` 149, `PlaceBeacon` 96, `WeaponAssembly` 32, `Skill` 10, `TraderLoyalty` 10, and a few more |
| `Fail` | What fails the quest | Often another quest being done: a choice of route |

A `Quest` start condition names another quest and the state it must be in. `status: [4]` means "completed"; `[2]`
means "started". So quests form a tree: each quest points back at the ones before it, and **the chain** behind a quest is
every quest you must do first, all the way back.

`CounterCreator` conditions hold the details of kill and survival tasks: what to kill (PMCs, scavs, a boss, followers),
how (headshots only, a set weapon, a distance, a time of day, set gear), where (a map), and whether it must be in one
raid.

## How a quest unlocks a trader item

A quest's `rewards.Success` list can hold an `AssortmentUnlock`. SPT 4.1.6 has 231 of them, from 165 quests:

```json
{ "type": "AssortmentUnlock", "traderId": "54cb50c76803fa8b248b4571", "loyaltyLevel": 1, "target": "68a96951...", "items": [ ... ] }
```

`target` is the trader's offer (an id in that trader's `assort.json`), `loyaltyLevel` is the level it sits at, and
`items` is what it sells (a whole gun with its parts, here). Each trader also has a `questassort.json` that lists its
quest-locked offers under `started`, `success` and `fail`, by the quest that opens them. An offer appears once that quest
reaches that state and you have the loyalty level.

## Loyalty levels

Each trader's `base.json` has `loyaltyLevels`: for each level, the player level (`minLevel`), money spent with them
(`minSalesSum`) and standing (`minStanding`) it needs, plus what they pay you (`buy_price_coef`, see
[What traders pay](03-how-prices-work.md#what-traders-pay)). `assort.json`'s `loyal_level_items` puts each offer at a
level. Loyalty 4 needs a high level, a lot of money spent and good standing, so far fewer players have it.

## Hidden traders and event quests

- **Lightkeeper**'s quests sit at the end of long, hard chains, so everything in his chain scores as very hard.
- **The BTR Driver** has no quest that unlocks him. You reach him through Skier's A Helping Hand (level 20) and meet
  him in raid, and his quests fail some of Skier's and Lightkeeper's.
- **Event quests:** SPT's `configs/quest.json` has `showNonSeasonalEventQuests` off, so event quests outside their
  season are never given. Anything only they unlock can never be bought from a trader at all.

## How Better Flea scores an unlock

Better Flea scores each quest that unlocks a trader item from 0 to 1, from SPT's own data, so it works for modded
quests and traders too:

| Part | Weight | What counts |
|---|---|---|
| Level | 35% | The highest level lock anywhere in the chain. Level 45+ counts in full. |
| Chain | 45% | The work in this quest and every quest before it, on a log scale. 150 points counts in full. |
| This quest | 20% | Its own work. 12 points counts in full. |

**Work points:**
- Kills: PMC 0.25, scav 0.04, boss 3 (Goons 4, Zryachiy 5, followers 1.5, raiders and rogues 1).
- Times 2 for headshots only, 100 m or more, or all in one raid; 1.3 for a set weapon or gear; 1.2 for a time of day.
- Maps: Labs and Labyrinth 3; Lighthouse, Reserve and Streets 2; Shoreline, Interchange and night Factory 1.
- Hand-ins: roubles' worth / 100k if found in raid (/300k otherwise), at most 5.
- Skill 0.4 per level; loyalty requirement 1 per level over 1; Lightkeeper's own quests +2.
- Needing a trader at LL3 or LL4 to start the chain: 4 or 6.
- Fetching a quest item in raid +1; a quest that fails another (a choice of route) +1; a hidden trader +1.

**Floors:** a level lock is worth at least level / 75 by itself (level 45 = 0.6). Anything in the Lightkeeper chain is
at least 0.9, BTR Driver quests at least 0.6, seasonal event quests at least 0.8, and event quests SPT never gives 1.0.

**Tiers:** very hard 0.75 and up, hard 0.55 and up, medium 0.35 and up, easy below. In SPT 4.1.6: 19 very hard, 31 hard,
70 medium, 45 easy.

A few examples:

| Quest | Trader | Score | Tier | Unlocks |
|---|---|---|---|---|
| Trouble in the Big City | Lightkeeper | 0.91 | very hard | SIG MCX-SPEAR |
| Calibration | Mechanic | 0.84 | very hard | 5.45 7N40, Altyn helmet |
| Psycho Sniper | Mechanic | 0.72 | hard | AK-104 |
| Wet Job - Part 3 | Peacekeeper | 0.49 | medium | HK 416A5 |

## What the score is used for

1. **Gun pricing.** A gun or part a trader sells keeps a share of its flea price over the trader's price, by the higher
   of its loyalty level's share and its quest tier's share: medium 0.2, hard 0.5, very hard 0.9. An event-only unlock
   keeps all of it. [Guns and attachments](04-guns-and-attachments.md#how-a-gun-is-priced)
2. **The price index.** Every base-game item a trader sells is capped the same way: an easy unlock keeps nothing over
   the trader's price (a Mosin Tacfire grip, 17.1k on the flea, is 3.1k), loyalty 4 keeps half (an AS VAL dust cover,
   32.4k on the flea, is 17.3k). [Research: price index](../Research/price-index.md)

The scores are worked out by `Research/tools/quest_difficulty.py` from your SPT database:

```
python -I Research/tools/quest_difficulty.py [path-to-SPT_Data/database] [output.md] [download-folder]/quest-difficulty.tsv
```
