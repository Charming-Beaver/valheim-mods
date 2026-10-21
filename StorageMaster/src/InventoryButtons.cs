using HarmonyLib;
using StorageMaster.UI;
using UnityEngine;
using UnityEngine.UI;

namespace StorageMaster
{
    /// <summary>
    /// Valheim-style buttons in menus next to the inventory panel and the open chest's panel. Each menu opens from a badge
    /// on the panel's edge, above its weight box, which can be moved out of the way of other mods' windows (#5).
    /// SeneaL UI draws panels of its own over the game's, which it hides; then the badges sit next to its panels (#64).
    /// </summary>
    public static class InventoryButtons
    {
        private const float ButtonW = 160f, ButtonH = 36f, Gap = 4f, Pad = 8f;
        private static readonly Vector2 SeneaLBadgeSize = new Vector2(48f, 30f);

        private static Button _sort, _mode, _quickStack, _autoSort, _storage, _info, _options, _sortChest, _chestMode, _backpackQuickStack;
        private static Badge _badge, _chestBadge;
        private static RectTransform _menu, _chestMenu, _chestBadgeLook;
        // What the chest menu shows: a chest, the backpack, or the backpack with its own Quick stack.
        private enum ChestMenuFor { Chest, Backpack, BackpackWithQuickStack }
        private static ChestMenuFor _chestMenuFor;

        public static void Ensure()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null) return;
            if (_menu == null)
            {
                if (!UIKit.Init() || gui.m_weight == null) return;
                MakeMenus(gui);
            }
            if (_badge != null && !InventoryGui.IsVisible()) return;

            // On SeneaL UI's panel when it draws its own, else on the game's.
            RectTransform seneaL = SeneaLCompat.InventoryPanel;
            if (_badge == null || _badge.Panel != (seneaL != null ? seneaL : gui.m_player))
            {
                if (_badge != null) Object.Destroy(_badge.gameObject);
                _badge = seneaL != null
                    ? Badge.CreateNextTo(seneaL, SeneaLLook(seneaL, BoxOf(gui.m_player, gui.m_weight)), SeneaLBadgeSize, "StorageMaster_Badge", _menu,
                        Plugin.InventoryMenuOpen, Plugin.InventoryBadgeX, Plugin.InventoryBadgeY)
                    : Badge.Create(gui.m_player, BoxOf(gui.m_player, gui.m_weight), "StorageMaster_Badge", _menu,
                        Plugin.InventoryMenuOpen, Plugin.InventoryBadgeX, Plugin.InventoryBadgeY, gui.m_armor != null ? BoxOf(gui.m_player, gui.m_armor) : null);
            }
            else _badge.Apply();

