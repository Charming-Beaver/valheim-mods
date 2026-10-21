using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace StorageMaster
{
    /// <summary>
    /// SeneaL UI decides by its own rules which items "Place stacks" leaves in your inventory: its locked slots, the hotbar
    /// (unless its TransferHotbar option is on) and its extra rows (equipment and quick slots). Storage Master keeps
    /// those too, on top of its own rules. The extra rows sit below the regular ones, and the regular rows can end with
    /// slots you can't use (past its slot count), so Sort and the Storage window leave both alone.
    /// SeneaL UI also locks slots with Alt + click, so while it's on, its locks are the only ones: see Locks. Uses SeneaL
    /// UI's classes through reflection, so it isn't needed when SeneaL UI isn't installed.
    /// </summary>
    public static class SeneaLCompat
    {
        public const string Guid = "seneaL.valheim.ui";

        private static bool _looked;
        private static FieldInfo _active;
        private static Func<Inventory, bool> _ready;
        private static Func<Inventory, ItemDrop.ItemData, bool> _keep;
        private static Func<Inventory, int> _mainRows;
        private static Func<Inventory, int, int, bool> _dead;
        private static Func<int, int, bool> _locked;
        private static Func<Inventory, int, int, bool> _toggleLock;
        private static Func<Inventory, HashSet<int>> _locksOf;
        private static Action _saveLocks;

        /// <summary>True when SeneaL UI is installed (it can still be switched off, or not laid out an inventory yet).</summary>
        public static bool Active
        {
            get
            {
                Init();
                return _ready != null;
            }
        }

        /// <summary>True when SeneaL UI would leave this item of your inventory where it is.</summary>
        public static bool Keeps(Inventory inventory, ItemDrop.ItemData item) => Ready(inventory) && _keep != null && _keep(inventory, item);

        /// <summary>How many rows of the player inventory are regular inventory (all of them without SeneaL UI).</summary>
        public static int PlayerRows(Inventory inventory)
        {
            return Ready(inventory) && _mainRows != null ? Mathf.Clamp(_mainRows(inventory), 1, inventory.GetHeight()) : inventory.GetHeight();
        }

        /// <summary>True for a slot of the regular rows that SeneaL UI doesn't let you use (past its slot count).</summary>
        public static bool IsDead(Inventory inventory, Vector2i pos) => Ready(inventory) && _dead != null && _dead(inventory, pos.x, pos.y);

        /// <summary>True when SeneaL UI locks the slots of your inventory (instead of Storage Master).</summary>
        public static bool HasLocks(Inventory inventory) => Ready(inventory) && _toggleLock != null;

        /// <summary>True for a slot of your inventory that SeneaL UI has locked.</summary>
        public static bool IsLocked(Inventory inventory, Vector2i pos) => HasLocks(inventory) && _locked(pos.x, pos.y);

        /// <summary>Locks or unlocks a slot of your inventory with SeneaL UI's locks; returns whether it's locked now.</summary>
        public static bool ToggleLock(Inventory inventory, Vector2i pos) => HasLocks(inventory) && _toggleLock(inventory, pos.x, pos.y);

        /// <summary>Locks these slots of your inventory with SeneaL UI's locks (it saves them on your character).</summary>
        public static void AddLocks(Inventory inventory, IEnumerable<Vector2i> slots)
        {
            if (!HasLocks(inventory)) return;
            HashSet<int> locks = _locksOf(inventory);
            foreach (Vector2i pos in slots) locks.Add(pos.y * 128 + pos.x); // SeneaL UI's key for a slot
            _saveLocks();
        }

        /// <summary>True when SeneaL UI is switched on and has laid out this inventory with its extra rows.</summary>
        private static bool Ready(Inventory inventory)
        {
            Init(); // first, as it sets the other delegates too
            return _ready != null && (bool)_active.GetValue(null) && _ready(inventory);
        }

        private static void Init()
        {
            if (_looked) return;
            _looked = true;
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info) || info.Instance == null) return;
            Assembly assembly = info.Instance.GetType().Assembly;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            Type slots = assembly.GetType("SeneaLUI.Inv.Slots");
            FieldInfo active = assembly.GetType("SeneaLUI.Plugin")?.GetField("Active", flags);
            MethodInfo ready = slots?.GetMethod("Ready", flags, null, new[] { typeof(Inventory) }, null);
            MethodInfo keep = assembly.GetType("SeneaLUI.Inv.Transfer")?.GetMethod("Keep", flags, null, new[] { typeof(Inventory), typeof(ItemDrop.ItemData) }, null);
            MethodInfo mainRows = slots?.GetMethod("MainRows", flags, null, new[] { typeof(Inventory) }, null);
            MethodInfo dead = slots?.GetMethod("Dead", flags, null, new[] { typeof(Inventory), typeof(int), typeof(int) }, null);
            if (active == null || ready == null)
            {
                Debug.LogWarning("[Storage Master] SeneaL UI is loaded, but its slots weren't found. Sort, Quick stack and Place stacks may move items from its locked slots and extra rows.");
                return;
            }
            _active = active;
            _ready = (Func<Inventory, bool>)Delegate.CreateDelegate(typeof(Func<Inventory, bool>), ready);
            if (keep != null)
                _keep = (Func<Inventory, ItemDrop.ItemData, bool>)Delegate.CreateDelegate(typeof(Func<Inventory, ItemDrop.ItemData, bool>), keep);
            else
                Debug.LogWarning("[Storage Master] SeneaL UI's slot rules weren't found. Quick stack and Place stacks may move items from its locked slots and extra rows.");
            if (mainRows != null && dead != null)
            {
                _mainRows = (Func<Inventory, int>)Delegate.CreateDelegate(typeof(Func<Inventory, int>), mainRows);
                _dead = (Func<Inventory, int, int, bool>)Delegate.CreateDelegate(typeof(Func<Inventory, int, int, bool>), dead);
            }
            else
                Debug.LogWarning("[Storage Master] SeneaL UI's rows weren't found. Sort may move items into its extra rows.");
            MethodInfo locked = slots.GetMethod("Locked", flags, null, new[] { typeof(int), typeof(int) }, null);
            MethodInfo toggleLock = slots.GetMethod("ToggleLock", flags, null, new[] { typeof(Inventory), typeof(int), typeof(int) }, null);
            MethodInfo locksOf = slots.GetMethod("LocksOf", flags, null, new[] { typeof(Inventory) }, null);
            MethodInfo saveLocks = slots.GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (locked != null && toggleLock != null && locksOf?.ReturnType == typeof(HashSet<int>) && saveLocks != null)
            {
                _locked = (Func<int, int, bool>)Delegate.CreateDelegate(typeof(Func<int, int, bool>), locked);
                _toggleLock = (Func<Inventory, int, int, bool>)Delegate.CreateDelegate(typeof(Func<Inventory, int, int, bool>), toggleLock);
                _locksOf = (Func<Inventory, HashSet<int>>)Delegate.CreateDelegate(typeof(Func<Inventory, HashSet<int>>), locksOf);
                _saveLocks = (Action)Delegate.CreateDelegate(typeof(Action), saveLocks);
            }
            else
                Debug.LogWarning("[Storage Master] SeneaL UI's locks weren't found. Alt + click may lock a slot in both mods.");
        }
    }
}
