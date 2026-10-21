using HarmonyLib;
using StorageMaster.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StorageMaster
{
    /// <summary>
    /// Puts a name field over the title of the normal chest window (opened with E), so chests can be named there too.
    /// Uses the same name as the Storage window. SeneaL UI draws a chest panel of its own over the game's, which it hides;
    /// then the field goes over SeneaL UI's title, left of its header buttons (#72).
    /// </summary>
    public static class ChestRename
    {
        private const float FieldW = 260f, FieldH = 38f, ResetSize = 28f, SeneaLFieldH = 28f, SeneaLFontSize = 17f;

        private static TMP_InputField _field;
        private static Button _reset;
        private static Container _shown;
        // SeneaL UI's chest title while the field is over it (hidden meanwhile).
        private static TMP_Text _seneaLTitle;
        // The text area's margins in the game's chest panel.
        private static Vector2 _areaMin, _areaMax;
        private static int _swallowUntilFrame = -10;

        /// <summary>True while typing a name, so the keys don't also move you, close the chest and so on.</summary>
        public static bool BlocksInput =>
            (_field != null && _field.gameObject.activeInHierarchy && _field.isFocused) || Time.frameCount <= _swallowUntilFrame;

        private static void Ensure(InventoryGui gui)
        {
            if (_field != null || gui.m_containerName == null || !UIKit.Init()) return;
            Transform parent = gui.m_containerName.transform.parent;

            _field = UIKit.InputField(parent, "", FieldW, FieldH, plain: true);
            _field.name = "StorageMaster_ChestName";
            foreach (TMP_Text t in new[] { _field.textComponent, _field.placeholder as TMP_Text })
            {
                if (t == null) continue;
                t.fontSize = 22;
                t.enableAutoSizing = false;
                t.alignment = TextAlignmentOptions.Center;
            }
            _field.textComponent.color = UIKit.Parchment;
            _field.characterLimit = 32;
            _field.onEndEdit.AddListener(text =>
            {
                Save();
                _swallowUntilFrame = Time.frameCount + 1;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            });
            UIKit.Tooltip(_field.gameObject, "Click to name this chest, then press Enter. The name shows when you look at it and in the Storage window.");

            // The X sits inside the field's right end, so it never covers the game's buttons next to the title.
            _reset = UIKit.Button(_field.transform, "X", ResetName, ResetSize, ResetSize);
            _reset.name = "StorageMaster_ResetName";
            RectTransform reset = (RectTransform)_reset.transform;
            reset.anchorMin = reset.anchorMax = reset.pivot = new Vector2(1f, 0.5f);
            reset.anchoredPosition = new Vector2(-5f, 0f);
            // Equal margins on both sides keep the centered name centered.
            RectTransform area = _field.textViewport;
            if (area != null)
            {
                area.offsetMin = new Vector2(ResetSize + 8f, area.offsetMin.y);
                area.offsetMax = new Vector2(-(ResetSize + 8f), area.offsetMax.y);
                _areaMin = area.offsetMin;
                _areaMax = area.offsetMax;
            }
            UIKit.Tooltip(_reset.gameObject, "Remove the name.");
        }

        private static void Save()
        {
            if (_shown == null || _field == null) return;
            if (_field.text.Trim() != ChestAccess.GetLabel(_shown)) ChestAccess.SetLabel(_shown, _field.text);
        }

        private static void ResetName()
        {
            if (_shown == null) return;
            ChestAccess.SetLabel(_shown, "");
            _field.SetTextWithoutNotify("");
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>
        /// Moves the field onto the panel it's shown on: the game's chest panel (centered like its title), or SeneaL UI's
        /// (left aligned like its title).
        /// </summary>
        private static void Host(Transform panel, TMP_Text seneaLTitle)
        {
            if (_field.transform.parent == panel) return;
            _field.transform.SetParent(panel, false);
            _field.transform.SetAsLastSibling();
            bool seneaL = seneaLTitle != null;
            foreach (TMP_Text t in new[] { _field.textComponent, _field.placeholder as TMP_Text })
            {
                if (t == null) continue;
                // Smaller in SeneaL UI's lower header, where the field is only as tall as its title.
                t.fontSize = seneaL ? SeneaLFontSize : 22;
                t.alignment = seneaL ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            }
            // In the lower field the text gets most of its height, or a line that doesn't fit isn't drawn at all.
            RectTransform area = _field.textViewport;
            if (area != null)
            {
                area.offsetMin = new Vector2(seneaL ? 8f : _areaMin.x, seneaL ? 2f : _areaMin.y);
                area.offsetMax = new Vector2(_areaMax.x, seneaL ? -2f : _areaMax.y);
            }
        }

        /// <summary>
        /// Puts the field over SeneaL UI's title, from its left edge up to its header buttons (which stay clickable).
        /// </summary>
        private static void PositionOver(TMP_Text title, RectTransform panel)
        {
            Rect t = InPanel(panel, title.rectTransform);
            float right = t.xMin + FieldW;
            foreach (UIInputHandler button in panel.GetComponentsInChildren<UIInputHandler>())
            {
                if (button.transform.IsChildOf(_field.transform)) continue;
                Rect b = InPanel(panel, (RectTransform)button.transform);
                if (b.yMax > t.yMin && b.yMin < t.yMax && b.xMin > t.xMin + 20f) right = Mathf.Min(right, b.xMin - 6f);
            }
            RectTransform rt = (RectTransform)_field.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(Mathf.Max(80f, right - t.xMin), SeneaLFieldH);
            rt.position = panel.TransformPoint(new Vector3(t.xMin - 8f, t.center.y));
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        private static Rect InPanel(RectTransform panel, RectTransform rt)
        {
            rt.GetWorldCorners(_corners);
            Vector2 min = panel.InverseTransformPoint(_corners[0]), max = panel.InverseTransformPoint(_corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Centers the field on the game's title, wherever the title sits.</summary>
        private static void Position(TMP_Text title)
        {
            RectTransform trt = title.rectTransform;
            RectTransform rt = (RectTransform)_field.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(FieldW, FieldH);
            rt.position = trt.TransformPoint(trt.rect.center);
        }

        [HarmonyPatch]
        private static class Patches
        {
            [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateContainer)), HarmonyPostfix]
            private static void UpdateContainer(InventoryGui __instance)
            {
                Ensure(__instance);
                if (_field == null) return;
                Container c = __instance.m_currentContainer;
                bool show = c != null && c.m_nview != null && c.m_nview.IsValid() && c.GetComponent<TombStone>() == null;

                if (c != _shown)
                {
                    // Switched chests or closed the window: keep what was typed.
                    Save();
                    _shown = show ? c : null;
                    if (_shown != null) _field.SetTextWithoutNotify(ChestAccess.GetLabel(_shown));
                }
                RectTransform seneaL = SeneaLCompat.ChestShown ? SeneaLCompat.ChestPanel : null;
                TMP_Text seneaLTitle = seneaL != null ? seneaL.Find("title")?.GetComponent<TMP_Text>() : null;
                if (_seneaLTitle != null && _seneaLTitle != seneaLTitle) _seneaLTitle.enabled = true;
                _seneaLTitle = seneaLTitle;
                Host(seneaLTitle != null ? seneaL : __instance.m_containerName.transform.parent, seneaLTitle);

                _field.gameObject.SetActive(show);
                __instance.m_containerName.enabled = !show;
                if (seneaLTitle != null) seneaLTitle.enabled = !show;
                if (!show)
                {
                    _reset.gameObject.SetActive(false);
                    return;
                }

                if (seneaLTitle != null) PositionOver(seneaLTitle, seneaL);
                else Position(__instance.m_containerName);
                TMP_Text shownTitle = seneaLTitle != null ? seneaLTitle : __instance.m_containerName;
                // Only the chest's owner can store the name, which you are while it's open.
                bool owner = c.IsOwner();
                _field.interactable = owner;
                string label = ChestAccess.GetLabel(c);
                if (_field.placeholder is TMP_Text ph) ph.text = shownTitle.text;
                if (!_field.isFocused && _field.text != label) _field.SetTextWithoutNotify(label);
                _reset.gameObject.SetActive(owner && label.Length > 0);
            }
        }
    }
}
