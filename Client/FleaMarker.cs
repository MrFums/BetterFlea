using System.Collections.Generic;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFlea.Client;

// A small Flea Market icon on an item: orange for "bought from a trader", red for "banned from the
// Flea Market". Hovering it explains which.
//
// In an item cell it sits where the game puts its found-in-raid checkmark (bottom right, just above
// the count or durability text), or directly above that checkmark when there is one. It's its own
// object rather than a change to the game's icon, so mods that restyle the checkmark keep working,
// and it stays out of any automatic layout so it never moves other icons. Item cells are reused for
// other items, so every update either shows or hides it.
internal static class FleaMarker
{
    private const string ObjectName = "BetterFleaMarker";
    private const string FleaIconPath = "Characteristics/Icons/AddOffer";

    private static readonly Color BoughtColour = new Color32(0xd9, 0xa4, 0x41, 0xff);
    private static readonly Color BannedColour = new Color32(0xc8, 0x4b, 0x4b, 0xff);

    private static Sprite _icon;

    /// <param name="cell">The item cell, or null for the inspect window.</param>
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

        var banned = item != null && FleaStatus.IsFleaBanned(item);
        var bought = !banned && item != null && FleaStatus.IsBoughtFromTrader(item);
        var existing = host.Find(ObjectName);

        if (!banned && !bought)
        {
            if (existing != null)
            {
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
        marker.Text = banned ? Tags.FleaBannedText : Tags.BoughtFromTraderText;
        marker.Tooltip = tooltip;
        marker.Panel = panel;
        marker.Cell = cell != null ? (RectTransform)cell.transform : null;
        marker.CountText = cell != null && cell.BottomPanel != null ? cell.BottomPanel.ItemValue : null;
        marker.Insured = cell != null && cell.BottomPanel != null && cell.BottomPanel.InsuredIcon != null
            ? cell.BottomPanel.InsuredIcon.transform as RectTransform
            : null;
        marker.RepairBuff = cell != null && cell.BottomPanel != null && cell.BottomPanel.RepairBuffIcon != null
            ? cell.BottomPanel.RepairBuffIcon.rectTransform
            : null;
        marker.transform.SetAsLastSibling();
        marker.gameObject.SetActive(true);
        marker.Follow();
    }

    // The inspect window's checkmark box is much bigger than the checkmark drawn in it.
    private const float InspectScale = 0.6f;

    // In the inspect window, an item with no checkmark has its checkmark slot hidden, and a hidden slot
    // is never laid out, so there's no reliable spot to put the icon. Instead the slot is switched back
    // on with its own image and hover turned off: the game lays it out exactly where a checkmark would
    // go, and the icon sits in it. Every time the game (or a checkmark mod) sets the slot up again,
    // it's handed back first, so their checkmark is never affected.
    private static class CheckmarkSlot
    {
        // What the slot's image looked like before it was borrowed, by panel.
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

            // The image stays switched on, holding the normal checkmark, so the slot takes up exactly
            // the space a real checkmark does. It's just fully transparent and ignores the mouse.
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

        // Runs after the game (or a checkmark mod) has set the slot up for the new item, so only what's
        // still ours is undone. The sprite is left alone: whoever shows a checkmark sets its sprite anyway,
        // and a colour they've just set is never transparent, so it's kept.
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

        // Positioned by its right-middle point.
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
        public RectTransform Cell;
        public TextMeshProUGUI CountText;
        public RectTransform Insured;
        public RectTransform RepairBuff;

        // The cell lays itself out during the frame, so keep following it.
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

            // Start bottom-right of the cell, where the game puts its checkmark, above any count text.
            Cell.GetWorldCorners(Corners);
            var cellBottomRight = parent.InverseTransformPoint(Corners[3]);
            var right = cellBottomRight.x - Padding;
            var bottom = cellBottomRight.y + Padding;
            if (CountText != null && CountText.gameObject.activeInHierarchy && !string.IsNullOrEmpty(CountText.text))
            {
                // Top of the text itself, not of its box, which can be much taller than the text.
                var textTop = CountText.rectTransform.TransformPoint(new Vector3(0f, CountText.textBounds.max.y, 0f));
                bottom = Mathf.Max(bottom, parent.InverseTransformPoint(textTop).y + Padding);
            }

            // With a checkmark, always sit straight above it, centred on it.
            var checkmark = Visible(Panel != null && Panel.gameObject.activeInHierarchy && Panel._questIconImage != null ? Panel._questIconImage.rectTransform : null);
            if (checkmark != null)
            {
                checkmark.GetWorldCorners(Corners);
                var checkLow = parent.InverseTransformPoint(Corners[0]);
                var checkHigh = parent.InverseTransformPoint(Corners[2]);
                right = (checkLow.x + checkHigh.x) / 2f + width / 2f;
                bottom = Mathf.Max(bottom, checkHigh.y + Gap);
            }

            // Step up past the game's own corner icons (checkmark, insured, repair buff) until clear.
            var obstacles = new[] { checkmark, Visible(Insured), Visible(RepairBuff) };
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

            // Positioned by the marker's right-middle point.
            rect.localPosition = new Vector3(right, bottom + height / 2f, 0f);
        }

        private static RectTransform Visible(RectTransform target)
        {
            return target != null && target.gameObject.activeInHierarchy ? target : null;
        }

        // Measured in game: the inspect window's checkmark is drawn at about 40% of its box, centred.
        private const float VisibleCheckmarkShare = 0.4f;

        // Measured from the icon image: the hand stops 3 of its 24 pixels above the bottom edge.
        private const float HandBottomMargin = 0.125f;

        // The inspect window always has its checkmark slot laid out now (borrowed when the item has
        // no checkmark), so the icon is placed from that slot every time: just right of the checkmark
        // when there is one, in its spot when there isn't. The bottom of the hand lines up with the
        // bottom of the checkmark either way.
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

            // Positioned by the marker's right-middle point.
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
