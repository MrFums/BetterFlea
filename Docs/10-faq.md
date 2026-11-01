# FAQ and troubleshooting

[Back to the guide](README.md)

## Settings

**I changed bans but the game still shows the item as banned (or not).**
Bans need a server restart, then a game restart. The game reads which items can be sold only when it starts.

**I can't open the settings page from another PC.**
Use the server's address instead of `127.0.0.1`. The server also has to accept connections from other PCs: in
`SPT_Data/configs/http.json`, `ip` must be the server's network address or `0.0.0.0`, not `127.0.0.1`. Only open it on
a network you trust.

**I forgot the settings page password.**
Delete `SPT_Runtime/user/mods/BetterFlea/data/admin.json`, restart the server and make a new account. Settings are
kept.

**I edited a config file and nothing changed.**
Restart the server after editing files by hand. Better yet, use the settings page: it checks the values and applies most
changes straight away.

**How do I get the defaults back?**
Reset to default on a setting or tab, or Reset everything in the More menu. Or delete a file in `configs/` and restart.

## Selling

**My offer won't sell.**
Check, in this order:
1. **Is it over its normal price?** The game's average when you list is a *new* one's price; a worn or stripped item
   is worth less. Open the Lookup tab, Offers on the flea, and look at your offer's odds.
2. **Did you buy it in the last 48 hours?** Then it never sells more than 15% over normal.
3. **Is it wrecked?** Wrecked gear sells about 6 times in 10 even at normal, and almost never over it.
4. **Does someone have it cheaper?** Buyers go to the cheapest offer first.
5. **Did you buy several just now?** The flip penalty adds up to 10 minutes.

**Can rare items sell slower, or everything straight away?**
Turn on **Set sale times** (Selling tab, with Advanced settings on) and set a shortest and longest wait for common,
rare and very rare items. 0 and 0 sells straight away. To keep buyers but slow them for rarer items, use
`buyers.rarityMultipliers` instead. [Set sale times](07-selling.md#set-sale-times)

**Can players get the flea earlier, or list more offers?**
Yes: **Flea access** (Selling tab). Set the level it opens at, more offer slots at every flea rep step, and faster rep
from sales. Players see a new level or slot count after logging in again. [Flea access](07-selling.md#flea-access)

**My flea rating doesn't change after a sale.**
SPT adds the rep but doesn't tell the game, so it only showed after logging in again. From 2.4.2, Better Flea sends
the new rating after each sale.

**Why can't I list an item at all?**
It's banned (red icon if you've turned it on in the F12 menu), locked by the trader item lock (orange icon), above your
unlock level, or the server has player selling off. Hover the icon to see why.

**The sell fee is huge.**
The fee grows fast when you ask more than an item is worth. [The flea fee](03-how-prices-work.md#the-flea-fee)

## Prices

**A modded item has a silly price.**
Set it yourself under Your prices (Economy tab), and please report it so it can be looked at.

**An item's base price is 0 and it sells for far less than its Better Flea price.**
SPT gave the item no base price when the server started, so Better Flea has nothing to replace. SPT works base
prices out at every start from the handbook price x 1.5 (`SPT_Data/configs/ragfair.json`,
`dynamic.generateBaseFleaPrices`), skipping items with no handbook price. Better Flea never sets a base price to 0.
Check, in this order:
1. **SPT version.** An item from a newer SPT isn't in an older one's handbook.
2. **The handbook.** Search `SPT_Data/database/templates/handbook.json` for the item's id: it should be there with a
   price over 0. If not, the SPT install is damaged or edited: reinstall SPT over it (profiles are kept).
3. **The flea config.** In `ragfair.json`, `generateBaseFleaPrices.useHandbookPrice` should be `true`. Some mods turn it
   off.
4. **Another mod.** Move all server mods except Better Flea out of `user/mods`, restart, and look the item up on the
   Lookup tab. If the price is back, put the mods back half at a time until it goes to 0 again. Start with mods that
   change prices, traders, the flea or the handbook.

**Traders pay less than I'm used to.**
Live trader prices (Economy tab) is on by default: traders pay what they do in the live game since patch 1.1, about a
fifth less than SPT 4.1's 0.16.9 numbers. Turn it off for SPT's. [What traders pay](03-how-prices-work.md#what-traders-pay)

**SVM's trader prices are ignored.**
A trader whose prices SVM has changed keeps SVM's, and the server log says which. Left at SVM's defaults, Better Flea's
live prices win. To use SVM's for every trader, turn Live trader prices off.

**An item on the flea is cheaper from a trader.**
That's the floor working the other way: no flea offer goes under what a trader *pays*, but a trader can *sell* for less.
For modded traders that sell under what another trader pays (Artem's SF High Cut, bought back by Ragman for more),
the flea stays over Ragman's price. The unsupported **No floor for trader loops** setting changes that.

## Other mods

**Another flea mod does the same thing.**
The mod that loads last usually wins. Turn the feature off in one of the two.

**Price Index or Flogger show different prices.**
Both read Better Flea's prices with Better Flea 2.3.0 or newer. Update both.

**The server console fills with "[Client Request] /betterflea/item-price".**
That's SPT logging every request Price Index makes. From 2.4.2, SPT no longer logs the plugins' own requests
(item-price, client-data, item-levels, flip-notice). Every other request is still logged.

**"durability value is invalid, defaulting to 1" in the server log.**
SPT writes this for an item with durability at 0 (destroyed armour from a raid) when it's priced. Fixed in 2.4.2: such
an item is priced as next to worthless, with no error.

**The server takes a long time to start with lots of mods.**
Better Flea only reads other mods' files when a setting needs it (a Mod items rule, or a mod switched off under
Modded attachments), with time limits. If startup still hangs, report it with your mod list and server log.

## Performance

**The server uses too much memory.**
Pick the Low memory flea style, or lower `listings.multiplier` and the on-demand settings (Listings tab). Better Flea
makes most offers only when someone looks, so it normally uses about as much memory as SPT without it. Low memory
saves about 60 MB on a test install (630 MB down to 570 MB); most of the memory is SPT and your other mods.

## Fika

**Do I install the plugin on the headless client?**
No. The server mod on the server, the plugin on every real player's game.

## Reporting a problem

Post in Better Flea's Discord channel on the SPT server, in the mod's comments, or on
[GitHub issues](https://github.com/MrFums/BetterFlea/issues). Include your server log
(`SPT_Runtime/user/logs/spt/`), your mod list, and what you expected. For sale or price problems, turn on Debug logging
(Debug tab) first and reproduce it.
