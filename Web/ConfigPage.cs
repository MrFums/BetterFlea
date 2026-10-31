using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers.Http;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Services.Ragfair;
using BetterFlea.Flea;
using BetterFlea.Offers;
using BetterFlea.Prices;
using BetterFlea.Selling;

namespace BetterFlea.Web;

// After everything else has loaded: from then on, saved changes are copied into the parts of SPT that are only
// set at startup. Offers already listed keep the settings they were made with.
[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks)]
public class LiveSettings(
    ISptLogger<LiveSettings> logger,
    ConfigManager configManager,
    FleaCategoryFilter categoryFilter,
    FleaPriceMultipliers fleaPriceMultipliers,
    RagfairConfig ragfairConfig,
    GlobalTable globalTable,
    RagfairOfferService ragfairOfferService,
    RagfairOfferGenerator ragfairOfferGenerator) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        configManager.Changed += Apply;
        FleaRefresh.Bind(ragfairOfferService, ragfairOfferGenerator, message => logger.Info(message));
        configManager.Saved += FleaRefresh.Saved;
        return Task.CompletedTask;
    }

    private void Apply()
    {
        try
        {
            categoryFilter.ApplyLive();
            fleaPriceMultipliers.Apply();
            ArmorOffers.ApplyPlateRemoval(ragfairConfig);
            BetterFleaLoad.ApplyFoundInRaidOnly(configManager, globalTable);
            ListingCountGate.ForgetCaches();
            OnDemandOffers.SettingsChanged();
            OfferLifecycle.SettingsChanged();
            WipeEconomy.SettingsChanged();
            LivePrices.SettingsChanged();
        }
        catch (Exception ex)
        {
            logger.Warning($"[BetterFlea] Settings were saved, but applying them failed: {ex.Message}");
        }
    }
}

