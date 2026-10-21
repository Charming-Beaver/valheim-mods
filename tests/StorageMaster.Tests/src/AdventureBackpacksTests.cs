using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
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

        // ------------------------------------------------------------------ helpers

        /// <summary>Puts on a backpack and returns its inventory.</summary>
        private static Inventory WearBackpack()
        {
            ItemDrop.ItemData item = Put(Mine, BackpackPrefab, 1, 7, 3);
            Assert.True(Me.EquipItem(item), "Backpack equipped");
            Inventory backpack = AdventureBackpacksCompat.Backpack(Me);
            Assert.True(backpack != null, "The equipped backpack has an inventory");
            // A new Meadows backpack has a single row of 3 slots.
            Assert.True(backpack.GetWidth() >= 3, $"The backpack has 3 slots in a row (it's {backpack.GetWidth()} x {backpack.GetHeight()})");
            backpack.RemoveAll();
            return backpack;
        }

        private static void OpenBackpack(InventoryGui gui)
        {
            Type api = Chainloader.PluginInfos[AdventureBackpacksCompat.Guid].Instance.GetType().Assembly.GetType("AdventureBackpacks.API.ABAPI");
            api.GetMethod("OpenBackpack", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Player), typeof(InventoryGui) }, null)
                .Invoke(null, new object[] { Me, gui });
        }
    }
}
