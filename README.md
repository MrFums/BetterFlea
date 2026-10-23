# Better Flea

**Makes the flea better (duh). More live-like, much more balanced, and highly configurable.**

Better Flea makes the Flea Market in SPT better. Yeah that's the best I could come up with :) :

- Guns are listed the way players actually sell them: stripped, stock, found, budget, modded and meta builds, broken-down guns, and guns looted off scavs and bosses. Priced against what traders sell them for.
- Your own guns turn up: builds you save, and guns you die with, sold by whoever killed you.
- Body armour comes with realistic plates, and gear comes in every condition from mint to wrecked.
- Offers look real: a normal number of items in each offer, partly used keys, meds, food and fuel, and the odd scam to watch out for.
- A living market: offers last up to 24 hours, other players buy the good deals, sellers have flea ratings, and now and then something is "sold before you".
- Selling depends on your price: undercut and it sells fast, overprice and it sits there. Flipping is slowed down.
- Prices change a little over time. An offer is never cheaper than what a trader pays for the item.
- Turn off whole groups of items on the Flea Market, with simple true/false toggles.
- 3 times the base game's offers, with about the same server memory as SPT without the mod.

Everything can be turned off or tuned in the settings.

## What you need
- SPT 4.1.x. Fika compatible.
- Better Flea has 2 parts. **The server needs part 1. Every real player needs part 2.**

