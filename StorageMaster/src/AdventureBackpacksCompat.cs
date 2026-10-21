using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace StorageMaster
{
    /// <summary>
    /// Vapok's Adventure Backpacks keeps the equipped backpack's items in an inventory of its own, which it opens in the
    /// chest panel. Quick stack takes items from it too (#48), and Sort chest sorts it when it's open (#6).
    /// Uses Adventure Backpacks' public API through reflection, so it isn't needed when Adventure Backpacks isn't installed.
    /// </summary>
    public static class AdventureBackpacksCompat
    {
        public const string Guid = "vapok.mods.adventurebackpacks";

        private static bool _looked;
        private static Func<Player, Inventory> _equipped;

        public static bool Active
        {
            get
            {
                Init();
                return _equipped != null;
            }
        }

        /// <summary>The inventory of the backpack you're wearing, or null without one.</summary>
        public static Inventory Backpack(Player player)
        {
            Init();
            return _equipped != null && player != null ? _equipped(player) : null;
        }

        /// <summary>True when the open "chest" is your backpack, which Adventure Backpacks opens in the chest panel.</summary>
        public static bool IsBackpack(Container container)
        {
            if (container == null) return false;
            Inventory backpack = Backpack(Player.m_localPlayer);
            return backpack != null && container.GetInventory() == backpack;
        }

        private static void Init()
        {
            if (_looked) return;
            _looked = true;
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info) || info.Instance == null) return;
            Type api = info.Instance.GetType().Assembly.GetType("AdventureBackpacks.API.ABAPI");
            MethodInfo equipped = api?.GetMethod("GetEquippedBackpackInventory", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Player) }, null);
            if (equipped == null)
            {
                Debug.LogWarning("[Storage Master] Adventure Backpacks is loaded, but its API wasn't found. Quick stack leaves your backpack out.");
                return;
            }
            _equipped = (Func<Player, Inventory>)Delegate.CreateDelegate(typeof(Func<Player, Inventory>), equipped);
        }
    }
}
