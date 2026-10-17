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
    /// Uses the same name as the Storage window.
    /// </summary>
    public static class ChestRename
    {
        private const float FieldW = 260f, FieldH = 38f, ResetSize = 28f;

        private static TMP_InputField _field;
        private static Button _reset;
        private static Container _shown;
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
                _field.gameObject.SetActive(show);
                __instance.m_containerName.enabled = !show;
                if (!show)
                {
                    _reset.gameObject.SetActive(false);
                    return;
                }

                Position(__instance.m_containerName);
                // Only the chest's owner can store the name, which you are while it's open.
                bool owner = c.IsOwner();
                _field.interactable = owner;
                string label = ChestAccess.GetLabel(c);
                if (_field.placeholder is TMP_Text ph) ph.text = __instance.m_containerName.text;
                if (!_field.isFocused && _field.text != label) _field.SetTextWithoutNotify(label);
                _reset.gameObject.SetActive(owner && label.Length > 0);
            }
        }
    }
}
