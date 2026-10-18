using System.Collections;
using System.Linq;
using UnityEngine;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    public static class SorterTests
    {
        [Test]
        private static void SortPlayer_SortsByNameBelowTheHotbar()
        {
            ItemDrop.ItemData club = Put(Mine, "Club", 1, 0, 0);
            ItemDrop.ItemData hotbarWood = Put(Mine, "Wood", 10, 3, 0);
            Put(Mine, "Stone", 10, 5, 2);
            Put(Mine, "Resin", 10, 1, 1);
            Put(Mine, "Flint", 10, 7, 3);
            Put(Mine, "Raspberry", 10, 2, 3);

            Sorter.SortPlayer(silent: true);

            Assert.Equal(new Vector2i(0, 0), club.m_gridPos, "Club in the hotbar");
            Assert.Equal(new Vector2i(3, 0), hotbarWood.m_gridPos, "Wood in the hotbar");
            AssertPacked(Mine, fromRow: 1);
            AssertSortedByName(InReadingOrder(Mine, fromRow: 1).ToArray());
        }

        [Test]
        private static void SortPlayer_MergesPartialStacks()
        {
            Put(Mine, "Wood", 30, 0, 1);
            Put(Mine, "Wood", 30, 4, 2);
            Put(Mine, "Wood", 5, 6, 3);

            Sorter.SortPlayer(silent: true);

            var wood = InReadingOrder(Mine);
            Assert.Equal(65, Count(Mine, "Wood"), "Wood in total");
            Assert.Equal(2, wood.Count, "Wood stacks");
            Assert.Equal(50, wood[0].m_stack, "First stack");
            Assert.Equal(15, wood[1].m_stack, "Second stack");
        }

        [Test]
        private static void SortPlayer_LeavesLockedSlotsAlone()
        {
            Lock(0, 1);
            ItemDrop.ItemData locked = Put(Mine, "Wood", 10, 0, 1);
            Put(Mine, "Wood", 10, 5, 2);
            Put(Mine, "Stone", 10, 3, 3);

            Sorter.SortPlayer(silent: true);

            Assert.Equal(new Vector2i(0, 1), locked.m_gridPos, "Locked wood's slot");
            Assert.Equal(10, locked.m_stack, "Locked wood's stack (nothing merged into it)");
            Assert.Equal(20, Count(Mine, "Wood"), "Wood in total");
            var free = InReadingOrder(Mine, fromRow: 1).Where(i => i != locked).ToList();
            Assert.Equal(new Vector2i(1, 1), free[0].m_gridPos, "First sorted item, right after the locked slot");
            Assert.Equal(new Vector2i(2, 1), free[1].m_gridPos, "Second sorted item");
        }

        [Test]
        private static void SortPlayer_ByAmount()
        {
            Plugin.SortMode.Value = Sorter.Mode.Amount;
            Put(Mine, "Wood", 5, 0, 1);
            Put(Mine, "Stone", 40, 1, 1);
            Put(Mine, "Resin", 20, 2, 1);

            Sorter.SortPlayer(silent: true);

            var stacks = InReadingOrder(Mine).Select(i => i.m_stack).ToArray();
            Assert.Equal("40,20,5", string.Join(",", stacks), "Stacks in order");
        }

        [Test]
        private static void SortPlayer_ByType()
        {
            Plugin.SortMode.Value = Sorter.Mode.Type;
            Put(Mine, "TrophyDeer", 1, 0, 1);
            Put(Mine, "Wood", 5, 1, 1);
            Put(Mine, "Raspberry", 5, 2, 1);
            Put(Mine, "Club", 1, 3, 1);

            Sorter.SortPlayer(silent: true);

            // Weapons, then food, then materials, then trophies.
            string order = string.Join(",", InReadingOrder(Mine).Select(i => i.m_dropPrefab.name));
            Assert.Equal("Club,Raspberry,Wood,TrophyDeer", order, "Order");
        }

        [Test]
        private static void SortContainer_SortsAndMerges()
        {
            Container chest = SpawnChest();
            Inventory inv = chest.GetInventory();
            Put(inv, "Wood", 20, 3, 1);
            Put(inv, "Stone", 5, 0, 0);
            Put(inv, "Wood", 20, 4, 0);
            Put(inv, "Resin", 7, 1, 1);

            Sorter.SortContainer(chest, silent: true);

            Assert.Equal(3, inv.GetAllItems().Count, "Stacks after merging");
            Assert.Equal(40, Count(inv, "Wood"), "Wood");
            AssertPacked(inv, fromRow: 0);
            AssertSortedByName(InReadingOrder(inv).ToArray());
        }

        [Test]
        private static IEnumerator AutoSort_SortsOnPickup()
        {
            Plugin.AutoSort.Value = true;
            Put(Mine, "Stone", 10, 6, 3);
            Put(Mine, "Wood", 10, 2, 2);
            ItemDrop drop = ItemDrop.DropItem(NewItem("Resin", 5), 5, Me.transform.position + Vector3.up, Quaternion.identity);
            yield return Frames(2);

            Assert.True(Me.Pickup(drop.gameObject, autoequip: false, autoPickupDelay: false), "Picked up the resin");
            yield return null;

            Assert.Equal(5, Count(Mine, "Resin"), "Resin picked up");
            AssertPacked(Mine, fromRow: 1);
            AssertSortedByName(InReadingOrder(Mine, fromRow: 1).ToArray());
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The items fill the slots from the given row on without gaps.</summary>
        private static void AssertPacked(Inventory inv, int fromRow)
        {
            var items = InReadingOrder(inv, fromRow);
            for (int i = 0; i < items.Count; i++)
            {
                var expected = new Vector2i(i % inv.GetWidth(), fromRow + i / inv.GetWidth());
                Assert.Equal(expected, items[i].m_gridPos, $"Slot of item {i + 1} ({Describe(inv)})");
            }
        }

        private static void AssertSortedByName(ItemDrop.ItemData[] items)
        {
            for (int i = 1; i < items.Length; i++)
            {
                string a = Localized(items[i - 1]), b = Localized(items[i]);
                Assert.True(string.Compare(a, b, System.StringComparison.CurrentCultureIgnoreCase) <= 0, $"\"{a}\" comes before \"{b}\"");
            }
        }
    }
}
