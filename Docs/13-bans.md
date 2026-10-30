# Bans in depth

[Back to the guide](README.md)

What decides whether an item can be bought or sold on the flea, and why changing it sometimes doesn't seem to work.
Settings are on the [Bans tab](02-settings-reference.md#bans-tab).

## Three places can stop an item

| Layer | Where it lives | What it decides |
|---|---|---|
| **1. The item's own flag** | `CanSellOnRagfair` in the item's data (`templates/items.json`, or a mod's files) | Whether the game lets you list it. The game client reads this from the server when it starts, and other client mods (Price Index, quick-sell mods) ask the game the same question |
| **2. SPT's flea blacklist** | `configs/ragfair.json`, `dynamic.blacklist`, plus everything Better Flea bans | Which items SPT makes generated offers for |
| **3. Better Flea's own rules** | Better Flea's settings | The trader item lock, unlock levels, selling turned off, found-in-raid only. The server enforces them; the plugin shows them |

The three don't always agree, which is where most ban confusion comes from.

## What we learned

**Turning off BSG's bans in SPT only covers the offers.** SPT's own switch (`enableBsgList` in `ragfair.json`), set
before the server starts, marks every item sellable at startup. Turned off later, as a mod does, it only stops SPT
keeping BSG's banned items off its own offers. Each item still says it can't be sold, so the game shows it as banned and
won't let you list one. Better Flea also clears the item's own flag, so turning off **BSG's banned items** really lifts
the bans.

**Some things should never be sellable.** SPT's own switch makes *everything* sellable, money and dogtags included, and
dogtags on the flea would make the dogtag barters easy to buy. Whatever you pick, Better Flea keeps these off:
- quest items;
- money, dogtags, secure containers and armbands;
- anything the real game can't trade anywhere, such as prestige and Arena rewards;
- SPT's dev, test and unreleased items (54 of them: the developer's balaclava, test items, shrapnel and the like).
  SPT keeps them out of the game with its item blacklist, but their own data said "sellable", so a player who had one
  could list it.

**One banned insert blocks the whole helmet.** A helmet or armour can't be listed if any of its built-in inserts is
banned, even when the helmet itself isn't. The inserts aren't in the handbook, so anything that only goes through the
handbook misses them (the Champion's helmet and its top plate). Better Flea lifts an item's built-in inserts along with
the item.

**The game reads item data once, at startup.** After changing bans on the server, restart the server and then the
game. Until then the game still shows the old bans.

**Banning the obvious way changes bot loot.** SPT has two blacklists. The item blacklist (`configs/item.json`) also
changes what bots carry. Better Flea only uses the flea's own blacklist, so bans don't touch raids.

## Mods' own bans

A mod can mark its items `CanSellOnRagfair: false`. Better Flea treats that as the mod's own ban:
- It stays, even with BSG's bans off.
- **Mod items** (Bans tab), that mod's card, **Allow**, lets them on the flea anyway: everything the mod bans, or only
  some kinds.
- **Ban** on the same card does the opposite: takes everything that mod adds (or some kinds) off the flea.

Items WTT ContentBackport adds are real game items, so they count as the game's: BSG's banned items covers them.

## Banning and allowing

- **By kind:** banned categories. Each item is in exactly one category.
- **One item:** banned items. Search by name on the page.
- **An exception:** allowed items always beat a banned category. "No guns but the Mosin" is the guns category banned
  plus the Mosin allowed.
- **By mod:** Mod items.
- **By level:** item unlock levels. See [Unlock levels](14-unlock-levels.md).

## What players see

With the plugin, a banned item is greyed out in the add-offer screen with the reason, and (if turned on in the F12
menu) shows a red icon. Without the plugin, the game shows SPT's error message instead. See
[The client plugin](16-client-plugin.md).
