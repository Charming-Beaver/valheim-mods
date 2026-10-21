using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StorageMaster.UI
{
    /// <summary>
    /// A box like the game's weight box on a panel's right edge (with SeneaL UI: like its pills, under its panel's bottom
    /// left corner). Clicking it opens or closes a menu of buttons next to it; holding the drag key (Plugin.DragKey) and
    /// dragging moves it. Where it is and whether the menu is open are saved.
    /// </summary>
    public class Badge : Button, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float MenuGap = 4f;

        public RectTransform Menu;
        /// <summary>The panel it belongs to: its parent, or (SeneaL UI's panels) a sibling it sits next to.</summary>
        public RectTransform Panel;
        public ConfigEntry<bool> MenuOpen;
        public ConfigEntry<int> X, Y;
        /// <summary>What is drawn over the badge: the game's boxes on the same panel (armor, weight), or the panel it sits next to.</summary>
        public Transform[] Boxes;

        private Vector2 _home;
        private Vector2 _homeSize;
        private bool _homeUnderPanel;
        private bool _moving;
        private Vector2 _grab, _live;
        private static readonly Vector3[] _corners = new Vector3[4];

        public RectTransform Rect => (RectTransform)transform;
        private RectTransform Parent => (RectTransform)transform.parent;

        /// <summary>
        /// Its usual place; the saved X/Y are how far it was moved from there. Next to a panel, it's just under the panel's
        /// bottom left corner, which moves with the panel's size.
        /// </summary>
        public Vector2 Home
        {
            get
            {
                if (!_homeUnderPanel) return _home;
                Rect panel = PanelRect();
                return new Vector2(panel.xMin + 16f + _homeSize.x / 2f, panel.yMin - _homeSize.y / 2f + 6f);
            }
        }

        /// <summary>Puts a badge just above <paramref name="below"/> (the panel's weight box), with the same look and size.</summary>
        public static Badge Create(RectTransform panel, RectTransform below, string name, RectTransform menu,
            ConfigEntry<bool> menuOpen, ConfigEntry<int> x, ConfigEntry<int> y, params Transform[] boxes)
        {
            Badge badge = Make(below, panel, name, null);
            badge.Panel = panel;
            badge._home = below.anchoredPosition + new Vector2(0f, below.sizeDelta.y + 12f);
            badge.Boxes = new Transform[boxes.Length + 1];
            badge.Boxes[0] = below;
            boxes.CopyTo(badge.Boxes, 1);
            return badge.Finish(menu, menuOpen, x, y);
        }

        /// <summary>
        /// Puts a badge next to another mod's panel (SeneaL UI's), tucked under its bottom left corner, with the look of
        /// <paramref name="look"/> (one of that panel's boxes) at the given size.
        /// </summary>
        public static Badge CreateNextTo(RectTransform panel, RectTransform look, Vector2 size, string name, RectTransform menu,
            ConfigEntry<bool> menuOpen, ConfigEntry<int> x, ConfigEntry<int> y)
        {
            Badge badge = Make(look, (RectTransform)panel.parent, name, size);
            RectTransform rt = badge.Rect;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            badge.Panel = panel;
            badge._homeUnderPanel = true;
            badge._homeSize = size;
            badge.Boxes = new Transform[] { panel };
            return badge.Finish(menu, menuOpen, x, y);
        }

        private static Badge Make(RectTransform look, RectTransform parent, string name, Vector2? size)
        {
            // A copy of the box (the weight box's frame and wooden face, or SeneaL UI's pill, with their materials and
            // colors), so it looks the same, also when a UI mod restyles that box. Its icon, text and any scripts are removed.
            GameObject go = Instantiate(look.gameObject, parent, false);
            go.name = name;
            // The box can be hidden (SeneaL UI hides the game's), the badge mustn't be.
            go.SetActive(true);
            RectTransform rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            if (size.HasValue) rt.sizeDelta = size.Value;
            Image face = null;
            foreach (Transform child in rt)
            {
                // The wooden face is "bkg" in the game's box, SeneaL UI's pill is "bg".
                if ((child.name == "bkg" || child.name == "bg") && face == null && child.TryGetComponent(out Image image))
                {
                    face = image;
                    OnlyLooks(child.gameObject);
                }
                else Destroy(child.gameObject);
            }
            OnlyLooks(go);
            if (face == null) face = go.GetComponent<Image>() ?? go.AddComponent<Image>();

            // Three bars: the usual sign for a menu (smaller on a smaller box).
            float scale = size.HasValue ? Mathf.Min(1f, size.Value.y / 40f) : 1f;
            for (int i = -1; i <= 1; i++)
            {
                RectTransform bar = UIKit.Solid(rt, UIKit.Parchment, "bar").rectTransform;
                bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
                bar.sizeDelta = new Vector2(30f, 4f) * scale;
                bar.anchoredPosition = new Vector2(1f, i * 9f) * scale;
            }

            Badge badge = rt.gameObject.AddComponent<Badge>();
            badge.targetGraphic = face;
            // Lighter only while the mouse is over it; it stays selected after a click, which mustn't keep it lit.
            ColorBlock colors = badge.colors;
            colors.highlightedColor = new Color(1f, 0.85f, 0.6f);
            colors.selectedColor = colors.normalColor;
            badge.colors = colors;
            return badge;
        }

        private Badge Finish(RectTransform menu, ConfigEntry<bool> menuOpen, ConfigEntry<int> x, ConfigEntry<int> y)
        {
            Menu = menu;
            MenuOpen = menuOpen;
            X = x;
            Y = y;
            onClick.AddListener(Toggle);
            UIKit.ButtonSound(gameObject);
            UIKit.Tooltip(gameObject, " ");

            menu.SetParent(Parent, false);
            menu.anchorMin = menu.anchorMax = Rect.anchorMin;
            menu.SetAsLastSibling();
            // An open menu is never covered, e.g. by the crafting panel. Its tooltips go into its layer too, so they show.
            OnTop(menu.gameObject, true, 2);
            Apply();
            return this;
        }

        /// <summary>Removes everything but the looks (RectTransform, CanvasRenderer, Image) from a copied object.</summary>
        private static void OnlyLooks(GameObject go)
        {
            foreach (Component c in go.GetComponents<Component>())
            {
                if (!(c is RectTransform || c is CanvasRenderer || c is Image)) DestroyImmediate(c);
            }
        }

        /// <summary>
        /// Draws an object over the rest of the inventory screen (the crafting panel and so on) with a layer of its own,
        /// or takes that layer away again so it's drawn in its place among its siblings.
        /// </summary>
        private static void OnTop(GameObject go, bool on, int above)
        {
            Canvas canvas = go.GetComponent<Canvas>();
            if (!on)
            {
                if (canvas == null) return;
                // Without its own layer the badge's tooltip goes into the inventory screen's, which is drawn over the panel.
                DestroyImmediate(go.GetComponent<GraphicRaycaster>());
                DestroyImmediate(canvas);
                return;
            }
            if (canvas == null)
            {
                canvas = go.AddComponent<Canvas>();
                // A layer of its own needs its own raycaster to be clicked.
                go.AddComponent<GraphicRaycaster>();
            }
            // Set every time: Unity drops it when the layer was added while the inventory was hidden.
            Canvas screen = go.transform.parent.GetComponentInParent<Canvas>();
            int order = (screen != null ? screen.sortingOrder : 0) + above;
            if (!canvas.overrideSorting) canvas.overrideSorting = true;
            if (canvas.sortingOrder != order) canvas.sortingOrder = order;
        }

        /// <summary>Whether the badge overlaps its panel, the game's boxes or ExtraSlots' panel (the things drawn over it).</summary>
        private bool OverItsPanel()
        {
            Bounds mine = RectTransformUtility.CalculateRelativeRectTransformBounds(Parent, Rect);
            mine.Expand(new Vector3(0f, 0f, 1f));
            Rect panel = PanelRect();
            if (new Bounds(panel.center, new Vector3(panel.width, panel.height, 1f)).Intersects(mine)) return true;
            foreach (Transform sibling in Parent)
            {
                if (!sibling.gameObject.activeInHierarchy || !(sibling.name.StartsWith("ExtraSlots") || System.Array.IndexOf(Boxes, sibling) >= 0)) continue;
                Bounds theirs = RectTransformUtility.CalculateRelativeRectTransformBounds(Parent, sibling);
                theirs.Expand(new Vector3(0f, 0f, 1f));
                if (theirs.size.x > 0f && theirs.Intersects(mine)) return true;
            }
            return false;
        }

        /// <summary>
        /// Its panel's own rect in its parent's units (without the panel's children, which can reach far outside it, like
        /// SeneaL UI's hidden tooltips).
        /// </summary>
        private Rect PanelRect()
        {
            if (Panel == Parent) return Parent.rect;
            Panel.GetWorldCorners(_corners);
            Vector2 min = Parent.InverseTransformPoint(_corners[0]), max = Parent.InverseTransformPoint(_corners[2]);
            return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public void Toggle()
        {
            MenuOpen.Value = !MenuOpen.Value;
            Apply();
        }

        /// <summary>Moves the badge this far from its usual place (kept on screen) and saves it.</summary>
        public void MoveTo(Vector2 offset)
        {
            _live = offset;
            _moving = true;
            Apply();
            _moving = false;
            Vector2 kept = Rect.anchoredPosition - Home;
            X.Value = Mathf.RoundToInt(kept.x);
            Y.Value = Mathf.RoundToInt(kept.y);
            Apply();
        }

        /// <summary>Puts the badge where it was saved (kept on screen) and the menu next to it, if it's open.</summary>
        public void Apply()
        {
            KeepUnder();
            Rect.anchoredPosition = Home + (_moving ? _live : new Vector2(X.Value, Y.Value));
            Rect.anchoredPosition += ShiftOnScreen(Rect);
            // Over its own panel it goes under it like the game's boxes; anywhere else nothing may cover it, so it can't be lost.
            OnTop(gameObject, !OverItsPanel(), 1);
            if (GetComponent<UITooltip>() is UITooltip tip)
            {
                tip.m_topic = Plugin.Name;
                tip.m_text = $"Click: open or close the menu\n{Plugin.DragKey.Value} + drag: move this button";
            }

            // A hidden badge (SeneaL UI's chest badge without a chest) hides its menu too, which isn't always inside its panel.
            bool open = MenuOpen.Value && gameObject.activeInHierarchy;
            if (Menu.gameObject.activeSelf != open) Menu.gameObject.SetActive(open);
            if (!open) return;
            OnTop(Menu.gameObject, true, 2);
            // Next to the badge, top edges level; on the other side if there's no room, and moved up if it would hang off the bottom.
            Vector2 half = Rect.sizeDelta / 2f;
            Vector2 top = Rect.anchoredPosition + new Vector2(0f, half.y);
            Menu.pivot = new Vector2(0f, 1f);
            Menu.anchoredPosition = top + new Vector2(half.x + MenuGap, 0f);
            Vector2 shift = ShiftOnScreen(Menu);
            if (shift.x < 0f)
            {
                Menu.pivot = new Vector2(1f, 1f);
                Menu.anchoredPosition = top - new Vector2(half.x + MenuGap, 0f);
                shift = ShiftOnScreen(Menu);
            }
            Menu.anchoredPosition += shift;
        }

        /// <summary>
        /// Drawn before the game's boxes and ExtraSlots' panel (which it adds to the inventory panel later on), and so
        /// before the panel's background: moved over any of them, the badge goes under it, like the game's boxes do.
        /// </summary>
        private void KeepUnder()
        {
            int first = int.MaxValue;
            foreach (Transform sibling in Parent)
            {
                if (sibling.name.StartsWith("ExtraSlots") || System.Array.IndexOf(Boxes, sibling) >= 0) first = Mathf.Min(first, sibling.GetSiblingIndex());
            }
            if (first < transform.GetSiblingIndex()) transform.SetSiblingIndex(first);
        }

        /// <summary>How far (in its parent's units) <paramref name="rt"/> has to move to be fully on screen.</summary>
        private static Vector2 ShiftOnScreen(RectTransform rt)
        {
            RectTransform root = UIKit.CanvasRoot as RectTransform;
            if (root == null) return Vector2.zero;
            rt.GetWorldCorners(_corners);
            Vector2 min = root.InverseTransformPoint(_corners[0]);
            Vector2 max = root.InverseTransformPoint(_corners[2]);
            Rect area = root.rect;
            Vector2 shift = Vector2.zero;
            if (min.x < area.xMin) shift.x = area.xMin - min.x;
            else if (max.x > area.xMax) shift.x = area.xMax - max.x;
            if (min.y < area.yMin) shift.y = area.yMin - min.y;
            else if (max.y > area.yMax) shift.y = area.yMax - max.y;
            float scale = ((RectTransform)rt.parent).lossyScale.x;
            return scale > 0f ? shift * (root.lossyScale.x / scale) : Vector2.zero;
        }

        public static bool DragKeyHeld()
        {
            switch (Plugin.DragKey.Value)
            {
                case Plugin.DragKeys.Shift: return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                case Plugin.DragKeys.Alt: return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr);
                default: return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            }
        }

        public void OnBeginDrag(PointerEventData e)
        {
            // Without the drag key, dragging does nothing, so the badge can't be moved by accident.
            if (!DragKeyHeld()) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Parent, e.position, e.pressEventCamera, out Vector2 local)) return;
            _grab = local - Rect.anchoredPosition;
            _live = Rect.anchoredPosition - Home;
            _moving = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_moving) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Parent, e.position, e.pressEventCamera, out Vector2 local)) return;
            _live = local - _grab - Home;
            Apply();
        }

        public void OnEndDrag(PointerEventData e)
        {
            // Saved only at the end, as every change of a setting writes the config file.
            if (_moving) MoveTo(_live);
        }

        // The click that ends a drag doesn't open or close the menu. It comes before OnEndDrag.
        public override void OnPointerClick(PointerEventData e)
        {
            if (!_moving) base.OnPointerClick(e);
        }
    }
}
