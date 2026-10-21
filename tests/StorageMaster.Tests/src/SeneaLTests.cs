using System;
using System.Reflection;
using BepInEx.Bootstrap;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    /// <summary>With SeneaL UI, Place stacks keeps what either mod keeps (#18).</summary>
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

        /// <summary>Locks or unlocks a slot with SeneaL UI's own locks; returns whether it's locked now.</summary>
        private static bool ToggleSeneaLLock(int x, int y)
        {
            Type slots = Chainloader.PluginInfos[SeneaLCompat.Guid].Instance.GetType().Assembly.GetType("SeneaLUI.Inv.Slots");
            MethodInfo toggle = slots.GetMethod("ToggleLock", BindingFlags.Public | BindingFlags.Static);
            return (bool)toggle.Invoke(null, new object[] { Mine, x, y });
        }
    }
}
