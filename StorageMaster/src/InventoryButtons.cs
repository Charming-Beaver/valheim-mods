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
            if (gui == null || gui.m_player == null || _sort != null || !UIKit.Init()) return;

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
            rt.anchoredPosition = new Vector2(8, -index * (ButtonH + Gap));
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
