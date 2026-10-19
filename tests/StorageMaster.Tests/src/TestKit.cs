using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StorageMaster.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace StorageMaster.Tests
{
    /// <summary>Setting up scenes (chests, items, locks) and checking the results.</summary>
    public static class TestKit
    {
        public const string ChestPrefab = "piece_chest_wood";

        // Saved-data keys of the mod (see Locks and Exclusions).
        private const string LocksKey = "InventoryTools.Locks";
        private const string ExcludedKey = "InventoryTools.Excluded";

        public static string ScreenshotPath;

        private static readonly List<Container> _chests = new List<Container>();

        public static Player Me => Player.m_localPlayer;
        public static Inventory Mine => Me.GetInventory();

        // ------------------------------------------------------------------ setup

        /// <summary>Back to a clean state: no test chests, empty inventory, no locks or skips, default settings.</summary>
        public static IEnumerator Reset()
        {
            StorageWindow.Close();
            InfoWindow.Close();
            OptionsWindow.Close();
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            if (InventoryGui.instance != null) InventoryGui.instance.m_dragItem = null;

            foreach (Container c in _chests) Destroy(c);
            _chests.Clear();
            Me.UnequipAllItems();
            Mine.RemoveAll();
            Me.m_customData.Remove(LocksKey);
            Me.m_customData.Remove(ExcludedKey);
            // Both cache what they loaded per player; forget it so the cleared data is read again.
            Locks._loadedFor = null;
            Exclusions._loadedFor = null;
            // A quick stack summary still due from the last test would show up in the next one.
            QuickStack._reportAt = -1f;
            ChestAccess._waiting.Clear();
            // Messages and "New material" popups would cover the screenshots.
            MessageHud.instance.m_msgQeue.Clear();
            MessageHud.instance.ClearUnlockQueue();
            MessageHud.instance.HideAll();

            foreach (var entry in new BepInEx.Configuration.ConfigEntryBase[]
                     {
                         Plugin.SortMode, Plugin.AutoSort, Plugin.QuickStackRange, Plugin.QuickStackSkipsHotbar,
                         Plugin.StorageRange, Plugin.StorageMaxChests, Plugin.StorageShowInventory,
                         Plugin.DragKey, Plugin.InventoryMenuOpen, Plugin.InventoryBadgeX, Plugin.InventoryBadgeY,
                         Plugin.ChestMenuOpen, Plugin.ChestBadgeX, Plugin.ChestBadgeY,
                     })
            {
                entry.BoxedValue = entry.DefaultValue;
            }
            // Destroying objects takes a frame.
            yield return null;
        }

        /// <summary>Removes chests left over from an earlier run that crashed, and any other containers near the player.</summary>
        public static IEnumerator ClearArea(float radius)
        {
            foreach (Container c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                if (c != null && Vector3.Distance(c.transform.position, Me.transform.position) < radius) Destroy(c);
            }
            yield return null;
        }

        private static void Destroy(Container c)
        {
            if (c == null || c.m_nview == null || !c.m_nview.IsValid()) return;
            c.m_nview.ClaimOwnership();
            ZNetScene.instance.Destroy(c.gameObject);
        }

        /// <summary>
        /// A wooden chest standing on the ground, <paramref name="distance"/> meters from the player
        /// (spread around them by <paramref name="angle"/> degrees, so chests don't overlap).
        /// </summary>
        public static Container SpawnChest(float distance = 3f, float angle = 0f)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(ChestPrefab);
            Vector3 pos = Me.transform.position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            GameObject go = Object.Instantiate(prefab, pos, Quaternion.identity);
            Container c = go.GetComponent<Container>();
            _chests.Add(c);
            return c;
        }

        /// <summary>Adds an item at a grid position. Prefab names are the item's spawn name, e.g. "Wood".</summary>
        public static ItemDrop.ItemData Put(Inventory inv, string prefab, int stack, int x, int y, int quality = 1)
        {
            ItemDrop.ItemData item = NewItem(prefab, stack, quality);
            Assert.True(inv.GetItemAt(x, y) == null, $"Slot {x},{y} is already taken");
            Assert.True(inv.AddItem(item, stack, x, y), $"Could not add {stack} {prefab} at {x},{y}");
            return inv.GetItemAt(x, y);
        }

        /// <summary>A new item that isn't in any inventory yet.</summary>
        public static ItemDrop.ItemData NewItem(string prefab, int stack = 1, int quality = 1)
        {
            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(prefab);
            Assert.True(itemPrefab != null, $"No item prefab called {prefab}");
            ItemDrop.ItemData item = itemPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = itemPrefab;
            item.m_stack = stack;
            item.m_quality = quality;
            item.m_durability = item.GetMaxDurability();
            return item;
        }

        public static void Lock(int x, int y) => Locks.Toggle(new Vector2i(x, y));

        /// <summary>Waits until a condition holds, failing after a timeout.</summary>
        public static IEnumerator WaitUntil(System.Func<bool> condition, string what, float timeout = 5f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) throw new AssertionException($"Timed out waiting for: {what}");
                yield return null;
            }
        }

        /// <summary>Waits for the inventory's opening animation, which slides the panels in.</summary>
        public static IEnumerator InventorySettled()
        {
            Vector3 last = Vector3.positiveInfinity;
            int still = 0;
            yield return WaitUntil(() =>
            {
                Vector3 now = InventoryGui.instance.m_player.position;
                still = now == last ? still + 1 : 0;
                last = now;
                return still >= 5;
            }, "the inventory to stop moving");
        }

        public static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        /// <summary>Turns the mouse wheel one notch down over a scroll list, as the game reports it; returns how far the list moved.</summary>
        public static IEnumerator ScrollOneNotch(ScrollRect scroll, float[] moved)
        {
            // Let the list lay itself out first, so there's something to scroll.
            yield return Frames(3);
            Canvas.ForceUpdateCanvases();
            Assert.True(scroll.content.rect.height > scroll.viewport.rect.height + 200f,
                $"The list is taller than its view (list {scroll.content.rect.height:0}, view {scroll.viewport.rect.height:0})");
            float notch = ((InputSystemUIInputModule)EventSystem.current.currentInputModule).scrollDeltaPerTick;
            float before = scroll.content.anchoredPosition.y;
            scroll.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, -notch) });
            yield return null;
            moved[0] = Mathf.Abs(scroll.content.anchoredPosition.y - before);
        }

        /// <summary>Saves a screenshot to BepInEx/StorageMaster.Tests.screenshots/name.png (after the frame is drawn).</summary>
        public static IEnumerator Screenshot(string name)
        {
            // Let the UI lay itself out first.
            yield return Frames(3);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(ScreenshotPath, name + ".png"));
            yield return Frames(2);
        }

        // ------------------------------------------------------------------ reading state

        public static int Count(Inventory inv, string prefab) => inv.CountItems(Name(prefab));

        /// <summary>The item's internal name (like "$item_wood") from its prefab name (like "Wood").</summary>
        public static string Name(string prefab) => ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;

        public static string Localized(ItemDrop.ItemData item) => Localization.instance.Localize(item.m_shared.m_name);

        /// <summary>Items in reading order (row by row), skipping rows above <paramref name="fromRow"/>.</summary>
        public static List<ItemDrop.ItemData> InReadingOrder(Inventory inv, int fromRow = 0)
        {
            return inv.GetAllItems().Where(i => i.m_gridPos.y >= fromRow)
                .OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).ToList();
        }

        public static string Describe(Inventory inv)
        {
            return string.Join(", ", InReadingOrder(inv).Select(i => $"{i.m_dropPrefab?.name ?? i.m_shared.m_name} x{i.m_stack} @{i.m_gridPos.x},{i.m_gridPos.y}"));
        }
    }

    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new AssertionException($"{what}: expected {expected}, got {actual}");
        }
    }
}
