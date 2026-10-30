# What Better Flea changes in SPT

[Back to the guide](README.md)

For mod authors and anyone chasing a clash between mods. Better Flea changes SPT in three ways: Harmony patches on
SPT's own methods, changes to SPT's data and config while the server runs, and its own HTTP routes. Nothing is written
to SPT's files or to player profiles.

If your mod patches or replaces the same methods, the two can fight. The usual symptom is that whichever mod loads last
wins that feature.

## Server patches

| SPT method | Kind | What Better Flea does there |
|---|---|---|
| `RagfairPriceService.Load` | Postfix | Swaps in the base prices (price index, live prices or your prices), estimates modded items, sets trader cuts |
| `RagfairPriceService.GetDynamicItemPrice` | Postfix | The price pipeline for generated offers: drift, demand, floor, spread, multipliers, Wipe Cycle |
| `TraderHelper.GetHighestSellToTraderPrice` | Prefix | Counts only traders that actually buy the item (SPT counted traders that pay nothing) |
| `ItemHelper.GetRepairableItemQualityValue` | Postfix | Armour condition by max and current durability. Used by the flea, player listings and Fence; not by trader buy prices |
| `RagfairServerHelper.GetOfferCountByBaseType` | Postfix | How many offers each item gets |
| `RagfairServerHelper.CalculateDynamicStackCount` | Postfix | How many items are in one offer |
| `RagfairOfferGenerator.CreateOffersFromAssort` | Prefix, postfix | Custom builds, and tracking which offers come from which trader assort entry |
| `RagfairOfferGenerator.CreateSingleOfferForItem` | Prefix | Builds each gun offer in one of the build tiers |
| `RagfairOfferGenerator.CreateAndAddFleaOffer` | Prefix, postfix | Final pricing of each offer, and when it was listed and how long it runs |
| `RagfairOfferGenerator.RandomiseOfferItemUpdProperties` | Postfix | Condition: used keys, meds, food, fuel; worn guns, armour, plates and helmets |
| `RagfairOfferGenerator.CreateBarterBarterScheme` | Prefix | Barter offers that ask for items people actually carry |
| `RagfairOfferHolder.AddOffer` | Prefix, postfix | Offers made on demand, and the memory cap |
| `RagfairOfferHelper.GetValidOffers` | Prefix | Makes the searched items' offers when someone looks, and only checks those |
| `RagfairCategoriesService.GetCategoriesFromOffers` | Prefix | Category counts include offers not made yet |
| `RagfairOfferHelper.PassesSearchFilterCriteria` | Postfix | Hides player offers from players under the flea's level, badges or not |
| `RagfairController.GetOffers` | Postfix | Item unlock levels: marks locked offers on the page, for that player only |
| `RagfairController.GetItemMinAvgMaxFleaPriceValues` | Postfix | The average the game shows when you list becomes Better Flea's normal price |
| `RagfairController.AddPlayerOffer` | Prefix, postfix | Selling rules (off, found in raid, bans, locks, levels) and the listing's context for sale speed and undercuts |
| `RagfairSellHelper.CalculateSellChance` | Prefix | Sale chance by price against normal |
| `RagfairSellHelper.RollForSale` | Prefix, postfix | When buyers come, and how many each takes |
| `RagfairOfferHelper.CompleteOffer` | Prefix | Demand: a lot sold of one item can lower its price |
| `TradeHelper.BuyItem` | Prefix, postfix | Demand from buying, the flip penalty, the 48-hour rule and the trader item lock (three patches) |
| `TradeController.ConfirmRagfairTrading` | Prefix | "Sold before you" |
| `LocationLifecycleService.HandlePostRaidPmc` | Prefix | Remembers guns players die with, for the flea |
| `ProfileActivityService.SetActivityTimestamp` | Postfix | Who is online, for the Wipe Cycle |

## Data and config changed while the server runs

| What | Where | Why |
|---|---|---|
| Flea prices | `templates.Prices` (SPT's `prices.json` in memory) | Base prices from the price index, live prices or your prices |
| Trader cuts | Each trader's `loyaltyLevels[].buy_price_coef` | Live trader prices (not for a trader another mod changed) |
| `CanSellOnRagfair` | Item templates | Lifting BSG's bans properly, keeping never-sold items and SPT's dev items off |
| Flea blacklist | `ragfair.json` `dynamic.blacklist.custom` in memory | Banned categories, items and mod items. Not SPT's item blacklist (`ItemConfig`), which would also change bot loot |
| BSG blacklist switch | `ragfair.json` `dynamic.blacklist.enableBsgList` in memory | The BSG's banned items setting |

## HTTP routes

`/betterflea` (the settings page and its `/betterflea/api/...`), and `/betterflea/item-price`,
`/betterflea/client-data`, `/betterflea/item-levels` and `/betterflea/flip-notice`. See [API](09-api.md).

## Client plugin patches

| Game method | What the plugin does |
|---|---|
| `Item.CanSellOnRagfair`, `CompoundItem.CanSellOnRagfair`, `AmmoBox.CanSellOnRagfair` (getters) | The game, and mods that ask it, see the server's bans, locks and levels |
| `RagFair.CanBeSelectedAtRagfair` | Greys items out in the add-offer screen with Better Flea's reason |
| `RagFair.AddOffer` | Stops a listing the server would refuse with a small warning, quick-sell mods included |
| `ClientBackendSession.ApplyProfileChanges` | After a purchase, asks the server again (something may now be locked) |
| `ItemView.SetQuestItemViewPanel`, `QuestItemViewPanel.Show` | The flea-banned and bought-from-trader icons |
| `ItemSpecificationPanel.RecreateAttributeBars` | The unlock level line in the inspect window |
| `NodeBaseView.OffersCountUpdatedHandler`, `SelectView`, `DeselectView` | The lock and level on flea categories |

## Working alongside Better Flea

- **Reading Better Flea's prices:** call the [item price route](09-api.md#item-price) instead of SPT's price tables.
  It includes everything the flea does.
- **Changing prices yourself:** Better Flea reads base prices through SPT's own `RagfairPriceService.GetFleaPriceForItem`,
  so a price your mod sets in `templates.Prices` after Better Flea's `RagfairPriceService.Load` postfix is the base
  price it uses, with its pipeline on top. Players can also set prices with Your prices (Economy tab).
- **Changing trader cuts:** Better Flea notices cuts another mod changed from SPT's defaults and leaves those traders
  alone, as long as your change is in place when the server loads prices.
- **Adding flea bans:** set `CanSellOnRagfair: false` on your items. Better Flea treats it as your ban.
