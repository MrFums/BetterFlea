using System.Diagnostics;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Flea;

// Which server mod added which items, so they can be banned from the flea by mod. The item list is noted before
// any other mod loads; a new item belongs to the mod whose files carry its id. Nothing in other mods is touched.
[Injectable(TypePriority = OnLoadOrder.Watermark + 1)]
public class ModItemsLoad(ISptLogger<ModItemsLoad> logger, IReadOnlyList<SptMod> mods, TemplateTable templates) : IOnLoad
{
    // Never throws: a fault here must not stop the server or any other mod loading.
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            ModItems.Start(mods, templates, message => logger.Warning(message));
        }
        catch (Exception ex)
        {
            logger.Warning($"[BetterFlea] Mod items: couldn't start ({ex.GetType().Name}: {ex.Message}). Items can't be banned by mod, only one by one or by type.");
        }

        return Task.CompletedTask;
    }
}

internal static class ModItems
{
    private const int IdLength = 24;
    private const long MaxFileBytes = 64L * 1024 * 1024;

    // A mod past any of these is skipped, so one huge or broken mod folder can't hold up the server. Its items
    // can still be banned one by one or by type.
    private const int MaxModFiles = 20000;
    private const long MaxModBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan MaxModTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxTotalTime = TimeSpan.FromSeconds(30);

    private static readonly HashSet<string> Scanned = new(StringComparer.OrdinalIgnoreCase) { ".json", ".jsonc", ".json5", ".dll" };

    private static TemplateTable? _templates;
    private static HashSet<MongoId> _baseItems = [];
    private static Lazy<Dictionary<string, string>> _modById = new(() => new Dictionary<string, string>());
    private static Action<string>? _warn;

    // Mods whose files couldn't be read, and why. Shown on the settings page.
    private static readonly List<(string Mod, string Reason)> SkippedMods = [];
    public static IReadOnlyList<(string Mod, string Reason)> Skipped
    {
        get
        {
            _ = _modById.Value;
            lock (SkippedMods)
            {
                return SkippedMods.ToList();
            }
        }
    }

    public static bool IsBaseItem(MongoId tpl) => _baseItems.Contains(tpl);

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

