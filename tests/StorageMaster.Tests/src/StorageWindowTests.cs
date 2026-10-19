using System.Collections;
using System.Collections.Generic;
using System.Linq;
using StorageMaster.UI;
using UnityEngine;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    public static class StorageWindowTests
    {
        [Test]
        private static IEnumerator OpenClaimsNearbyChestsAndCloseReleasesThem()
        {
            Container a = SpawnChest(3f, 0f), b = SpawnChest(4f, 60f), c = SpawnChest(5f, 120f);
            Container far = SpawnChest(Plugin.StorageRange.Value + 3f, 180f);
            Put(a.GetInventory(), "Wood", 10, 0, 0);
            Put(b.GetInventory(), "Stone", 10, 0, 0);
            Put(Mine, "Resin", 5, 2, 2);

            yield return OpenAndWait();

            Assert.Equal(3, StorageWindow._cards.Count, "Chests in the window");
            Assert.True(StorageWindow._cards.All(card => card.Chest != far), "The chest out of range isn't in the window");
            Assert.Equal("a,b,c", string.Join(",", StorageWindow._cards.Select(card => card.Chest == a ? "a" : card.Chest == b ? "b" : "c")),
                "Nearest first");
            Assert.True(a.IsInUse() && b.IsInUse() && c.IsInUse(), "Chests are in use while the window is open");
            yield return Screenshot("storage-window");

            StorageWindow.Close();
            yield return null;
            Assert.True(!a.IsInUse() && !b.IsInUse() && !c.IsInUse(), "Chests are free again after closing");
        }

        [Test]
        private static IEnumerator OpensOnlyMaxChests()
        {
            Plugin.StorageMaxChests.Value = 2;
            Container a = SpawnChest(3f, 0f), b = SpawnChest(4f, 90f);
            SpawnChest(5f, 180f);

            yield return OpenAndWait();

            Assert.Equal(2, StorageWindow._cards.Count, "Chests in the window");
            Assert.True(StorageWindow._cards.Any(card => card.Chest == a) && StorageWindow._cards.Any(card => card.Chest == b), "The two nearest chests");
        }

        [Test]
        private static IEnumerator GatherHereCollectsFromChestsAndMe()
        {
            Container a = SpawnChest(3f, 0f), b = SpawnChest(4f, 90f), c = SpawnChest(5f, 180f);
            Put(a.GetInventory(), "Stone", 1, 0, 0);
            Put(b.GetInventory(), "Wood", 10, 0, 0);
            Put(c.GetInventory(), "Wood", 15, 3, 1);
            Put(Mine, "Wood", 5, 2, 2);
            Put(Mine, "Wood", 4, 0, 0);

            yield return OpenAndWait();
            Gather("Wood", a, includeMe: true);

            Assert.Equal(30, Count(a.GetInventory(), "Wood"), "Wood in the chosen chest");
            Assert.Equal(0, Count(b.GetInventory(), "Wood") + Count(c.GetInventory(), "Wood"), "Wood left in the other chests");
            Assert.Equal(4, Count(Mine, "Wood"), "Wood left on me (only the hotbar)");
        }

        [Test]
        private static IEnumerator GatherHereCanLeaveMyInventoryAlone()
        {
            Container a = SpawnChest(3f, 0f), b = SpawnChest(4f, 90f);
            Put(b.GetInventory(), "Wood", 10, 0, 0);
            Put(Mine, "Wood", 5, 2, 2);

            yield return OpenAndWait();
            Gather("Wood", a, includeMe: false);

            Assert.Equal(10, Count(a.GetInventory(), "Wood"), "Wood in the chosen chest");
            Assert.Equal(5, Count(Mine, "Wood"), "Wood left on me");
        }

        [Test]
        private static IEnumerator GatherHereSkipsChestsMarkedSkip()
        {
            Container a = SpawnChest(3f, 0f), b = SpawnChest(4f, 90f), c = SpawnChest(5f, 180f);
            Put(b.GetInventory(), "Wood", 10, 0, 0);
            Put(c.GetInventory(), "Wood", 15, 0, 0);
            Exclusions.Set(b, true);

            yield return OpenAndWait();
            Gather("Wood", a, includeMe: false);

            Assert.Equal(15, Count(a.GetInventory(), "Wood"), "Wood in the chosen chest");
            Assert.Equal(10, Count(b.GetInventory(), "Wood"), "Wood in the skipped chest");
        }

        [Test]
        private static IEnumerator GatherHereOverflowsIntoTheNextChest()
        {
            Container a = SpawnChest(3f, 0f), b = SpawnChest(4f, 90f), c = SpawnChest(5f, 180f);
            Inventory inv = a.GetInventory();
            int free = inv.GetWidth() * inv.GetHeight() - 1;
            for (int i = 0; i < free; i++) Put(inv, "Stone", 50, (i + 1) % inv.GetWidth(), (i + 1) / inv.GetWidth());
            Put(c.GetInventory(), "Wood", 50, 0, 0);
            Put(c.GetInventory(), "Wood", 50, 1, 0);
            Put(c.GetInventory(), "Wood", 50, 2, 0);

            yield return OpenAndWait();
            Gather("Wood", a, includeMe: false);

            Assert.Equal(50, Count(a.GetInventory(), "Wood"), "Wood in the chosen chest (one free slot)");
            Assert.Equal(100, Count(b.GetInventory(), "Wood"), "Wood that overflowed into the next chest");
            Assert.Equal(0, Count(c.GetInventory(), "Wood"), "Wood left where it was");
        }

        [Test]
        private static IEnumerator TidyUpBringsSpreadItemsTogetherAndSorts()
        {
            Container a = SpawnChest(3f, 0f), b = SpawnChest(4f, 90f), c = SpawnChest(5f, 180f);
            Put(a.GetInventory(), "Wood", 30, 4, 1);
            Put(b.GetInventory(), "Wood", 5, 0, 0);
            Put(b.GetInventory(), "Stone", 20, 2, 1);
            Put(c.GetInventory(), "Stone", 3, 1, 0);
            Put(c.GetInventory(), "Resin", 7, 3, 1);

            yield return OpenAndWait();
            StorageWindow.TidyUp();

            Assert.Equal(35, Count(a.GetInventory(), "Wood"), "All wood in the chest that had the most");
            Assert.Equal(23, Count(b.GetInventory(), "Stone"), "All stone in the chest that had the most");
            Assert.Equal(7, Count(c.GetInventory(), "Resin"), "Resin stays");
            Assert.Equal(0, Count(b.GetInventory(), "Wood") + Count(c.GetInventory(), "Stone"), "Nothing left behind");
            foreach (Container chest in new[] { a, b, c })
            {
                var items = InReadingOrder(chest.GetInventory());
                for (int i = 0; i < items.Count; i++)
                {
                    Assert.Equal(new Vector2i(i % chest.GetInventory().GetWidth(), i / chest.GetInventory().GetWidth()), items[i].m_gridPos,
                        $"Sorted chest, item {i + 1}");
                }
            }
        }

        /// <summary>One mouse wheel notch scrolls the chest list by about a row of chests, not a few pixels (#3).</summary>
        [Test]
        private static IEnumerator OneWheelNotchScrollsTheChestList()
        {
            for (int i = 0; i < 16; i++) SpawnChest(3f + i % 3, i * 360f / 16);

            yield return OpenAndWait();
            var moved = new float[1];
            yield return ScrollOneNotch(StorageWindow._cardList.parent.parent.GetComponent<UnityEngine.UI.ScrollRect>(), moved);

            Assert.True(moved[0] >= 100f, $"One notch scrolled {moved[0]:0.#} px");
        }

        /// <summary>
        /// Hovering over an item in the window shows the game's tooltip with its name and description, also with mods
        /// like SeneaL UI that blank tooltips on inventory grids they don't draw themselves (#21).
        /// </summary>
        [Test]
        private static IEnumerator HoveringAnItemShowsItsTooltip()
        {
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 10, 0, 0);
            Put(Mine, "Stone", 5, 2, 2);
            yield return OpenAndWait();
            yield return Frames(3);

            InventoryGrid chestGrid = StorageWindow._cards[0].Grid;
            yield return AssertTooltipOnHover(chestGrid.GetElement(0, 0, chestGrid.m_width), "Wood");
            InventoryGrid myGrid = StorageWindow._playerGrid;
            yield return AssertTooltipOnHover(myGrid.GetElement(2, 2, myGrid.m_width), "Stone");
        }

        private static IEnumerator AssertTooltipOnHover(InventoryElement element, string prefab)
        {
            UITooltip tip = element.m_tooltip;
            // What Unity's event system does when the mouse enters the slot. The real cursor can't be moved reliably,
            // e.g. when the game window doesn't have focus.
            tip.OnPointerEnter(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { pointerEnter = tip.gameObject });
            // The game fills the shared tooltip right away and shows it half a second later, but only while the real
            // mouse is over the slot, so check what it was filled with. A few frames on, the window has updated its grids
            // again (which is when other mods blank the slot's tooltip) and it must still be there.
            yield return Frames(5);
            Assert.True(UITooltip.m_current == tip && UITooltip.m_tooltip != null,
                $"The game started showing the tooltip of {prefab} (slot tooltip: \"{tip.m_topic}\" / \"{tip.m_text}\")");
            string shown = string.Join(" | ", UITooltip.m_tooltip.GetComponentsInChildren<TMPro.TMP_Text>(true).Select(t => t.text));
            ItemDrop.ItemData item = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData;
            string name = Localization.instance.Localize(item.m_shared.m_name);
            string description = Localization.instance.Localize(item.m_shared.m_description);
            Assert.True(shown.Contains(name) && shown.Contains(description), $"Tooltip of {prefab} shows its name and description: \"{shown}\"");
            UITooltip.HideTooltip();
        }

        /// <summary>The window's and the inventory buttons' own tooltips have a tooltip to show (#21).</summary>
        [Test]
        private static IEnumerator ButtonTooltipsCanShow()
        {
            InventoryGui.instance.Show(SpawnChest());
            yield return WaitUntil(() => StorageMaster.InventoryButtons._sortChest != null, "the inventory buttons");
            InventoryGui.instance.Hide();
            yield return OpenAndWait();

            var roots = new List<GameObject> { StorageWindow._root, StorageMaster.InventoryButtons._sort.transform.parent.gameObject };
            var missing = roots.SelectMany(r => r.GetComponentsInChildren<UITooltip>(true))
                .Where(t => t.GetComponent<InventoryElement>() == null && t.m_tooltipPrefab == null)
                .Select(t => t.name).ToList();
            Assert.True(missing.Count == 0, $"{missing.Count} tooltips have nothing to show, e.g. {missing.FirstOrDefault()}");
        }

        // ------------------------------------------------------------------ helpers

        private static IEnumerator OpenAndWait()
        {
            StorageWindow.Open();
            Assert.True(StorageWindow.IsOpen, "Storage window open");
            yield return WaitUntil(() => StorageWindow._cards.All(card => card.Claimed), "every chest in the window to open");
        }

        /// <summary>Chooses an item and clicks Gather here on a chest's card.</summary>
        private static void Gather(string prefab, Container into, bool includeMe)
        {
            StorageWindow._selected = Name(prefab);
            StorageWindow._includeMe.isOn = includeMe;
            StorageWindow.Card card = StorageWindow._cards.First(c => c.Chest == into);
            StorageWindow.GatherInto(card);
        }
    }
}
