using System.Collections;
using System.Collections.Generic;
using System.Linq;
using StorageMaster.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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

        // Opening takes the chests over with the game's "Place stacks" request. Mods that replace its answer with their
        // own quick stack (Quick Stack Store Sort Trash Restock) mustn't get to move anything then (#49).
        [Test]
        private static IEnumerator OpeningLeavesMyInventoryAlone()
        {
            Container a = SpawnChest(3f, 0f);
            Put(a.GetInventory(), "Wood", 10, 0, 0);
            Put(Mine, "Wood", 5, 2, 2);

            yield return OpenAndWait();

            Assert.Equal(5, Count(Mine, "Wood"), "Wood left on me");
            Assert.Equal(10, Count(a.GetInventory(), "Wood"), "Wood in the chest");
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

        /// <summary>
        /// Find item fades everything that doesn't match. Matches keep their full color: the orange highlight lights up
        /// the slot behind the item, not the item itself.
        /// </summary>
        [Test]
        private static IEnumerator FindItemLightsUpMatchesWithoutTintingThem()
        {
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 10, 0, 0);
            Put(chest.GetInventory(), "Stone", 10, 1, 0);
            yield return OpenAndWait();
            StorageWindow._search.text = "wood";
            yield return Frames(3);

            InventoryGrid grid = StorageWindow._cards.Single(c => c.Chest == chest).Grid;
            InventoryElement wood = grid.m_elements.Single(e => e.Position == new Vector2i(0, 0));
            InventoryElement stone = grid.m_elements.Single(e => e.Position == new Vector2i(1, 0));
            Transform highlight = wood.transform.Find(StorageWindow.HighlightName);
            Assert.True(highlight != null && highlight.gameObject.activeSelf, "Wood's slot is highlighted");
            Assert.True(highlight.GetSiblingIndex() < wood.m_icon.transform.GetSiblingIndex(), "The highlight is under the item's icon");
            Assert.Equal(1f, wood.m_icon.color.a, "Wood's icon opacity");
            Assert.True(stone.m_icon.color.a < 0.5f, $"Stone is faded (opacity {stone.m_icon.color.a})");
            yield return Screenshot("find-item");
        }

        /// <summary>
        /// Only Find item shows the F key hint, and F (which can't be pressed in a test, so its method is called) puts
        /// the cursor into it. The Options window's X/Y fields have no hint (#41).
        /// </summary>
        [Test]
        private static IEnumerator OnlyFindItemShowsTheFKeyAndFFocusesIt()
        {
            yield return OpenAndWait();
            Assert.True(StorageWindow._search.GetComponentInChildren<UIInputHint>(true) != null, "Find item shows the F key");

            StorageWindow.FocusSearch();
            yield return Frames(2);
            Assert.True(StorageWindow._search.isFocused, "Find item has the cursor");
            Assert.Equal("", StorageWindow._search.text, "Find item's text");

            OptionsWindow.Open();
            yield return Frames(2);
            var withHint = OptionsWindow._window.Root.GetComponentsInChildren<TMPro.TMP_InputField>(true)
                .Where(f => f.GetComponentInChildren<UIInputHint>(true) != null).Select(f => f.transform.parent.name).ToList();
            Assert.True(withHint.Count == 0, $"Fields in Options with a key hint: {string.Join(", ", withHint)}");
        }

        /// <summary>The window's and the inventory buttons' own tooltips have a tooltip to show (#21).</summary>
        [Test]
        private static IEnumerator ButtonTooltipsCanShow()
        {
            InventoryGui.instance.Show(SpawnChest());
            yield return WaitUntil(() => StorageMaster.InventoryButtons._sortChest != null, "the inventory buttons");
            InventoryGui.instance.Hide();
            yield return OpenAndWait();

            var roots = new List<GameObject> { StorageWindow._root };
            foreach (Badge badge in new[] { StorageMaster.InventoryButtons._badge, StorageMaster.InventoryButtons._chestBadge })
            {
                roots.Add(badge.gameObject);
                roots.Add(badge.Menu.gameObject);
            }
            var missing = roots.SelectMany(r => r.GetComponentsInChildren<UITooltip>(true))
                .Where(t => t.GetComponent<InventoryElement>() == null && t.m_tooltipPrefab == null)
                .Select(t => t.name).ToList();
            Assert.True(missing.Count == 0, $"{missing.Count} tooltips have nothing to show, e.g. {missing.FirstOrDefault()}");
        }

        /// <summary>
        /// After Tidy up, an item is only spread over several chests if they're full (#7). Random but repeatable setups:
        /// six chests, some nearly full, with partial stacks of several items scattered over them. (Seed 3 found the bug.)
        /// </summary>
        [Test]
        private static IEnumerator TidyUpLeavesNothingSpreadOutUnlessChestsAreFull()
        {
            string[] kinds = { "Wood", "Stone", "Resin", "Flint", "Raspberry", "BoneFragments", "RoundLog", "LeatherScraps" };
            for (int seed = 1; seed <= 20; seed++)
            {
                var random = new System.Random(seed);
                var chests = new List<Container>();
                for (int i = 0; i < 6; i++) chests.Add(SpawnChest(3f + i * 0.8f, i * 60f));
                foreach (Container chest in chests)
                {
                    Inventory inv = chest.GetInventory();
                    int slots = inv.GetWidth() * inv.GetHeight();
                    int fill = random.Next(0, slots + 1);
                    for (int s = 0; s < fill; s++)
                    {
                        string kind = kinds[random.Next(kinds.Length)];
                        Put(inv, kind, random.Next(1, 51), s % inv.GetWidth(), s / inv.GetWidth());
                    }
                }
                var before = kinds.ToDictionary(k => k, k => chests.Sum(c => Count(c.GetInventory(), k)));

                yield return OpenAndWait();
                // The game warns when it's handed a stack that only partly fits; moving items must not cause that.
                var warnings = new List<string>();
                void Listen(string message, string stack, LogType type)
                {
                    if (message.Contains("occupied slot")) warnings.Add(message);
                }
                Application.logMessageReceived += Listen;
                try
                {
                    StorageWindow.TidyUp();
                }
                finally
                {
                    Application.logMessageReceived -= Listen;
                }
                Assert.True(warnings.Count == 0, $"Seed {seed}: {warnings.Count} warnings, e.g. \"{warnings.FirstOrDefault()}\"");

                foreach (string kind in kinds)
                {
                    Assert.Equal(before[kind], chests.Sum(c => Count(c.GetInventory(), kind)), $"Seed {seed}: {kind} in total");
                    var holders = chests.Where(c => Count(c.GetInventory(), kind) > 0).ToList();
                    if (holders.Count < 2) continue;
                    // Spread out is only fine if no holder could take more of it.
                    var roomy = holders.Where(c => c.GetInventory().CanAddItem(NewItem(kind), 1)).ToList();
                    Assert.True(roomy.Count <= 1,
                        $"Seed {seed}: {kind} is in {holders.Count} chests and {roomy.Count} of them still have room: " +
                        string.Join("; ", holders.Select(c => $"{Count(c.GetInventory(), kind)} ({c.GetInventory().GetAllItems().Count}/{c.GetInventory().GetWidth() * c.GetInventory().GetHeight()} slots)")));
                }

                StorageWindow.Close();
                yield return Reset();
            }
        }

        /// <summary>Chests over the Max chests limit don't take part, so the window says how many there are (#7).</summary>
        [Test]
        private static IEnumerator SaysHowManyChestsAreOverTheLimit()
        {
            Plugin.StorageMaxChests.Value = 2;
            SpawnChest(3f, 0f);
            SpawnChest(4f, 90f);
            SpawnChest(5f, 180f);

            yield return OpenAndWait();
            yield return Frames(2);

            string summary = StorageWindow._summary.text;
            Assert.True(summary.Contains("1 more over the Max chests limit"), $"Summary: \"{summary}\"");
        }

        /// <summary>Tidy up says which chests it had to leave out: still opening, or open for someone else (#7).</summary>
        [Test]
        private static IEnumerator TidyUpSaysWhichChestsWereLeftOut()
        {
            Container a = SpawnChest(3f, 0f), b = SpawnChest(4f, 90f), c = SpawnChest(5f, 180f);
            Put(a.GetInventory(), "Wood", 20, 0, 0);
            Put(b.GetInventory(), "Wood", 5, 0, 0);
            Put(c.GetInventory(), "Wood", 7, 0, 0);
            yield return OpenAndWait();
            // As if c hadn't answered yet (on a server that takes a moment).
            StorageWindow.Card waiting = StorageWindow._cards.First(card => card.Chest == c);
            waiting.Claimed = false;

            StorageWindow.TidyUp();

            string shown = MessageHud.instance.m_messageCenterText.text;
            Assert.True(shown.Contains("1 chest was left out: 1 still opening"), $"Message: \"{shown}\"");
            Assert.Equal(7, Count(c.GetInventory(), "Wood"), "Wood in the chest that was still opening");
            Assert.Equal(25, Count(a.GetInventory(), "Wood"), "Wood brought together in the other chests");

            waiting.Denied = true;
            StorageWindow.TidyUp();
            shown = MessageHud.instance.m_messageCenterText.text;
            Assert.True(shown.Contains("1 in use by someone else"), $"Message: \"{shown}\"");
        }

        /// <summary>
        /// Closing the window keeps the cards for the next chests (#16). A reused card must show its new chest: its items,
        /// name and Skip box, nothing left over from the chest it showed before.
        /// </summary>
        [Test]
        private static IEnumerator ReusedCardsShowTheirOwnChest()
        {
            Container a = SpawnChest(3f, 0f), b = SpawnChest(4f, 90f), c = SpawnChest(5f, 180f);
            Put(a.GetInventory(), "Wood", 10, 0, 0);
            Put(a.GetInventory(), "Stone", 20, 4, 1);
            Put(b.GetInventory(), "Resin", 5, 1, 0);
            Put(c.GetInventory(), "Flint", 7, 2, 1);
            yield return OpenAndWait();
            // As a player would: name c, mark b Skip.
            TMP_InputField name = StorageWindow._cards.First(card => card.Chest == c).Label;
            name.text = "Building";
            name.onEndEdit.Invoke(name.text);
            SkipBox(StorageWindow._cards.First(card => card.Chest == b)).isOn = true;
            yield return Frames(2);
            AssertCardsShowTheirChests();
            StorageWindow.Close();
            yield return null;

            // The nearest chest is gone and a new one is the farthest, so every card moves on to another chest.
            RemoveChest(a);
            Container d = SpawnChest(6f, 270f);
            Put(d.GetInventory(), "Coal", 3, 0, 1);
            yield return null;
            yield return OpenAndWait();
            yield return Frames(2);

            Assert.Equal("b,c,d", string.Join(",", StorageWindow._cards.Select(card => card.Chest == b ? "b" : card.Chest == c ? "c" : "d")), "Chests in the window");
            AssertCardsShowTheirChests();
            Assert.True(SkipBox(StorageWindow._cards.First(card => card.Chest == b)).isOn, "b is still marked Skip");
            Assert.True(StorageWindow._cards.Where(card => card.Chest != b).All(card => !SkipBox(card).isOn), "Only b is marked Skip");
            Assert.Equal("Building", StorageWindow._cards.First(card => card.Chest == c).Label.text, "c's name");
        }

        private static Toggle SkipBox(StorageWindow.Card card) => card.Root.GetComponentInChildren<Toggle>(true);

        /// <summary>Each card shows exactly its own chest: every slot with its item and amount, the others empty.</summary>
        private static void AssertCardsShowTheirChests()
        {
            foreach (StorageWindow.Card card in StorageWindow._cards)
            {
                Inventory inv = card.Chest.GetInventory();
                string chest = card.Label.text != "" ? card.Label.text : $"the chest at {card.Chest.transform.position}";
                Assert.Equal(ChestAccess.GetLabel(card.Chest), card.Label.text, "Name on the card");
                Assert.Equal(Exclusions.IsExcluded(card.Chest), SkipBox(card).isOn, $"Skip on the card of {chest}");
                Assert.Equal(inv.GetWidth() * inv.GetHeight(), card.Grid.m_elements.Count, $"Slots on the card of {chest}");
                foreach (InventoryElement element in card.Grid.m_elements)
                {
                    ItemDrop.ItemData item = inv.GetItemAt(element.Position.x, element.Position.y);
                    string slot = $"slot {element.Position.x},{element.Position.y} of {chest}";
                    if (item == null)
                    {
                        Assert.True(!element.m_used && !element.m_icon.enabled, $"{slot} is empty");
                        continue;
                    }
                    Assert.True(element.m_used && element.m_icon.enabled && element.m_icon.sprite == item.GetIcon(), $"{slot} shows {item.m_shared.m_name}");
                    Assert.Equal($"{item.m_stack}/{item.m_shared.m_maxStackSize}", element.m_amount.text, $"Amount in {slot}");
                }
            }
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