            if (_chestMenu == null) return;
            RectTransform seneaLChest = SeneaLCompat.ChestPanel;
            // SeneaL UI can draw its chest panel before its inventory panel, whose pill the badge looks like.
            RectTransform chestLook = seneaLChest != null ? SeneaLLook(seneaL, BoxOf(gui.m_container, gui.m_containerWeight)) : null;
            if (_chestBadge == null || _chestBadge.Panel != (seneaLChest != null ? seneaLChest : gui.m_container) || _chestBadgeLook != chestLook)
            {
                if (_chestBadge != null) Object.Destroy(_chestBadge.gameObject);
                _chestBadgeLook = chestLook;
                _chestBadge = seneaLChest != null
                    ? Badge.CreateNextTo(seneaLChest, chestLook, SeneaLBadgeSize, "StorageMaster_ChestBadge",
                        _chestMenu, Plugin.ChestMenuOpen, Plugin.ChestBadgeX, Plugin.ChestBadgeY)
                    : Badge.Create(gui.m_container, BoxOf(gui.m_container, gui.m_containerWeight), "StorageMaster_ChestBadge", _chestMenu,
                        Plugin.ChestMenuOpen, Plugin.ChestBadgeX, Plugin.ChestBadgeY);
            }
            // Next to SeneaL UI's chest panel it isn't hidden along with it, and neither is its menu.
            if (seneaLChest != null && _chestBadge.gameObject.activeSelf != SeneaLCompat.ChestShown)
            {
                _chestBadge.gameObject.SetActive(SeneaLCompat.ChestShown);
                if (!SeneaLCompat.ChestShown) _chestMenu.gameObject.SetActive(false);
            }
            if (_chestBadge.gameObject.activeInHierarchy)
            {
                // First, so the menu is kept on screen at its new size.
                UpdateChestMenu(gui.m_currentContainer);
                _chestBadge.Apply();
            }
        }

        /// <summary>
        /// SeneaL UI's armor pill in the header of its inventory panel, for the badges to look like; the game's box when
        /// SeneaL UI doesn't draw its inventory panel (yet).
        /// </summary>
        private static RectTransform SeneaLLook(RectTransform inventoryPanel, RectTransform gameBox)
        {
            Transform pill = inventoryPanel != null ? inventoryPanel.Find("chip_shield") : null;
            return pill != null ? (RectTransform)pill : gameBox;
        }

        private static void MakeMenus(InventoryGui gui)
        {
            RectTransform menu = _menu = Menu("StorageMaster_Menu", 7);
            _sort = Make(menu, "Sort", () => Sorter.SortPlayer(silent: false), 0,
                "Sorts everything below the hotbar and merges partial stacks.\nAlt + click a slot to lock it, so Sort leaves it alone.");
            _mode = Make(menu, "", Sorter.CycleMode, 1, "Click to change what Sort orders by.");
            _quickStack = Make(menu, "Quick stack", QuickStack.Run, 2,
                "Puts your items into nearby chests that already hold the same kind of item.\nThe hotbar, equipped items and locked slots stay with you.");
            _autoSort = Make(menu, "", Plugin.ToggleAutoSort, 3, "Sort automatically every time you pick something up.");
            _storage = Make(menu, "Storage", StorageWindow.Open, 4,
                "Opens every chest nearby in one window, to move, find, gather and tidy up items.");
            _info = Make(menu, "Info", InfoWindow.Open, 5, "How Storage Master works, and its hotkeys.");
            _options = Make(menu, "Options", OptionsWindow.Open, 6,
                "Hotkeys, settings, and the menu buttons' drag key and positions (Reset puts a menu button back).");

            if (gui.m_container != null && gui.m_containerWeight != null)
            {
                RectTransform chestMenu = _chestMenu = Menu("StorageMaster_ChestMenu", 2);
                _sortChest = Make(chestMenu, "Sort chest", () => Sorter.SortContainer(gui.m_currentContainer), 0,
                    "Sorts this chest (merging partial stacks) using the same order as your inventory.");
                _chestMode = Make(chestMenu, "", Sorter.CycleMode, 1, "Click to change what Sort and Sort chest order by.");
                _backpackQuickStack = Make(chestMenu, "Quick stack", QuickStack.RunBackpack, 2,
                    "Puts the items in your backpack into nearby chests that already hold the same kind of item.\nYour inventory stays as it is.");
                _backpackQuickStack.gameObject.SetActive(false);
                _chestMenuFor = ChestMenuFor.Chest;
            }
            Refresh();
        }

        /// <summary>
        /// Adventure Backpacks opens the backpack in the chest panel, where Sort chest sorts it (#6). When Quick stack leaves
        /// the backpack out, the backpack gets a Quick stack of its own.
        /// </summary>
        private static void UpdateChestMenu(Container open)
        {
            if (_sortChest == null) return;
            ChestMenuFor now = !AdventureBackpacksCompat.IsBackpack(open) ? ChestMenuFor.Chest
                : Plugin.QuickStackIncludesBackpack.Value ? ChestMenuFor.Backpack : ChestMenuFor.BackpackWithQuickStack;
            if (now == _chestMenuFor) return;
            _chestMenuFor = now;
            _sortChest.SetLabel(now == ChestMenuFor.Chest ? "Sort chest" : "Sort backpack");
            bool quickStack = now == ChestMenuFor.BackpackWithQuickStack;
            _backpackQuickStack.gameObject.SetActive(quickStack);
            _chestMenu.sizeDelta = new Vector2(_chestMenu.sizeDelta.x, MenuHeight(quickStack ? 3 : 2));
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
            menu.sizeDelta = new Vector2(ButtonW + 2 * Pad, MenuHeight(buttons));
            return menu;
        }

        private static float MenuHeight(int buttons) => buttons * ButtonH + (buttons - 1) * Gap + 2 * Pad;

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
