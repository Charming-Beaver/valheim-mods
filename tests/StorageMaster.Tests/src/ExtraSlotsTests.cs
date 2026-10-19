using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using StorageMaster.UI;
using UnityEngine;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    /// <summary>
    /// With shudnal's ExtraSlots (run-tests.ps1 -With shudnal-ExtraSlots): its slots below the regular inventory rows
    /// are left alone by Sort, Quick stack and Gather here, like the hotbar (#4).
    /// </summary>
    public static class ExtraSlotsTests
    {
        [Test, RequiresMod(ExtraSlotsCompat.Guid)]
        private static IEnumerator SortLeavesExtraSlotsAlone()
        {
            Assert.True(ExtraSlotsCompat.Active, "ExtraSlots' API found");
            var inSlots = new List<ItemDrop.ItemData>();
            yield return FillSlots(inSlots);
            var positions = inSlots.ToDictionary(i => i, i => i.m_gridPos);
            Put(Mine, "Wood", 10, 5, 3);
            Put(Mine, "Stone", 10, 2, 2);

            Sorter.SortPlayer(silent: true);

            foreach (ItemDrop.ItemData item in inSlots)
            {
                Assert.True(Mine.ContainsItem(item), $"{item.m_shared.m_name} still in the inventory");
                Assert.Equal(positions[item], item.m_gridPos, $"Slot of {item.m_shared.m_name}");
            }
            int rows = ExtraSlotsCompat.PlayerRows(Mine);
            foreach (ItemDrop.ItemData item in Mine.GetAllItems().Where(i => !inSlots.Contains(i)))
            {
                Assert.True(item.m_gridPos.y < rows, $"{item.m_shared.m_name} sorted into a regular row, not row {item.m_gridPos.y}");
            }
        }

        [Test, RequiresMod(ExtraSlotsCompat.Guid)]
        private static IEnumerator QuickStackLeavesExtraSlotsAlone()
        {
            var inSlots = new List<ItemDrop.ItemData>();
            yield return FillSlots(inSlots);
            Container chest = SpawnChest();
            foreach (ItemDrop.ItemData item in inSlots) Put(chest.GetInventory(), item.m_dropPrefab.name, 1, inSlots.IndexOf(item), 0);
            int inChestBefore = chest.GetInventory().NrOfItems();

            QuickStack.Run();
            yield return Frames(10);

            foreach (ItemDrop.ItemData item in inSlots) Assert.True(Mine.ContainsItem(item), $"{item.m_shared.m_name} stays in its slot");
            Assert.Equal(inChestBefore, chest.GetInventory().NrOfItems(), "Stacks in the chest");
        }

        [Test, RequiresMod(ExtraSlotsCompat.Guid)]
        private static IEnumerator GatherHereLeavesExtraSlotsAlone()
        {
            var inSlots = new List<ItemDrop.ItemData>();
            yield return FillSlots(inSlots);
            ItemDrop.ItemData sample = inSlots[0];
            Container chest = SpawnChest();

            yield return StorageWindowOpen();
            StorageWindow._selected = sample.m_shared.m_name;
            StorageWindow._includeMe.isOn = true;
            StorageWindow.GatherInto(StorageWindow._cards.First(c => c.Chest == chest));

            Assert.True(Mine.ContainsItem(sample), $"{sample.m_shared.m_name} stays in its slot");
        }

        [Test, RequiresMod(ExtraSlotsCompat.Guid)]
        private static IEnumerator StorageWindowShowsOnlyTheRegularRows()
        {
            yield return StorageWindowOpen();
            yield return Frames(3);

            int rows = ExtraSlotsCompat.PlayerRows(Mine);
            Assert.True(rows < Mine.GetHeight(), $"ExtraSlots adds rows below the regular ones ({rows} of {Mine.GetHeight()})");
            var shown = StorageWindow._playerGrid.m_elements.Where(e => e.gameObject.activeSelf).ToList();
            Assert.Equal(Mine.GetWidth() * rows, shown.Count, "Slots shown");
            Assert.True(shown.All(e => e.Position.y < rows), "Only regular rows shown");
        }

        // ------------------------------------------------------------------ helpers

        private static IEnumerator StorageWindowOpen()
        {
            StorageWindow.Open();
            yield return WaitUntil(() => StorageWindow._cards.All(c => c.Claimed), "the chests to open");
        }

        /// <summary>
        /// Puts fitting items into active ExtraSlots slots (quick, food, ammo; some only unlock with progression) and
        /// keeps the ones ExtraSlots accepted.
        /// </summary>
        private static IEnumerator FillSlots(List<ItemDrop.ItemData> placed)
        {
            Type api = Chainloader.PluginInfos[ExtraSlotsCompat.Guid].Instance.GetType().Assembly.GetType("ExtraSlots.API");
            var slots = ((IEnumerable)api.GetMethod("GetExtraSlots").Invoke(null, null)).Cast<object>().ToList();
            T Get<T>(object slot, string property) => (T)slot.GetType().GetProperty(property).GetValue(slot);

            var tried = new List<string>();
            foreach (object slot in slots)
            {
                if (!Get<bool>(slot, "IsActive") || Get<bool>(slot, "IsEquipmentSlot")) continue;
                string prefab = Get<bool>(slot, "IsAmmoSlot") ? "ArrowWood" : Get<bool>(slot, "IsMiscSlot") ? "Coins" : "Raspberry";
                if (placed.Any(i => i.m_dropPrefab.name == prefab)) continue;
                Vector2i pos = Get<Vector2i>(slot, "GridPosition");
                tried.Add($"{Get<string>(slot, "ID")}@{pos.x},{pos.y}={prefab}");
                if (Mine.GetItemAt(pos.x, pos.y) != null) continue;
                ItemDrop.ItemData item = NewItem(prefab, 5);
                if (Mine.AddItem(item, 5, pos.x, pos.y)) placed.Add(Mine.GetItemAt(pos.x, pos.y));
            }
            // ExtraSlots checks its slots and moves out what doesn't belong; only keep what it accepted.
            yield return Frames(10);
            placed.RemoveAll(i => !Mine.ContainsItem(i) || !ExtraSlotsCompat.IsSlot(i.m_gridPos));
            Assert.True(placed.Count > 0, $"No item stayed in an ExtraSlots slot (tried {string.Join(", ", tried)})");
            TestPlugin.Log.LogInfo($"ExtraSlots slots used: {string.Join(", ", placed.Select(i => $"{i.m_dropPrefab.name}@{i.m_gridPos.x},{i.m_gridPos.y}"))}");
        }
    }
}
