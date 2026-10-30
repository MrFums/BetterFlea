# Getting started

[Back to the guide](README.md)

## The two parts

Better Flea is a server mod with a small client plugin.

| Part | Folder | Who needs it | What it does |
|---|---|---|---|
| Server mod | `SPT_Runtime/user/mods/BetterFlea/` | The server | Every rule: offers, prices, bans, selling |
| BepInEx plugin | `BepInEx/plugins/BetterFlea/` | Every real player | Shows the server's rules in game: icons, greyed-out items, unlock levels, the flip penalty pop-up |

The server decides everything. The plugin only shows it. A player without the plugin still can't sell what the
server forbids, but gets SPT's error message instead of a small warning.

**Fika:** install the server mod on the server and the plugin on every player's game. Don't install the plugin on a
Fika headless client.

## Install

1. Download `BetterFlea-<version>.zip` from [Releases](https://github.com/MrFums/BetterFlea/releases).
2. Extract it into your SPT folder (the one with `EscapeFromTarkov.exe`). Both parts go to the right folders.
3. Start the server. Better Flea writes its settings into `SPT_Runtime/user/mods/BetterFlea/configs/` the first time.

## The settings page

Open `https://127.0.0.1:6969/betterflea` on the server PC, or click it under **Mod Pages** in the SPT launcher. From
another PC, use the server's address instead of `127.0.0.1`.

- **First visit:** you make an admin account. Changing settings needs that login. Anyone else who opens the page
  sees the flea's stats, Item lookup and the offers on the flea, but can't change anything.
- **Forgot the password?** Delete `SPT_Runtime/user/mods/BetterFlea/data/admin.json` and restart the server, then
  make a new account. Your settings are kept.
- **Searching:** the box at the top finds any setting by name or by words in its description. Press `/` to jump to it.
- **Advanced settings:** some settings are hidden until you turn on the **Advanced settings** switch, at the top of
  the page next to the flea style.
- **Saving:** most changes apply when you press Save. A few need a server restart, and the page says which. Bans
  also need the game restarted, because the game reads which items can be sold only when it starts.
- **Undoing:** Reset to default on a setting or a tab, or Reset everything in the More menu.
- **Languages:** English, Russian, Chinese (Simplified), German, French, Spanish, Portuguese (Brazil), Polish,
  Japanese and Korean. It starts in your browser's language.

Please change settings on the page, not in the `configs` files. The page checks values, keeps the notes in the files,
and applies most changes without a restart. If you do edit a file by hand, restart the server afterwards.

## Flea styles

A flea style sets many settings at once. Pick one with **Flea style** at the top of the settings page, then fine-tune
on the tabs.

| Style | What it's for |
|---|---|
| **Balanced** (default) | Live-like, without item unlock levels or the Wipe Cycle. |
| **Live progression** | Balanced plus item unlock levels and the Wipe Cycle. As close to the live flea as it gets. |
| **Base SPT** | Better Flea's flea changes off, so the flea works like plain SPT (the memory saver stays on). Useful to compare. |
| **Rough market** | Fewer offers and most of them worn out, stripped or broken. Good deals get snapped up fast. |
| **Overpriced** | Everything costs about 50% more, quick sells are gone and guns cost more than at the traders. |
| **Flea sale** | An event weekend: everything 15% cheaper, lots of offers and deals, fast turnover. |
| **Chaos** | Everything turned up: piles of offers, scams and giant packs, wild prices. For fun. |
| **Random** | Every number nudged up or down at random. Pick it again for a different flea. |
| **Low memory** | Far fewer offers for small servers. Items still fill up when you open them. |

## Updating

Close the game and the server, extract the new zip over the old one, and start the server. Your settings are kept.
New settings are added with their defaults. A setting still at an old default moves to the new default once; one
you changed is left alone. Which updates have run is kept in `data/applied-updates.json`.

## Uninstalling

Delete `SPT_Runtime/user/mods/BetterFlea/` and `BepInEx/plugins/BetterFlea/`. Better Flea doesn't change player
profiles, so there's nothing else to clean up.

## Files Better Flea keeps

All under `SPT_Runtime/user/mods/BetterFlea/`:

| File | What it is |
|---|---|
| `configs/*.jsonc` | Your settings, one file per tab |
| `data/admin.json` | The settings page login (a hash, not the password) |
| `data/applied-updates.json` | Which one-time default moves have run |
| `data/live-prices.json` | The last live prices fetched, if live prices are on |
| `data/price-drift.json` | Each item's current price drift |
| `data/purchase-locks.json` | Items bought from traders, for the trader item lock |
| `data/recent-purchases.json` | Recent purchases, for the flip penalty and the 48-hour rule |
| `data/lost-guns.json` | Guns players died with that may turn up on the flea |

Deleting a `configs` file and restarting writes it back with the defaults.
