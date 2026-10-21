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
    /// With Vapok's Adventure Backpacks (run-tests.ps1 -With Vapok-AdventureBackpacks): Quick stack takes items from the
    /// backpack you're wearing too (#48), and Sort chest sorts the backpack when it's open (#6).
    /// </summary>
    public static class AdventureBackpacksTests
    {
        private const string BackpackPrefab = "BackpackMeadows";

        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator QuickStackTakesItemsFromTheBackpack()
        {
            Assert.True(AdventureBackpacksCompat.Active, "Adventure Backpacks' API found");
            Inventory backpack = WearBackpack();
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 1, 0, 0);
            Put(chest.GetInventory(), "Stone", 1, 1, 0);
            Put(backpack, "Wood", 20, 0, 0);
            Put(backpack, "Resin", 5, 1, 0);
            Put(Mine, "Stone", 10, 2, 2);

            QuickStack.Run();
            yield return WaitUntil(() => Count(chest.GetInventory(), "Wood") == 21, "21 wood in the chest");
            yield return WaitUntil(() => QuickStack._reportAt < 0f, "the quick stack summary");

            Assert.Equal(0, Count(backpack, "Wood"), "Wood left in the backpack");
            Assert.Equal(5, Count(backpack, "Resin"), "Resin stays in the backpack (the chest has none)");
            Assert.Equal(11, Count(chest.GetInventory(), "Stone"), "Stone from the inventory in the chest");
            string shown = MessageHud.instance.m_messageCenterText.text;
            Assert.True(shown.StartsWith("Quick stacked 30 items into 1 chest"), $"Summary shown: \"{shown}\"");
        }

        /// <summary>Only the backpack holds something the chest has: the chest is still found.</summary>
        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator QuickStackWithOnlyTheBackpackMatching()
        {
            Inventory backpack = WearBackpack();
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 1, 0, 0);
            Put(backpack, "Wood", 20, 2, 0);

            QuickStack.Run();
            yield return WaitUntil(() => Count(chest.GetInventory(), "Wood") == 21, "21 wood in the chest");
        }

        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator QuickStackLeavesTheBackpackWhenTurnedOff()
        {
            Plugin.QuickStackIncludesBackpack.Value = false;
            Inventory backpack = WearBackpack();
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 1, 0, 0);
            Put(backpack, "Wood", 20, 0, 0);
            Put(Mine, "Wood", 5, 2, 2);

            QuickStack.Run();
            yield return WaitUntil(() => Count(chest.GetInventory(), "Wood") == 6, "6 wood in the chest");
            yield return Frames(10);

            Assert.Equal(20, Count(backpack, "Wood"), "Wood left in the backpack");
        }

        /// <summary>
        /// The reporter of #48 opens the backpack together with the inventory: the menu with Quick stack stays usable, and
        /// the chest menu's Sort chest becomes Sort backpack and sorts it.
        /// </summary>
        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator OpenBackpackCanBeSortedAndQuickStacked()
        {
            Inventory backpack = WearBackpack();
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Resin", 1, 0, 0);
            Put(backpack, "Wood", 10, 0, 0);
            Put(backpack, "Resin", 6, 1, 0);
            Put(backpack, "Wood", 15, 2, 0);
            InventoryGui gui = InventoryGui.instance;
            gui.Show(null);
            yield return InventorySettled();
            OpenBackpack(gui);
            yield return WaitUntil(() => gui.m_currentContainer != null && gui.IsContainerOpen(), "the backpack to open");
            Assert.True(AdventureBackpacksCompat.IsBackpack(gui.m_currentContainer), "The open container is the backpack");
            yield return Frames(3);

            Assert.True(InventoryButtons._quickStack.gameObject.activeInHierarchy, "Quick stack is shown with the backpack open");
            Assert.True(InventoryButtons._sortChest.gameObject.activeInHierarchy, "Sort is shown next to the backpack");
            Assert.Equal("Sort backpack", InventoryButtons._sortChest.GetComponentInChildren<TMPro.TMP_Text>().text, "Label of the backpack's Sort button");
            Assert.True(!InventoryButtons._backpackQuickStack.gameObject.activeSelf, "No Quick stack of its own: the inventory's takes the backpack along");
            yield return Screenshot("adventure-backpacks-open");

            InventoryButtons._sortChest.onClick.Invoke();
            Assert.Equal("Resin x6 @0,0, Wood x25 @1,0", Describe(backpack), "Backpack after Sort backpack");
            string shown = MessageHud.instance.m_messageCenterText.text;
            Assert.True(shown.StartsWith("Backpack sorted"), $"Message shown: \"{shown}\"");

            InventoryButtons._quickStack.onClick.Invoke();
            yield return WaitUntil(() => Count(chest.GetInventory(), "Resin") == 7, "7 resin in the chest");
            Assert.Equal(0, Count(backpack, "Resin"), "Resin left in the backpack");

            // A real chest gets its label back.
            gui.CloseContainer();
            gui.Show(chest);
            yield return WaitUntil(() => gui.m_currentContainer == chest, "the chest to open");
            yield return Frames(3);
            Assert.Equal("Sort chest", InventoryButtons._sortChest.GetComponentInChildren<TMPro.TMP_Text>().text, "Label of the chest's Sort button");
        }

        /// <summary>With the backpack left out of Quick stack, the open backpack gets a Quick stack that moves only its items.</summary>
        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator OpenBackpackHasItsOwnQuickStackWhenLeftOut()
        {
            Plugin.QuickStackIncludesBackpack.Value = false;
            Inventory backpack = WearBackpack();
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 1, 0, 0);
            Put(backpack, "Wood", 20, 0, 0);
            Put(Mine, "Wood", 5, 2, 2);
            InventoryGui gui = InventoryGui.instance;
            gui.Show(null);
            yield return InventorySettled();
            OpenBackpack(gui);
            yield return WaitUntil(() => gui.m_currentContainer != null && gui.IsContainerOpen(), "the backpack to open");
            yield return Frames(3);

            Assert.True(InventoryButtons._backpackQuickStack.gameObject.activeInHierarchy, "The backpack's menu has a Quick stack");
            UnityEngine.RectTransform button = (UnityEngine.RectTransform)InventoryButtons._backpackQuickStack.transform;
            float bottom = -button.anchoredPosition.y + button.rect.height;
            Assert.True(bottom <= InventoryButtons._chestMenu.rect.height,
                $"The menu ({InventoryButtons._chestMenu.rect.height:0}) is tall enough for the third button (bottom at {bottom:0})");
            yield return Screenshot("adventure-backpacks-own-quick-stack");

            InventoryButtons._backpackQuickStack.onClick.Invoke();
            yield return WaitUntil(() => Count(chest.GetInventory(), "Wood") == 21, "21 wood in the chest");
            yield return WaitUntil(() => QuickStack._reportAt < 0f, "the quick stack summary");
            Assert.Equal(0, Count(backpack, "Wood"), "Wood left in the backpack");
            Assert.Equal(5, Count(Mine, "Wood"), "Wood stays in the inventory");

            // Back to a real chest: no backpack Quick stack there.
            gui.CloseContainer();
            gui.Show(chest);
            yield return WaitUntil(() => gui.m_currentContainer == chest, "the chest to open");
            yield return Frames(3);
            Assert.True(!InventoryButtons._backpackQuickStack.gameObject.activeSelf, "No backpack Quick stack next to a chest");
        }

        // ------------------------------------------------------------------ Storage window (#60)

        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator StorageWindowShowsTheBackpackUnderTheInventory()
        {
            Inventory backpack = WearBackpack();
            Put(backpack, "Resin", 6, 1, 0);
            SpawnChest();
            yield return OpenStorageWindow();

            InventoryGrid grid = StorageWindow._backpackGrid;
            Assert.True(grid.gameObject.activeInHierarchy, "The backpack is shown");
            Assert.True(grid.GetInventory() == backpack, "It shows the backpack's items");
            Assert.Equal(3, grid.m_elements.Count, "Slots shown");
            Assert.Equal(Localization.instance.Localize(backpack.GetName()), StorageWindow._backpackTitle.text, "Heading");
            UnityEngine.RectTransform mine = (UnityEngine.RectTransform)StorageWindow._playerGrid.transform;
            UnityEngine.RectTransform its = (UnityEngine.RectTransform)grid.transform;
            Assert.True(its.anchoredPosition.y < mine.anchoredPosition.y - mine.rect.height * mine.localScale.y, "It's under the inventory");
            Assert.Equal(1f, mine.localScale.x, "The inventory keeps its size");
            yield return Screenshot("storage-window-backpack");

            // Taking it off hides it.
            Me.UnequipItem(Mine.GetAllItems().First(i => i.m_dropPrefab?.name == BackpackPrefab));
            yield return Frames(3);
            Assert.True(!grid.gameObject.activeInHierarchy, "No backpack shown without one on");
        }

        /// <summary>A big backpack under the inventory: both keep their size and the area scrolls.</summary>
        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator StorageWindowScrollsForABigBackpack()
        {
            Inventory backpack = WearBackpack("BackpackMistlands", 4, 7, 4);
            Put(backpack, "Resin", 6, 6, 3);
            yield return OpenStorageWindow();

            UnityEngine.RectTransform content = StorageWindow._playerContent;
            UnityEngine.UI.ScrollRect scroll = content.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            float view = scroll.viewport.rect.height;
            Assert.Equal(1f, StorageWindow._playerGrid.transform.localScale.x, "Inventory scale");
            Assert.Equal(1f, StorageWindow._backpackGrid.transform.localScale.x, "Backpack scale");
            if (content.rect.height > view + 1f)
            {
                Assert.True(scroll.verticalScrollbar.gameObject.activeInHierarchy, $"Scroll bar shown (content {content.rect.height:0}, view {view:0})");
                scroll.verticalNormalizedPosition = 0f;
                yield return Frames(2);
            }
            else
            {
                TestPlugin.Log.LogInfo($"The screen is tall enough for inventory and backpack (content {content.rect.height:0}, view {view:0}); nothing to scroll");
            }
            yield return Screenshot("storage-window-big-backpack");
        }

        /// <summary>
        /// Ctrl + click: an inventory item goes into a chest, not the backpack; a backpack item goes into a chest; a chest
        /// item goes into the inventory.
        /// </summary>
        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator CtrlClickSkipsTheBackpack()
        {
            Inventory backpack = WearBackpack();
            Container chest = SpawnChest();
            Inventory inv = chest.GetInventory();
            Put(inv, "Stone", 5, 0, 0);
            ItemDrop.ItemData mineWood = Put(Mine, "Wood", 10, 2, 2);
            ItemDrop.ItemData backpackResin = Put(backpack, "Resin", 6, 1, 0);
            yield return OpenStorageWindow();

            StorageWindow.QuickMove(Mine, mineWood);
            Assert.Equal(10, Count(inv, "Wood"), "Wood from the inventory in the chest");
            Assert.Equal(0, Count(backpack, "Wood"), "Wood in the backpack");

            StorageWindow.QuickMove(backpack, backpackResin);
            Assert.Equal(6, Count(inv, "Resin"), "Resin from the backpack in the chest");
            Assert.Equal(0, Count(backpack, "Resin"), "Resin left in the backpack");

            StorageWindow.QuickMove(inv, inv.GetAllItems().First(i => i.m_shared.m_name == Name("Stone")));
            Assert.Equal(5, Count(Mine, "Stone"), "Stone from the chest in the inventory");
            Assert.Equal(0, Count(backpack, "Stone"), "Stone in the backpack");
        }

        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator StorageWindowDragsBetweenBackpackAndChest()
        {
            Inventory backpack = WearBackpack();
            Container chest = SpawnChest();
            Put(backpack, "Resin", 6, 0, 0);
            yield return OpenStorageWindow();

            InventoryGrid backpackGrid = StorageWindow._backpackGrid;
            InventoryGrid chestGrid = StorageWindow._cards.First(c => c.Chest == chest).Grid;
            StorageWindow.OnSelected(backpackGrid, backpack.GetItemAt(0, 0), new Vector2i(0, 0), InventoryGrid.Modifier.Select);
            StorageWindow.OnSelected(chestGrid, null, new Vector2i(2, 1), InventoryGrid.Modifier.Select);
            Assert.Equal(6, Count(chest.GetInventory(), "Resin"), "Resin dropped into the chest");
            Assert.Equal(0, Count(backpack, "Resin"), "Resin left in the backpack");

            StorageWindow.OnSelected(chestGrid, chest.GetInventory().GetItemAt(2, 1), new Vector2i(2, 1), InventoryGrid.Modifier.Select);
            StorageWindow.OnSelected(backpackGrid, null, new Vector2i(2, 0), InventoryGrid.Modifier.Select);
            Assert.Equal(6, Count(backpack, "Resin"), "Resin back in the backpack");
            Assert.True(backpack.GetItemAt(2, 0) != null, "In the slot it was dropped on");
        }

        /// <summary>Find item counts what's in the backpack as on you; Tidy up and Sort all leave the backpack alone.</summary>
        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator FindCountsTheBackpackAndTidyUpLeavesItAlone()
        {
            Inventory backpack = WearBackpack();
            Container a = SpawnChest(3f, 0f);
            Container b = SpawnChest(3f, 90f);
            Put(a.GetInventory(), "Resin", 5, 0, 0);
            Put(b.GetInventory(), "Resin", 3, 0, 0);
            Put(backpack, "Wood", 4, 0, 0);
            Put(backpack, "Resin", 6, 2, 0);
            Put(Mine, "Resin", 2, 2, 2);
            yield return OpenStorageWindow();

            StorageWindow._selected = Name("Resin");
            yield return Frames(2);
            Assert.True(StorageWindow._selInfo.text.StartsWith("8 in 2 chests, 8 on you"), $"Selection info: \"{StorageWindow._selInfo.text}\"");

            StorageWindow.TidyUp();
            StorageWindow.SortAll();
            Assert.Equal("Wood x4 @0,0, Resin x6 @2,0", Describe(backpack), "Backpack after Tidy up and Sort all");
        }

        /// <summary>Gather here takes from the backpack only when "Also gather from my backpack" is ticked.</summary>
        [Test, RequiresMod(AdventureBackpacksCompat.Guid)]
        private static IEnumerator GatherHereFromTheBackpackWhenTicked()
        {
            Inventory backpack = WearBackpack();
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Resin", 5, 0, 0);
            Put(backpack, "Resin", 6, 1, 0);
            Put(Mine, "Resin", 2, 2, 2);
            yield return OpenStorageWindow();
            Assert.True(StorageWindow._includeBackpack.gameObject.activeInHierarchy && StorageWindow._includeBackpack.interactable, "The backpack checkbox is shown");
            StorageWindow._selected = Name("Resin");
            StorageWindow._includeMe.isOn = false;
            StorageWindow.Card card = StorageWindow._cards.First(c => c.Chest == chest);

            StorageWindow._includeBackpack.isOn = false;
            StorageWindow.GatherInto(card);
            Assert.Equal(6, Count(backpack, "Resin"), "Resin stays in the backpack when not ticked");

            StorageWindow._includeBackpack.isOn = true;
            yield return Screenshot("storage-window-gather-from-backpack");
            StorageWindow.GatherInto(card);
            Assert.Equal(11, Count(chest.GetInventory(), "Resin"), "Resin from the backpack gathered");
            Assert.Equal(0, Count(backpack, "Resin"), "Resin left in the backpack");
            Assert.Equal(2, Count(Mine, "Resin"), "Resin stays in the inventory (its box isn't ticked)");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Puts on a backpack and returns its inventory. A new Meadows backpack has a single row of 3 slots.</summary>
        private static Inventory WearBackpack(string prefab = BackpackPrefab, int quality = 1, int width = 3, int height = 1)
        {
            ItemDrop.ItemData item = Put(Mine, prefab, 1, 7, 3, quality);
            Assert.True(Me.EquipItem(item), "Backpack equipped");
            Inventory backpack = AdventureBackpacksCompat.Backpack(Me);
            Assert.True(backpack != null, "The equipped backpack has an inventory");
            Assert.True(backpack.GetWidth() == width && backpack.GetHeight() == height,
                $"The backpack is {width} x {height} (it's {backpack.GetWidth()} x {backpack.GetHeight()})");
            backpack.RemoveAll();
            return backpack;
        }

        private static IEnumerator OpenStorageWindow()
        {
            StorageWindow.Open();
            yield return WaitUntil(() => StorageWindow._cards.All(c => c.Claimed), "every chest in the window to open");
            yield return Frames(3);
        }

        private static void OpenBackpack(InventoryGui gui)
        {
            Type api = Chainloader.PluginInfos[AdventureBackpacksCompat.Guid].Instance.GetType().Assembly.GetType("AdventureBackpacks.API.ABAPI");
            api.GetMethod("OpenBackpack", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Player), typeof(InventoryGui) }, null)
                .Invoke(null, new object[] { Me, gui });
        }
    }
}
