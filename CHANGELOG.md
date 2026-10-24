# Changelog

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