// The settings page: https://<server>:6969/betterflea. Anyone can see how the flea is doing; changing settings
// needs the page's own admin login, made by whoever opens the page first.
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload)]
public class ConfigPage(ISptLogger<ConfigPage> logger, ConfigManager configManager, TemplateTable templates,
    LocaleService locales, ItemHelper itemHelper, PresetHelper presetHelper) : IHttpListener
{
    private const string Root = "/betterflea";

    private static readonly Dictionary<string, string> Titles = new()
    {
        ["flea.jsonc"] = "Bans", ["listings.jsonc"] = "Listings", ["condition.jsonc"] = "Condition", ["weapons.jsonc"] = "Weapons",
        ["armor.jsonc"] = "Armour", ["market.jsonc"] = "Market", ["prices.jsonc"] = "Prices", ["selling.jsonc"] = "Selling", ["economy.jsonc"] = "Economy",
        ["debug.jsonc"] = "Debug"
    };

    // Settings only read at startup.
    private static readonly Dictionary<string, string[]> RestartPaths = new()
    {
        ["flea.jsonc"] = ["bannedCategories", "bannedItems", "allowedItems", "enableBsgBlacklist", "bannedModItems", "allowedModItems"],
        ["listings.jsonc"] = ["listings.onDemand.enabled"]
    };

    public bool CanHandle(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        return (path.Equals(Root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase))
            && !path.Equals(ClientDataRoute.Path, StringComparison.OrdinalIgnoreCase)
            && !path.Equals(ClientDataRoute.LevelsPath, StringComparison.OrdinalIgnoreCase)
            && !path.Equals(ItemPriceRoute.Path, StringComparison.OrdinalIgnoreCase)
            && !path.Equals(FlipNoticeRoute.Path, StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(MongoId sessionId, HttpContext context, CancellationToken cancellationToken)
    {
        Language.Value = GameLocales.GetValueOrDefault(context.Request.Headers["X-BetterFlea-Lang"].ToString());
        var web = configManager.Mod.Web;
        if (!web.Enabled)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var path = (context.Request.Path.Value ?? "").TrimEnd('/').ToLowerInvariant();
        if (!await Allowed(context, path))
        {
            return;
        }

        try
        {
            switch (path)
            {
                case Root:
                    // Always asked for again, or a browser keeps showing the page from before an update.
                    context.Response.Headers.CacheControl = "no-cache";
                    await Send(context, "text/html; charset=utf-8", Resource("index.html"), cancellationToken);
                    break;
                case Root + "/icon.png":
                    context.Response.Headers.CacheControl = "max-age=86400";
                    await Send(context, "image/png", Resource("icon.png"), cancellationToken);
                    break;
                case Root + "/api/settings":
                    await SendJson(context, Settings(), cancellationToken);
                    break;
                case Root + "/api/items":
                    await SendJson(context, Items(context.Request.Query["q"].ToString(), context.Request.Query["ids"].ToString()), cancellationToken);
                    break;
                case Root + "/api/status":
                    await SendJson(context, FleaHistory.Status(ItemName), cancellationToken);
                    break;
                case Root + "/api/economy":
                    await SendJson(context, Wipe(context.Request.Query["level"].ToString(), context.Request.Query["id"].ToString()), cancellationToken);
                    break;
                case Root + "/api/chart":
                    await SendJson(context, Chart(context.Request.Query["id"].ToString()), cancellationToken);
                    break;
                case Root + "/api/offers":
                    await SendJson(context, Offers(), cancellationToken);
                    break;
                case Root + "/api/mods":
                    await SendJson(context, Mods(), cancellationToken);
                    break;
                case Root + "/api/notes":
                    await SendJson(context, Notes(), cancellationToken);
                    break;
                case Root + "/api/save" when HttpMethods.IsPost(context.Request.Method):
                    await Save(context, cancellationToken);
                    break;
                case Root + "/api/auth":
                    await SendJson(context, new JsonObject { ["account"] = AdminLogin.HasAccount, ["admin"] = AdminLogin.IsAdmin(context),
                        ["user"] = AdminLogin.IsAdmin(context) ? AdminLogin.User : null }, cancellationToken);
                    break;
                case Root + "/api/overview":
                    await SendJson(context, Overview(), cancellationToken);
                    break;
                case Root + "/api/setup" or Root + "/api/login" when HttpMethods.IsPost(context.Request.Method):
                    await LogIn(context, path == Root + "/api/setup", cancellationToken);
                    break;
                case Root + "/api/logout" when HttpMethods.IsPost(context.Request.Method):
                    AdminLogin.LogOut(context);
                    await SendJson(context, new JsonObject { ["ok"] = true }, cancellationToken);
                    break;
                case var language when LanguageFile.IsMatch(language) && LanguageExists(language):
                    context.Response.Headers.CacheControl = "no-cache";
                    await Send(context, "application/json; charset=utf-8", Resource("i18n." + language[(language.LastIndexOf('/') + 1)..]), cancellationToken);
                    break;
                default:
                    context.Response.StatusCode = 404;
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[BetterFlea] Settings page: {ex.Message}");
            context.Response.StatusCode = 500;
            await SendJson(context, new JsonObject { ["error"] = ex.Message }, cancellationToken);
        }
    }

    private JsonObject Settings()
    {
        var files = new JsonArray();
        foreach (var file in ConfigManager.PageFiles)
        {
            var (about, notes, order) = Describe(configManager.FileText(file));
            files.Add(new JsonObject
            {
                ["file"] = file,
                ["title"] = Titles[file],
                ["about"] = about,
                ["values"] = configManager.FileValues(file),
                ["defaults"] = ConfigManager.FileDefaults(file),
                ["notes"] = notes,
                ["order"] = order,
                ["restart"] = new JsonArray((RestartPaths.GetValueOrDefault(file) ?? []).Select(p => (JsonNode)p).ToArray())
            });
        }

        return new JsonObject { ["version"] = new ModMetadata().Version.ToString(), ["restartNeeded"] = RestartNeeded(), ["files"] = files };
    }

    // The page's language (sent with each request) as the game's locale code; item and category names come from
    // the game's own text in it, English where it has none.
    private static readonly AsyncLocal<string?> Language = new();

    private static readonly Dictionary<string, string> GameLocales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ru"] = "ru", ["zh"] = "ch", ["de"] = "ge", ["fr"] = "fr", ["es"] = "es", ["pt"] = "po", ["pl"] = "pl", ["ja"] = "jp", ["ko"] = "kr"
    };

    private string Localised(string key, string english)
    {
        var language = Language.Value;
        return language is not null && locales.GetLocaleDb(language) is { } local && local.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : english;
    }

    private string ItemName(MongoId tpl)
    {
        var id = tpl.ToString();
        return Localised(id + " Name", (locales.GetLocaleDb("en") ?? []).GetValueOrDefault(id + " Name", id));
    }

    // The Economy tab: live price status, the Wipe Cycle's level, what moves most at a level (?level=30), and one
    // item's price through a wipe (?id=...).
    // What anyone can see without logging in: whether live prices and the wipe cycle are on, and where the wipe is.
    private JsonObject Overview()
    {
        var economy = configManager.Mod.Economy;
        var (_, _, fetched, _, error, _, fallback) = LivePrices.Status();
        return new JsonObject
        {
            ["version"] = new ModMetadata().Version.ToString(),
            ["betterFleaPricing"] = new JsonObject
            {
                ["enabled"] = economy.BetterFleaPricing.Enabled, ["estimateModdedItems"] = economy.BetterFleaPricing.EstimateModdedItems
            },
            ["livePrices"] = new JsonObject
            {
                ["enabled"] = economy.LivePrices.Enabled, ["gameMode"] = economy.LivePrices.GameMode, ["fetched"] = fetched, ["failed"] = error is not null,
                ["fallback"] = fallback,
                ["estimateModdedItems"] = economy.LivePrices.EstimateModdedItems
            },
            ["wipeCycle"] = new JsonObject { ["enabled"] = economy.WipeCycle.Enabled, ["now"] = economy.WipeCycle.Enabled ? Wipe("", "") : null }
        };
    }

    private async Task LogIn(HttpContext context, bool create, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = JsonNode.Parse(await reader.ReadToEndAsync(cancellationToken));
        var user = body?["user"]?.GetValue<string>() ?? "";
        var password = body?["password"]?.GetValue<string>() ?? "";
        var error = create ? AdminLogin.Create(context, user, password)
            : await AdminLogin.LogIn(context, user, password) ? null : "Wrong username or password.";
        if (error is not null)
        {
            context.Response.StatusCode = create ? 400 : 401;
        }

        await SendJson(context, error is null ? new JsonObject { ["ok"] = true } : new JsonObject { ["error"] = error }, cancellationToken);
    }

    private JsonObject Wipe(string level, string id)
    {
        var at = int.TryParse(level, out var asked) && asked > 0 ? asked : Math.Max(1, WipeEconomy.Level);
        var movers = new JsonArray();
        foreach (var (tpl, factor) in WipeEconomy.Movers(at, 12))
        {
            movers.Add(new JsonObject { ["name"] = ItemName(tpl), ["factor"] = Math.Round(factor, 3) });
        }

        var result = new JsonObject
        {
            ["level"] = WipeEconomy.Level,
            ["target"] = WipeEconomy.Target,
            ["nextChangeAt"] = WipeEconomy.NextChangeAt,
            ["previewLevel"] = at,
            ["market"] = Math.Round(WipeEconomy.MarketFactor(at), 3),
            ["movers"] = movers
        };

        if (MongoId.IsValidMongoId(id))
        {
            result["curve"] = new JsonArray(Enumerable.Range(1, 70).Select(l => (JsonNode)Math.Round(WipeEconomy.FactorAt(new MongoId(id), l), 3)).ToArray());
            var tpl = new MongoId(id);
            result["basePrice"] = Math.Round(templates.Prices.GetValueOrDefault(tpl));
            // One new item, priced like the flea's offers: every multiplier, drift, the Wipe Cycle and the floor. A gun
            // on its own is only the gun's base part, so it's priced as its default build, the way the flea lists it.
            var build = presetHelper.GetDefaultPresets().Values.FirstOrDefault(preset => preset.Encyclopedia == tpl)?.Items;
            var fleaPrice = BetterFlea.Selling.SaleDelayGate.NormalPrice(build is { Count: > 1 } ? build : [new Item { Id = new MongoId(), Template = tpl }]);
            result["fleaPrice"] = fleaPrice is { } f ? Math.Round(f) : null;
            result["stockBuild"] = build is { Count: > 1 };
            result["odds"] = Odds(build is { Count: > 1 } ? build : [new Item { Id = new MongoId(), Template = tpl }]);
            var lookup = LivePrices.Lookup(tpl);
            result["sptPrice"] = lookup.Spt is { } s ? Math.Round(s) : null;
            result["livePrice"] = lookup.Live;
            result["liveIgnored"] = lookup.LiveIgnored;
            result["indexPrice"] = lookup.Index;
            result["baseGame"] = lookup.BaseGame;
            result["ownPrice"] = lookup.Override;
        }

        var (indexOn, indexPrices, built, indexEstimated) = LivePrices.IndexStatus();
        result["index"] = new JsonObject
        {
            ["on"] = indexOn, ["prices"] = indexPrices, ["built"] = built, ["estimated"] = indexEstimated,
            ["differences"] = new JsonArray(LivePrices.Differences(8, index: true).Select(entry => (JsonNode)new JsonObject
            {
                ["name"] = ItemName(entry.Tpl), ["spt"] = Math.Round(entry.Spt), ["live"] = entry.Live
            }).ToArray())
        };
        var (on, prices, fetched, modes, error, estimated, fallback) = LivePrices.Status();
        result["live"] = new JsonObject
        {
            ["on"] = on, ["prices"] = prices, ["fetched"] = fetched, ["error"] = error, ["fallback"] = fallback, ["estimated"] = estimated,
            ["modes"] = new JsonArray(modes.Select(mode => (JsonNode)mode).ToArray()),
            ["differences"] = new JsonArray(LivePrices.Differences(8).Select(entry => (JsonNode)new JsonObject
            {
                ["name"] = ItemName(entry.Tpl), ["spt"] = Math.Round(entry.Spt), ["live"] = entry.Live
            }).ToArray())
        };
        return result;
    }

    // How a player's offer of these items sells at a range of prices (BetterFlea.Selling.SaleOdds). Null with sale
    // delays off.
    private JsonObject? Odds(List<Item> items)
    {
        if (BetterFlea.Selling.SaleOdds.For(items, itemHelper) is not { } table)
        {
            return null;
        }

        return new JsonObject
        {
            ["normal"] = table.Normal, ["wrecked"] = table.Wrecked, ["plain"] = table.Plain, ["hours"] = table.Hours,
            ["rows"] = new JsonArray(table.Rows.Select(row => (JsonNode)new JsonObject
            {
                ["ratio"] = row.Ratio, ["price"] = row.Price, ["sells"] = Math.Round(row.Sells, 3),
                ["minutes"] = row.MedianMinutes is { } m ? Math.Round(m) : null
            }).ToArray())
        };
    }

    // One item's stock chart: hourly candles of its normal price with sales volume (PriceHistory), every player's
    // offers of it that are up, and players' sales of it, for the page to show all or one player's.
    private JsonObject Chart(string id)
    {
        if (!MongoId.IsValidMongoId(id))
        {
            return new JsonObject { ["error"] = "No item." };
        }

        var tpl = new MongoId(id);
        var candles = new JsonArray(PriceHistory.Candles(tpl).Select(candle => (JsonNode)new JsonArray(candle.Time,
            Math.Round(candle.Open), Math.Round(candle.High), Math.Round(candle.Low), Math.Round(candle.Close), candle.Volume)).ToArray());
        var listings = new JsonArray(BetterFlea.Selling.OrderBook.AllPlayerOffers()
            .Where(entry => entry.Offer.Items![0].Template == tpl)
            .Select(entry => (JsonNode)new JsonObject
            {
                ["player"] = entry.Player, ["price"] = entry.Offer.RequirementsCost is { } cost ? Math.Round(cost) : null,
                ["quantity"] = entry.Offer.Quantity, ["listedAt"] = entry.Offer.StartTime, ["endsAt"] = entry.Offer.EndTime
            }).ToArray());
        var sales = new JsonArray(PriceHistory.Sales(tpl).Select(sale => (JsonNode)new JsonObject
        {
            ["player"] = sale.Name, ["price"] = sale.Price, ["amount"] = sale.Amount, ["listedAt"] = sale.ListedAt, ["soldAt"] = sale.SoldAt
        }).ToArray());
        return new JsonObject
        {
            ["name"] = ItemName(tpl), ["candles"] = candles, ["listings"] = listings, ["sales"] = sales,
            ["players"] = new JsonArray(BetterFlea.Selling.OrderBook.PlayerNames().Select(name => (JsonNode)name).ToArray()),
            ["now"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["sampleMs"] = PriceHistory.LastSampleMs
        };
    }

    // Every player's offers that are up: what each is, its price against its normal, when it sells if a buyer is
    // already lined up, and its odds at other prices.
    private JsonObject Offers()
    {
        var list = new JsonArray();
        foreach (var (player, offer) in BetterFlea.Selling.OrderBook.AllPlayerOffers().Take(30))
        {
            var items = offer.Items!;
            var odds = Odds(items);
            var sale = offer.SellResults?.ToList().Where(result => result.SellTime is not null).Min(result => result.SellTime);
            list.Add(new JsonObject
            {
                ["player"] = player, ["name"] = ItemName(items[0].Template), ["price"] = offer.RequirementsCost is { } cost ? Math.Round(cost) : null,
                ["quantity"] = offer.Quantity, ["endsAt"] = offer.EndTime, ["sellsAt"] = sale, ["odds"] = odds
            });
        }

        return new JsonObject { ["offers"] = list, ["now"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
    }

    // Server mods that add items: how many, and of which types (the bannedCategories names). Items that can never be
    // on the flea (SVM's pockets, say) are left out, so a mod that adds only those isn't listed.
    private JsonObject Mods()
    {
        var mods = new JsonArray();
        // Only what the flea could ever have: items in the handbook. Pockets and the like aren't.
        var listable = templates.Handbook.Items.Select(entry => entry.Id).ToHashSet();
        var all = ModItems.All
            .Where(entry => listable.Contains(entry.Key))
            .Select(entry => (Tpl: entry.Key, Item: templates.Items.GetValueOrDefault(entry.Key), Mod: entry.Value))
            .Where(entry => entry.Item is not null);
        JsonArray Types(IEnumerable<(MongoId Tpl, TemplateItem? Item, string Mod)> items) => new(items
            .GroupBy(entry => FleaCategories.Find(entry.Item!, itemHelper)?.Name ?? FleaCategories.Uncategorised)
            .OrderByDescending(type => type.Count())
            .Select(type => (JsonNode)new JsonObject { ["name"] = type.Key, ["items"] = type.Count() }).ToArray());
        foreach (var group in all.GroupBy(entry => entry.Mod).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            // Sellable: what can be banned. Flea-banned: what the mod itself keeps off the flea, which can be allowed.
            var sellable = group.Where(entry => entry.Item!.Properties?.CanSellOnRagfair is not false).ToList();
            var banned = group.Where(entry => FleaCategoryFilter.WasFleaBanned(entry.Tpl)).ToList();
            mods.Add(new JsonObject
            {
                ["name"] = group.Key,
                ["items"] = sellable.Count,
                ["types"] = Types(sellable),
                ["banned"] = banned.Count,
                ["bannedTypes"] = Types(banned)
            });
        }

        var skipped = new JsonArray(ModItems.Skipped
            .Select(entry => (JsonNode)new JsonObject { ["name"] = entry.Mod, ["reason"] = entry.Reason }).ToArray());
        return new JsonObject { ["mods"] = mods, ["skipped"] = skipped };
    }

    // This version's part of the changelog, as markdown.
    private JsonObject Notes()
    {
        var version = new ModMetadata().Version.ToString();
        var text = Encoding.UTF8.GetString(Resource("CHANGELOG.md")).Replace("\r\n", "\n");
        // A beta (2.4.0-beta.1) shows its release's section, "## 2.4.0 (beta)".
        var heading = $"## {version.Split('-')[0]}";
        var start = text.IndexOf($"{heading}\n", StringComparison.Ordinal);
        start = start >= 0 ? start : text.IndexOf($"{heading} ", StringComparison.Ordinal);
        var section = "";
        if (start >= 0)
        {
            start = text.IndexOf('\n', start) + 1;
            var end = text.IndexOf("\n## ", start, StringComparison.Ordinal);
            section = (end < 0 ? text[start..] : text[start..end]).Trim();
        }

        return new JsonObject { ["version"] = version, ["notes"] = section };
    }

    private List<(string Id, string Name, string Short, bool Category)>? _catalogue;

    // Everything that can go on the flea list, by English name: handbook items and handbook categories.
    private List<(string Id, string Name, string Short, bool Category)> Catalogue()
    {
        if (_catalogue is not null)
        {
            return _catalogue;
        }

        var english = locales.GetLocaleDb("en") ?? new Dictionary<string, string>();
        var list = new List<(string Id, string Name, string Short, bool Category)>();
        foreach (var category in templates.Handbook.Categories)
        {
            var id = category.Id.ToString();
            list.Add((id, english.GetValueOrDefault(id, id), "", true));
        }

        foreach (var item in templates.Handbook.Items)
        {
            var id = item.Id.ToString();
            list.Add((id, english.GetValueOrDefault(id + " Name", id), english.GetValueOrDefault(id + " ShortName", ""), false));
        }

        return _catalogue = list;
    }

    private string LocalName((string Id, string Name, string Short, bool Category) entry) =>
        Localised(entry.Category ? entry.Id : entry.Id + " Name", entry.Name);

    // ?q=ledx finds items and categories by name or short name (English or the page's language); ?ids=a,b gives the
    // names of those ids.
    private JsonObject Items(string query, string ids)
    {
        var catalogue = Catalogue();
        var found = new JsonArray();
        IEnumerable<(string Id, string Name, string Short, bool Category)> matches;
        if (ids.Length > 0)
        {
            var wanted = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            matches = catalogue.Where(entry => wanted.Contains(entry.Id));
        }
        else
        {
            query = query.Trim();
            matches = query.Length < 2
                ? []
                : catalogue.Where(entry => entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Short.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Id.Equals(query, StringComparison.OrdinalIgnoreCase)
                        || Language.Value is not null && LocalName(entry).Contains(query, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(entry => entry.Category ? 0 : 1)
                    .ThenBy(entry => entry.Name.Length)
                    .Take(20);
        }

        foreach (var (id, name, _, category) in matches)
        {
            var entry = new JsonObject { ["id"] = id, ["name"] = LocalName((id, name, "", category)), ["category"] = category };
            // For Your prices: what the item costs without the admin's own price.
            if (!category && MongoId.IsValidMongoId(id))
            {
                entry["usual"] = Math.Round(LivePrices.UsualPrice(new MongoId(id)));
            }

            found.Add(entry);
        }

        return new JsonObject { ["items"] = found };
    }

    private async Task Save(HttpContext context, CancellationToken cancellationToken)
    {
        var file = context.Request.Query["file"].ToString();
        if (!ConfigManager.PageFiles.Contains(file))
        {
            context.Response.StatusCode = 400;
            await SendJson(context, new JsonObject { ["error"] = "Unknown settings file." }, cancellationToken);
            return;
        }

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var values = JsonNode.Parse(await reader.ReadToEndAsync(cancellationToken));
        var error = values is JsonObject ? configManager.Save(file, values) : "Expected the file's settings.";
        if (error is not null)
        {
            context.Response.StatusCode = 400;
            await SendJson(context, new JsonObject { ["error"] = error }, cancellationToken);
            return;
        }

        logger.Info($"[BetterFlea] Settings saved from the settings page: configs/{file}");
        await SendJson(context, new JsonObject
        {
            ["values"] = configManager.FileValues(file), ["restartNeeded"] = RestartNeeded(), ["remaking"] = FleaRefresh.Remakes(file)
        }, cancellationToken);
    }

    // The restart-only settings as they were when this server started (taken before the first save).
    private Dictionary<string, string>? _startedWith;

    private string RestartValues(string file)
    {
        var values = configManager.FileValues(file);
        return string.Join("|", RestartPaths[file].Select(path => path.Split('.').Aggregate((JsonNode?)values, (node, key) => node?[key])?.ToJsonString()));
    }

    private bool RestartNeeded()
    {
        _startedWith ??= RestartPaths.Keys.ToDictionary(file => file, RestartValues);
        return _startedWith.Any(entry => RestartValues(entry.Key) != entry.Value);
    }

    // Anyone can see the flea's stats and look items up; the settings need the admin login (AdminLogin).
    private static readonly HashSet<string> OpenPaths = new(StringComparer.Ordinal)
    {
        Root, Root + "/icon.png", Root + "/api/status", Root + "/api/notes", Root + "/api/overview", Root + "/api/items", Root + "/api/economy", Root + "/api/offers", Root + "/api/chart",
        Root + "/api/auth", Root + "/api/setup", Root + "/api/login", Root + "/api/logout"
    };

    private static async Task<bool> Allowed(HttpContext context, string path)
    {
        if (OpenPaths.Contains(path) || LanguageFile.IsMatch(path) || AdminLogin.IsAdmin(context))
        {
            return true;
        }

        context.Response.StatusCode = 401;
        await SendJson(context, new JsonObject { ["error"] = "Log in as the admin first." }, context.RequestAborted);
        return false;
    }
    private static readonly Regex KeyLine = new("^\\s*\"([^\"]+)\"\\s*:", RegexOptions.Compiled);
    private static readonly Regex TermLine = new("^(?!(?:true|false|null)\\b)([a-z][A-Za-z0-9]*)\\s*[=:]\\s+(.+)$", RegexOptions.Compiled);

    // From a settings file's text: the line under its title, a note for each setting and the order they're written in.
    // A setting's note is the comment on its own line. Comment blocks above a group often explain several settings
    // at once ("usedChance = ..."): each of those goes to the setting it names, in that group and the ones after it,
    // and the rest of the block describes the group.
    private static (string About, JsonObject Notes, JsonArray Order) Describe(string text)
    {
        var notes = new JsonObject();
        var order = new JsonArray();
        var path = new List<string>();
        var general = new List<string>();
        var terms = new Dictionary<string, string>();
        var scopes = new List<Dictionary<string, string>?>();
        string? lastTerm = null;
        var about = "";
        var started = false;

        void ClearBlock()
        {
            general.Clear();
            terms = new Dictionary<string, string>();
            lastTerm = null;
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (!started)
            {
                if (line.StartsWith('{'))
                {
                    started = true;
                }
                else if (about.Length == 0 && line.StartsWith("//"))
                {
                    var title = line[2..].Trim();
                    var colon = title.IndexOf(':');
                    about = colon >= 0 ? title[(colon + 1)..].Trim() : "";
                }

                continue;
            }

            if (line.StartsWith("//"))
            {
                var content = line[2..];
                var trimmed = content.Trim();
                var term = TermLine.Match(trimmed);
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (term.Success)
                {
                    terms[term.Groups[1].Value] = term.Groups[2].Value.Trim();
                    lastTerm = term.Groups[1].Value;
                }
                else if (lastTerm is not null && content.StartsWith("   "))
                {
                    terms[lastTerm] += " " + trimmed;
                }
                else
                {
                    general.Add(trimmed);
                    lastTerm = null;
                }

                continue;
            }

            if (line.Length == 0)
            {
                ClearBlock();
                continue;
            }

            var (code, comment) = SplitComment(line);
            var match = KeyLine.Match(code);
            if (match.Success)
            {
                var name = match.Groups[1].Value;
                var depth = path.Count;
                while (scopes.Count > depth + 1)
                {
                    scopes.RemoveAt(scopes.Count - 1);
                }

                while (scopes.Count < depth + 1)
                {
                    scopes.Add(null);
                }

                if (terms.Count > 0)
                {
                    scopes[depth] = terms;
                }

                var own = string.Join(" ", general.Where(part => !(terms.Count > 0 && part.EndsWith(':'))).Append(comment).Where(part => part.Length > 0));
                var note = own.Length > 0 ? own : NamedNote(scopes, depth, name);
                var key = string.Join(".", path.Append(name));
                if (!string.IsNullOrEmpty(note))
                {
                    notes[key] = note;
                }

                order.Add(key);
                ClearBlock();
                if (Depth(code[match.Length..]) > 0)
                {
                    path.Add(name);
                }

                continue;
            }

            ClearBlock();
            for (var closed = -Depth(code); closed > 0 && path.Count > 0; closed--)
            {
                path.RemoveAt(path.Count - 1);
            }
        }

        return (about, notes, order);
    }

    private static string? NamedNote(List<Dictionary<string, string>?> scopes, int depth, string name)
    {
        for (var level = Math.Min(depth, scopes.Count - 1); level >= 0; level--)
        {
            if (scopes[level]?.TryGetValue(name, out var note) == true)
            {
                return note;
            }
        }

        return null;
    }
    // Opened minus closed braces and brackets, outside strings.
    private static int Depth(string code)
    {
        var depth = 0;
        var inString = false;
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '"' && (i == 0 || code[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString)
            {
                depth += c is '{' or '[' ? 1 : c is '}' or ']' ? -1 : 0;
            }
        }

        return depth;
    }

    private static (string Code, string Comment) SplitComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && line[i] == '/' && line[i + 1] == '/')
            {
                return (line[..i].TrimEnd(), line[(i + 2)..].Trim());
            }
        }

        return (line, "");
    }

    private static readonly Dictionary<string, byte[]> Resources = new();

    // The settings page in another language (Web/i18n): /betterflea/i18n/ru.json.
    private static readonly Regex LanguageFile = new("^" + Regex.Escape(Root) + "/i18n/[a-z]{2}\\.json$", RegexOptions.Compiled);

    private static bool LanguageExists(string path) =>
        Assembly.GetExecutingAssembly().GetManifestResourceInfo("BetterFlea.web.i18n." + path[(path.LastIndexOf('/') + 1)..]) is not null;

    private static byte[] Resource(string name)
    {
        lock (Resources)
        {
            if (!Resources.TryGetValue(name, out var bytes))
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"BetterFlea.web.{name}")
                    ?? throw new FileNotFoundException(name);
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                Resources[name] = bytes = copy.ToArray();
            }

            return bytes;
        }
    }

    private static async Task Send(HttpContext context, string type, byte[] body, CancellationToken cancellationToken)
    {
        context.Response.ContentType = type;
        await context.Response.Body.WriteAsync(body, cancellationToken);
    }

    private static Task SendJson(HttpContext context, JsonNode body, CancellationToken cancellationToken)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(body.ToJsonString(), cancellationToken);
    }
}
