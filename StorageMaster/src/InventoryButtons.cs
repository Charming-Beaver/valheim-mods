using HarmonyLib;
using StorageMaster.UI;
using UnityEngine;
using UnityEngine.UI;

namespace StorageMaster
{
    /// <summary>Valheim-style buttons next to the inventory panel and the open chest's panel.</summary>
    public static class InventoryButtons
    {
        private const float ButtonW = 160f, ButtonH = 36f, Gap = 4f;

        private static Button _sort, _mode, _quickStack, _autoSort, _storage, _info, _sortChest;

        public static void Ensure()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null) return;
            if (_sort != null)
            {
                if (InventoryGui.IsVisible()) Reposition(gui);
                return;
            }
            if (!UIKit.Init()) return;

            _sort = Make(gui.m_player, "Sort", () => Sorter.SortPlayer(silent: false), 0,
                "Sorts everything below the hotbar and merges partial stacks.\nAlt + click a slot to lock it, so Sort leaves it alone.");
            _mode = Make(gui.m_player, "", Sorter.CycleMode, 1, "Click to change what Sort orders by.");
            _quickStack = Make(gui.m_player, "Quick stack", QuickStack.Run, 2,
                "Puts your items into nearby chests that already hold the same kind of item.\nThe hotbar, equipped items and locked slots stay with you.");
            _autoSort = Make(gui.m_player, "", Plugin.ToggleAutoSort, 3, "Sort automatically every time you pick something up.");
            _storage = Make(gui.m_player, "Storage", StorageWindow.Open, 4,
                "Opens every chest nearby in one window, to move, find, gather and tidy up items.");
            _info = Make(gui.m_player, "Info", InfoWindow.Open, 5, "How Storage Master works, its hotkeys and settings.");

            if (gui.m_container != null)
            {
                _sortChest = Make(gui.m_container, "Sort chest", () => Sorter.SortContainer(gui.m_currentContainer), 0,
                    "Sorts this chest (merging partial stacks) using the same order as your inventory.");
            }
            Refresh();
            Reposition(gui);
        }

        /// <summary>
        /// The armor and weight boxes stick out to the right of the inventory panel (and the weight box of the chest panel),
        /// so the buttons start just after them instead of covering them.
        /// </summary>
        private static void Reposition(InventoryGui gui)
        {
            float x = Gap + OverhangRight(gui.m_player, gui.m_armor, gui.m_weight);
            foreach (Button b in new[] { _sort, _mode, _quickStack, _autoSort, _storage, _info })
            {
                if (b == null) continue;
                RectTransform rt = (RectTransform)b.transform;
                rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            }
            if (_sortChest != null && gui.m_container != null)
            {
                RectTransform rt = (RectTransform)_sortChest.transform;
                rt.anchoredPosition = new Vector2(Gap + OverhangRight(gui.m_container, gui.m_containerWeight), rt.anchoredPosition.y);
            }
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        /// <summary>How far (in the panel's units) the boxes holding these texts reach past the panel's right edge.</summary>
        private static float OverhangRight(RectTransform panel, params TMPro.TMP_Text[] texts)
        {
            panel.GetWorldCorners(_corners);
            float panelRight = panel.InverseTransformPoint(_corners[2]).x;
            float overhang = 0f;
            foreach (TMPro.TMP_Text text in texts)
            {
                if (text == null || !text.gameObject.activeInHierarchy) continue;
                // The text itself, in case its box isn't inside the panel.
                text.rectTransform.GetWorldCorners(_corners);
                overhang = Mathf.Max(overhang, panel.InverseTransformPoint(_corners[2]).x - panelRight + 10f);
                // The text's box is its ancestor that sits directly in the panel.
                Transform box = text.transform;
                while (box.parent != null && box.parent != panel) box = box.parent;
                if (box.parent != panel || !(box is RectTransform boxRt)) continue;
                boxRt.GetWorldCorners(_corners);
                float right = panel.InverseTransformPoint(_corners[2]).x;
                overhang = Mathf.Max(overhang, right - panelRight);
            }
            return overhang;
        }

        public static void Refresh()
        {
            if (_mode != null) _mode.SetLabel("By: " + Sorter.ModeLabel);
            if (_autoSort != null) _autoSort.SetLabel(Plugin.AutoSort.Value ? "Auto-sort: <color=#8fd16a>On</color>" : "Auto-sort: Off");
        }

        private static Button Make(RectTransform panel, string label, UnityEngine.Events.UnityAction onClick, int index, string tooltip)
        {
            Button b = UIKit.Button(panel, label, onClick, ButtonW, ButtonH);
            RectTransform rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(Gap, -index * (ButtonH + Gap));
            UIKit.Tooltip(b.gameObject, tooltip);
            return b;
        }

        [HarmonyPatch]
        private static class Patches
        {
            // Auto-sort after every successful pickup (not while dragging an item around).
            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup)), HarmonyPostfix]
            private static void Pickup(Humanoid __instance, bool __result)
            {
                if (__result && Plugin.AutoSort.Value && __instance == Player.m_localPlayer)
                {
                    Sorter.SortPlayer(silent: true);
                }
            }
        }
    }
}
