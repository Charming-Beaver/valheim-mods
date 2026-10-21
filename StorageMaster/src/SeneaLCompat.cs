using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace StorageMaster
{
    /// <summary>
    /// SeneaL UI decides by its own rules which items "Place stacks" leaves in your inventory: its locked slots, the hotbar
    /// (unless its TransferHotbar option is on) and its extra rows (equipment and quick slots). Storage Master keeps
    /// those too, on top of its own rules. Uses SeneaL UI's classes through reflection, so it isn't needed when SeneaL UI
    /// isn't installed.
    /// </summary>
    public static class SeneaLCompat
    {
        public const string Guid = "seneaL.valheim.ui";

        private static bool _looked;
        private static FieldInfo _active;
        private static Func<Inventory, bool> _ready;
        private static Func<Inventory, ItemDrop.ItemData, bool> _keep;

        /// <summary>True when SeneaL UI would leave this item of your inventory where it is.</summary>
        public static bool Keeps(Inventory inventory, ItemDrop.ItemData item)
        {
            Init();
            return _keep != null && (bool)_active.GetValue(null) && _ready(inventory) && _keep(inventory, item);
        }

        private static void Init()
        {
            if (_looked) return;
            _looked = true;
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info) || info.Instance == null) return;
            Assembly assembly = info.Instance.GetType().Assembly;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            FieldInfo active = assembly.GetType("SeneaLUI.Plugin")?.GetField("Active", flags);
            MethodInfo ready = assembly.GetType("SeneaLUI.Inv.Slots")?.GetMethod("Ready", flags, null, new[] { typeof(Inventory) }, null);
            MethodInfo keep = assembly.GetType("SeneaLUI.Inv.Transfer")?.GetMethod("Keep", flags, null, new[] { typeof(Inventory), typeof(ItemDrop.ItemData) }, null);
            if (active == null || ready == null || keep == null)
            {
                Debug.LogWarning("[Storage Master] SeneaL UI is loaded, but its slot rules weren't found. Quick stack and Place stacks may move items from its locked slots and extra rows.");
                return;
            }
            _active = active;
            _ready = (Func<Inventory, bool>)Delegate.CreateDelegate(typeof(Func<Inventory, bool>), ready);
            _keep = (Func<Inventory, ItemDrop.ItemData, bool>)Delegate.CreateDelegate(typeof(Func<Inventory, ItemDrop.ItemData, bool>), keep);
        }
    }
}
