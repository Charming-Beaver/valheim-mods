using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using StorageMaster.UI;
using UnityEngine;
using UnityEngine.UI;
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

        /// <summary>
        /// The badges sit just under SeneaL UI's own inventory and chest panels (the game's are hidden), look like its
        /// pills, can be clicked, and by default cover none of its panels or each other (#64).
        /// </summary>
        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static IEnumerator BadgesSitNextToSeneaLsPanels()
        {
            InventoryGui gui = InventoryGui.instance;
            gui.Show(SpawnChest());
            yield return WaitUntil(() => InventoryGui.IsVisible() && SeneaLCompat.ChestShown && InventoryButtons._chestBadge != null
                && InventoryButtons._chestBadge.Panel == SeneaLCompat.ChestPanel && InventoryButtons._chestBadge.gameObject.activeInHierarchy, "SeneaL UI's chest panel and its badge");
            yield return InventorySettled();
            Badge inventory = InventoryButtons._badge, chest = InventoryButtons._chestBadge;
            RectTransform inventoryPanel = SeneaLCompat.InventoryPanel, chestPanel = SeneaLCompat.ChestPanel;
            Assert.True(inventoryPanel != null, "SeneaL UI draws its own inventory panel");
            Assert.True(inventory.Panel == inventoryPanel, "The inventory badge belongs to SeneaL UI's inventory panel");

            foreach (Badge badge in new[] { inventory, chest })
            {
                Assert.True(badge.gameObject.activeInHierarchy && badge.IsInteractable(), $"{badge.name} is shown and can be clicked");
                CanvasGroup blocking = badge.GetComponentsInParent<CanvasGroup>().FirstOrDefault(g => !g.interactable || !g.blocksRaycasts || g.alpha < 0.5f);
                Assert.True(blocking == null, $"{badge.name} isn't in a hidden or unclickable group ({blocking?.name})");
                Assert.True(badge.transform.GetSiblingIndex() < badge.Panel.GetSiblingIndex(), $"{badge.name} is drawn under its panel, so it slips under it when moved over it");
                // SeneaL UI draws its pills' background on a child.
                Image pill = inventoryPanel.Find("chip_shield/bg")?.GetComponent<Image>(), face = badge.transform.Find("bg")?.GetComponent<Image>();
                Assert.True(pill != null && face != null, $"SeneaL UI's armor pill and {badge.name} have a background");
                Assert.True(face.sprite == pill.sprite && face.color == pill.color, $"{badge.name} looks like SeneaL UI's pills (sprite {face.sprite?.name}/{pill.sprite?.name})");
            }

            var ours = new (string name, RectTransform rt)[]
            {
                ("inventory badge", inventory.Rect), ("inventory menu", inventory.Menu), ("chest badge", chest.Rect), ("chest menu", chest.Menu),
            };
            var theirs = new (string name, RectTransform rt)[] { ("inventory panel", inventoryPanel), ("chest panel", chestPanel) };
            foreach (var a in ours)
            {
                foreach (var b in ours.Concat(theirs))
                {
                    if (a.rt == b.rt) continue;
                    // Like the game's boxes, the badges tuck under their panel's edge a little.
                    Rect ra = BadgeTests.Shrink(BadgeTests.InRoot(a.rt), 10f), rb = BadgeTests.Shrink(BadgeTests.InRoot(b.rt), 10f);
                    Assert.True(!ra.Overlaps(rb), $"The {a.name} covers the {b.name} ({BadgeTests.InRoot(a.rt)} and {BadgeTests.InRoot(b.rt)})");
                }
            }
            yield return Screenshot("seneal-badges");
        }

        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static IEnumerator ChestBadgeGoesWithSeneaLsChestPanel()
        {
            InventoryGui gui = InventoryGui.instance;
            gui.Show(SpawnChest());
            yield return WaitUntil(() => InventoryGui.IsVisible() && SeneaLCompat.ChestShown && InventoryButtons._chestBadge != null
                && InventoryButtons._chestBadge.gameObject.activeInHierarchy, "SeneaL UI's chest panel and its badge");
            Assert.True(InventoryButtons._chestBadge.Menu.gameObject.activeInHierarchy, "The chest menu is open");

            gui.Hide();
            yield return Frames(2);
            yield return BadgeTests.ShowInventory();
            yield return Frames(2);

            Assert.True(!SeneaLCompat.ChestShown, "No chest shown");
            Assert.True(!InventoryButtons._chestBadge.gameObject.activeInHierarchy, "The chest badge is hidden without a chest");
            Assert.True(!InventoryButtons._chestMenu.gameObject.activeInHierarchy, "and so is its menu");
            // As after a change in the Options window.
            InventoryButtons.ApplyBadges();
            yield return Frames(2);
            Assert.True(!InventoryButtons._chestMenu.gameObject.activeInHierarchy, "The chest menu stays hidden when the badges are put back");
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
