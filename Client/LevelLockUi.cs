using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT.HandBook;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Ragfair;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFlea.Client.Patches;

// Like live: the item's inspect window says which level it unlocks at on the flea, and flea categories show a lock
// with the level they open at. Both come from the server's level lock (modded items included).
internal static class LevelLockUi
{
    private const string BadgeName = "BetterFleaLevel";
    private const string HighlightName = "BetterFleaHighlight";

    private static readonly Color LockColor = new Color32(104, 196, 214, 255);
    private static readonly Color HighlightColor = new Color32(156, 98, 20, 150);

    private static Sprite _lockSprite;
    private static bool _lookedForSprite;

    // A lock icon from the game's own UI (the flea's "LOCKED" button).
    public static Sprite LockSprite
    {
        get
        {
            if (_lockSprite != null || _lookedForSprite)
            {
                return _lockSprite;
            }

            _lookedForSprite = true;
            try
            {
                _lockSprite = Resources.FindObjectsOfTypeAll<OfferView>()
                    .Where(view => view._lockedButton != null)
                    .SelectMany(view => view._lockedButton.GetComponentsInChildren<Image>(true))
                    .Select(image => image.sprite)
                    .FirstOrDefault(sprite => sprite != null && sprite.name.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0);
                _lockSprite ??= Resources.FindObjectsOfTypeAll<Sprite>()
                    .FirstOrDefault(sprite => sprite.name.Equals("icon_lock", StringComparison.OrdinalIgnoreCase) || sprite.name.Equals("lock", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"No lock icon found for the flea categories: {ex.Message}");
            }

            // Looked for again later if the flea's offer list wasn't loaded yet.
            _lookedForSprite = _lockSprite != null;
            return _lockSprite;
        }
    }

    // ---------- inspect window ----------

    internal enum Attribute
    {
        FleaUnlockLevel
    }

    public static ItemAttribute UnlockAttribute(Item item)
    {
        var level = item == null ? 0 : FleaStatus.UnlockLevel(item.TemplateId.ToString());
        if (level <= 0)
        {
            return null;
        }

        return new ItemAttribute(Attribute.FleaUnlockLevel)
        {
            Name = "Unlocks on flea market",
            DisplayNameFunc = () => ClientText.Get("Unlocks on flea market"),
            StringValue = () => ClientText.Get("{0} Level", level),
            FullStringValue = () => ClientText.Get("Can be bought and sold on the Flea Market from level {0}.", level),
            DisplayType = () => EItemAttributeDisplayType.Compact
        };
    }

    public static void StyleUnlockRow(Item item, ViewList<ItemAttribute, CompactCharacteristicPanel> viewers)
    {
        if (viewers == null)
        {
            return;
        }

        foreach (var pair in viewers)
        {
            var viewer = pair.Value;
            if (viewer == null)
            {
                continue;
            }

            var ours = pair.Key?.Id is Attribute;
            var highlight = viewer.transform.Find(HighlightName);
            if (!ours)
            {
                // The game reuses rows, so take ours off any it gave back.
                highlight?.gameObject.SetActive(false);
                continue;
            }

            if (highlight == null)
            {
                var go = new GameObject(HighlightName, typeof(RectTransform));
                go.transform.SetParent(viewer.transform, false);
                go.transform.SetAsFirstSibling();
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                var image = go.AddComponent<Image>();
                image.color = HighlightColor;
                image.raycastTarget = false;
                highlight = go.transform;
            }

            var level = FleaStatus.UnlockLevel(item?.TemplateId.ToString());
            highlight.gameObject.SetActive(level > FleaStatus.PlayerLevel);
            if (viewer.Icon != null && LockSprite != null)
            {
                viewer.Icon.sprite = LockSprite;
                viewer.Icon.color = Color.white;
                viewer.Icon.gameObject.SetActive(true);
            }
        }
    }

    // ---------- flea categories ----------

    // Live's rule: a category of items shows the lowest level any of them needs, a category of categories the
    // highest of theirs (so the top one says when all of it is open).
    private static readonly ConditionalWeakTable<HandbookNode, Box> Levels = new ConditionalWeakTable<HandbookNode, Box>();

    private sealed class Box
    {
        public int Stamp;
        public int Level;
    }

    public static int NodeLevel(HandbookNode node)
    {
        if (node?.Data == null)
        {
            return 0;
        }

        if (node.Data.Type == ENodeType.Item)
        {
            return FleaStatus.UnlockLevel(node.Data.Item?.TemplateId.ToString() ?? node.Data.Id);
        }

        var box = Levels.GetOrCreateValue(node);
        if (box.Stamp == FleaStatus.LevelsStamp + 1)
        {
            return box.Level;
        }

        var children = node.OriginalChildren.Count > 0 ? node.OriginalChildren : node.Children;
        var categories = 0;
        var highest = 0;
        var lowestItem = int.MaxValue;
        foreach (var child in children)
        {
            if (child?.Data == null)
            {
                continue;
            }

            var level = NodeLevel(child);
            if (child.Data.Type == ENodeType.Category)
            {
                categories++;
                highest = Math.Max(highest, level);
            }
            else if (level > 0)
            {
                lowestItem = Math.Min(lowestItem, level);
            }
        }

        box.Level = categories > 0 ? Math.Max(highest, lowestItem == int.MaxValue ? 0 : lowestItem) : lowestItem == int.MaxValue ? 0 : lowestItem;
        box.Stamp = FleaStatus.LevelsStamp + 1;
        return box.Level;
    }

    public static void UpdateBadge(NodeBaseView view)
    {
        if (view == null || view._background == null || view.Node == null)
        {
            return;
        }

        var level = FleaStatus.LevelLockOn && view.Ragfair != null ? NodeLevel(view.Node) : 0;
        var row = view._background.transform;
        var badge = row.Find(BadgeName);
        if (level <= FleaStatus.PlayerLevel)
        {
            badge?.gameObject.SetActive(false);
            return;
        }

        if (badge == null)
        {
            badge = CreateBadge(view, row);
        }

        badge.gameObject.SetActive(true);
        var text = badge.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
        {
            text.text = level + " lvl";
            text.color = view.Selected ? (Color)view._hoverTextColor : Color.white;
        }
    }

    private static Transform CreateBadge(NodeBaseView view, Transform row)
    {
        var go = new GameObject(BadgeName, typeof(RectTransform));
        go.transform.SetParent(row, false);
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-12f, 0f);
        rect.sizeDelta = new Vector2(90f, 0f);

        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        if (LockSprite != null)
        {
            var icon = new GameObject("Icon", typeof(RectTransform));
            icon.transform.SetParent(go.transform, false);
            var image = icon.AddComponent<Image>();
            image.sprite = LockSprite;
            image.color = LockColor;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var size = icon.AddComponent<LayoutElement>();
            size.preferredWidth = 12f;
            size.preferredHeight = 14f;
        }

        var text = UnityEngine.Object.Instantiate(view.CategoryChildCount, go.transform, false);
        text.name = "Text";
        text.gameObject.SetActive(true);
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.fontStyle |= FontStyles.Bold;
        foreach (var element in text.GetComponents<LayoutElement>())
        {
            UnityEngine.Object.Destroy(element);
        }

        foreach (var fitter in text.GetComponents<ContentSizeFitter>())
        {
            UnityEngine.Object.Destroy(fitter);
        }

        return go.transform;
    }
}

