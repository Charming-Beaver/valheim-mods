using System.Collections;
using StorageMaster.UI;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    /// <summary>Opens each window so it can be looked at in BepInEx/StorageMaster.Tests.screenshots.</summary>
    public static class ScreenshotTests
    {
        [Test]
        private static IEnumerator InventoryButtons()
        {
            Put(Mine, "Wood", 30, 0, 1);
            Put(Mine, "Club", 1, 0, 0);
            Lock(0, 1);
            InventoryGui.instance.Show(null);
            yield return WaitUntil(InventoryGui.IsVisible, "inventory to open");
            yield return Frames(5);

            Assert.True(StorageMaster.InventoryButtons._sort != null && StorageMaster.InventoryButtons._sort.gameObject.activeInHierarchy, "Sort button shown");
            yield return Screenshot("inventory");
        }

        [Test]
        private static IEnumerator ChestButtons()
        {
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Stone", 10, 0, 0);
            InventoryGui.instance.Show(chest);
            yield return WaitUntil(InventoryGui.IsVisible, "chest to open");
            yield return Frames(5);

            Assert.True(StorageMaster.InventoryButtons._sortChest != null && StorageMaster.InventoryButtons._sortChest.gameObject.activeInHierarchy,
                "Sort chest button shown");
            yield return Screenshot("chest");
        }

        [Test]
        private static IEnumerator InfoWindowOpens()
        {
            InfoWindow.Open();
            yield return Frames(5);
            Assert.True(InfoWindow.IsOpen, "Info window open");
            yield return Screenshot("info-window");
        }

        /// <summary>One mouse wheel notch scrolls the Info window's text by a few lines (#3).</summary>
        [Test]
        private static IEnumerator OneWheelNotchScrollsTheInfoWindow()
        {
            InfoWindow.Open();
            var moved = new float[1];
            yield return ScrollOneNotch(InfoWindow._root.GetComponentInChildren<UnityEngine.UI.ScrollRect>(), moved);

            Assert.True(moved[0] >= 100f, $"One notch scrolled {moved[0]:0.#} px");
        }
    }
}
