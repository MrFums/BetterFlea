# Guns and attachments

[Back to the guide](README.md)

Guns are where SPT's flea goes most wrong, and where Better Flea does the most work. This page covers how a gun offer
is built, and how it's priced part by part. Settings are on the [Weapons](02-settings-reference.md#weapons-tab) and
[Prices](02-settings-reference.md#prices-tab) tabs.

## The problem with SPT's gun prices

SPT prices a gun as the sum of its parts' flea prices. But on the real flea, a whole gun sells for far less than its
parts do one by one:

| Gun | Default build on the flea | Its parts, loose |
|---|--:|--:|
| FN SCAR-L | 59k | 205k |
| HK 416A5 | 129k | 201k |
| SVDS | 112k | 319k |
| Glock 18C | 72k | 300k |

Loose parts are thinly traded and listed high; whole guns sell cheap, because most players can buy the same gun from a
trader. So "sum of the parts" makes every gun far too dear, and it makes buying a gun and stripping it a money printer.

## How a gun is priced

With `guns.enabled` on (Prices tab), a gun offer's value is worked out like this:

**1. The gun itself** (the receiver, the root item) goes by trader prices. For each trader that sells it:

```
trader value = the trader's price x 95%                      (guns.traderPricePercent)
if trader value is at least the flea price:  worth = flea price
otherwise:                                  worth = trader value + (flea price - trader value) x share
```

The easiest trader offer counts (the lowest worth). The **share** is how much of the flea price over the trader price
the gun keeps, because few players can buy it there:

| Unlock | Share kept | Setting |
|---|---|---|
| Loyalty 1 or 2 | 0 | `guns.fleaShareByLoyalty` |
| Loyalty 3 | 0.2 | |
| Loyalty 4 | 0.5 | |
| Easy quest | 0 | `guns.fleaShareByDifficulty` |
| Medium quest | 0.2 | |
| Hard quest | 0.5 | |
| Very hard quest | 0.9 | |
| Only an event quest SPT never gives | all of it | |

The higher of the loyalty share and the quest share counts. How hard each quest is comes from scoring SPT's whole
quest tree: see [The SPT quest tree](06-quest-tree.md). A gun no trader sells (Fence doesn't count) keeps its flea price.

**2. Every attachment** is worth its own base price, what it sells for on its own (`guns.partsAtFleaPrice`, on). So a
gun with a Vudu on it costs about a Vudu more, and taking a gun apart makes no money. Turn it off and attachments go by
trader prices too, the same way as the gun.

**3. Wear.** A worn gun is worth less as a whole, not just its receiver:

```
condition = (durability / max durability) ^ (log(p) / log(0.5))      p = guns.priceAtHalfDurabilityPercent / 100
```

At the default p = 0.5 that's a straight line: a gun at 70% durability is worth 70%. SPT's own takes the square root
of the durability off the receiver only (a gun at half durability kept about 71%), so a worn gun with costly parts
was barely cheaper.

**4. Drift.** Guns drift with the market at 40% of the gun's own item drift, steadier than loose items.

**5. Where sellers list it.** Other sellers list a gun a little under its value as a rule: 12% under to its value
(`offerMinPercent`, `offerMaxPercent`), so a buyer gets a little off and relisting at the value earns about what the fee
takes. Near-stock guns (the factory gun) sit 3 to 15% under. Never under the floor.

**6. Deals.** Now and then a build is listed as a deal (20-35% under, 1 in 20 builds) or a steal (40-60% under, 1 in
100). Never under what traders pay for the parts plus 10%. Deals are bought by someone else within minutes.

The same value is what a player's own gun listing is measured against, so the sale speed is fair.

## How a gun offer is built

![Gun builds](../imageExamples/flea-gun-builds.gif)

Each gun offer is one of eight tiers, picked by weight. Cheap guns lean to the first tiers, dear guns to modded builds:
nobody leaves a dear gun stock or strips it bare.

| Tier | What it is |
|---|---|
| Ground loot | Picked up and sold as found: exactly the factory gun, 97-100/100 |
| Stock | The factory gun, maybe a part or two changed |
| Found | Like it came out of a raid; some exactly as a scav, boss or guard carries it |
| Budget | A complete build from the cheap end of each slot, with a cheap optic |
| Modded | A proper build; 30% are the gun's meta build, copied part for part |
| Stripped | Built, then the good stuff sold off: scope, suppressor, lights, nice stock |
| Broken | Main parts taken off |
| Gutted | Nearly everything sold off: just the upper, just the lower, or the bare gun |

**How the build is put together:**
1. **Barrel first.** The barrel decides the build: a 10.3" M4 is a close-quarters gun, a 20" one a marksman rifle.
2. **The factory parts stay**, as the factory part or an upgrade: muzzle device, stock, grip, sights.
3. **The rest in SPT's own bot order**, so parts that block other slots go on early.
4. **The optic before its mounts:** the optic is picked, then the mounts it needs to sit in the best spot.
5. **Optics suit the gun:** pistols get red dots, SMGs and shotguns up to 4x, rifles up to 8x; 1-6x LPVOs on 7.62s
   and marksman rifles, the Specter DR on SMGs; budget sights mostly on lower tiers.
6. **Parts are scored** from the game's own item data: ergonomics and recoil for the build's style (ergo, hybrid or
   recoil), parts that are easy to get (trader parts over found-only ones), and colour (tan, black, olive).
7. **Stripping**, for tiers that strip: scope, suppressor, laser or light, foregrip, then a good stock and grip swapped
   for cheap ones, in that order.
8. **Wear** to match the tier: broken and stripped guns had a hard life, modded and meta builds were looked after.

Parts from mods you don't have, and flea-banned parts, come off with everything on them. If every part that fits a
required slot is banned (Eco's AK-105 Kochevnik only takes flea-banned grips), the slot still gets one, so the gun
works.

**Your own builds:**
- Save a build in the game's Builds screen with a name starting **"Meta"** and that exact build is used as the gun's
  meta build. Good for streamer builds.
- Any build players save can turn up now and then (`savedBuildChance`, an easter egg).
- A gun you die with can turn up 5 to 30 minutes after the raid, sold under your killer's name, maybe with its scope
  or suppressor taken off first.

## Modded guns and parts

Modded attachments are picked as often as base-game ones that fit as well (`moddedParts.percent`, 100). Modded scopes
get the middle rating of base-game optics of their kind. A mod's parts can be kept off builds (`disabledMods`), and a
mod that marks parts its guns need as flea-banned can be worked around with `allowFleaBannedParts`. A modded "optic"
the handbook files under Mounts or Iron sights is treated as a mount, so modded rifles get scopes as often as base-game
ones. More in [Modded items](08-modded-items.md).
