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

            inv.StackAll(Mine, message: false);

            Assert.Equal(11, Count(inv, "Wood"), "Wood in the chest (only the free stack)");
            Assert.Equal(15, Count(Mine, "Wood"), "Wood kept in the hotbar and both locked slots");
        }

        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static void AltClickLocksOnlyWithSeneaL()
        {
            Assert.True(!Locks.HandlesAltClick(InventoryGui.instance.m_playerGrid), "Alt + click on the inventory is left to SeneaL UI");

            Lock(2, 3); // as Alt + click in the Storage window
            Assert.True(SeneaLLocked(2, 3), "The Storage window locks the slot with SeneaL UI's lock");
            Assert.True(!Me.m_customData.TryGetValue(LocksKey, out string own) || own == "", "No Storage Master lock of its own");
            Lock(2, 3);
            Assert.True(!SeneaLLocked(2, 3), "The Storage window unlocks SeneaL UI's lock");
        }

        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static void SortKeepsSeneaLsLockedSlots()
        {
            ItemDrop.ItemData wood = Put(Mine, "Wood", 5, 6, 2);
            Put(Mine, "Stone", 5, 3, 3);
            Assert.True(ToggleSeneaLLock(6, 2), "SeneaL UI locked the slot");

            Sorter.SortPlayer(silent: true);

            Assert.Equal(new Vector2i(6, 2), wood.m_gridPos, "Wood stays in SeneaL's locked slot");
            Assert.True(Mine.GetItemAt(0, 1)?.m_shared.m_name == "$item_stone", "Stone sorted to the first free slot");
        }

        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static void EarlierLocksAreHandedOverToSeneaL()
        {
            // Locked with Storage Master before installing SeneaL UI, read back as after logging in.
            Me.m_customData[LocksKey] = "2,3;4,1";
            Locks._loadedFor = null;

            Assert.True(Locks.IsLocked(2, 3) && Locks.IsLocked(4, 1), "Earlier locks are still locked");
            Assert.True(SeneaLLocked(2, 3) && SeneaLLocked(4, 1), "They're SeneaL UI's locks now");
            Assert.Equal("", Me.m_customData[LocksKey], "Storage Master's own locks");
            ToggleSeneaLLock(2, 3); // SeneaL UI's Alt + click
            Assert.True(!Locks.IsLocked(2, 3), "SeneaL UI's Alt + click unlocks them");
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

        private static bool SeneaLLocked(int x, int y)
        {
            MethodInfo locked = SlotsType.GetMethod("Locked", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int), typeof(int) }, null);
            return (bool)locked.Invoke(null, new object[] { x, y });
        }

        /// <summary>Locks or unlocks a slot with SeneaL UI's own locks; returns whether it's locked now.</summary>
        private static bool ToggleSeneaLLock(int x, int y)
        {
            MethodInfo toggle = SlotsType.GetMethod("ToggleLock", BindingFlags.Public | BindingFlags.Static);
            return (bool)toggle.Invoke(null, new object[] { Mine, x, y });
        }
    }
}
