using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Flea;

// Which server mod added which items, so they can be banned from the flea by mod. The item list is noted before
// any other mod loads; a new item belongs to the mod whose files carry its id. Nothing in other mods is touched.
[Injectable(TypePriority = OnLoadOrder.Watermark + 1)]
public class ModItemsLoad(IReadOnlyList<SptMod> mods, TemplateTable templates) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        ModItems.Start(mods, templates);
        return Task.CompletedTask;
    }
}

internal static class ModItems
{
    private const int IdLength = 24;
    private const long MaxFileBytes = 64L * 1024 * 1024;
    private static readonly HashSet<string> Scanned = new(StringComparer.OrdinalIgnoreCase) { ".json", ".jsonc", ".json5", ".dll" };

    private static TemplateTable? _templates;
    private static HashSet<MongoId> _baseItems = [];
    private static Lazy<Dictionary<string, string>> _modById = new(() => new Dictionary<string, string>());

    public static string? ModOf(MongoId tpl) =>
        _baseItems.Contains(tpl) ? null : _modById.Value.GetValueOrDefault(tpl.ToString().ToLowerInvariant());

    public static IReadOnlyDictionary<MongoId, string> All
    {
        get
        {
            var all = new Dictionary<MongoId, string>();
            foreach (var tpl in _templates?.Items.Keys ?? Enumerable.Empty<MongoId>())
            {
                if (ModOf(tpl) is { } mod)
                {
                    all[tpl] = mod;
                }
            }

            return all;
        }
    }

    public static void Start(IReadOnlyList<SptMod> mods, TemplateTable templates)
    {
        _templates = templates;
        _baseItems = templates.Items.Keys.ToHashSet();
        var self = typeof(ModItems).Assembly;
        var others = mods.Where(mod => !mod.Assemblies.Contains(self)).ToList();
        _modById = new Lazy<Dictionary<string, string>>(() => Index(others), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    // Every item-like id in each mod's folder. An id found in several mods goes to the one that mentions it most.
    private static Dictionary<string, string> Index(List<SptMod> mods)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>();
        foreach (var mod in mods)
        {
            var name = mod.ModMetadata.Name ?? Path.GetFileName(mod.Directory);
            foreach (var file in Files(mod.Directory))
            {
                try
                {
                    foreach (var id in Ids(File.ReadAllBytes(file)))
                    {
                        if (!counts.TryGetValue(id, out var byMod))
                        {
                            counts[id] = byMod = new Dictionary<string, int>();
                        }

                        byMod[name] = byMod.GetValueOrDefault(name) + 1;
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Write(() => $"Mod items: can't read {file}: {ex.Message}");
                }
            }
        }

        var baseIds = _baseItems.Select(tpl => tpl.ToString().ToLowerInvariant()).ToHashSet();
        return counts.Where(entry => !baseIds.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value.MaxBy(byMod => byMod.Value).Key);
    }

    private static IEnumerable<string> Files(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(file => Scanned.Contains(Path.GetExtension(file)) && new FileInfo(file).Length <= MaxFileBytes)
                .ToList();
        }
        catch (Exception ex)
        {
            DebugLog.Write(() => $"Mod items: can't list {directory}: {ex.Message}");
            return [];
        }
    }

    // 24 hex characters in a row, as plain text (json) or as UTF-16 (strings inside a dll).
    private static IEnumerable<string> Ids(byte[] bytes)
    {
        foreach (var id in Runs(bytes, 0, 1)) yield return id;
        foreach (var id in Runs(bytes, 0, 2)) yield return id;
        foreach (var id in Runs(bytes, 1, 2)) yield return id;
    }

    private static IEnumerable<string> Runs(byte[] bytes, int start, int stride)
    {
        var run = new char[IdLength];
        var length = 0;
        for (var i = start; i <= bytes.Length - stride; i += stride)
        {
            var c = (char)bytes[i];
            var isHex = (stride == 1 || bytes[i + 1] == 0) && char.IsAsciiHexDigit(c);
            if (isHex)
            {
                if (length < IdLength)
                {
                    run[length] = char.ToLowerInvariant(c);
                }

                length++;
                continue;
            }

            if (length == IdLength)
            {
                yield return new string(run);
            }

            length = 0;
        }

        if (length == IdLength)
        {
            yield return new string(run);
        }
    }
}
