using System.Collections;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    public static class QuickStackTests
    {
        [Test]
        private static IEnumerator MovesItemsTheChestAlreadyHolds()
        {
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 1, 0, 0);
            Put(Mine, "Wood", 20, 2, 2);
            Put(Mine, "Stone", 10, 3, 2);

            QuickStack.Run();
            yield return WaitUntil(() => Count(chest.GetInventory(), "Wood") == 21, "21 wood in the chest");

            Assert.Equal(0, Count(Mine, "Wood"), "Wood left on me");
            Assert.Equal(10, Count(Mine, "Stone"), "Stone stays (the chest has none)");
            Assert.Equal(0, Count(chest.GetInventory(), "Stone"), "Stone in the chest");
        }

        [Test]
        private static IEnumerator KeepsHotbarLockedAndEquippedItems()
        {
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 1, 0, 0);
            Put(chest.GetInventory(), "Club", 1, 1, 0);
            Put(Mine, "Wood", 5, 0, 0);
            Lock(0, 1);
            Put(Mine, "Wood", 7, 0, 1);
            Put(Mine, "Wood", 10, 4, 2);
            ItemDrop.ItemData club = Put(Mine, "Club", 1, 1, 2);
            Me.EquipItem(club);

            QuickStack.Run();
            yield return WaitUntil(() => Count(chest.GetInventory(), "Wood") == 11, "11 wood in the chest");
            yield return Frames(5);

            Assert.Equal(11, Count(chest.GetInventory(), "Wood"), "Wood in the chest (only the free stack)");
            Assert.Equal(12, Count(Mine, "Wood"), "Wood kept in the hotbar and the locked slot");
            Assert.True(Mine.ContainsItem(club), "Equipped club stays with me");
        }

        [Test]
        private static IEnumerator MovesTheHotbarWhenAllowed()
        {
            Plugin.QuickStackSkipsHotbar.Value = false;
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 1, 0, 0);
            Put(Mine, "Wood", 5, 0, 0);

            QuickStack.Run();
            yield return WaitUntil(() => Count(chest.GetInventory(), "Wood") == 6, "6 wood in the chest");
        }

        [Test]
        private static IEnumerator SkipsChestsMarkedSkipAndOutOfRange()
        {
            Container skipped = SpawnChest(3f);
            Container far = SpawnChest(Plugin.QuickStackRange.Value + 3f, 90f);
            Put(skipped.GetInventory(), "Wood", 1, 0, 0);
            Put(far.GetInventory(), "Wood", 1, 0, 0);
            Exclusions.Set(skipped, true);
            Put(Mine, "Wood", 10, 2, 2);

            QuickStack.Run();
            yield return Frames(30);

            Assert.Equal(10, Count(Mine, "Wood"), "Wood left on me");
            Assert.Equal(1, Count(skipped.GetInventory(), "Wood"), "Wood in the skipped chest");
            Assert.Equal(1, Count(far.GetInventory(), "Wood"), "Wood in the far chest");
        }

        /// <summary>The game's own "Place stacks" button keeps locked slots too (but not the hotbar).</summary>
        [Test]
        private static void PlaceStacksKeepsLockedSlots()
        {
            Container chest = SpawnChest();
            Inventory inv = chest.GetInventory();
            Put(inv, "Wood", 1, 0, 0);
            Put(Mine, "Wood", 5, 0, 0);
            Lock(0, 1);
            Put(Mine, "Wood", 7, 0, 1);
            Put(Mine, "Wood", 10, 4, 2);

            inv.StackAll(Mine, message: false);

            Assert.Equal(16, Count(inv, "Wood"), "Wood in the chest (hotbar and free stack)");
            Assert.Equal(7, Count(Mine, "Wood"), "Wood kept in the locked slot");
        }
    }
}
