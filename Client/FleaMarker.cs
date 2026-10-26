using System.Collections.Generic;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFlea.Client;

// Flea icon on an item: orange = bought from a trader, red = flea banned.
// Sits where the game draws its found-in-raid checkmark, as a separate object so checkmark mods keep working.
// Cells get reused for other items, so every update shows or hides it.
internal static class FleaMarker
{
    private const string ObjectName = "BetterFleaMarker";
    private const string FleaIconPath = "Characteristics/Icons/AddOffer";

    private static readonly Color BoughtColour = new Color32(0xd9, 0xa4, 0x41, 0xff);
    private static readonly Color BannedColour = new Color32(0xc8, 0x4b, 0x4b, 0xff);

    private static Sprite _icon;

    public static void Update(QuestItemViewPanel panel, ItemView cell, Item item, SimpleTooltip tooltip)
    {
        var host = cell != null ? cell.transform : panel != null ? panel.transform.parent : null;
        if (panel == null || host == null)
        {
            return;
        }

        var inspect = cell == null;
        if (inspect)
        {
            CheckmarkSlot.Return(panel);
        }

        var banned = item != null && Plugin.ShowFleaBannedIcon.Value && FleaStatus.IsFleaBanned(item);
        var bought = !banned && item != null && Plugin.ShowTraderIcon.Value && FleaStatus.IsBoughtFromTrader(item);
        var existing = host.Find(ObjectName);

        if (!banned && !bought)
        {
            if (existing != null)
            {
                existing.GetComponent<Marker>()?.Remember(panel, cell, item, tooltip);
                existing.gameObject.SetActive(false);
            }

            return;
        }

        if (inspect)
        {
            CheckmarkSlot.Borrow(panel);
        }

        var marker = existing != null ? existing.GetComponent<Marker>() : Create(panel, host, inspect);
        if (marker == null)
        {
            return;
        }

        marker.Image.color = banned ? BannedColour : BoughtColour;
        marker.Text = banned ? Tags.FleaBanned : Tags.BoughtFromTrader;
        marker.Remember(panel, cell, item, tooltip);
        marker.Cell = cell != null ? (RectTransform)cell.transform : null;
        marker.CountText = cell != null && cell.BottomPanel != null ? cell.BottomPanel.ItemValue : null;
        marker.Insured = cell != null && cell.BottomPanel != null && cell.BottomPanel.InsuredIcon != null
            ? cell.BottomPanel.InsuredIcon.transform as RectTransform
            : null;
        marker.RepairBuff = cell != null && cell.BottomPanel != null && cell.BottomPanel.RepairBuffIcon != null
            ? cell.BottomPanel.RepairBuffIcon.rectTransform
            : null;
        // An equipped gun (and a flea offer's item) shows a durability bar along the bottom.
        var durability = cell is SlotItemView slotItem ? slotItem._durabilitySlider : cell is RagfairOfferItemView offerItem ? offerItem._durabilitySlider : null;
        marker.Durability = durability != null ? durability.transform as RectTransform : null;
        marker.transform.SetAsLastSibling();
        marker.gameObject.SetActive(true);
        marker.Follow();
    }

    // Redraw every marker after an icon setting changes.
    public static void RefreshAll()
    {
        foreach (var marker in UnityEngine.Object.FindObjectsOfType<Marker>(true))
        {
            if (marker.ShownItem != null)
            {
                Update(marker.Panel, marker.CellView, marker.ShownItem, marker.Tooltip);
            }
        }
    }

    // The inspect window's checkmark box is much bigger than the checkmark drawn in it.
    private const float InspectScale = 0.6f;

    // The inspect window hides the checkmark slot when there's no checkmark, and hidden slots aren't laid out.
    // So we switch it back on, invisible and with no hover, and put the icon in it. Handed back before the
    // game (or a checkmark mod) sets it up again.
    private static class CheckmarkSlot
    {
        private static readonly Dictionary<int, (float Alpha, bool Raycast)> Borrowed = new();

