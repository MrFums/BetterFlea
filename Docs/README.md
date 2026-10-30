# The Better Flea guide

Everything about Better Flea in one place: what each part of the mod does, every setting and what it's for, how
prices are worked out (in SPT and in Better Flea), and what we learned building it. Written for Better Flea 2.4.0 on
SPT 4.1.x.

New here? Read [Getting started](01-getting-started.md), then jump to [I want to...](#i-want-to) below.

## Pages

| # | Page | What's in it |
|---|---|---|
| 1 | [Getting started](01-getting-started.md) | Install, the two parts, the settings page, flea styles, updating, uninstalling |
| 2 | [Settings reference](02-settings-reference.md) | Every tab and every setting: what it does, its default, when to change it |
| 3 | [How prices work](03-how-prices-work.md) | SPT's handbook and flea prices, trader prices, the flea fee, and Better Flea's price pipeline |
| 4 | [Guns and attachments](04-guns-and-attachments.md) | How gun offers are built and priced, part by part |
| 5 | [Armour and condition](05-armour-and-condition.md) | Plates, inserts, durability, wrecked gear, used keys, meds and food |
| 6 | [The SPT quest tree](06-quest-tree.md) | How SPT locks trader items behind quests, and how Better Flea scores how hard each unlock is |
| 7 | [Selling on the flea](07-selling.md) | How fast your listings sell, buyers, undercuts, flipping, fees |
| 8 | [Modded items, and advice for mod authors](08-modded-items.md) | How Better Flea handles other mods' items, and how to price yours so it works everywhere |
| 9 | [API for other mods](09-api.md) | The routes your mod can call, with request and reply shapes |
| 10 | [FAQ and troubleshooting](10-faq.md) | Common questions and problems |

Deeper research, with the data behind it, is in [../Research](../Research/README.md). Every change, version by
version, is in [../CHANGELOG.md](../CHANGELOG.md).

## I want to...

| ...do this | Go to |
|---|---|
| Make the flea feel like live, unlock levels and all | Pick the **Live progression** [flea style](01-getting-started.md#flea-styles) |
| Turn Better Flea's changes off without uninstalling | Flea style **Base SPT** |
| Run on a low-memory PC | Flea style **Low memory**, or [on-demand offers](02-settings-reference.md#listings-tab) |
| Ban guns, ammo or a whole kind of item | [Bans tab](02-settings-reference.md#bans-tab): banned categories |
| Ban one item, or allow one that's banned | [Bans tab](02-settings-reference.md#bans-tab): banned items, allowed items |
| Ban everything another mod adds | [Bans tab](02-settings-reference.md#bans-tab): Mod items |
| Let a mod's own flea-banned items on the flea | [Bans tab](02-settings-reference.md#bans-tab): Mod items, Allow |
| Lift BSG's bans (M995, Bitcoins and so on) | [Bans tab](02-settings-reference.md#bans-tab): BSG's banned items off, then restart the game |
| Make everything on the flea dearer or cheaper | [Prices tab](02-settings-reference.md#prices-tab): price multiplier |
| Make marked keys dearer | [Prices tab](02-settings-reference.md#prices-tab): marked key multiplier (1.5 is a good start) |
| Have more or fewer offers | [Listings tab](02-settings-reference.md#listings-tab): multiplier, minimum offers |
| Stop players flipping trader items | [Selling tab](02-settings-reference.md#selling-tab): trader item lock |
| Make player sales slower or faster | [Selling tab](02-settings-reference.md#selling-tab): sale delay |
| Turn off selling on the flea entirely | [Selling tab](02-settings-reference.md#selling-tab): enabled off |
| Fix a modded item with a silly price | [Economy tab](02-settings-reference.md#economy-tab): Your prices |
| See what an item is worth and how it would sell | Economy tab: Item lookup, and [sale odds](07-selling.md#sale-odds) |
| Use today's real flea prices | [Economy tab](02-settings-reference.md#economy-tab): live prices |
| Have prices follow the wipe | [Economy tab](02-settings-reference.md#economy-tab): Wipe Cycle (experimental) |
| Use SVM's trader prices | Economy tab: turn **Live trader prices** off. [How trader prices work](03-how-prices-work.md#what-traders-pay) |
| Show Better Flea's prices in my own mod | [API for other mods](09-api.md) |
| Price my mod's items so they work well | [Advice for mod authors](08-modded-items.md#advice-for-mod-authors) |

## Words used in this guide

| Word | Meaning |
|---|---|
| **Template id** (`_tpl`) | The 24-character id of a kind of item, the same for every M4A1. |
| **Item id** (`_id`) | The id of one actual item in a stash or offer. |
| **Handbook price** | The price in SPT's `templates/handbook.json`. What traders base their buy price on. |
| **Base price** | The price an item starts from before anything is added on top. In Better Flea: the price index, live price, your price, or SPT's flea price. |
| **Normal price** | What one of an item, as it is (its condition, plates, parts), is worth on Better Flea's flea right now. The price your own listing is measured against. |
| **Floor** | The lowest price any generated offer can have: the best trader buy price plus a margin. |
| **Generated offer** | An offer the server makes up (an NPC seller). Not a real player's. |
| **Loyalty level (LL)** | A trader's level for you, 1 to 4. Higher levels unlock more items. |
| **Wrecked** | Gear under half a new one's worth, all its parts together. |
| **Plain item** | A single item with no durability that isn't a gun: a GPU, a bandage, an empty rig. |
