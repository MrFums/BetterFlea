using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Flea;

// Only handbook items can reach the flea. Banned items go on the ragfair custom blacklist.
// ItemConfig.Blacklist is left alone, otherwise bot/PMC loot changes too.
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
        var allowed = ToMongoIdSet(config.AllowedItems, "allowedItems");
        var banned = ToMongoIdSet(config.BannedItems, "bannedItems");

        ragfairConfig.Dynamic.Blacklist.EnableBsgList = config.EnableBsgBlacklist;

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
            .Where(id => !items.ContainsKey(id))
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

    // true = force allowed, false = ban, null = leave as is.
    // A direct TPL beats a category ID; allowed beats banned.
    private bool? Decide(TemplateItem item, HashSet<MongoId> allowed, HashSet<MongoId> banned, out string reason)
    {
        var tpl = item.Id;
        reason = "bannedItems";

        if (allowed.Contains(tpl)) return true;
        if (banned.Contains(tpl)) return false;
        if (allowed.Count > 0 && itemHelper.IsOfBaseclasses(tpl, allowed)) return true;
        if (banned.Count > 0 && itemHelper.IsOfBaseclasses(tpl, banned)) return false;

        var category = FleaCategories.Find(item, itemHelper);
        if (category is not null && category.IsBanned(configManager.Mod.Flea.BannedCategories))
        {
            reason = category.Name;
            return false;
        }

        return null;
    }

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
