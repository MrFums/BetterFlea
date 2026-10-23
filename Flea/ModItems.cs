using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Flea;

// Which server mod added which items, so they can be banned from the flea by mod. Each mod's load steps are
// watched, and new items that appear while one runs belong to that mod. Runs before any other mod loads.
[Injectable(TypePriority = OnLoadOrder.Watermark + 1)]
public class ModItemsLoad(IReadOnlyList<SptMod> mods, TemplateTable templates) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        ModItems.Watch(mods, templates);
        return Task.CompletedTask;
    }
}

internal static class ModItems
{
    private static TemplateTable? _templates;
    private static readonly Dictionary<Assembly, string> ModByAssembly = new();
    private static readonly ConcurrentDictionary<MongoId, string> ModByItem = new();

    public static string? ModOf(MongoId tpl) => ModByItem.GetValueOrDefault(tpl);

    public static IReadOnlyDictionary<MongoId, string> All => ModByItem;

    public static void Watch(IReadOnlyList<SptMod> mods, TemplateTable templates)
    {
        _templates = templates;
        var self = typeof(ModItems).Assembly;
        var patched = new HashSet<MethodBase>();
        foreach (var mod in mods)
        {
            foreach (var assembly in mod.Assemblies.Where(assembly => assembly != self))
            {
                ModByAssembly[assembly] = mod.ModMetadata.Name;
                foreach (var type in LoadSteps(assembly))
                {
                    // A shared base class can carry the method for several mods: patched once, credited by instance.
                    var method = type.GetInterfaceMap(typeof(IOnLoad)).TargetMethods.FirstOrDefault();
                    if (method is null || method.IsAbstract || !patched.Add(method))
                    {
                        continue;
                    }

                    try
                    {
                        new LoadStepPatch(method).Enable();
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Write(() => $"Mod items: can't watch {type.FullName}: {ex.Message}");
                    }
                }
            }
        }
    }

    private static IEnumerable<Type> LoadSteps(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        return types.OfType<Type>().Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IOnLoad).IsAssignableFrom(type));
    }

    public static HashSet<MongoId> Snapshot() => _templates?.Items.Keys.ToHashSet() ?? [];

    public static void Credit(HashSet<MongoId> before, Type step)
    {
        if (_templates is null || !ModByAssembly.TryGetValue(step.Assembly, out var mod))
        {
            return;
        }

        foreach (var tpl in _templates.Items.Keys)
        {
            if (!before.Contains(tpl))
            {
                ModByItem.TryAdd(tpl, mod);
            }
        }
    }
}

public class LoadStepPatch(MethodBase target) : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => target;

    [PatchPrefix]
    public static void Prefix(out HashSet<MongoId> __state)
    {
        __state = ModItems.Snapshot();
    }

    [PatchPostfix]
    public static void Postfix(object __instance, HashSet<MongoId> __state, ref Task __result)
    {
        var step = __instance.GetType();
        if (__result.IsCompleted)
        {
            ModItems.Credit(__state, step);
            return;
        }

        var before = __state;
        __result = __result.ContinueWith(task =>
        {
            ModItems.Credit(before, step);
            return task;
        }, TaskScheduler.Default).Unwrap();
    }
}
