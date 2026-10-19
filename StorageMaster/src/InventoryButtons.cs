using HarmonyLib;
using StorageMaster.UI;
using UnityEngine;
using UnityEngine.UI;

namespace StorageMaster
{
    /// <summary>
    /// Valheim-style buttons in menus next to the inventory panel and the open chest's panel. Each menu opens from a badge
    /// on the panel's edge, above its weight box, which can be moved out of the way of other mods' windows (#5).
    /// </summary>
    public static class InventoryButtons
    {
        private const float ButtonW = 160f, ButtonH = 36f, Gap = 4f, Pad = 8f;

        private static Button _sort, _mode, _quickStack, _autoSort, _storage, _info, _sortChest, _chestMode;
        private static Badge _badge, _chestBadge;

        public static void Ensure()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null) return;
            if (_badge != null)
            {
                if (InventoryGui.IsVisible())
                {
                    _badge.Apply();
                    if (_chestBadge != null && _chestBadge.gameObject.activeInHierarchy) _chestBadge.Apply();
                }
                return;
            }
            if (!UIKit.Init() || gui.m_weight == null) return;

            RectTransform menu = Menu("StorageMaster_Menu", 6);
            _sort = Make(menu, "Sort", () => Sorter.SortPlayer(silent: false), 0,
                "Sorts everything below the hotbar and merges partial stacks.\nAlt + click a slot to lock it, so Sort leaves it alone.");
            _mode = Make(menu, "", Sorter.CycleMode, 1, "Click to change what Sort orders by.");
            _quickStack = Make(menu, "Quick stack", QuickStack.Run, 2,
                "Puts your items into nearby chests that already hold the same kind of item.\nThe hotbar, equipped items and locked slots stay with you.");
            _autoSort = Make(menu, "", Plugin.ToggleAutoSort, 3, "Sort automatically every time you pick something up.");
            _storage = Make(menu, "Storage", StorageWindow.Open, 4,
                "Opens every chest nearby in one window, to move, find, gather and tidy up items.");
            _info = Make(menu, "Info", InfoWindow.Open, 5, "How Storage Master works, its hotkeys and settings.");
            _badge = Badge.Create(gui.m_player, BoxOf(gui.m_player, gui.m_weight), "StorageMaster_Badge", menu,
                Plugin.InventoryMenuOpen, Plugin.InventoryBadgeX, Plugin.InventoryBadgeY, gui.m_armor != null ? BoxOf(gui.m_player, gui.m_armor) : null);

            if (gui.m_container != null && gui.m_containerWeight != null)
            {
                RectTransform chestMenu = Menu("StorageMaster_ChestMenu", 2);
                _sortChest = Make(chestMenu, "Sort chest", () => Sorter.SortContainer(gui.m_currentContainer), 0,
                    "Sorts this chest (merging partial stacks) using the same order as your inventory.");
                _chestMode = Make(chestMenu, "", Sorter.CycleMode, 1, "Click to change what Sort and Sort chest order by.");
                _chestBadge = Badge.Create(gui.m_container, BoxOf(gui.m_container, gui.m_containerWeight), "StorageMaster_ChestBadge", chestMenu,
                    Plugin.ChestMenuOpen, Plugin.ChestBadgeX, Plugin.ChestBadgeY);
            }
            Refresh();
        }

        /// <summary>The box around a text, i.e. its ancestor that sits directly in the panel (or the text's parent if none does).</summary>
        private static RectTransform BoxOf(RectTransform panel, TMPro.TMP_Text text)
        {
            Transform box = text.transform;
            while (box.parent != null && box.parent != panel) box = box.parent;
            return (RectTransform)(box.parent == panel ? box : text.transform.parent);
        }

        /// <summary>Puts the badges back where they were saved, e.g. after a change in the Info window.</summary>
        public static void ApplyBadges()
        {
            if (_badge != null) _badge.Apply();
            if (_chestBadge != null) _chestBadge.Apply();
        }

        public static void Refresh()
        {
            foreach (Button mode in new[] { _mode, _chestMode })
            {
                if (mode != null) mode.SetLabel("By: " + Sorter.ModeLabel);
            }
            if (_autoSort != null) _autoSort.SetLabel(Plugin.AutoSort.Value ? "Auto-sort: <color=#8fd16a>On</color>" : "Auto-sort: Off");
        }

        private static RectTransform Menu(string name, int buttons)
        {
            RectTransform menu = UIKit.DarkPanel(UIKit.CanvasRoot, name).rectTransform;
            menu.sizeDelta = new Vector2(ButtonW + 2 * Pad, buttons * ButtonH + (buttons - 1) * Gap + 2 * Pad);
            return menu;
        }

        private static Button Make(RectTransform menu, string label, UnityEngine.Events.UnityAction onClick, int index, string tooltip)
        {
            Button b = UIKit.Button(menu, label, onClick, ButtonW, ButtonH);
            RectTransform rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(Pad, -Pad - index * (ButtonH + Gap));
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
