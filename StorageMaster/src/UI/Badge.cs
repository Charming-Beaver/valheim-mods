using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StorageMaster.UI
{
    /// <summary>
    /// A box like the game's weight box on a panel's right edge. Clicking it opens or closes a menu of buttons next to it;
    /// holding the drag key (Plugin.DragKey) and dragging moves it. Where it is and whether the menu is open are saved.
    /// </summary>
    public class Badge : Button, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float MenuGap = 4f;

        public RectTransform Menu;
        /// <summary>Its usual place; the saved X/Y are how far it was moved from there.</summary>
        public Vector2 Home;
        public ConfigEntry<bool> MenuOpen;
        public ConfigEntry<int> X, Y;

        private bool _moving;
        private Vector2 _grab, _live;
        private static readonly Vector3[] _corners = new Vector3[4];

        public RectTransform Rect => (RectTransform)transform;
        private RectTransform Parent => (RectTransform)transform.parent;

        /// <summary>Puts a badge just above <paramref name="below"/> (the panel's weight box), with the same look and size.</summary>
        public static Badge Create(RectTransform panel, RectTransform below, string name, RectTransform menu,
            ConfigEntry<bool> menuOpen, ConfigEntry<int> x, ConfigEntry<int> y)
        {
            RectTransform rt = UIKit.Rect(panel, name);
            rt.anchorMin = below.anchorMin;
            rt.anchorMax = below.anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = below.sizeDelta;

            // The weight box is a frame with a wooden face; the badge copies both.
            Image frame = rt.gameObject.AddComponent<Image>();
            Image belowFrame = below.GetComponent<Image>();
            frame.sprite = belowFrame != null ? belowFrame.sprite : UIKit.PanelSprite;
            frame.type = belowFrame != null ? belowFrame.type : Image.Type.Sliced;
            Image belowFace = null;
            foreach (Transform child in below)
            {
                if (child.name == "bkg") belowFace = child.GetComponent<Image>();
            }
            Image face = UIKit.Rect(rt, "bkg").Fill().gameObject.AddComponent<Image>();
            face.sprite = belowFace != null ? belowFace.sprite : UIKit.PanelSprite;
            face.color = belowFace != null ? belowFace.color : new Color(0.784f, 0.784f, 0.784f);

            // Three bars: the usual sign for a menu.
            for (int i = -1; i <= 1; i++)
            {
                RectTransform bar = UIKit.Solid(rt, UIKit.Parchment, "bar").rectTransform;
                bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
                bar.sizeDelta = new Vector2(30f, 4f);
                bar.anchoredPosition = new Vector2(1f, i * 9f);
            }

            Badge badge = rt.gameObject.AddComponent<Badge>();
            badge.targetGraphic = face;
            ColorBlock colors = badge.colors;
            colors.highlightedColor = colors.selectedColor = new Color(1f, 0.85f, 0.6f);
            badge.colors = colors;
            badge.Menu = menu;
            badge.Home = below.anchoredPosition + new Vector2(0f, below.sizeDelta.y + 12f);
            badge.MenuOpen = menuOpen;
            badge.X = x;
            badge.Y = y;
            badge.onClick.AddListener(badge.Toggle);
            UIKit.ButtonSound(rt.gameObject);
            UIKit.Tooltip(rt.gameObject, " ");

            menu.SetParent(panel, false);
            menu.anchorMin = menu.anchorMax = rt.anchorMin;
            menu.SetAsLastSibling();
            badge.Apply();
            return badge;
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
            Rect.anchoredPosition = Home + (_moving ? _live : new Vector2(X.Value, Y.Value));
            Rect.anchoredPosition += ShiftOnScreen(Rect);
            if (GetComponent<UITooltip>() is UITooltip tip)
            {
                tip.m_topic = Plugin.Name;
                tip.m_text = $"Click: open or close the menu\n{Plugin.DragKey.Value} + drag: move this button";
            }

            bool open = MenuOpen.Value;
            if (Menu.gameObject.activeSelf != open) Menu.gameObject.SetActive(open);
            if (!open) return;
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
