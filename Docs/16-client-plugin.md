# The client plugin

[Back to the guide](README.md)

The server makes all the rules. The BepInEx plugin, `BepInEx/plugins/BetterFlea/BetterFlea.Client.dll`, shows them in
game. Every real player should install it; a Fika headless client shouldn't.

## What it does

| Feature | What the player sees |
|---|---|
| **Flea icons on items** | A red icon on items banned on the flea, an orange one on items bought from a trader (only with the trader item lock on). Hover the icon to see why |
| **Greyed-out items** | In the add-offer screen, items you can't list are grey, with Better Flea's reason |
| **Item unlock levels** | The inspect window says the level an item unlocks at; flea categories show a lock and their level; locked offers show locked |
| **Selling turned off** | The sell button is greyed out with a small warning, instead of an error that sends you to the menu |
| **Flip penalty pop-up** | When a listing gets the flip penalty: the item, how many you bought, and how much longer buyers take |
| **Quick-sell mods** | Items that can't go on the flea are sold to a trader instead of failing |
| **Other mods' price checks** | Mods that ask the game whether an item can be listed (Price Index, quick-sell mods) get Better Flea's answer |

## Settings (F12 menu)

Open the BepInEx configuration manager with F12.

| Setting | Default | What it does |
|---|---|---|
| Icons, **ShowFleaBannedIcon** | off | The red icon on flea-banned items |
| Icons, **ShowTraderIcon** | on | The orange icon on items bought from a trader. Only shows when the server has the trader item lock on |

The plugin's id is `com.fums.betterflea` from 2.4.0 (`com.fums.betterflea.client` before), so these two settings went
back to their defaults once when updating to 2.4.0.

## Without the plugin

The server still enforces everything. A player without the plugin can't list what the server forbids either, but gets
SPT's error message instead of the small warning, doesn't see icons or unlock levels, and doesn't get the flip penalty
pop-up (the in-game message still comes).

## Updating

The plugin only changes in some releases; the release notes say when every player needs to update it. Use the plugin
from the same release as the server mod.
