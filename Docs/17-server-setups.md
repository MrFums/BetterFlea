# Ready-made setups

[Back to the guide](README.md)

Starting points for common kinds of server. Pick the flea style first, then change the settings listed. Each line is
`file: setting = value`; search the setting's name on the settings page to find it. Everything not listed stays at the
style's value.

## Live-like (the default)

Flea style **Balanced**. Nothing to change. Add item unlock levels and the Wipe Cycle with **Live progression**.

## Hardcore

The flea is there, but it's a struggle.

Flea style **Live progression**, then:

| Setting | Value | Why |
|---|---|---|
| `prices.jsonc: priceMultiplier` | 1.3 | Everything 30% dearer |
| `prices.jsonc: markedKeyMultiplier` | 1.5 | Marked keys worth farming |
| `listings.jsonc: listings.multiplier` | 1.5 | Fewer offers |
| `selling.jsonc: foundInRaidOnly` | true | Only loot can be sold. Bought items aren't found in raid, so no flipping at all |
| `selling.jsonc: saleDelay.expensivePercent` | 10 | Buyers less patient with high prices |
| `selling.jsonc: saleDelay.neverSellsPercent` | 60 | Nothing sells over 1.6x normal |

For a much rougher market, try the **Rough market** flea style instead: fewer offers, mostly worn, stripped or broken.

## Casual or solo

Selling is quick and the flea is generous.

Flea style **Balanced**, then:

| Setting | Value | Why |
|---|---|---|
| `prices.jsonc: priceMultiplier` | 0.85 | Everything 15% cheaper |
| `selling.jsonc: saleDelay.minMinutes`, `maxMinutes` | 1, 3 | Shorter waits at the normal price |
| `selling.jsonc: saleDelay.uniqueMinMinutes`, `uniqueMaxMinutes` | 2, 8 | Shorter waits for guns and gear |
| `selling.jsonc: saleDelay.expensivePercent` | 25 | More buyers stay interested in high prices |
| `selling.jsonc: undercut.enabled` | false | Nobody undercuts you |

Or the **Flea sale** style: everything cheaper and lots of deals.

## A co-op group (Fika)

Flea style **Balanced** or **Live progression**. Everything works in co-op: unlock levels and locked offers are worked
out per player, buyers and sales are shared. Worth setting:

| Setting | Value | Why |
|---|---|---|
| `selling.jsonc: purchaseLock.enabled` | true | Nobody sells on what a trader sold them |
| `market.jsonc: soldBeforeYouChance` | 0.1 | More "sold before you", if your group likes the race |

Remember: the server mod on the server, the plugin on every player's game, not on the headless client.

## No flipping at all

| Setting | Value | Why |
|---|---|---|
| `selling.jsonc: foundInRaidOnly` | true | The simplest: only found-in-raid items can be sold, and nothing bought is found in raid |

Or, to still let players sell what they bought, only never for a profit:

| Setting | Value | Why |
|---|---|---|
| `selling.jsonc: purchaseLock.enabled` | true | Trader items can't be listed |
| `selling.jsonc: purchaseLock.includeFleaPurchases` | true | Flea purchases neither |

The [48-hour rule](07-selling.md#what-stops-flipping) and the flip penalty are already on by default.

## Traders only

| Setting | Value | Why |
|---|---|---|
| `listings.jsonc: listings.multiplier` | 0 | No generated offers |
| `selling.jsonc: enabled` | false | Players can't sell |

Or ban everything under Bans, banned categories. To get rid of the flea entirely, a mod that disables it in SPT is the
cleaner way.

## A small or low-memory server

Flea style **Low memory**: far fewer offers kept around, filled when someone opens an item. If you need it lower again,
reduce `listings.jsonc: listings.multiplier` and `onDemand.maxExtraOffers`.

## Heavy modding

Many mods that add items:
- Leave **Estimate modded items** on (Economy tab) so their prices come into line with the base game.
- Check the Bans tab's **Mod items**: ban what's too strong, allow what a mod hides that you want on the flea.
- If a mod's attachments flood gun builds, lower `weapons.jsonc: moddedParts.percent` or switch the mod off there.
- Fix any odd price under **Your prices**.
- Turn on `prices.jsonc: floor.logTraderProfitLoops` once and read the server log, to see items one trader sells for
  less than another pays.