| Part | Folder | Who installs it |
|---|---|---|
| 1. Server mod | `SPT_Runtime\user\mods\BetterFlea\` | The server |
| 2. BepInEx plugin | `BepInEx\plugins\BetterFlea\` | Every player |

**Fika headless client: do NOT install the BepInEx plugin on it.** It only needs the server mod on the server.

## How to install
1. Download `BetterFlea-2.0.0.zip`.
2. Open your SPT install folder.
3. Extract the zip into this folder. The 2 parts go to the correct folders by themselves.
4. Only the server host needs the server component. Obviously. Every REAL player needs the BepInEx plugin.

## How to update
Close the game and the server, extract the new zip over the old one, and start the server. Your old settings are carried over.

## How to uninstall
Delete these 2 folders:
- `SPT_Runtime\user\mods\BetterFlea\`
- `BepInEx\plugins\BetterFlea\`

Better Flea does not change player profiles. You do not need to clean anything else.

## Settings
All settings are in `SPT_Runtime\user\mods\BetterFlea\configs\`, one file for each part of the mod. Each setting has a note next to it.

| File | What's in it |
|---|---|
| `flea.jsonc` | What can be on the Flea Market |
| `listings.jsonc` | How many offers there are and how many items are in one |
| `condition.jsonc` | Partly used items and gun wear |
| `weapons.jsonc` | How guns are built, and players' own guns turning up |
| `armor.jsonc` | Armour plates and condition |
| `market.jsonc` | How long offers last, other players buying, seller ratings, packs and barters |
| `prices.jsonc` | Price floor, price changes, gun prices, marked keys |
| `selling.jsonc` | Players selling their own items |
| `debug.jsonc` | Extra logging |

- Open the files with a text editor, Notepad++ or VSCode if you're a normal human.
- After you change a setting, restart the server.
- If the server log says "Unknown setting", check the spelling of that setting.
- If you are stupid and change random settings and want them back to default, just delete that file and restart the server. It will regenerate.

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

For single items, use item IDs or category IDs:
- `bannedItems`: these items are never on the Flea Market.
- `allowedItems`: these items are always on the Flea Market, even if a group is banned.

## 2. Offers

**How many offers**

Base SPT feels too empty to me on the Flea, this pumps it up. 3 times the offers by default.

- `multiplier`: 1 = normal, 3 = triple, 0 = no offers (only traders).
- `categoryMultipliers` and `minimumOffers`: more offers for a group, or a minimum per item. Guns get at least 60.
- Most offers are only made when someone looks at the item, so the server uses about as much memory as SPT without the mod (`onDemand`).

**Items in one offer**

Makes offers more like the live flea - for example wires you will see a lot more of one type listed but something like Bulbex won't have 8 in one listing as they are much rarer, players usually don't have that many to flog.

**Partly used items**

I personally hate seeing marked keys listed with full durability. I then also dislike having a 1/x use key being half the price of a 2/x. In live, players usually try and flog it for a bit more - it's annoying but more realistic :)

- Keys, meds, food, fuel and repair kits are sometimes partly used. Less left = cheaper.
- Used items are one per offer. Full ones sometimes come a few at a time.
- Some fuel cans are empty. Most are cheap, some are priced like a full one, so check before you buy.

## 3. Guns and armour

**Gun builds**

Guns aren't just SPT's factory builds. Each gun offer is one of these:

| Tier | What it is |
|---|---|
| `stripped` | The good stuff sold off: scope, suppressor, lights, nice stock. |
| `stock` | Factory gun, maybe a part or two changed. |
| `ground` | Picked up off the ground and sold as found. |
| `found` | Looks like it came out of a raid, some exactly as a scav or boss carries it. |
| `budget` | A full build from cheap parts. |
| `modded` | A proper build. Some are the gun's meta build. |
| `broken` | Main parts taken off. |
| `gutted` | Nearly everything sold off. |

- Cheap guns are mostly stripped or broken, dear guns mostly modded. Gun wear matches the build.
- Want an exact build to show up (a streamer's, say)? Save it in the game's Builds screen with a name starting with "Meta".
- Some guns and magazines have rounds in them. Watch out for the mag with one good round sat on top of junk.

**Players' own guns**

- Builds you save in the Builds screen can turn up on the flea.
- Die in a raid and your gun might turn up a while later, sold by whoever killed you.

**Armour**

- Armour and rigs come with both plates, one, or none, in every condition from mint to wrecked. Helmets and face shields too.
- Face shields, visors and night vision on helmets are sometimes listed flipped down (`flippedDownChance`).

## 4. Market

- Offers last up to 24 hours, like live. Good deals don't last long, overpriced ones sit there.
- Other players buy offers, and now and then something you try to buy was "sold before you".
- Seller ratings go up to 300. Someone selling a stack of one item is usually a flipper with a high rating.
- Rare barter offers that want items people actually have lying around.
- Packs: several items you have to buy together. Most are scams, so do the maths. Very rarely someone lists a silly amount.

## 5. Prices

I would personally recommend setting the markedKeyMultiplier to 1.5 as I feel marked keys a bit too cheap in base SPT. I've left this untouched for the default.

- `markedKeyMultiplier`: the Flea Market price of marked keys. 1 = normal, 1.5 = 50% more.
- `floor`: an offer is never cheaper than what a trader pays for the item. No buying on the flea to sell to a trader.
- `drift`: prices go up and down over time.
- `sellers`: some sellers ask too much, and now and then someone sells one cheap.
- `guns`: guns are priced against what traders sell them for, so a stock gun isn't 3 times the trader's price.

## 6. Player selling

I've tried to balance flipping, it's far too overpowered and it's very easy to just buy from Fence and resell. purchaseLock is off by default - turn it on and any item bought from a trader cannot be sold on the flea.

- `saleDelay`: how fast your offer sells depends on your price. Cheaper than normal sells in under a minute, the normal price takes a few minutes, overpriced barely sells. Good gun builds still sell now and then.
- `foundInRaidOnly`: true = only found in raid items can be sold. Off by default.

Well whats the difference between flipPenalty and purchaseLock?

| | `flipPenalty` | `purchaseLock` |
|---|---|---|
| **What happens** | The item **can** be sold. The player waits **longer** for a buyer. | The item can **never** be sold on the Flea Market. |
| **Which purchases** | Traders, Fence and the Flea Market | Traders and Fence |
| **When it starts** | After the player buys **many** of one item | From the **first** item |
| **Default** | On | Off |
| **The player sees** | Nothing. The item only sells slower. | A grey item and a warning |

## What the BepInEx plugin does
The server makes all the rules. The BepInEx plugin shows them to the player:

- **Flea Market icon on items (off by default).** Red = banned on the Flea Market. Orange = bought from a trader (only when `purchaseLock` is on). Hover the icon to see why. Turn them on in the **F12 menu** (BepInEx configuration manager) or in `BepInEx\config\com.fums.betterflea.client.cfg`.
- **These items are grey** when you add an offer on the Flea Market.
- **Quick-sell mods** sell these items to a trader. They do not try the Flea Market first.

A player without the plugin also cannot sell these items on the Flea Market. But that player sees a big error message, not the small warning.

## Modded items
Better Flea works from what an item is (its type, stats and price), not from lists of names, so it's built to handle items other mods add: guns, attachments, armour, meds and traders. I can't test every mod though, so if something modded looks wrong on the flea, let me know.

## Other mods
- **Mods that show prices or colour banned items** see Better Flea's bans correctly.
- **Quicksell mods** should work with the BepInEx plugin. I have only tested with ShowMeTheMoney so please let me know of any issues for others.
- **All Quests Checkmarks** will still work. Better Flea adds its own icon and does not change or mess with it's icon.
- **Other mods that change the Flea Market** (number of offers, prices, items in one offer) can do the same thing as Better Flea. The mod that loads last usually wins. Turn off the same feature in one of the 2 mods.

## Reporting issues
Please report issues in my Discord channel on the SPT server, in this mod's comments, or on [GitHub issues](https://github.com/MrFums/BetterFlea/issues).

## Roadmap
No dates, just ideas I'd like to get to:
- **Prices that react to buying.** Buy up the cheapest offers of an item and its price goes up for a while.
- **Wipe progression.** Prices swing early in a wipe and settle down later.
- **Real competition.** Other sellers undercutting your listings.
- **Web settings page.** Change the settings in your browser instead of editing the files.

## AI use
Claude / Gemini helped with Better Flea development. The original server aspects of the mod I had for myself on SPT 3.10 and 3.11 but did not release as I made changes directly to the database as I didn't have that included in the mod. 

The base of the server mod was written by hand and then passed over with an LLM to review and fix any of my syntax / logic issues as I am still learning C#.

Claude also helped write the documentation, since the mod does a lot of individual things and keeping track of them all by hand was a nightmare.

The absolutely fantastic and not definitely slop mod icon was also generated by AI. Shocker I know.

## Debug
`debugLogging`: true = write a lot of extra lines in the server log. Use this only to test. Then set it back to false. Unless you are actively developing or wanting to contribute to my mod, just leave this disabled.
