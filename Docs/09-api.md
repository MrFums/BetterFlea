# API for other mods

[Back to the guide](README.md)

Your mod can ask the server for Better Flea's prices and rules. Price Index and Flogger use these routes, so what they
show and sell matches the flea.

## The basics

- The routes are on the SPT server, at the server's address (`https://127.0.0.1:6969` locally).
- They need no login, and answer plain JSON (not zlib-compressed like SPT's own routes).
- From a BepInEx plugin, use SPT's `RequestHandler`, which sends the player's session for you.
- **These routes and their shapes won't change without a note in the changelog.**
- If a route isn't there (Better Flea not installed, or too old), the server answers with an error or a 404. Fall back
  to your own prices or data when that happens.

## Item price

`POST /betterflea/item-price`, Better Flea 2.3.0 or newer.

What Better Flea would sell one of this item for on the flea, in roubles, with every setting applied: the base price,
condition, key uses, drift, the price multiplier, the marked key multiplier, the Wipe Cycle, the floor and gun pricing.
It's the same price a player's own listing is measured against ([normal price](07-selling.md#your-items-normal-price)).

**Request:** the item and everything in it, as a JSON list, the way the game stores items: the root item first with
`_id` and `_tpl`, and for each part also `parentId` and `slotId`. Add `upd` for condition, key uses or stack size. The
item doesn't have to be in anyone's stash.

```json
[
  { "_id": "6720aaaaaaaaaaaaaaaaaaaa", "_tpl": "5c0e655586f774045612eeb2" },
  { "_id": "6720aaaaaaaaaaaaaaaaaaab", "_tpl": "6570e025615f54368b04fcb0", "parentId": "6720aaaaaaaaaaaaaaaaaaaa",
    "slotId": "Soft_armor_front", "upd": { "Repairable": { "Durability": 23.9, "MaxDurability": 48.5 } } }
]
```

**Reply:**

```json
{ "Price": 47079 }
```

`Price` is 0 when there's no price: an empty list, an unknown item, or more than 300 items. Send `[]` once at startup;
a reply of `{"Price":0}` tells you the route is there.

```csharp
var json = RequestHandler.PostJson("/betterflea/item-price", JsonConvert.SerializeObject(items));
var price = JsonConvert.DeserializeObject<JObject>(json)?["Price"]?.Value<double>() ?? 0;
```

## Flea status

`GET /betterflea/client-data`

What the flea allows for the player making the request:

| Field | What it is |
|---|---|
| `FleaBanned` | Template ids that can't be sold on the flea |
| `BoughtFromTrader` | Item ids this player bought from a trader that the trader item lock keeps off the flea |
| `PurchaseLockOn` | The trader item lock is on |
| `SellingOff` | Players can't sell on the flea at all |
| `LevelLockOn` | Item unlock levels are on |
| `LevelLockSelling` | Unlock levels also stop players selling the item |
| `LevelsVersion` | Changes whenever the unlock levels change. Fetch `/betterflea/item-levels` again when it does |
| `PlayerLevel` | The player's level |

A quick-sell mod should check `FleaBanned`, `BoughtFromTrader`, `SellingOff` and the unlock levels before trying the
flea, and go to a trader instead.

## Item unlock levels

`GET /betterflea/item-levels`

The level each item unlocks at on the flea, by template id:

```json
{ "5780cf7f2459777de4559322": 40 }
```

Items with no unlock level aren't listed, and the list is empty when unlock levels are off.

## Flip notice

`GET /betterflea/flip-notice`

Used by Better Flea's own plugin straight after a listing: the flip penalty on it, if any. Each notice is given out
once.

```json
{ "Tpl": "5c0e655586f774045612eeb2", "Minutes": 6.5, "Bought": 7, "ClearsIn": 18 }
```

`Minutes` 0 means no penalty. `Bought` is how many of the item counted (Fence purchases count extra), `ClearsIn` the
minutes until listing it would carry no wait. You'll rarely need this one.

## The settings page's own data

The settings page reads JSON from `/betterflea/api/...`. These are **not** a stable API: they follow what the page
needs and can change in any version. The ones a guest can read:

| Route | What it returns |
|---|---|
| `/betterflea/api/status` | The flea's stats: offers, players, packs, barters, sales, deals sold, memory, uptime, price movers and history |
| `/betterflea/api/overview` | The version, who's logged in, and the economy settings (Better Flea pricing, live prices, Wipe Cycle) |
| `/betterflea/api/items?q=<text>` | Item search by name |
| `/betterflea/api/economy?id=<tpl>` | Item lookup: base, SPT, Better Flea and live prices, actual value, sale odds, the wipe curve |
| `/betterflea/api/offers` | Every player's offers that are up, with sale odds |

Everything else under `/betterflea/api/` needs the admin login.

## Detecting Better Flea

From a client plugin, the simplest check is the route: send `[]` to `/betterflea/item-price` and look for
`{"Price":0}`. Better Flea's client plugin is also there in BepInEx's `Chainloader.PluginInfos` under the id
`com.fums.betterflea` (2.4.0 and newer; older versions used `com.fums.betterflea.client`). The server mod's GUID is
`com.fums.betterflea`.
