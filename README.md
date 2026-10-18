# BetterFlea

**Makes the flea better. More live-like, more control, and a few handy extras.**

BetterFlea makes the Flea Market in SPT better. Yeah that's the best I could come up with :) :

- Turn off whole groups of items on the Flea Market, with simple true/false toggles.
- Offers look more real: a normal number of items in each offer, more sellers for common items, and some keys, meds, food, fuel and repair kits that are partly used.
- Prices change a little over time. An offer is never cheaper than what a trader pays for the item.
- Options for player selling: a wait before your item sells, a longer wait if you buy items to resell them, and FIR flea.

## What you need
- SPT 4.1.x. Fika compatible.
- BetterFlea has 2 parts. **The server needs part 1. Every real player needs part 2.**

| Part | Folder | Who installs it |
|---|---|---|
| 1. Server mod | `SPT_Runtime\user\mods\BetterFlea\` | The server |
| 2. BepInEx plugin | `BepInEx\plugins\BetterFlea\` | Every player |

**Fika headless client: do NOT install the BepInEx plugin on it.** It only needs the server mod on the server.

## How to install
1. Download `BetterFlea-1.0.0.zip`.
2. Open your SPT install folder.
3. Extract the zip into this folder. The 2 parts go to the correct folders by themselves.
4. Only the server host needs the server component. Obviously. Every REAL player needs the BepInEx plugin.

## How to uninstall
Delete these 2 folders:
- `SPT_Runtime\user\mods\BetterFlea\`
- `BepInEx\plugins\BetterFlea\`

BetterFlea does not change player profiles. You do not need to clean anything else.

## Settings
All settings are in `SPT_Runtime\user\mods\BetterFlea\config.jsonc`. Each setting has a note next to it.

- Open the file with a text editor, Notepad++ or VSCode if you're a normal human.
- After you change a setting, restart the server.
- If the server log says "Unknown setting", check the spelling of that setting. 
- If you are stupid and change random settings in the config and want it back to default, just delete it and restart the server. It will regenerate.

The file has 4 parts. They are explained below.

## 1. Flea: what players can buy
- `enableBsgBlacklist`: true = also use BSG's own list of banned items (for example M993 ammo or Knight's Mask - shameless plug go install my Knight Mask fix mod).
- `bannedCategories`: true = remove that whole group of items from the Flea Market.

| Group | Switches |
|---|---|
| `weapons` | `guns`, `throwables`, `melee` |
| `ammo` | `rounds`, `ammoBoxes` |
| `weaponParts` | `magazines`, `sights`, `suppressors`, `otherParts` |
| `gear` | `bodyArmor`, `armoredRigs`, `chestRigs`, `backpacks`, `armorPlates`, `headwear`, `helmetAttachments`, `faceCovers`, `eyewear`, `headphones`, `armbands`, `containers` |
| `keys` | `mechanicalKeys`, `markedKeys`, `keycards` |
| `medical` | `medkits`, `bandagesAndSplints`, `painkillers`, `stimulants` |
| `other` | `foodAndDrink`, `barterItems`, `info`, `maps`, `posters`, `specialEquipment` |

Each item is in only one group. For example, `mechanicalKeys` does not include marked keys, and `chestRigs` does not include armored rigs.

For single items, use item IDs or category IDs:
- `bannedItems`: these items are never on the Flea Market.
- `allowedItems`: these items are always on the Flea Market, even if a group is banned.

## 2. Generated offers: the offers the server makes
These are the offers from the server, not offers from players.

**How many offers (`listings`)**

Base SPT feels too empty to me on the Flea, this pumps it up. Set this around 2 or 3 to fix this if you don't like it too. 2 by default.

- Normal SPT has around 7 to 30 offers for each item.
- `multiplier`: 1 = normal, 2 = double, 0 = no offers (only traders). The highest is 10.
- `categoryMultipliers`: a different number for one group, for example `"keys.markedKeys": 3.0`.
- `rarityMultipliers`: more offers for common items, fewer for rare items.
- More offers use more server memory, it's not much but don't go stupid with it. I usually like it around the x2 or x3 mark and don't notice a change in resource usage.

**Items in one offer (`itemsPerOffer`)**

Makes offers more like the live flea - for example wires you will see a lot more of one type listed but something like Bulbex won't have 8 in one listing as they are much rarer, players usually don't have that many to flog.

| Profile | Which items | Items in one offer |
|---|---|---|
| `bulk` | Common small barter items: bulbs, wires, bolts, tape, cigarettes | 1 to 20. Usually small. 20 is rare. |
| `stock` | Rare small barter items. Common meds, food and weapon parts. | 1 to 6. Usually 1 or 2. |
| `few` | Very rare items, big items, and all gear: GPU, LEDX, rigs, helmets | 1 to 3. Usually 1. |

- `max`: the most items in one offer.
- `falloff`: from 0 to 1. Lower = more offers with only 1 item.
- `armoredRigsSingle`: true = armored rigs always have 1 per offer.
- Ammo remains untouched.

**Partly used items (`usedItems`)**

I personally hate seeing marked keys listed with full durability. I then also dislike having a 1/x use key being half the price of a 2/x. In live, players usually try and flog it for a bit more - it's annoying but more realistic :)

- Some offers have items that are partly used, for example a key with 3 uses left or fuel at 10/100.
- The price is lower when less is left.
- `usedChance`: the chance that an offer is partly used. 0.6 = 60%.
- Keys: `usesLeftFalloff` from 0 to 1 (lower = more keys with only 1 use left). `oneUsePricePercent` = the price of a key with 1 use left, in % of a new key.
- Meds, food, fuel and repair kits: `lowAmountBias` (1 = any amount left, higher = more offers that are almost empty).

## 3. Prices

I would personally recommend setting the markedKeyMultiplier to 1.5 as I feel marked keys a bit too cheap in base SPT. I've left this untouched for the default.

- `markedKeyMultiplier`: the Flea Market price of the 7 marked keys. 1 = normal, 1.5 = 50% more. Trader prices do not change.
- `floor`: an offer is never cheaper than the most a trader pays for the item, plus `marginPercent`. This stops players buying on the Flea Market and selling to a trader for profit.
- `logTraderProfitLoops`: true = at start, the server log shows items that one trader sells for less than another trader pays. BetterFlea only shows these. It does not change them.
- `drift`: prices go up and down a little over time, at most `maxDriftPercent`. Only new offers get the new price.

## 4. Player selling

I've tried to balance flipping, it's far too overpowered and it's very easy to just buy from Fence and resell. I have left purchaseLock on by default - this makes it so any item bought from a trader cannot be sold on the flea; disable this if you don't like it.
Well whats the difference between flipPenalty and purchaseLock? 

| | `flipPenalty` | `purchaseLock` |
|---|---|---|
| **What happens** | The item **can** be sold. The player waits **longer** for a buyer. | The item can **never** be sold on the Flea Market. |
| **Which purchases** | Traders, Fence and the Flea Market | Traders and Fence |
| **When it starts** | After the player buys **many** of one item (more than 2). Fence counts 3 times. | From the **first** item |
| **How long** | 60 minutes. A server restart forgets it. | **Forever**, while the player has the item. It is saved. |
| **Ammo and other stacks** | Yes | No |
| **Needs** | `saleDelay` on | Nothing |
| **The player sees** | Nothing. The item only sells slower. | The orange icon, a grey item and a warning |

**In short:** `purchaseLock` = "you cannot sell what you bought from a trader". `flipPenalty` = "buying lots of one item to resell is slow". Use both: `flipPenalty` also covers items bought on the Flea Market, and ammo.

- `enabled`: false = players cannot sell on the Flea Market.
- `foundInRaidOnly`: true = players can only sell items that are found in raid. Parts on a gun must also be found in raid.
- `saleDelay`: players wait between `minMinutes` and `maxMinutes` before the first buyer comes. `noDelayChance` is the chance of no wait.
- `flipPenalty`: if a player buys many of one item and then sells it on the Flea Market, they wait longer for a buyer.
  - `freePurchases`: a player can buy this many with no extra wait.
  - `maxExtraMinutes`: the longest extra wait.
  - `fenceMultiplier`: 1 item from Fence counts as this many purchases.
  - This only works when `saleDelay` is on. When the server restarts, it forgets old purchases.
- `purchaseLock` (on by default): true = items bought from a trader (also Fence) can never be sold on the Flea Market.
  - Players can still use these items, or sell them to a trader.
  - Parts on a bought gun are also locked.
  - Ammo, ammo boxes and other items that stack are not locked.
  - The server saves the locks in `data\purchase-locks.json`. It removes locks for items that a player does not have any more.

## What the BepInEx plugin does
The server makes all the rules. The BepInEx plugin shows them to the player:

- **Flea Market icon on items.** Red = banned on the Flea Market. Orange = bought from a trader (only when `purchaseLock` is on). Move the mouse over the icon to see why.
- **The same note in the item tooltip.**
- **These items are grey** when you add an offer on the Flea Market.
- **Quick-sell mods** sell these items to a trader. They do not try the Flea Market first.

A player without the plugin also cannot sell these items on the Flea Market. But that player sees a big error message, not the small warning.

The icon comes from the game files on each player's computer.

## Other mods
- **Mods that show prices or colour banned items** see BetterFlea's bans correctly.
- **Quicksell mods** should work with the BepInEx plugin. I have only tested with ShowMeTheMoney so please let me know of any issues for others.
- **All Quests Checkmarks** will still work. BetterFlea adds its own icon and does not change or mess with it's icon.
- **Other mods that change the Flea Market** (number of offers, prices, items in one offer) can do the same thing as BetterFlea. The mod that loads last usually wins. Turn off the same feature in one of the 2 mods.

## AI use
Claude / Gemini helped with BetterFlea development and mainly documentation / commenting. The original server aspects of the mod I had for myself on SPT 3.10 and 3.11 but did not release as I made changes directly to the database as I didn't have that included in the mod. 

The base of the server mod was written by hand and then passed over with an LLM to review and fix any of my syntax / logic issues as I am still learning C#.

The absolutely fantastic and not definitely slop mod icon was also generated by AI. Shocker I know.

## Debug
`debugLogging`: true = write a lot of extra lines in the server log. Use this only to test. Then set it back to false. Unless you are actively developing or wanting to contribute to my mod, just leave this disabled.
