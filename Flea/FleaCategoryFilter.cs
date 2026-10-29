using BetterFlea.Offers;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Flea;

// Bans go on the ragfair blacklist, not ItemConfig.Blacklist (that would change bot loot too).
[Injectable(InjectionType.Singleton)]
public class FleaCategoryFilter(
    ISptLogger<FleaCategoryFilter> logger,
    ConfigManager configManager,
    TemplateTable templates,
    RagfairConfig ragfairConfig,
    ItemHelper itemHelper)
{
    public void Apply()
    {
        var config = configManager.Mod.Flea;
        var items = templates.Items;
        HandbookTree.Bind(templates);
        RememberFleaBanned(items);
        var allowed = ToMongoIdSet(config.AllowedItems, "allowedItems");
        var banned = ToMongoIdSet(config.BannedItems, "bannedItems");

        ragfairConfig.Dynamic.Blacklist.EnableBsgList = config.EnableBsgBlacklist;
        ApplyLive();

        var bannedPerCategory = new Dictionary<string, int>();
        var forced = 0;
        var blocked = 0;

        foreach (var entry in templates.Handbook.Items)
        {
            if (!items.TryGetValue(entry.Id, out var item) || item.Properties is null)
            {
                continue;
            }

            bool? decision = Decide(item, allowed, banned, out var reason);
            if (decision is null)
            {
                continue;
            }

            if (decision.Value)
            {
                item.Properties.CanSellOnRagfair = true;
                ragfairConfig.Dynamic.Blacklist.Custom.Remove(entry.Id);
                forced++;
            }
            else
            {
                ragfairConfig.Dynamic.Blacklist.Custom.Add(entry.Id);
                bannedPerCategory[reason] = bannedPerCategory.GetValueOrDefault(reason) + 1;
                blocked++;
            }
        }

        var missing = allowed.Concat(banned)
            .Where(id => !items.ContainsKey(id) && !HandbookTree.IsCategory(id))
            .Select(id => id.ToString())
            .Distinct()
            .ToList();

        if (missing.Count > 0)
        {
            logger.Warning($"[BetterFlea] {missing.Count} ID(s) not found in item DB: {string.Join(", ", missing)}");
        }

        var breakdown = string.Join(", ", bannedPerCategory.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}"));
        DebugLog.Summary(() =>
            $"Flea filter applied: {blocked} banned, {forced} forced allowed " +
            $"(bsgBlacklist={config.EnableBsgBlacklist})" + (blocked > 0 ? $" - {breakdown}" : string.Empty));
    }

    // The parts that can change while the server runs. Bans are only applied at startup.
    public void ApplyLive()
    {
        ApplyOfferCurrency();
        ApplyOfferExpiry();
        ApplySpread();
        ApplyPacks();
        ragfairConfig.Dynamic.ShowDefaultPresetsOnly = !configManager.Mod.GeneratedOffers.Presets.ShowCustomBuilds;
    }

    private void ApplyOfferExpiry()
    {
        var expiry = configManager.Mod.GeneratedOffers.OfferExpiry;
        var min = Math.Max(1, expiry.MinMinutes);
        var max = Math.Max(min, expiry.MaxMinutes);
        ragfairConfig.Dynamic.EndTimeSeconds = new MinMax<int>(min * 60, max * 60);
    }

    private MinMax<double>? _sptSpread;

    // How far an ordinary offer strays from normal. Before ApplyPacks: packs are priced from the same range.
    private void ApplySpread()
    {
        var spread = configManager.Mod.Prices.Spread;
        var ranges = ragfairConfig.Dynamic.PriceRanges;
        _sptSpread ??= ranges.Default;
        if (!spread.Enabled)
        {
            ranges.Default = _sptSpread;
            return;
        }

        var low = 1 + Math.Clamp(spread.MinPercent, -50, 0) / 100;
        var high = Math.Max(low, 1 + Math.Clamp(spread.MaxPercent, 0, 100) / 100);
        ranges.Default = new MinMax<double>(low, high);
    }

    // SPT's own pack settings, put back when ours are switched off.
    private (double Chance, int Min, int Max, MinMax<double> Prices)? _sptPacks;

    // SPT's pack offers (several items you have to buy together).
    private void ApplyPacks()
    {
        var packs = configManager.Mod.GeneratedOffers.Packs;
        var pack = ragfairConfig.Dynamic.Pack;
        var ranges = ragfairConfig.Dynamic.PriceRanges;
        _sptPacks ??= (pack.ChancePercent, pack.ItemCountMin, pack.ItemCountMax, ranges.Pack);

        if (!packs.Enabled)
        {
            (pack.ChancePercent, pack.ItemCountMin, pack.ItemCountMax, ranges.Pack) = _sptPacks.Value;
            return;
        }

        pack.ChancePercent = Math.Clamp(packs.ChancePercent, 0, 100);
        pack.ItemCountMin = Math.Max(2, packs.Min);
        pack.ItemCountMax = Math.Max(pack.ItemCountMin, packs.Max);

        // SPT discounts packs (0.75-0.96 of normal). Ours are priced from the normal range instead, then made a
        // scam or a fair bundle (SellerPricing).
        ranges.Pack = ranges.Default;
    }

    private void ApplyOfferCurrency()
    {
        var weights = configManager.Mod.GeneratedOffers.OfferCurrency;
        var roubles = Math.Max(0, weights.Roubles);
        var dollars = Math.Max(0, weights.Dollars);
        var euros = Math.Max(0, weights.Euros);

        // SPT fails with nothing to pick from.
        if (roubles + dollars + euros <= 0)
        {
            roubles = 100;
        }

        ragfairConfig.Dynamic.OfferCurrencyChangePercent = new Dictionary<MongoId, double>
        {
            [new MongoId("5449016a4bdc2d6f028b456f")] = roubles,
            [new MongoId("5696686a4bdc2da3298b456a")] = dollars,
            [new MongoId("569668774bdc2da2298b4568")] = euros
        };
    }

    // true = allow, false = ban, null = leave. A tpl beats a category; allowed beats banned. A category is a handbook
    // one (what the settings page offers) or an item type.
    private bool? Decide(TemplateItem item, HashSet<MongoId> allowed, HashSet<MongoId> banned, out string reason)
    {
        var tpl = item.Id;
        reason = "bannedItems";

        if (allowed.Contains(tpl)) return true;
        if (banned.Contains(tpl)) return false;
        if (allowed.Count > 0 && (HandbookTree.InAny(tpl, allowed) || itemHelper.IsOfBaseclasses(tpl, allowed))) return true;
        if (banned.Count > 0 && (HandbookTree.InAny(tpl, banned) || itemHelper.IsOfBaseclasses(tpl, banned))) return false;

        var category = FleaCategories.Find(item, itemHelper);
        var flea = configManager.Mod.Flea;
        // Only asked when a mod setting is used, or BSG's bans are off: finding which mod added an item reads every
        // mod's files.
        var mod = flea.BannedModItems.Count > 0 || flea.AllowedModItems.Count > 0 || (!flea.EnableBsgBlacklist && WasFleaBanned(tpl))
            ? ModItems.ModOf(tpl) : null;

        // A mod's own flea ban, lifted. Allowed beats banned, as above.
        if (mod is not null && WasFleaBanned(tpl) && ModListHas(flea.AllowedModItems, mod, category?.Name))
        {
            return true;
        }

        // Turning off BSG's bans lifts the game's own, not a mod's: SPT's switch covers both, so a mod's stays here.
        // WTT ContentBackport's items are the game's, with the game's bans.
        if (mod is not null && !flea.EnableBsgBlacklist && WasFleaBanned(tpl)
            && !mod.Contains("ContentBackport", StringComparison.OrdinalIgnoreCase))
        {
            reason = "mod " + mod + " (its own ban)";
            return false;
        }

        if (mod is not null && ModListHas(flea.BannedModItems, mod, category?.Name))
        {
            reason = "mod " + mod;
            return false;
        }
        if (category is not null && category.IsBanned(configManager.Mod.Flea.BannedCategories))
        {
            reason = category.Name;
            return false;
        }

        return null;
    }

    // What the game data and other mods ban from the flea, as it stood before Better Flea's own bans and allows.
    // Taken once: bans are only applied at startup.
    private static HashSet<MongoId>? _fleaBanned;

    public static bool WasFleaBanned(MongoId tpl) => _fleaBanned?.Contains(tpl) == true;

    private void RememberFleaBanned(Dictionary<MongoId, TemplateItem> items)
    {
        _fleaBanned ??= items.Where(entry => entry.Value.Properties?.CanSellOnRagfair == false)
            .Select(entry => entry.Key)
            .Concat(ragfairConfig.Dynamic.Blacklist.Custom)
            .ToHashSet();
    }

    private static bool ModListHas(Dictionary<string, List<string>> byMod, string mod, string? category) =>
        byMod.TryGetValue(mod, out var types)
        && types.Any(type => type.Equals("all", StringComparison.OrdinalIgnoreCase)
            || type.Equals(category ?? FleaCategories.Uncategorised, StringComparison.OrdinalIgnoreCase));

    private HashSet<MongoId> ToMongoIdSet(IEnumerable<string> raw, string section)
    {
        var set = new HashSet<MongoId>();
        foreach (var s in raw)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                continue;
            }

            if (!MongoId.IsValidMongoId(s))
            {
                logger.Warning($"[BetterFlea] Ignoring invalid ID in {section}: '{s}'");
                continue;
            }

            set.Add(new MongoId(s));
        }

        return set;
    }
}
