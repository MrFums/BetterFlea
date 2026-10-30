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
   is worth less. Open the Economy tab, Offers on the flea, and look at your offer's odds.
2. **Did you buy it in the last 48 hours?** Then it never sells more than 15% over normal.
3. **Is it wrecked?** Wrecked gear sells about 6 times in 10 even at normal, and almost never over it.
4. **Does someone have it cheaper?** Buyers go to the cheapest offer first.
5. **Did you buy several just now?** The flip penalty adds up to 10 minutes.

**Why can't I list an item at all?**
It's banned (red icon if you've turned it on in the F12 menu), locked by the trader item lock (orange icon), above your
unlock level, or the server has player selling off. Hover the icon to see why.

**The sell fee is huge.**
The fee grows fast when you ask more than an item is worth. [The flea fee](03-how-prices-work.md#the-flea-fee)

## Prices

**A modded item has a silly price.**
Set it yourself under Your prices (Economy tab), and please report it so it can be looked at.

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

**The server takes a long time to start with lots of mods.**
Better Flea only reads other mods' files when a setting needs it (a Mod items rule, or a mod switched off under
Modded attachments), with time limits. If startup still hangs, report it with your mod list and server log.

## Performance

**The server uses too much memory.**
Pick the Low memory flea style, or lower `listings.multiplier` and the on-demand settings (Listings tab). Better Flea
makes most offers only when someone looks, so it normally uses about as much memory as SPT without it.

## Fika

**Do I install the plugin on the headless client?**
No. The server mod on the server, the plugin on every real player's game.

## Reporting a problem

Post in Better Flea's Discord channel on the SPT server, in the mod's comments, or on
[GitHub issues](https://github.com/MrFums/BetterFlea/issues). Include your server log
(`SPT_Runtime/user/logs/spt/`), your mod list, and what you expected. For sale or price problems, turn on Debug logging
(Debug tab) first and reproduce it.
