using System;
using System.Collections.Generic;
using UnityEngine;

namespace StorageMaster
{
    /// <summary>
    /// Sorting for the player inventory (skipping the hotbar row and locked slots) and for open chests.
    /// Partial stacks of the same item are merged first.
    /// </summary>
    public static class Sorter
    {
        public enum Mode { NameAsc, NameDesc, Type, Quality, Weight, Amount }

        private static readonly string[] Labels = { "Name A-Z", "Name Z-A", "Type", "Quality", "Weight", "Amount" };

        public static string ModeLabel => Labels[(int)Plugin.SortMode.Value];

        public static void CycleMode()
        {
            int count = Labels.Length;
            Plugin.SortMode.Value = (Mode)(((int)Plugin.SortMode.Value + 1) % count);
            InventoryButtons.Refresh();
        }

        public static void SortPlayer(bool silent)
        {
            Player player = Player.m_localPlayer;
            if (player == null || IsDragging(silent)) return;

            Inventory inventory = player.GetInventory();
            int width = inventory.GetWidth(), height = inventory.GetHeight();

            // Free slots: everything below the hotbar that isn't locked, in reading order.
            var slots = new List<Vector2i>();
            for (int y = 1; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (!Locks.IsLocked(x, y)) slots.Add(new Vector2i(x, y));

            var items = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item.m_gridPos.y >= 1 && !Locks.IsLocked(item.m_gridPos.x, item.m_gridPos.y)) items.Add(item);
            }
            if (items.Count == 0)
            {
                if (!silent) Plugin.Show("Nothing to sort");
                return;
            }

            int merged = MergeStacks(inventory, items);
            items.Sort(Compare);
            for (int i = 0; i < items.Count; i++) items[i].m_gridPos = slots[i];
            inventory.Changed();
            if (!silent) Report("Inventory", merged);
        }

        public static void SortContainer(Container container, bool silent = false)
        {
            if (container == null || IsDragging(silent)) return;
            if (!container.IsOwner())
            {
                Plugin.Show("$msg_inuse");
                return;
            }
            Inventory inventory = container.GetInventory();
            var items = new List<ItemDrop.ItemData>(inventory.GetAllItems());
            if (items.Count == 0) return;

            int merged = MergeStacks(inventory, items);
            items.Sort(Compare);
            int width = inventory.GetWidth();
            for (int i = 0; i < items.Count; i++) items[i].m_gridPos = new Vector2i(i % width, i / width);
            // Changed() makes the chest save itself, which shares the new order with other players.
            inventory.Changed();
            if (!silent) Report("Chest", merged);
        }

        private static bool IsDragging(bool silent)
        {
            if (InventoryGui.instance == null || InventoryGui.instance.m_dragItem == null) return false;
            if (!silent) Plugin.Show("Put down the item you are holding first");
            return true;
        }

        private static void Report(string what, int merged)
        {
            Plugin.Show($"{what} sorted by {ModeLabel}" + (merged > 0 ? $"\n<size=70%>{merged} stack{(merged == 1 ? "" : "s")} merged</size>" : ""));
        }

        /// <summary>Combines partial stacks of the same item the way the game does when picking things up.</summary>
        private static int MergeStacks(Inventory inventory, List<ItemDrop.ItemData> items)
        {
            int removed = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData target = items[i];
                int max = target.m_shared.m_maxStackSize;
                if (max <= 1 || target.m_stack >= max) continue;
                for (int j = i + 1; j < items.Count && target.m_stack < max; j++)
                {
                    ItemDrop.ItemData other = items[j];
                    if (other.m_shared.m_name != target.m_shared.m_name || other.m_quality != target.m_quality || other.m_worldLevel != target.m_worldLevel)
                    {
                        continue;
                    }
                    int move = Mathf.Min(max - target.m_stack, other.m_stack);
                    target.m_stack += move;
                    other.m_stack -= move;
                    if (other.m_cheated) target.m_cheated = true;
                    if (other.m_stack <= 0)
                    {
                        inventory.RemoveItem(other);
                        items.RemoveAt(j);
                        j--;
                        removed++;
                    }
                }
            }
            return removed;
        }

        private static int Compare(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            int c;
            switch (Plugin.SortMode.Value)
            {
                case Mode.NameDesc: return -ByName(a, b);
                case Mode.Type: c = TypeOrder(a).CompareTo(TypeOrder(b)); break;
                case Mode.Quality: c = b.m_quality.CompareTo(a.m_quality); break;
                case Mode.Weight: c = b.GetWeight().CompareTo(a.GetWeight()); break;
                case Mode.Amount: c = b.m_stack.CompareTo(a.m_stack); break;
                default: c = 0; break;
            }
            if (c != 0) return c;
            c = ByName(a, b);
            // Same name: better quality and bigger stacks first, so leftovers end up last.
            return c != 0 ? c : b.m_quality != a.m_quality ? b.m_quality.CompareTo(a.m_quality) : b.m_stack.CompareTo(a.m_stack);
        }

        private static int ByName(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            return string.Compare(Localization.instance.Localize(a.m_shared.m_name), Localization.instance.Localize(b.m_shared.m_name), StringComparison.CurrentCultureIgnoreCase);
        }

        private static int TypeOrder(ItemDrop.ItemData item)
        {
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow: return 0;
                case ItemDrop.ItemData.ItemType.Shield: return 1;
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable: return 2;
                case ItemDrop.ItemData.ItemType.Helmet: return 3;
                case ItemDrop.ItemData.ItemType.Chest: return 4;
                case ItemDrop.ItemData.ItemType.Legs: return 5;
                case ItemDrop.ItemData.ItemType.Hands: return 6;
                case ItemDrop.ItemData.ItemType.Shoulder: return 7;
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket: return 8;
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch: return 9;
                case ItemDrop.ItemData.ItemType.Consumable: return 10;
                case ItemDrop.ItemData.ItemType.Material: return 11;
                case ItemDrop.ItemData.ItemType.Fish: return 12;
                case ItemDrop.ItemData.ItemType.Trophy: return 13;
                default: return 14;
            }
        }
    }
}
