using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using StorageMaster.UI;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    /// <summary>
    /// With SeneaL UI, Place stacks keeps what either mod keeps (#18), and Sort and the Storage window leave its extra
    /// rows (equipment and quick slots) and its unusable slots alone (#62).
    /// </summary>
    public static class SeneaLTests
    {
        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static void PlaceStacksKeepsBothModsSlots()
        {
            Container chest = SpawnChest();
            Inventory inv = chest.GetInventory();
            Put(inv, "Wood", 1, 0, 0);
            Put(Mine, "Wood", 5, 0, 0);
            Put(Mine, "Wood", 7, 0, 1);
            Put(Mine, "Wood", 3, 1, 1);
            Put(Mine, "Wood", 10, 4, 2);
            Lock(0, 1);
            Assert.True(ToggleSeneaLLock(1, 1), "SeneaL UI locked the slot");
            try
            {
                inv.StackAll(Mine, message: false);
            }
            finally
            {
                ToggleSeneaLLock(1, 1); // SeneaL UI saves its locks, so they'd stay for the next tests
            }

            Assert.Equal(11, Count(inv, "Wood"), "Wood in the chest (only the free stack)");
            Assert.Equal(15, Count(Mine, "Wood"), "Wood kept in the hotbar, our locked slot and SeneaL's locked slot");
        }

        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static void SortLeavesSeneaLRowsAndUnusableSlotsAlone()
        {
            int rows = SeneaLCompat.PlayerRows(Mine);
            Assert.True(rows < Mine.GetHeight(), $"SeneaL UI adds rows below the regular ones ({rows} of {Mine.GetHeight()})");
            // Its rows below the regular ones are equipment, then quick slots for food, then slots that are off by default.
            ItemDrop.ItemData food = Put(Mine, "Raspberry", 5, 0, rows + 1);
            Assert.Equal(new Vector2i(0, rows + 1), food.m_gridPos, "Raspberries in SeneaL's first quick slot");
            // The last 4 slots of the regular rows can't be used, as with a slot count that doesn't fill the last row.
            int width = Mine.GetWidth(), usable = rows * width - 4;
            FieldInfo cap = SlotsType.GetField("Cap", BindingFlags.Public | BindingFlags.Static);
            object oldCap = cap.GetValue(null);
            cap.SetValue(null, usable);
            try
            {
                // Fills every usable slot below the hotbar, so sorting the raspberries in too would put them in an
                // unusable slot (they sort after the clubs).
                for (int i = width; i < usable; i++) Put(Mine, "Club", 1, i % width, i / width);

                Sorter.SortPlayer(silent: true);

                Assert.Equal(new Vector2i(0, rows + 1), food.m_gridPos, "Raspberries stay in SeneaL's quick slot");
                foreach (ItemDrop.ItemData item in Mine.GetAllItems().Where(i => i != food))
                {
                    Vector2i pos = item.m_gridPos;
                    Assert.True(pos.y * width + pos.x < usable, $"{item.m_shared.m_name} sorted into a usable slot, not {pos.x},{pos.y}");
                }
            }
            finally
            {
                cap.SetValue(null, oldCap);
            }
        }

        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static IEnumerator StorageWindowShowsOnlyTheRegularRows()
        {
            StorageWindow.Open();
            yield return WaitUntil(() => StorageWindow._cards.All(c => c.Claimed), "the chests to open");
            yield return Frames(3);

            int rows = SeneaLCompat.PlayerRows(Mine);
            Assert.True(rows < Mine.GetHeight(), $"SeneaL UI adds rows below the regular ones ({rows} of {Mine.GetHeight()})");
            var shown = StorageWindow._playerGrid.m_elements.Where(e => e.gameObject.activeSelf).ToList();
            Assert.Equal(Mine.GetWidth() * rows, shown.Count, "Slots shown");
            Assert.True(shown.All(e => e.Position.y < rows), "Only regular rows shown");
        }

        private static Type SlotsType => Chainloader.PluginInfos[SeneaLCompat.Guid].Instance.GetType().Assembly.GetType("SeneaLUI.Inv.Slots");

        /// <summary>Locks or unlocks a slot with SeneaL UI's own locks; returns whether it's locked now.</summary>
        private static bool ToggleSeneaLLock(int x, int y)
        {
            MethodInfo toggle = SlotsType.GetMethod("ToggleLock", BindingFlags.Public | BindingFlags.Static);
            return (bool)toggle.Invoke(null, new object[] { Mine, x, y });
        }
    }
}
