# Modded items, and advice for mod authors

[Back to the guide](README.md)

Better Flea works from what an item *is* (its type, stats and price), not from lists of names, so items other mods add
get the same treatment as base-game ones. This page explains how, and then how to price your own mod's items so they
work well with plain SPT, with Better Flea, and with other mods.

## How Better Flea handles modded items

**Which items are modded.** Better Flea takes a snapshot of SPT's own items before mods add theirs. Anything not in
it is modded. To show which mod an item came from (Bans tab, Weapons tab), it reads each mod's files for item ids. An
item belongs to the mod that *defines* it, not one that only mentions it (JSOC listing Eco's parts in its slots). The
scan only runs when a setting needs it (a Mod items rule, or a mod switched off under Modded attachments), and it has
limits so a huge mod folder can't stall the server: 20,000 files and 512 MB, 10 seconds per mod and 30 in total.

**Prices.** A modded item has no real flea price, only what its mod gives it, which is often a guess.
- **Estimate modded items** (Economy tab, on): the item is estimated twice, once from its flea price and once from its
  handbook price, each scaled the way base-game items of its kind are, and the one nearer what that kind usually costs
  is kept. It's capped at what 9 in 10 base-game items of its kind cost. Tested with Eco's Attachment Emporium, WTT
  Armory and WTT Artem: half their items keep their price, and the odd ones come into line (an M249 from 564k to 362k,
  a pistol grip from 54k to 5.6k).
- **A trader's price beats a guess.** A modded item a trader sells (not Fence) is worth at least 95% of the cheapest
  trader offer of it, and at most what base-game items of its kind sell for next to their trader price (1 to 2x). Artem's
  SF High Cut is about 68k next to Artem's 65k, not 164k.
- **Your prices** (Economy tab) fixes any one item by hand.

**Real items added by mods.** WTT ContentBackport adds base-game items SPT doesn't have yet. Those aren't "modded":
Better Flea's price index covers the 675 of them that can be traded.

**Bans.** An item whose mod marks it `CanSellOnRagfair: false` is that mod's own ban. Better Flea leaves it banned, even
with BSG's bans turned off, unless the server allows that mod's items (Bans tab, Mod items, Allow).

**Unlock levels.** A modded item unlocks with the base-game items of its kind that are worth about the same.

**Gun builds.** Modded parts are picked as often as base-game parts that fit as well, and missing parts (from a mod
that isn't installed) are left off. A modded "optic" the handbook files under Mounts or Iron sights is treated as a
mount.

## Advice for mod authors

These help your items behave well everywhere, not just with Better Flea.

### Price against the base game, not the real world

Look up a base-game item of the same kind and similar strength, and price yours near it. Better Flea's settings page
**Item lookup** (Lookup tab) shows any item's handbook-derived price, Better Flea's price and the real flea price side
by side. An item ten times dearer or cheaper than everything like it is what breaks economies.

### The handbook price matters more than the flea price

- **Traders pay a cut of the handbook price.** Ragman pays 62% of it in SPT 4.1 (50% live). A high handbook price makes
  your item a money printer at the traders.
- **Plain SPT 4.1 rebuilds flea prices from the handbook at startup** (handbook x 1.5 by default,
  `ragfair.json` `generateBaseFleaPrices.useHandbookPrice`). If your items are in the database by then, your handbook
  price becomes your flea price, whatever you put in `prices.json`.
- **The flea fee uses the item's worth**, so a silly price makes a silly fee.

Set both `handbookPriceRoubles` and `fleaPriceRoubles` (or their equivalents in your tooling), and keep the flea price
within a few times the handbook price. SPT itself cuts weapon parts priced over 6x their handbook price and electronics
over 11x.

### Don't let one trader undercut another

If your trader sells an item for less than any trader pays for it, players buy from yours and sell to the other,
forever. Check:

```
your trader's price  >=  handbook price x the best payout share among traders that buy that kind of item
```

With SPT 4.1's cuts that best share is up to 63% (Therapist, for meds) or 62% (Ragman, for gear). Leave some margin.
Better Flea's server log can list every item where this happens: Prices tab, Floor, **log trader profit loops**.

**Built-in inserts count.** A helmet's or armour's trader payout includes its built-in inserts. In the base game
those inserts aren't in the handbook, so they add nothing. Give your inserts handbook prices and the helmet's payout
jumps: Artem's SF High Cut is 59,650 for the shell plus two 48,141 inserts, so Ragman pays more for it than Artem sells
it for.

### Use the right categories

SPT, Better Flea and other mods classify items by their parent class (`_parent`) and handbook category. Get these
right and your item gets the right offer counts, condition, plates, build slot and level lock. A red dot filed as a
sight works as a sight; one filed as a mount is treated as a mount. Cloning an existing item (`itemTplToClone`) is the
safest start, but override what the clone shouldn't inherit, prices first.

### Set the flags deliberately

| Field | Why it matters |
|---|---|
| `CanSellOnRagfair` | The game client reads it at startup to decide whether an item can be listed. Better Flea treats `false` on a modded item as your ban and keeps it, unless the server owner allows it. |
| `RarityPvE` (`Common`, `Rare`, `Superrare`) | Offer counts, how often it's worn or used, how busy its market is. Missing means common. |
| `MaxDurability`, `Durability` | Condition and price of armour and guns. For armour with built-in inserts, the inserts carry the durability. |
| `QuestItem` | Quest items are never put on the flea. |

### Guns

- Give each gun a default preset (`Encyclopedia` set to the gun), so the flea can list it as a factory gun.
- Make sure every required slot has at least one part that can be on the flea, or the gun can't be listed. Better
  Flea works around a slot where every part is banned, but plain SPT doesn't.
- If your gun's slots list parts from another mod, make that a soft dependency: when the other mod isn't installed,
  SPT logs errors and Better Flea leaves those parts off.

### Ids and ownership

Use your own unique ids, and define each item in your own files. Don't reuse or patch base-game ids unless you mean to.
Better Flea (and anything else that asks "which mod is this from") goes by who defines the item.

### Check it

Install Better Flea on a test server and look at the server log at startup. It reports modded items it had to move a
long way ("Modded items: N lowered to what base-game offers of their kind go for next to a trader's price; most: ..."),
and the Bans tab shows your mod's items by type. If your items show up there, your prices are probably worth a second
look.