internal class InspectUnlockPrefixPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        typeof(ItemSpecificationPanel).GetMethod(nameof(ItemSpecificationPanel.RecreateAttributeBars), BindingFlags.Instance | BindingFlags.Public);

    // Added to the item's own list just while the rows are made, then taken off again in the postfix.
    [PatchPrefix]
    public static void Prefix(Item ____item, ref ItemAttribute __state)
    {
        __state = LevelLockUi.UnlockAttribute(____item);
        if (__state != null)
        {
            ____item.Attributes.Add(__state);
        }
    }

    [PatchPostfix]
    public static void Postfix(Item ____item, ViewList<ItemAttribute, CompactCharacteristicPanel> ____compactBarAttributeViewers, ItemAttribute __state)
    {
        if (__state != null)
        {
            ____item.Attributes.Remove(__state);
        }

        LevelLockUi.StyleUnlockRow(____item, ____compactBarAttributeViewers);
    }
}

internal class CategoryLevelPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        typeof(NodeBaseView).GetMethod(nameof(NodeBaseView.OffersCountUpdatedHandler), BindingFlags.Instance | BindingFlags.Public);

    [PatchPostfix]
    public static void Postfix(NodeBaseView __instance) => LevelLockUi.UpdateBadge(__instance);
}

internal class CategorySelectPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        typeof(NodeBaseView).GetMethod(nameof(NodeBaseView.SelectView), BindingFlags.Instance | BindingFlags.Public);

    [PatchPostfix]
    public static void Postfix(NodeBaseView __instance) => LevelLockUi.UpdateBadge(__instance);
}

internal class CategoryDeselectPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        typeof(NodeBaseView).GetMethod(nameof(NodeBaseView.DeselectView), BindingFlags.Instance | BindingFlags.Public);

    [PatchPostfix]
    public static void Postfix(NodeBaseView __instance) => LevelLockUi.UpdateBadge(__instance);
}
