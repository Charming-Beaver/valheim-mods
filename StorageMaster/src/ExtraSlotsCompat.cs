using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace StorageMaster
{
    /// <summary>
    /// Shudnal's ExtraSlots keeps its equipment, food, ammo, misc and quick slots as extra rows at the bottom of the player
    /// inventory. Those slots have fixed purposes (and hotkeys), so Storage Master leaves them alone, like the hotbar.
    /// Uses ExtraSlots' public API through reflection, so it isn't needed when ExtraSlots isn't installed.
    /// </summary>
    public static class ExtraSlotsCompat
    {
        public const string Guid = "shudnal.ExtraSlots";

        private static bool _looked;
        private static Func<int> _playerRows;
        private static Func<Vector2i, bool> _isSlot;

        public static bool Active
        {
            get
            {
                Init();
                return _isSlot != null;
            }
        }

        /// <summary>How many rows of the player inventory are regular inventory (all of them without ExtraSlots).</summary>
        public static int PlayerRows(Inventory inventory)
        {
            Init();
            return _playerRows != null ? Mathf.Clamp(_playerRows(), 1, inventory.GetHeight()) : inventory.GetHeight();
        }

        /// <summary>True for a grid position of the player inventory that is one of ExtraSlots' slots.</summary>
        public static bool IsSlot(Vector2i pos)
        {
            Init();
            return _isSlot != null && _isSlot(pos);
        }

        /// <summary>True for a player inventory position outside the regular rows (ExtraSlots' slots).</summary>
        public static bool IsOutsideRegularRows(Inventory inventory, Vector2i pos) => pos.y >= PlayerRows(inventory) || IsSlot(pos);

        private static void Init()
        {
            if (_looked) return;
            _looked = true;
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info) || info.Instance == null) return;
            Type api = info.Instance.GetType().Assembly.GetType("ExtraSlots.API");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            MethodInfo rows = api?.GetMethod("GetInventoryHeightPlayer", flags, null, Type.EmptyTypes, null);
            MethodInfo slot = api?.GetMethod("IsGridPositionASlot", flags, null, new[] { typeof(Vector2i) }, null);
            if (rows == null || slot == null)
            {
                Debug.LogWarning("[Storage Master] ExtraSlots is loaded, but its API wasn't found. Its slots may get sorted.");
                return;
            }
            _playerRows = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), rows);
            _isSlot = (Func<Vector2i, bool>)Delegate.CreateDelegate(typeof(Func<Vector2i, bool>), slot);
        }
    }
}
