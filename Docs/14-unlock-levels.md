# Item unlock levels

[Back to the guide](README.md)

Like the live flea, some items can only be bought and sold from a set player level: the M4A1 from level 25, a graphics
card from 40. Off by default; the **Live progression** flea style turns it on, or `levelLock.enabled` on the
[Bans tab](02-settings-reference.md#bans-tab).

![Item unlock level in the inspect window](../imageExamples/levelinspect.jpg)

*An item's inspect window says the level it unlocks at.*

![Locked categories and offers](../imageExamples/levellock.jpg)

*Flea categories show the level they open at, and offers you can't buy yet show locked. Shown on a low-level profile.*

## Where the levels come from

- **Base-game items:** the real game's levels (`minLevelForFlea` from tarkov.dev), shipped with the mod and updated
  whenever live prices are fetched.
- **Modded items** (`levelLock.moddedItems`, on): the level of the base-game items of the same kind that are worth
  about the same. On the real flea, levels follow price closely within a kind, so price stands in for how good an item
  is: a strong modded stim unlocks with the strongest real stims, a weak one with the weak ones. If a mod's own
  category has too few base-game items, the wider kind above it is used. An item with no price gets the level most items
  of its kind need.
- **Your own levels** (`levelLock.levels`, advanced): by item or by category id. `0` = no level. An item's own level
  beats its category's, and a handbook category beats an item type.

## Buying and selling

- An offer you can't buy yet shows as locked, named "Unlock level: X", for you only. In co-op, each player sees their
  own levels.
- The game won't buy a locked offer, and the server checks too.
- **Selling:** like live, you can't list an item you can't buy yet, unless `allowSellingBelowLevel` is on. Only the item
  you list counts, not what's in or on it.
- When building a gun from the flea, a locked part is left out instead of offered.

## Why not SPT's own

SPT has a built-in version (`tieredFlea` in `ragfair.json`), but it copies every offer on the flea for every search.
Better Flea looks only at the page of offers a player is shown, and copies only the locked ones, for that player alone.
That's cheaper, and safe in co-op.

## What the plugin shows

With the plugin, items show their unlock level when inspected, flea categories show a lock and the level they open at,
and locked offers show locked. Without it, the server still enforces the levels; players just don't see why.

## For other mods

`GET /betterflea/item-levels` gives every item's level, and `/betterflea/client-data` says whether levels are on and the
player's level. See [API](09-api.md#item-unlock-levels).
