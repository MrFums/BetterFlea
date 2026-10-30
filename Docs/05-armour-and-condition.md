# Armour and condition

[Back to the guide](README.md)

How worn and used items are made and priced: armour, plates, helmets, then keys, meds, food, fuel and repair kits.
Settings are on the [Armour](02-settings-reference.md#armour-tab) and [Condition](02-settings-reference.md#condition-tab)
tabs.

## Durability in Tarkov

Armour shows two numbers, **current / max**: 14/40 means 14 points left now out of a maximum of 40. Repairing raises
the current to the max but also lowers the max a little, so a 14/40 plate repairs to something like 37/37. A plate whose
max has already dropped to 14 (8/14, say) can never be repaired past 14. So for armour, **the max is what the item is
really worth**; the current is only the repair you still have to pay for.

## How armour is priced

Better Flea prices a plate, helmet, armour or any armoured part by both numbers, against a new one's max (`full`):

```
share of a new one's price = (max / full) ^ 1.2  x  (0.55 + 0.45 x current / max)
```

So the max counts most, and a piece at 0 current still keeps a little over half of what its max is worth.

| Monoclete plate | Before 2.4.0 | Now |
|---|--:|--:|
| 40/40 | 31.9k | 31.9k |
| 33/33 | | 25.3k |
| 5/33 | 12.6k | 15.6k |
| 8/14 | 14.3k | 7.3k |

SPT's own rule took the square root of current over full, which priced a ruined 8/14 plate like an 8/35 one, and let a
5/33 plate be bought cheap, repaired, and sold for about 9k profit. The flea, your own listings and Fence use the new
rule; what traders pay doesn't change.

### Built-in inserts

Most helmets and body armour have no durability of their own: 62 of 111 helmets, 39 of 42 body armours and 26 of 66
rigs in vanilla SPT. Their durability is in their **built-in inserts** (soft armour panels, helmet tops and napes),
and the number the game shows is the inserts added up. SPT priced an offer by the outer shell only, so a helmet at
6/14 cost the same as a new one. Better Flea prices such an item by its inserts together, on the same curve as above.
Artem's SF High Cut at 6/14 went from 70k to about 10k.

### Wrecked gear

An item counts as **wrecked** when it's under half a new one's worth, all its parts together. The parts are averaged by
what each is worth, and one part far worse than the rest pulls the average down:

```
wear = average - 0.5 x max(0, average - worst part - 0.2)
```

So an 80% average with one part at 10% counts as 55%, and a good carrier with one bad plate isn't wrecked. Guns are
never "wrecked"; they have their own [wear rule](04-guns-and-attachments.md#how-a-gun-is-priced).

**Wrecked offers from other sellers** aren't listed at what they're worth, which would put them far under everything
else (a 4/13 Bastion at 11k next to a cheapest decent one at 39k). Like on the live flea, the seller goes a little under
the cheapest decent offer, 3 to 15%, so the wrecked one tops the list but is a poor deal. With no decent offer up, it
goes a bit under what decent ones usually cost.

**Your own wrecked gear is a hard sell.** See [Selling: wrecked gear](07-selling.md#wrecked-gear).

## How armour offers are made

- **Plates:** 60% both, 12% front only, 8% back only, 20% none. 20% of plated ones mix a class 3 and a class 4; 15% are
  both class 3. 30% of armour that takes side plates has them. 5% are the best kit: class 4 front and back, class 6
  sides, all mint.
- **Class 5 and 6 front and back plates are flea-banned** in this game version, so front and back plates on offers are
  class 3 or 4. Side plates can be class 5 or 6. Every plate is checked against the slot it goes in.
- **Which plates a carrier gets goes by how it ranks against other carriers**, not by its price, so it keeps working
  when prices change: light carriers lean to heavier plates, heavy carriers (Osprey, IOTV, Redut) to lighter ones, and
  dearer armour more often has the light class 4s (Monoclete, Elaphros, Titan). With two different plates, the better
  one is usually in the front.
- **Condition by rarity:** mint, worn (a bit under max), destroyed (15-35% left but a good max: worth repairing) or
  obliterated (max worn right down too). About 1 offer in 8 is badly damaged. Helmets are damaged half as often.
- **Helmets:** about 1 in 5 comes with something on it; a quarter of the helmets bosses wear come off a boss (good max,
  low current); face shields and night vision are sometimes listed flipped down.
- **Repair kit bonus:** a repaired piece now and then has the gold background.

![Plate carriers](../imageExamples/flea-armour-plates.gif)

*Plate carriers come with whatever plates the seller had, in any condition.*

## Keys

Live, mechanical keys are nearly all new and marked keys are nearly all used.

![Marked keys with uses left](../imageExamples/flea-marked-keys.gif)

*Marked keys with uses left. Fewer uses = cheaper.*

| | Used | Uses left | One use left costs |
|---|---|---|---|
| Mechanical keys | 12% (half that for common keys, twice for rare) | spread across the uses | 20% of a new one |
| Marked keys | 88% | mostly 1, then 2, 3, 4... down to a few at 8; 1 in 10 used ones only one use down | 35% |
| Keycards | 40% | spread | 20% |

A used key never asks more than a new one normally costs, so the dear end of a key's list is mostly new keys, with the odd used one from a chancer.

## Meds, food, drink, fuel and repair kits

| | Used | Used price | Amount left counts |
|---|---|---|---|
| Meds | 12% | 90% of full | 0.5 |
| Food and drink | 8% | 68% | 0.15 |
| Fuel | 50% (6% empty) | 85% | 0.2 |
| Repair kits | 55% | follows the amount | 1 |

"Amount left counts" is how much the price follows what's left: 0 means a used one costs the same whatever is left, 1
means the price follows the amount exactly (SPT's own way). Live, a used water costs about a third less than a full one
whether 18/60 or 55/60 is left, used AFAKs are only a little cheaper than full ones, and a part-full fuel can costs
nearly as much as a full one. That's what these numbers copy. Cheap meds (under 30,000) are listed used less often;
meds with only a few uses (CALOK-B, splints) nearly always just one use down. Of empty fuel cans, 30% are priced like a
full one, for whoever doesn't check.

## Guns

Worn guns have a lowered max like live: mostly 93 to 99, where a gun starts to jam and gets sold. Nearly half of those
are listed straight after a repair (97/97). A gun nobody has used is at its full max, and a gun with a build on it has
always been used. Boss-only guns (M60E6, PKP, Mk 43, AK-50) are mostly listed as found at full durability, because
people farm the boss. More in [Guns and attachments](04-guns-and-attachments.md).
