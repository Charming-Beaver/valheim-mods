using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using StorageMaster.UI;
using UnityEngine;
using UnityEngine.UI;

namespace StorageMaster
{
    /// <summary>
    /// Locked inventory slots: Sort, auto-sort and quick stack never touch what's in them.
    /// Alt + click a slot to lock or unlock it. Saved per character.
    /// </summary>
    public static class Locks
    {
        // Saved-data keys keep the mod's working title so existing locks, names and skips carry over.
        private const string Key = "InventoryTools.Locks";
        private const string OverlayName = "StorageMaster_Lock";

        private static readonly HashSet<Vector2i> _locked = new HashSet<Vector2i>();
        private static Player _loadedFor;
        private static Sprite _lockSprite;
        private static bool _searchedSprite;

        public static bool IsLocked(int x, int y)
        {
            EnsureLoaded();
            return _locked.Contains(new Vector2i(x, y));
        }

        public static void Toggle(Vector2i pos)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            EnsureLoaded();
            bool now = !_locked.Remove(pos);
            if (now) _locked.Add(pos);
            player.m_customData[Key] = string.Join(";", _locked.Select(p => $"{p.x},{p.y}"));
            player.Message(MessageHud.MessageType.TopLeft, now ? "Slot locked: Sort and quick stack will leave it alone" : "Slot unlocked");
        }

        private static void EnsureLoaded()
        {
            Player player = Player.m_localPlayer;
            if (player == _loadedFor) return;
            _loadedFor = player;
            _locked.Clear();
            if (player == null || !player.m_customData.TryGetValue(Key, out string data)) return;
            foreach (string part in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] xy = part.Split(',');
                if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y)) _locked.Add(new Vector2i(x, y));
            }
        }

        private static bool IsPlayerGrid(InventoryGrid grid) => InventoryGui.instance != null && grid == InventoryGui.instance.m_playerGrid;

        private static bool AltHeld() => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        [HarmonyPatch]
        private static class Patches
        {
            // Alt + click toggles the lock instead of picking the item up.
            [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.OnLeftDown)), HarmonyPrefix]
            private static bool OnLeftDown(InventoryGrid __instance, UIInputHandler clickHandler)
            {
                if (!AltHeld() || !IsPlayerGrid(__instance)) return true;
                Vector2i pos = __instance.GetButtonPos(clickHandler.gameObject);
                if (pos.x >= 0) Toggle(pos);
                return false;
            }

            [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.OnLeftClick)), HarmonyPrefix]
            private static bool OnLeftClick(InventoryGrid __instance)
            {
                return !(AltHeld() && IsPlayerGrid(__instance));
            }

            // Draw a lock marker on locked slots of the player grid.
            [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui)), HarmonyPostfix]
            private static void UpdateGui(InventoryGrid __instance)
            {
                if (IsPlayerGrid(__instance)) DrawOverlays(__instance);
            }
        }

        /// <summary>Shows the lock marker on locked slots of a grid that displays your inventory.</summary>
        public static void DrawOverlays(InventoryGrid grid)
        {
                foreach (InventoryElement element in grid.m_elements)
                {
                    if (element == null) continue;
                    bool locked = IsLocked(element.Position.x, element.Position.y);
                    Transform overlay = element.transform.Find(OverlayName);
                    if (overlay == null)
                    {
                        if (!locked) continue;
                        overlay = CreateOverlay(element.transform);
                    }
                    overlay.gameObject.SetActive(locked);
                }
        }

        private static Transform CreateOverlay(Transform slot)
        {
            if (!_searchedSprite)
            {
                _searchedSprite = true;
                string[] names = { "lock", "Lock", "icon_lock", "padlock", "locked", "Locked" };
                _lockSprite = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s != null && names.Contains(s.name));
            }

            RectTransform root = UIKit.Rect(slot, OverlayName).Fill();
            Image tint = root.gameObject.AddComponent<Image>();
            tint.color = new Color(1f, 0.631f, 0.235f, 0.18f);
            tint.raycastTarget = false;

            RectTransform badge = UIKit.Rect(root, "badge");
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1, 1);
            badge.anchoredPosition = new Vector2(-2, -2);
            badge.sizeDelta = new Vector2(16, 16);
            Image img = badge.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            if (_lockSprite != null)
            {
                img.sprite = _lockSprite;
                img.preserveAspect = true;
            }
            else
            {
                img.color = new Color(1f, 0.631f, 0.235f, 0.95f);
                badge.sizeDelta = new Vector2(8, 8);
            }
            return root;
        }
    }
}