        public static bool IsBorrowed(QuestItemViewPanel panel) => panel != null && Borrowed.ContainsKey(panel.GetInstanceID());

        public static void Borrow(QuestItemViewPanel panel)
        {
            var image = panel._questIconImage;
            if (panel.gameObject.activeSelf || image == null)
            {
                return;
            }

            Borrowed[panel.GetInstanceID()] = (image.color.a, image.raycastTarget);

            // Kept on with the normal checkmark sprite so it takes up the same space, just transparent.
            image.sprite = panel._foundInRaidSprite != null ? panel._foundInRaidSprite : image.sprite;
            image.color = new Color(image.color.r, image.color.g, image.color.b, 0f);
            image.raycastTarget = false;
            SetHover(panel, false);
            if (panel._questItemLabel != null)
            {
                panel._questItemLabel.gameObject.SetActive(false);
            }

            panel.gameObject.SetActive(true);
        }

        // Only undo what's still ours: whoever shows a checkmark sets its sprite and colour anyway.
        public static void Return(QuestItemViewPanel panel)
        {
            var id = panel.GetInstanceID();
            if (!Borrowed.TryGetValue(id, out var before))
            {
                return;
            }

            Borrowed.Remove(id);
            var image = panel._questIconImage;
            if (image != null)
            {
                if (image.color.a == 0f)
                {
                    image.color = new Color(image.color.r, image.color.g, image.color.b, before.Alpha);
                }

                image.raycastTarget = before.Raycast;
            }

            SetHover(panel, true);
        }

        private static void SetHover(QuestItemViewPanel panel, bool on)
        {
            var hover = panel.GetComponent<HoverTrigger>();
            if (hover != null)
            {
                hover.enabled = on;
            }
        }
    }

