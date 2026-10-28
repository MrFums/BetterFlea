# Changelog

## 2.3.0

**New**
- Item lookup shows **Actual value**: what a new one sells for on the flea with every setting applied (the price multiplier, marked key multiplier, drift, Wipe Cycle and price floor). The base price above it is before any of those. SPT's own price is only shown when live prices change it.
- Item lookup has a **Refresh** button to get the item's prices again.
- **Better Flea pricing** (Economy tab, on by default): every base-game item starts from Better Flea's own fixed price instead of SPT's. It's built from a year of real PvP and PvE flea price history and trader prices, with junk listings and joke prices worked out, and it doesn't move: levels, multipliers, demand, drift and the Wipe Cycle still go on top. Think of it as a more reliable handbook. Modded items are estimated from similar base-game items (**Estimate modded items**), and the newer items WTT ContentBackport adds get Better Flea's price too (**WTT ContentBackport items**, on by default), and traders pay you what they do in the live game (**Live trader prices**, on by default: Therapist pays 51% of an item's base price instead of SPT's 63%, Fence 24% instead of 40%; a trader whose prices another mod like SVM has changed keeps that mod's prices). Live prices aren't used while it's on. How it's built is in the repo's Research folder.
- The settings page's Home tab recommends Price Index (item prices in game) and Flogger (quick sell), client mods that read Better Flea's own prices.
- Other mods can ask the server for an item's actual flea value (what Better Flea would sell it for). Price Index uses this to show Better Flea's prices. The README's "For mod developers" section explains how.
- **Undercutting sellers:** list something as the cheapest offer up and now and then another seller lists just under it a few minutes later (a rouble under, or down to a round number) and takes most of your buyers. The dearer your offer, the likelier: about 1 in 3 at the normal price, most of the time when overpriced. List well under the normal price and it sells before anyone bothers. Settings on the Selling tab (Undercutting sellers).
- **Supply and demand:** buy up an item and its next offers get dearer for a while (2% per offer bought, up to 30%, half of it gone after an hour). Sell a lot of one item and now and then its price drops a little (only what actually sells counts, so listing and pulling offers can't crash a price). A rise from buying never raises what your own listing is judged against, so buying an item up and selling it at the raised price doesn't pay. Settings on the Prices tab (Supply and demand).

**Changes**
- Gun prices: a gun's attachments are now worth what they sell for on their own (their flea price), so buying a gun and selling its parts one by one no longer makes money, and a gun with a Vudu on it costs about a Vudu more. Only the gun itself still goes by trader prices. Guns that are hard to unlock at a trader keep some of their flea price, since few players can buy them there. Each unlock is scored by how hard it really is: the quest it's locked behind (its level lock, every quest before it, PMC and boss kills, hard maps, found-in-raid hand-ins, skills) or a hidden trader like the BTR Driver or Lightkeeper. Very hard unlocks keep 90% of the gap between trader and flea price, hard ones half, medium ones a fifth, easy ones none. Loyalty 4 guns keep at least half and loyalty 3 guns a fifth. Guns locked behind event quests SPT never gives keep their full flea price, and a gun sold more easily somewhere else is priced by that. Modded quests and traders are scored the same way. All of it can be changed on the Prices tab (Gun prices); turn off **Parts at their flea price** to value attachments by trader prices too. Thanks to the Forge user who suggested it.
- Offers are priced to the rouble, not in whole-percent steps, so a list no longer has a dozen offers at exactly the same price, and now and then a seller lists a rouble under another the way people undercut. About 1 offer in 5 is an easy number to type instead, like 80,000, 79,999, 81,111 or 8,250, never more than 4% off its price (**Easy round prices** chance on the Prices tab, Sellers).
- Good deals are rare now, so you can't open the flea, buy something and sell it on for a profit whenever you like. SPT puts every offer anywhere from 20% under to 20% over normal, which left a 20% deal at the top of nearly every item's list; ordinary offers now go from 4% under to 15% over (**Price spread** on the Prices tab). Quick sells went from 3% of offers at 15-35% off to 0.4% at 10-25% off, gun build deals from 1 in 10 at 15-30% off to 1 in 20 at 20-35% off, plus a rarer **steal** (1 in 100 builds at 40-60% off, never below what traders pay for the parts plus 10%), and fair packs are 3-10% under the single offers instead of 8-20%. The deals that are left don't sit there: one 10% or more under normal is bought by someone else within 1-6 minutes of going up, so you have to be quick. New: now and then (about 1 in 150) someone lists a scoped gun build as if the scope weren't on it.
- Selling: buyers go down the list, like a real market. For the item you have open and every item you've listed, buyers keep turning up and buy from the top of the list: mostly the cheapest offer, sometimes one of the next few (nobody picks a pack or a barter first), if the price is one people pay. Refresh and the top offers shrink and go; yours sells once it's one of the cheapest. How busy an item is goes by what it is: its price, how rare it is and how many hideout builds, crafts and quests want it. Wires trade every few seconds, a LEDX every few minutes. Buyers mostly take one to three, so a stack counts down. Your offer at the top of the list at a fair price (at most 5% over normal) always sells within two minutes, whatever the item; much quicker for busy items. Items with uses (keys, meds, fuel) are compared with offers in the same condition, and wait a bit longer unless they're well under those. A bought offer is replaced by a new seller a few minutes later, not the moment you refresh. Guns and anything with durability (armour, helmets) are judged on their own, and so are packs: at the normal price they take 5-20 minutes to sell (longer for dear ones; a 500,000 gun about 10-45), never straight away; well under normal they go within a minute or two. Rare items wait a few minutes for their first buyer too (very rare ones 3-8), unless they're 15% or more under normal. The average price the game shows when you list is now Better Flea's normal price (it was the average of the offers up, about 5% over normal), so listing at the average is a fair price. Settings on the Selling tab (Buyers).
- Selling: buyers take what people actually buy at once, and come less often after each sale. A pile of one item sells a bit at a time: list 10 bundles of wires and maybe 2 go in the first while. Ammo goes in round lots (30, 60, 120, 200 or the whole stack), and a big stack usually sells most of the way.
- Selling: an offer now needs to be priced at the market to sell. Up to the normal price it sells as before; over it, fewer buyers bite, and past 15% over nobody does (was 30%, and anything up to 5% over sold like a normal one). If another seller already has the same item cheaper, only about a third of the buyers are left for yours, unless yours is well under the normal price.
- Selling: an offer with a low chance to sell (priced over normal) that does sell now finds its buyer any time up to 4 hours after listing, later on average the lower the chance. Before, it sold within the first hour or two or not at all. Setting **Slow sales: up to** on the Selling tab (Sale speed).
- Every gun gets 5-15 near-stock offers on top of the rest: the factory gun as it comes, now and then with a cheap red dot or holo added or no magazine, nothing else. Listed 3-15% under their value. Most are as new (someone selling on a drop, reward or raid find): 60% at full durability, the rest a few points under, 15 at most, never a lowered max.
- Players' saved builds turn up as other sellers' offers far less often (0.3% of a gun's offers, was 5%), and the same copied build (saved, meta or looted) isn't listed again for half an hour. Four of your own build on one list gave it away.
- Other sellers' flea ratings are higher: most are in double digits and about one in ten is in triple digits. Before, nearly half were under 1. Only the odd seller new to the flea is under 1 now.
- Settings still at an old default (quick sells, build deals, how fast deals sell, the most over normal an offer sells at) move to the new default once. Ones you changed are left alone.
- Gun prices: a worn gun is now worth less as a whole. Before, SPT's condition cut only applied to the receiver, so a worn gun with costly parts was barely cheaper. Now a gun at 70% durability is worth 70% of a new one, and at half durability half. Advanced setting **Value at half durability** on the Prices tab (Gun prices) changes how steep this is; 100 turns it off.

**Fixes**
- Items worth less than their handbook price were listed at about their handbook price (an LEDX at 970k when it's worth about 660k). SPT counted the BTR Driver, Lightkeeper and the Storyteller, who take no cut, as paying the full handbook price for everything, even what they don't buy. Only traders that buy an item count now.
- Live prices: an item whose live price was far from SPT's own kept SPT's price, even when SPT's was the wrong one. An FN SCAR-H X-17 stayed at 275k (it sells for 17k), so X-17 builds listed at 400k, and on PvE the M4A1 and AKM kept 133k. A live price far from SPT's is now kept if the handbook agrees with it.
- Item lookup priced a gun as the bare gun item (an M4A1 at 17k), not as a gun anyone would buy. It now shows the default build's value, and says when a live price was ignored instead of "the real flea doesn't sell it".
- "Normal" prices now match what SPT actually prices an offer at. SPT caps a weapon mod or electronic whose flea price is silly next to its handbook price (common with modded items), and never prices under what a trader pays; Better Flea's normal price didn't, so a modded muzzle with a 1.4M flea price and 42k offers counted as a 97% off deal. Other players were buying hundreds of these "deals" every few minutes, and a player could list one for far over its worth and have it sell as "normal".
- Gun offers are now always priced from what the gun is worth (its parts and condition): other sellers list them from 12% under to their value, a bit under as a rule (a bot's build isn't really worth full price), so relisting a bought gun at its value earns about what the listing fee takes (deals still turn up now and then). Guns drift with the market too, at 40% of a loose item's swing. Settings on the Prices tab (Gun prices). SPT's own price for a build could come out a third over its worth (most ASh-12 offers sat at 1.4-1.6 times their value) or next to nothing, and Better Flea only corrected it when it was too high in some cases.
- Ordinary gun offers listed 10-15% under their value counted as deals and were bought by someone else within minutes. Only a gun cheaper than the lowest normal gun offer (16% under by default) counts as a deal now.
- Gun offers in dollars or euros showed the rouble price as the dollar or euro amount (a 227k gun listed at 226,994 $).
- Startup no longer reads every server mod's files unless a mod ban is set (Mod items on the Bans tab). On servers with many big mods this could hold up loading for a long time. When it does read them, a mod whose files are too big or slow to read (over 20,000 files, 512 MB or 10 seconds), or can't be read at all, is skipped with a warning in the server log and shown as **Failed to load** on the Bans tab; its items can still be banned one by one or by type. Folder links inside mods are skipped.
- The red Flea banned icon now also shows on items the game itself won't let you list (the Death Shadow mask, many ammo boxes and plates), not only on items Better Flea or SPT bans. Things nobody expects to list get no icon: money, quest items, dogtags, armbands and secure containers.
- Better Flea pricing needs no internet: its prices are built into the mod. Only Live prices fetch from tarkov.dev. If Live prices are on but tarkov.dev can't be reached and no prices were saved yet (first start offline), Better Flea pricing is used instead of SPT's own prices, a warning goes in the server log and the Economy tab says so, and it tries again every 10 minutes.

## 2.2.0

**New**
- **Item unlock levels (off by default):** some items can only be bought and sold from a certain level (the M4A1 from 25, a graphics card from 40). Like live, an item's inspect window says "Unlocks on flea market: X Level", flea categories show a lock with the level they open at, offers you can't buy yet show locked, and items you can't sell yet are greyed out when adding an offer. Uses the real game's level for every item, kept up to date from tarkov.dev when Live prices are on. A modded item unlocks with the real items of its kind that are worth about the same, so a strong modded stim needs the level the strongest real stims need. Pick the new **Live progression** flea style, or turn it on in the Bans tab; selling can be left unlocked. With Advanced settings on, you can set your own level for any item or whole category.
- The default flea style, Live-like, is now called **Balanced**. It's live-like but has no item unlock levels or Wipe Cycle; **Live progression** turns both on and is the style closest to the live flea.
- **Seller badges:** like the live flea, some sellers have an Edge of Darkness (20%) or Unheard (15%) badge, and very rarely a Developer, Sherpa or Emissary one. The shares are on the Market tab.
- The trader item lock now remembers trader purchases while it's off too (and flea purchases, for "Lock flea purchases too"), so turning it on mid-wipe also locks what was bought before. An item is forgotten once its owner no longer has it, so the list stays small.
- Item lookup on the read-only page, so everyone can look up an item's prices.
- The read-only page shows when live prices were last updated.
- The orange trader icon is now on by default, so players see it as soon as the server turns the trader item lock on. The red flea banned icon stays off by default. Both can be changed in the F12 menu. If you already have the plugin, your current setting is kept.

**Fixes**
- A seller's rating could change from one offer to the next. Each seller name now keeps the same rating and badge on every offer, even after a restart.
- An overpriced listing could still sell straight away. Armour with one plate was judged against offers with full plates, so a 1-plate Zhuk at a full-plate price sold. The normal price for your listing now goes by what's actually in it: the item, its plates and parts, and their condition. A listing more than 30% over that price (expensivePercent) never sells, gun builds included.
- With Live prices on, guns got far too high a base price (the AKS-74U at 76,000 while its offers were 15,000). tarkov.dev's average for a gun counts every build listed, so guns now use the day's low price. Live prices are fetched again once after updating.
- Players under the flea's level could see offers from players with an Edge of Darkness or other badge.
- The Bans tab could credit another mod's items to SVM ("Other 2"). An item now only goes to a mod when that mod clearly adds it, and mods whose only items can never be on the flea (SVM's pockets) aren't listed.
- Banning or always allowing a whole category picked from the Bans tab search did nothing. Categories now work there, as well as single items.
- Items you ban had no red flea icon, and a player without the plugin could still list them. They now get the icon, and the server refuses the listing.
- The Save and Discard buttons jumped down while "Saved" showed, and "Log out admin" dropped to a second line when "Unsaved changes" showed. Messages now show under the bar, and nothing in the top bar moves any more.
- The flea icon on an equipped gun sat on top of its durability bar. It now sits next to the gun's mastery level, as it does in the inspect window.
- The red and orange flea icons often only showed after an item was moved. They now show on every item as soon as the plugin hears from the server.
- The flea icon's tooltip now has its heading (Flea banned, Bought from trader) in bold.
- The add-offer screen gave the game's own wording for items it can't list ("This item can't be sold at the Flea Market.") next to Better Flea's. Every reason now reads the same way: "Flea banned - cannot be listed", "Bought from trader - cannot be listed", "Contains ... - cannot be listed".
- The read-only page's "Updated X ago" for live prices was easy to miss. It now has its own line.
- The trader item lock could miss a purchase (an STM-9 bought from Skier wasn't locked) when the game sent it together with another action that gave a warning.
- "Lock flea purchases too" never locked anything: it looked for the wrong kind of purchase.
- After saving bans, levels or other settings that don't remake the flea, items stopped filling up when opened, so most showed only a few offers (often 15) instead of their full count (30 or more, 60 for guns). Items fill up again as before.
- The toy gun's 20x1mm disks were listed by the stack. They now come one or two at a time, like signal flares.
- With WTT-Armory installed, the server console could fill with "Could not add ... to cache, it does not exist in the item database!". Its M700XL lists parts from a mod that isn't installed, and Better Flea asked SPT about them when building one for the flea. Parts that aren't installed are now skipped.
- Which plates armour gets went by a carrier's price in roubles (40k to 400k), so it stopped working when prices change (Live prices, a price multiplier, or SPT's own price changes). It now goes by how the carrier ranks against other carriers with their default plates.

## 2.1.1

**New**
- **Admin login for the settings page:** the first person to open the page makes an admin account, and changing settings needs that login. It replaces SPT's web login and works the same on the server PC and from any other PC. You stay logged in for 7 days, even through server restarts.
- Everyone else gets a read-only page: the flea's stats and charts, the biggest price movers, and whether Live prices and Wipe Cycle are on or off.
- Forgot the password? Delete `SPT_Runtime/user/mods/BetterFlea/data/admin.json` and restart the server, then make a new account. Your settings are kept.

**Fixes**
- SVM (Server Value Modifier) failed to start with Better Flea installed.

## 2.1.0

**New**
- **Settings page in your browser:** open `https://127.0.0.1:6969/betterflea` on the server PC, or click it under Mod Pages in the SPT launcher. Every setting has a short note, a reset button, and sliders or +/- buttons. Nothing changes until you press Save, then the config files are updated (notes kept) and the changes apply straight away, apart from the bans.
- **Search:** finds any setting and takes you straight to it (press / to search).
- **Flea styles:** Live-like (the default), Base SPT (Better Flea's changes off), Rough market, Overpriced, Flea sale, Chaos, Random and Low memory.
- **Home tab:** offers on the flea and server memory over the last few hours, who's selling, the biggest price movers and what's new in this version.
- **Ban a mod's items:** the Bans tab lists the server mods that add items. Ban everything a mod adds, or only some types (its guns, its backpacks...).
- **All flea prices:** one setting makes every offer from other sellers dearer or cheaper. Trader prices don't change. The Overpriced and Flea sale styles use it.
- **Live prices (off by default):** the real flea's 24 hour average from tarkov.dev as every item's base price, from Live, PvE or Season. Saved, so an outage changes nothing. Modded items are moved onto the same scale using similar base-game items (can be turned off). Real-game items that mods add back, like content backports, aren't counted as modded. Junk averages (an item listed at a few roubles) are ignored.
- **Wipe Cycle (experimental, off by default):** prices follow how far into the wipe the players online are, shaped by how real flea prices moved through a wipe: hideout and quest items, guns by type, ammo by penetration, armour by class, stims. The Economy tab shows what it's doing right now and can preview any level.
- Seller ratings grow with the players online: mostly low up to level 15, mostly high by level 40.
- Saving remakes the flea's generated offers straight away, so changes show without waiting for old offers to expire. Settings that need a restart say so at the top of the page.
- Advanced settings are tucked away behind a switch. Banned and allowed items can be searched for by name.
- Switch whole sections off, and export your settings to a file and import them again.
- Shows whether there's a newer release on GitHub.
- Uses SPT's own web login: opens straight away on the server PC, SPT admin accounts anywhere else. `configs\web.jsonc` can turn the page off.
- Debug logging can now be switched on and off without a restart.

**Fixes**
- With player listings turned off, the client plugin greys out the sell button and shows a small warning instead of the error that sends you back to the menu.
- The client plugin no longer stops checking the server after one failed check (a server restart while the game was open), so turning listings back on shows up again.
- The Low memory style really is low now: about a third of the offers at startup (lists still fill up when opened), and memory is handed back sooner after browsing.
- Turning packs off now puts back SPT's own pack settings, and offers can be as short as SPT's 6 minutes, so Base SPT matches plain SPT.
- Picking a flea style no longer resets your bans.
- With offer counts turned off, money for sale was still cut down to almost nothing.
## 2.0.0

**Updating from 1.x:** extract the new zip over the old one. Your `config.jsonc` settings are copied into the new `configs\` folder on the first start and the old file is deleted.

### Highlights
- **Guns are listed the way players sell them.** Stripped, stock, ground loot, found, budget, modded and meta builds, broken-down and gutted guns, and guns looted off scavs, bosses and guards. Proper optics in the right places, matching colours, sensible mags, lasers on the side rails, and wear to match the build.
- **Gun prices make sense.** Flea guns are priced against what traders sell the same parts for, so a stock gun is around or under the trader's price. Nothing can be bought on the flea and sold to a trader for profit.
- **Your own guns turn up.** Builds you save can be listed by "someone who copied it", and a gun you die with can be listed later by whoever killed you.
- **Armour comes with real plates.** Both, one or none, mostly class 4, some class 3 or mixed, side plates now and then, and every condition from mint to obliterated.
- **A living market.** Offers last up to 24 hours, good deals get bought by other players, overpriced ones sit there, and now and then something is "sold before you".
- **Sellers feel like players.** Flea ratings up to 300 (flippers selling several of an item are usually high), some sellers overprice, the odd quick sell, rare barter offers, packs that are mostly scams, and very rarely a silly 60-item pack.
- **Selling depends on your price.** Undercut and it's gone in under a minute, the normal price takes a few minutes, overpriced barely sells. Good gun builds still sell now and then even when dear.
- **3 times the offers with about the same server memory.** Most offers are only made when someone looks at the item, and old ones are trimmed as you move on.
- **Settings split into a `configs\` folder**, one file per part of the mod.

### Gun offers
- Gun builds by tier: stripped, stock, ground loot (factory gun, 100/100 or just under), found, budget, modded, broken and gutted. Cheap guns lean stripped and broken, dear guns lean modded.
- Meta builds: the best build of each gun worked out from its stats, or your own build saved with a name starting "Meta".
- Looted builds: guns exactly as SPT's scavs, bosses and guards carry them.
- Optics placed like players do: main sight on the top rail, backup red dots next to magnified scopes, sensible zoom for the gun type.
- Lasers and lights go on the side rails, almost never on top in front of the scope.
- Some guns have rounds in the mag, always ammo the gun fires.
- Build deals: some guns listed below what their parts are worth.
- More gun offers: at least 60 per gun (30 for superrare guns), and more stock and ground-loot guns.

### Other offers
- Armour and rigs get realistic plates and condition. Helmets, face shields, masks and visors get condition too.
- Face shields, visors and night vision on helmets are sometimes listed flipped down.
- Loose magazines are sometimes loaded: full, half, a single round, or a scam with one good round on top of cheap ones.
- Full meds sometimes come several to an offer (up to 6). Used meds are always one per offer.
- Small meds (Ibuprofen, splints) are sometimes nearly finished. Big surgical kits wear down evenly.
- Repair kits are often listed worn down.
- Some fuel cans are listed empty: mostly the cheapest fuel, some priced like a full can.
- Now and then a pile of grenades or a handful of the same stim.
- Packs: about 3 in 100 offers on barter items, food, meds and fuel. Most are scams.
- Barter offers: about 1 in 200 offers asks for barter items people actually have, worth a bit less than cash.
- Fixed offers showing as "available in 00:00:00" and greyed out.

### Market
- Offers last 6 to 24 hours, and each one is found partway through its time.
- Simulated buyers: good deals sell fast, overpriced ones hardly ever.
- "Sold before you" on guns and barter items now and then.
- Seller ratings up to 300.
- Price drift up to 20%, saved through restarts.
- Sellers who overprice, and the odd quick sell. Never under the price floor.

### Player selling
- Sale speed depends on your price against the normal price (replaces the flat 2 to 20 minute wait).
- Flip penalty purchases are saved through a restart.

### Memory and speed
- Offers made on demand: most items start with 15% of their offers and fill up when someone looks.
- Only your last 4 lists stay filled. Unwatched items are trimmed after 2 minutes, with a hard cap on extra offers.
- Faster flea searches and category counts.

### Modded items
- Items are handled by their type, stats and price rather than a list of names, so modded guns, attachments, optics, armour, meds and traders are picked up too.
- The server log lists the other mods found and what Better Flea worked out from them.

### Saved through restarts (`data\` folder)
- Lost guns waiting to be listed, and ones already listed.
- Recent purchases for the flip penalty.
- Price drift.
- Purchase locks (as before).

### Settings
- Settings are now in `configs\` (flea, listings, condition, weapons, armor, market, prices, selling, debug).
- The old `config.jsonc` is copied over automatically and deleted.
- A deleted settings file is written back on the next start.
- Unknown or misspelt settings are named in the server log.

### Changed defaults
- `listings.multiplier` 2 → 3.
- `purchaseLock` is now off by default.
- The plugin's item icons are now off by default. Turn them on in the F12 menu (BepInEx configuration manager) or in `BepInEx\config\com.fums.betterflea.client.cfg`.

### Removed
- `missingPartsChance` and `maxMissingParts`. Broken-down and gutted guns cover this now.

## 1.0.0
- First release.
