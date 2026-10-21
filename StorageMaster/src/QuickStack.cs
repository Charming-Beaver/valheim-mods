using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StorageMaster
{
    /// <summary>Puts items into nearby chests that already contain the same kind of item.</summary>
    public static class QuickStack
    {
        private static bool _ours;
        private static int _moved, _chests, _busy;
        private static float _reportAt = -1f;

        public static void Run()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            if (InventoryGui.instance != null && InventoryGui.instance.m_dragItem != null)
            {
                Plugin.Show("Put down the item you are holding first");
                return;
            }

            var names = MovableNames(player);
            // Pressed again before the last summary showed: add to it rather than losing what was already moved.
            if (_reportAt < 0f) _moved = _chests = _busy = 0;
            int asked = 0;
            if (names.Count > 0)
            {
                foreach (Container c in ChestAccess.FindNearby(Plugin.QuickStackRange.Value))
                {
                    if (Exclusions.IsExcluded(c) || !ContainsAny(c.GetInventory(), names)) continue;
                    asked++;
                    Container chest = c;
                    ChestAccess.Request(chest, granted =>
                    {
                        if (granted) StackInto(chest.GetInventory());
                        else _busy++;
                    });
                }
            }

            if (asked == 0)
            {
                Plugin.Show("No nearby chest holds any of your items");
                return;
            }
            // Answers can come from other players' games, so the summary waits a moment.
            _reportAt = Time.time + 1f;
        }

        /// <summary>Stacks matching items from your inventory into a chest you already own; returns how many moved.</summary>
        public static int StackInto(Inventory chest)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return 0;
            _ours = true;
            int moved;
            try
            {
                moved = chest.StackAll(player.GetInventory(), message: false);
            }
            finally
            {
                _ours = false;
            }
            if (moved > 0)
            {
                _moved += moved;
                _chests++;
            }
            return moved;
        }

        public static void Update()
        {
            if (_reportAt < 0f || Time.time < _reportAt) return;
            _reportAt = -1f;
            string text = _moved > 0
                ? $"Quick stacked {_moved} item{(_moved == 1 ? "" : "s")} into {_chests} chest{(_chests == 1 ? "" : "s")}"
                : "Nothing could be stacked (the chests are full)";
            if (_busy > 0) text += $"\n<size=70%>{_busy} chest{(_busy == 1 ? " was" : "s were")} in use by someone else</size>";
            Plugin.Show(text);
            if (_moved > 0) InventoryGui.instance?.m_moveItemEffects.Create(Player.m_localPlayer.transform.position, Quaternion.identity);
        }

        private static HashSet<string> MovableNames(Player player)
        {
            var names = new HashSet<string>();
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (CanMove(player, item)) names.Add(item.m_shared.m_name);
            }
            return names;
        }

        private static bool ContainsAny(Inventory inventory, HashSet<string> names)
        {
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (names.Contains(item.m_shared.m_name)) return true;
            }
            return false;
        }

        /// <summary>Equipped items, locked slots and (by default) the hotbar row always stay with you.</summary>
        public static bool CanMove(Player player, ItemDrop.ItemData item)
        {
            return CanPlace(player, player.GetInventory(), item) && !(item.m_gridPos.y == 0 && Plugin.QuickStackSkipsHotbar.Value);
        }

        /// <summary>What "Place stacks" may move: not equipped items, locked slots, or other mods' slots that stay put.</summary>
        private static bool CanPlace(Player player, Inventory inventory, ItemDrop.ItemData item)
        {
            if (player.IsItemEquiped(item)) return false;
            if (Locks.IsLocked(item.m_gridPos.x, item.m_gridPos.y)) return false;
            if (ExtraSlotsCompat.IsOutsideRegularRows(inventory, item.m_gridPos)) return false;
            return !SeneaLCompat.Keeps(inventory, item);
        }

        [HarmonyPatch]
        private static class Patches
        {
            // The game's "Place stacks" moves everything that matches. Locked slots are always kept,
            // and for quick stack also the hotbar. First, so other mods' patches know to skip this call before theirs
            // run (see OtherPatches); the finalizer ends that.
            [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll)), HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static bool StackAll(Inventory __instance, Inventory fromInventory, bool message, ref int __result, MethodBase __originalMethod)
            {
                Player player = Player.m_localPlayer;
                if (player == null || fromInventory != player.GetInventory()) return true;
                OtherPatches.Begin(__originalMethod);

                int before = __instance.CountItems(null);
                foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(fromInventory.GetAllItems()))
                {
                    bool allowed = _ours ? CanMove(player, item) : CanPlace(player, fromInventory, item);
                    if (allowed && __instance.ContainsItemByName(item.m_shared.m_name) && __instance.AddItem(item))
                    {
                        fromInventory.RemoveItem(item);
                    }
                }
                __result = __instance.CountItems(null) - before;

                if (!_ours && message)
                {
                    player.Message(MessageHud.MessageType.Center, __result > 0 ? "$msg_stackall " + __result : "$msg_stackall_none");
                }
                __instance.Changed();
                fromInventory.Changed();
                Game.instance.IncrementPlayerStat(PlayerStatType.PlaceStacks);
                return false;
            }

            [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll)), HarmonyFinalizer]
            private static void StackAllDone(MethodBase __originalMethod) => OtherPatches.End(__originalMethod);
        }
    }
}
