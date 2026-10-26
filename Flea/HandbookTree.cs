using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Flea;

// The handbook's categories (Medical supplies > Stimulants and so on), the ones the settings page offers as
// "category". They aren't an item's own types, so a ban or level set on one is looked up here.
internal static class HandbookTree
{
    private const int MaxDepth = 10;

    private static TemplateTable? _templates;
    private static (int Items, Dictionary<MongoId, MongoId> Parents, HashSet<MongoId> Categories) _tree = (-1, new(), new());

    public static void Bind(TemplateTable templates) => _templates = templates;

    public static bool IsCategory(MongoId id) => Tree().Categories.Contains(id);

    // The categories an item is in, nearest first.
    public static IEnumerable<MongoId> CategoriesOf(MongoId tpl)
    {
        var parents = Tree().Parents;
        var id = tpl;
        for (var depth = 0; depth < MaxDepth && parents.TryGetValue(id, out var parent); depth++)
        {
            yield return parent;
            id = parent;
        }
    }

    public static bool InAny(MongoId tpl, HashSet<MongoId> categories) => CategoriesOf(tpl).Any(categories.Contains);

    // Built again when a mod adds items to the handbook.
    private static (int Items, Dictionary<MongoId, MongoId> Parents, HashSet<MongoId> Categories) Tree()
    {
        var tree = _tree;
        if (_templates?.Handbook is not { } handbook || tree.Items == handbook.Items.Count)
        {
            return tree;
        }

        var parents = new Dictionary<MongoId, MongoId>();
        var categories = new HashSet<MongoId>();
        foreach (var category in handbook.Categories)
        {
            categories.Add(category.Id);
            if (category.ParentId is { } parent)
            {
                parents[category.Id] = parent;
            }
        }

        foreach (var item in handbook.Items)
        {
            parents[item.Id] = item.ParentId;
        }

        return _tree = (handbook.Items.Count, parents, categories);
    }
}