    private static Marker Create(QuestItemViewPanel panel, Transform host, bool inspect)
    {
        var sprite = Icon();
        if (sprite == null)
        {
            return null;
        }

        var size = panel._questIconImage != null ? panel._questIconImage.rectTransform.rect.size : Vector2.zero;
        if (size.x <= 0 || size.y <= 0)
        {
            size = new Vector2(14, 14);
        }

        if (inspect)
        {
            size *= InspectScale;
        }

        var go = new GameObject(ObjectName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(host, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = true;

        var marker = go.AddComponent<Marker>();
        marker.Image = image;

        var hover = go.AddComponent<HoverTrigger>();
        hover.OnHoverStart += _ => marker.ShowTooltip();
        hover.OnHoverEnd += _ => marker.HideTooltip();
        return marker;
    }

    private static Sprite Icon()
    {
        if (_icon == null)
        {
            _icon = ResourcesCache.Pop<Sprite>(FleaIconPath);
            if (_icon == null)
            {
                Plugin.Log.LogWarning($"Flea Market icon '{FleaIconPath}' not found, item markers are off.");
            }
        }

        return _icon;
    }

    internal class Marker : MonoBehaviour
    {
        private const float Gap = 2f;
        private const float Padding = 2f;

        private static readonly Vector3[] Corners = new Vector3[4];

        public Image Image;
        public string Text;
        public SimpleTooltip Tooltip;
        public QuestItemViewPanel Panel;
        public ItemView CellView;
        public Item ShownItem;
        public RectTransform Cell;

        public void Remember(QuestItemViewPanel panel, ItemView cell, Item item, SimpleTooltip tooltip)
        {
            Panel = panel;
            CellView = cell;
            ShownItem = item;
            Tooltip = tooltip;
        }
        public TextMeshProUGUI CountText;
        public RectTransform Insured;
        public RectTransform RepairBuff;
        public RectTransform Durability;

        // The cell lays itself out during the frame.
        private void LateUpdate()
        {
            Follow();
        }

        public void Follow()
        {
            if (Cell == null)
            {
                FollowInInspectWindow();
                return;
            }

            var rect = (RectTransform)transform;
            var parent = (RectTransform)rect.parent;
            var width = rect.rect.width;
            var height = rect.rect.height;

            Cell.GetWorldCorners(Corners);
            var cellBottomRight = parent.InverseTransformPoint(Corners[3]);
            var right = cellBottomRight.x - Padding;
            var bottom = cellBottomRight.y + Padding;
            if (CountText != null && CountText.gameObject.activeInHierarchy && !string.IsNullOrEmpty(CountText.text))
            {
                // Top of the text, not of its box (the box can be much taller).
                var textTop = CountText.rectTransform.TransformPoint(new Vector3(0f, CountText.textBounds.max.y, 0f));
                bottom = Mathf.Max(bottom, parent.InverseTransformPoint(textTop).y + Padding);
            }

            var checkmark = Visible(Panel != null && Panel.gameObject.activeInHierarchy && Panel._questIconImage != null ? Panel._questIconImage.rectTransform : null);
            if (checkmark != null)
            {
                checkmark.GetWorldCorners(Corners);
                var checkLow = parent.InverseTransformPoint(Corners[0]);
                var checkHigh = parent.InverseTransformPoint(Corners[2]);
                right = (checkLow.x + checkHigh.x) / 2f + width / 2f;
                bottom = Mathf.Max(bottom, checkHigh.y + Gap);
            }

            // Step up past the checkmark, insured and repair buff icons and the durability bar.
            var obstacles = new[] { checkmark, Visible(Insured), Visible(RepairBuff), Visible(Durability) };
            for (var moved = true; moved;)
            {
                moved = false;
                foreach (var obstacle in obstacles)
                {
                    if (obstacle == null)
                    {
                        continue;
                    }

                    obstacle.GetWorldCorners(Corners);
                    var low = parent.InverseTransformPoint(Corners[0]);
                    var high = parent.InverseTransformPoint(Corners[2]);
                    var overlapsX = low.x < right && high.x > right - width;
                    var overlapsY = low.y < bottom + height && high.y > bottom;
                    if (overlapsX && overlapsY)
                    {
                        bottom = high.y + Gap;
                        moved = true;
                    }
                }
            }

            rect.localPosition = new Vector3(right, bottom + height / 2f, 0f);
        }

        private static RectTransform Visible(RectTransform target)
        {
            return target != null && target.gameObject.activeInHierarchy ? target : null;
        }

        // The inspect checkmark is drawn at about 40% of its box.
        private const float VisibleCheckmarkShare = 0.4f;

        // The hand stops 3 of its 24 pixels above the bottom edge.
        private const float HandBottomMargin = 0.125f;

        // Right of the checkmark when there is one, in its place when there isn't. Bottoms line up.
        private void FollowInInspectWindow()
        {
            if (Panel == null || Panel._questIconImage == null || !Panel.gameObject.activeInHierarchy)
            {
                return;
            }

            var rect = (RectTransform)transform;
            var parent = (RectTransform)rect.parent;

            Panel._questIconImage.rectTransform.GetWorldCorners(Corners);
            var boxBottomLeft = parent.InverseTransformPoint(Corners[0]);
            var boxTopRight = parent.InverseTransformPoint(Corners[2]);
            var boxSize = boxTopRight - boxBottomLeft;
            var centre = (boxBottomLeft + boxTopRight) / 2f;
            var visibleHalf = boxSize * (VisibleCheckmarkShare / 2f);

            var height = rect.rect.height;
            var width = rect.rect.width;
            var y = centre.y - visibleHalf.y + height * (0.5f - HandBottomMargin);

            var x = CheckmarkSlot.IsBorrowed(Panel)
                ? centre.x + width / 2f
                : centre.x + visibleHalf.x + Gap + width;
            rect.localPosition = new Vector3(x, y, 0f);
        }

        public void ShowTooltip()
        {
            if (Tooltip != null && !string.IsNullOrEmpty(Text))
            {
                Tooltip.Show(Text);
            }
        }

        public void HideTooltip()
        {
            if (Tooltip != null)
            {
                Tooltip.Close();
            }
        }

        private void OnDisable()
        {
            HideTooltip();
        }
    }
}