    public static void Start(IReadOnlyList<SptMod> mods, TemplateTable templates, Action<string> warn)
    {
        _templates = templates;
        _warn = warn;
        _baseItems = templates.Items.Keys.ToHashSet();
        var self = typeof(ModItems).Assembly;
        var others = mods.Where(mod => !mod.Assemblies.Contains(self)).ToList();
        _modById = new Lazy<Dictionary<string, string>>(() => SafeIndex(others), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    // A fault reading mods leaves no items by mod, never an exception for whoever asked.
    private static Dictionary<string, string> SafeIndex(List<SptMod> mods)
    {
        try
        {
            return Index(mods);
        }
        catch (Exception ex)
        {
            Skip("all mods", $"the mod scan failed ({ex.GetType().Name}: {ex.Message})");
            return new Dictionary<string, string>();
        }
    }

    // Every item-like id in each mod's folder. An id found in several mods goes to the one that defines it (the id as
    // a json key: "<id>": {), so JSOC listing Eco's parts in its slots doesn't take them; then to the one that has it
    // in a json file, then to the one that mentions it most. A tie goes to no one: a dll that just names an id (SVM
    // naming WTT's USEC customisation, say) mustn't take another mod's items.
    private static Dictionary<string, string> Index(List<SptMod> mods)
    {
        var counts = new Dictionary<string, Dictionary<string, (int Json, int All, int Defined)>>();
        var total = Stopwatch.StartNew();
        var (readFiles, readBytes) = (0, 0L);
        foreach (var mod in mods)
        {
            var name = mod.ModMetadata?.Name ?? Path.GetFileName(mod.Directory) ?? "unknown mod";
            if (total.Elapsed > MaxTotalTime)
            {
                Skip(name, "the mod scan ran out of time before reaching it");
                continue;
            }

            // Counted on their own first, so a mod skipped part way doesn't leave half its items behind.
            var own = new Dictionary<string, (int Json, int All, int Defined)>();
            var (files, bytes) = (0, 0L);
            var watch = Stopwatch.StartNew();
            string? problem = null;
            try
            {
                foreach (var file in Files(mod.Directory))
                {
                    var data = File.ReadAllBytes(file);
                    files++;
                    bytes += data.Length;
                    if (files > MaxModFiles || bytes > MaxModBytes || watch.Elapsed > MaxModTime)
                    {
                        problem = $"too big to read quickly ({files:N0}+ files, {bytes / 1048576.0:N0} MB, {watch.Elapsed.TotalSeconds:N0} s)";
                        break;
                    }

                    var json = !Path.GetExtension(file).Equals(".dll", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    foreach (var (id, defined) in Ids(data))
                    {
                        var seen = own.GetValueOrDefault(id);
                        own[id] = (seen.Json + json, seen.All + 1, seen.Defined + (defined ? 1 : 0));
                    }
                }
            }
            catch (Exception ex)
            {
                problem = $"its files couldn't be read ({ex.GetType().Name}: {ex.Message})";
            }

            if (problem is not null)
            {
                Skip(name, problem);
                continue;
            }

            readFiles += files;
            readBytes += bytes;
            foreach (var (id, seen) in own)
            {
                if (!counts.TryGetValue(id, out var byMod))
                {
                    counts[id] = byMod = new Dictionary<string, (int Json, int All, int Defined)>();
                }

                byMod[name] = seen;
            }
        }

        DebugLog.Summary(() => $"Mod items: read {readFiles:N0} files ({readBytes / 1048576.0:N0} MB) from {mods.Count} mods in {total.Elapsed.TotalSeconds:N1} s.");

        var baseIds = _baseItems.Select(tpl => tpl.ToString().ToLowerInvariant()).ToHashSet();
        var owners = new Dictionary<string, string>();
        foreach (var (id, byMod) in counts)
        {
            if (!baseIds.Contains(id) && Owner(byMod) is { } owner)
            {
                owners[id] = owner;
            }
        }

        return owners;
    }

    private static string? Owner(Dictionary<string, (int Json, int All, int Defined)> byMod)
    {
        var definers = byMod.Where(entry => entry.Value.Defined > 0).ToList();
        if (definers.Count == 1)
        {
            return definers[0].Key;
        }

        var ranked = byMod.OrderByDescending(entry => entry.Value.Defined > 0).ThenByDescending(entry => entry.Value.Json > 0)
            .ThenByDescending(entry => entry.Value.All).Take(2).ToList();
        if (ranked.Count == 2 && (ranked[0].Value.Defined > 0) == (ranked[1].Value.Defined > 0)
            && (ranked[0].Value.Json > 0) == (ranked[1].Value.Json > 0) && ranked[0].Value.All == ranked[1].Value.All)
        {
            return null;
        }

        return ranked[0].Key;
    }

    private static void Skip(string mod, string reason)
    {
        lock (SkippedMods)
        {
            SkippedMods.Add((mod, reason));
        }

        _warn?.Invoke($"[BetterFlea] Mod items: skipped {mod}: {reason}. Its items can't be banned by mod, only one by one or by type.");
    }

    // Folder links are skipped: one that points back up the tree would be read forever.
    private static IEnumerable<string> Files(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
        };
        return Directory.EnumerateFiles(directory, "*", options)
            .Where(file => Scanned.Contains(Path.GetExtension(file)) && new FileInfo(file).Length <= MaxFileBytes);
    }

    // 24 hex characters in a row, as plain text (json) or as UTF-16 (strings inside a dll). Defined: a json key that
    // opens an object ("<id>": {), the way mods add items.
    private static IEnumerable<(string Id, bool Defined)> Ids(byte[] bytes)
    {
        foreach (var (id, end) in Runs(bytes, 0, 1)) yield return (id, Defines(bytes, end));
        foreach (var (id, _) in Runs(bytes, 0, 2)) yield return (id, false);
        foreach (var (id, _) in Runs(bytes, 1, 2)) yield return (id, false);
    }

    private static bool Defines(byte[] bytes, int end)
    {
        if (end >= bytes.Length || bytes[end] != '"')
        {
            return false;
        }

        var i = end + 1;
        while (i < bytes.Length && char.IsWhiteSpace((char)bytes[i]))
        {
            i++;
        }

        if (i >= bytes.Length || bytes[i] != ':')
        {
            return false;
        }

        i++;
        while (i < bytes.Length && char.IsWhiteSpace((char)bytes[i]))
        {
            i++;
        }

        return i < bytes.Length && bytes[i] == '{';
    }

    // Each id with the index just past it.
    private static IEnumerable<(string Id, int End)> Runs(byte[] bytes, int start, int stride)
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
                yield return (new string(run), i);
            }

            length = 0;
        }

        if (length == IdLength)
        {
            yield return (new string(run), bytes.Length);
        }
    }
}
