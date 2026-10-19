using System.Collections;
using System.Linq;
using StorageMaster.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    /// <summary>The menu badges next to the inventory and the chest, which open the buttons and can be moved (#5).</summary>
    public static class BadgeTests
    {
        private static Badge Inventory => StorageMaster.InventoryButtons._badge;
        private static Badge Chest => StorageMaster.InventoryButtons._chestBadge;

        [Test]
        private static IEnumerator MenuIsOpenAtFirstAndTheBadgeOpensAndClosesIt()
        {
            yield return ShowInventory();
            Assert.True(Inventory.Menu.gameObject.activeInHierarchy, "The menu is open the first time");
            Assert.True(StorageMaster.InventoryButtons._sort.gameObject.activeInHierarchy, "Sort is shown");

            Click(Inventory.gameObject);
            Assert.True(!Inventory.Menu.gameObject.activeSelf, "Clicking the badge closes the menu");
            Assert.Equal(false, Plugin.InventoryMenuOpen.Value, "Saved as closed");

            InventoryGui.instance.Hide();
            yield return ShowInventory();
            Assert.True(!Inventory.Menu.gameObject.activeSelf, "Still closed when the inventory opens again");

            Click(Inventory.gameObject);
            Assert.True(Inventory.Menu.gameObject.activeInHierarchy, "Clicking again opens it");
        }

        [Test]
        private static IEnumerator DraggingWithoutTheDragKeyDoesNothing()
        {
            yield return ShowInventory();
            Vector2 before = Inventory.Rect.anchoredPosition;
            Vector2 start = RectTransformUtility.WorldToScreenPoint(null, Inventory.Rect.position);
            var e = new PointerEventData(EventSystem.current) { position = start, pressPosition = start };
            ExecuteEvents.Execute(Inventory.gameObject, e, ExecuteEvents.beginDragHandler);
            e.position = start + new Vector2(-200f, 100f);
            ExecuteEvents.Execute(Inventory.gameObject, e, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(Inventory.gameObject, e, ExecuteEvents.endDragHandler);
            yield return null;

            Assert.Equal(before, Inventory.Rect.anchoredPosition, "Badge position");
            Assert.Equal(0, Plugin.InventoryBadgeX.Value, "Saved X");
        }

        [Test]
        private static IEnumerator MovedBadgeIsSavedAndTheMenuFollowsIt()
        {
            yield return ShowInventory();
            Inventory.MoveTo(new Vector2(-150f, 40f));
            Assert.Equal(-150, Plugin.InventoryBadgeX.Value, "Saved X");
            Assert.Equal(40, Plugin.InventoryBadgeY.Value, "Saved Y");

            InventoryGui.instance.Hide();
            yield return ShowInventory();
            Assert.Equal(Inventory.Home + new Vector2(-150f, 40f), Inventory.Rect.anchoredPosition, "Badge position after opening the inventory again");
            Rect badge = InRoot(Inventory.Rect), menu = InRoot(Inventory.Menu);
            Assert.True(menu.xMin > badge.xMax && menu.xMin < badge.xMax + 20f, $"Menu right next to the badge (badge {badge}, menu {menu})");
            Assert.True(Mathf.Abs(menu.yMax - badge.yMax) < 1f, $"Menu's top level with the badge's (badge {badge}, menu {menu})");
        }

        [Test]
        private static IEnumerator BadgeAndMenuStayOnScreen()
        {
            yield return ShowInventory();
            Inventory.MoveTo(new Vector2(10000f, -10000f));
            Rect screen = ((RectTransform)UIKit.CanvasRoot).rect;
            Rect badge = InRoot(Inventory.Rect), menu = InRoot(Inventory.Menu);

            Assert.True(Inside(badge, screen), $"Badge on screen ({badge} in {screen})");
            Assert.True(Inside(menu, screen), $"Menu on screen ({menu} in {screen})");
            Assert.True(menu.xMax < badge.xMin, $"At the right edge the menu opens on the left (badge {badge}, menu {menu})");
            Assert.True(Plugin.InventoryBadgeX.Value < 10000 && Plugin.InventoryBadgeY.Value > -10000,
                $"Saved where it ended up ({Plugin.InventoryBadgeX.Value}, {Plugin.InventoryBadgeY.Value})");
        }

        /// <summary>
        /// Away from its panel (here over the crafting panel), the badge is drawn over the rest of the inventory screen,
        /// so it can't be lost behind another panel. Back on its panel it goes under it again. The menu is always on top.
        /// </summary>
        [Test]
        private static IEnumerator BadgeAwayFromItsPanelAndTheMenuAreNeverCovered()
        {
            yield return ShowInventory();
            InventoryGui gui = InventoryGui.instance;
            int screen = gui.GetComponentInParent<Canvas>().sortingOrder;
            Assert.True(OwnLayerAbove(Inventory.Menu.gameObject, screen), "The menu is drawn over the inventory screen");
            Assert.True(!OwnLayerAbove(Inventory.gameObject, screen), "At its usual place the badge is drawn in its place, under the panel");

            // Onto the crafting panel.
            Rect crafting = InRoot(gui.m_crafting), badge = InRoot(Inventory.Rect);
            Inventory.MoveTo(Inventory.Rect.anchoredPosition - Inventory.Home + (crafting.center - badge.center) / Inventory.Rect.lossyScale.x * UIKit.CanvasRoot.lossyScale.x);
            Assert.True(InRoot(Inventory.Rect).Overlaps(crafting), $"The badge is over the crafting panel ({InRoot(Inventory.Rect)}, {crafting})");
            Assert.True(OwnLayerAbove(Inventory.gameObject, screen), "Over the crafting panel the badge is drawn over it");
            Assert.True(Inventory.GetComponent<GraphicRaycaster>() != null, "and can be clicked there");

            Inventory.MoveTo(Vector2.zero);
            Assert.True(!OwnLayerAbove(Inventory.gameObject, screen), "Back at its usual place it's drawn under the panel again");
        }

        private static bool OwnLayerAbove(GameObject go, int sortingOrder) =>
            go.TryGetComponent(out Canvas canvas) && canvas.overrideSorting && canvas.sortingOrder > sortingOrder;

        /// <summary>By default, nothing covers the game's armor and weight boxes, the chest panel, or each other.</summary>
        [Test]
        private static IEnumerator BadgesAndMenusCoverNothingAtFirst()
        {
            InventoryGui gui = InventoryGui.instance;
            gui.Show(SpawnChest());
            yield return WaitUntil(() => InventoryGui.IsVisible() && Chest != null && Chest.Menu.gameObject.activeInHierarchy, "the chest and its menu");
            yield return InventorySettled();

            var ours = new (string name, RectTransform rt)[]
            {
                ("inventory badge", Inventory.Rect), ("inventory menu", Inventory.Menu), ("chest badge", Chest.Rect), ("chest menu", Chest.Menu),
            };
            var game = new (string name, RectTransform rt)[]
            {
                ("armor", (RectTransform)gui.m_armor.transform.parent), ("weight", (RectTransform)gui.m_weight.transform.parent),
                ("chest weight", (RectTransform)gui.m_containerWeight.transform.parent), ("inventory", gui.m_player), ("chest", gui.m_container),
            };
            foreach (var a in ours)
            {
                foreach (var b in ours.Concat(game))
                {
                    if (a.rt == b.rt) continue;
                    // Like the game's boxes, the badges overlap their panel's edge a little.
                    Rect ra = Shrink(InRoot(a.rt), 10f), rb = Shrink(InRoot(b.rt), 10f);
                    Assert.True(!ra.Overlaps(rb), $"The {a.name} covers the {b.name} ({InRoot(a.rt)} and {InRoot(b.rt)})");
                }
            }
        }

        /// <summary>
        /// The badges look like the game's weight boxes and, like them, are drawn before the panel's background,
        /// which covers their inner edge. They're also drawn under the game's boxes and ExtraSlots' panel.
        /// </summary>
        [Test]
        private static IEnumerator BadgesLookAndSitLikeTheWeightBoxes()
        {
            InventoryGui gui = InventoryGui.instance;
            gui.Show(SpawnChest());
            yield return WaitUntil(() => InventoryGui.IsVisible() && Chest != null && Chest.Menu.gameObject.activeInHierarchy, "the chest and its menu");
            // A click leaves the badge selected; that mustn't change its color.
            EventSystem.current.SetSelectedGameObject(Inventory.gameObject);
            yield return InventorySettled();
            // Buttons fade between colors.
            yield return new WaitForSecondsRealtime(0.3f);

            foreach (var (badge, weight) in new[] { (Inventory, gui.m_weight.transform.parent), (Chest, gui.m_containerWeight.transform.parent) })
            {
                // Drawn before the panel's background, the game's boxes and ExtraSlots' panel, so moved over them it goes under them.
                foreach (Transform sibling in weight.parent)
                {
                    bool covers = sibling == weight || sibling == gui.m_armor.transform.parent || sibling.name == "Bkg" || sibling.name.StartsWith("ExtraSlots");
                    if (covers) Assert.True(badge.transform.GetSiblingIndex() < sibling.GetSiblingIndex(), $"{badge.name} drawn before {Path(sibling)}");
                }
                foreach (string part in new[] { "", "bkg" })
                {
                    Image theirs = part == "" ? weight.GetComponent<Image>() : weight.Find(part).GetComponent<Image>();
                    Image ours = part == "" ? badge.GetComponent<Image>() : badge.transform.Find(part).GetComponent<Image>();
                    string what = $"{badge.name} {(part == "" ? "frame" : part)}";
                    Assert.True(ours != null && theirs != null, $"{what} has an image");
                    Assert.True(ours.sprite == theirs.sprite && ours.material == theirs.material && ours.color == theirs.color && ours.type == theirs.type,
                        $"{what} looks like the weight box's (sprite {ours.sprite?.name}/{theirs.sprite?.name}, material {ours.material?.name}/{theirs.material?.name}, color {ours.color}/{theirs.color})");
                    Color shown = ours.canvasRenderer.GetColor(), theirsShown = theirs.canvasRenderer.GetColor();
                    Assert.True(shown == theirsShown, $"{what} is tinted {shown}, the weight box {theirsShown}");
                }
            }
        }

        [Test]
        private static IEnumerator ChestMenuSortsTheChestAndSharesTheOrder()
        {
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 5, 3, 1);
            Put(chest.GetInventory(), "Wood", 7, 0, 0);
            Put(chest.GetInventory(), "Stone", 4, 2, 0);
            InventoryGui.instance.Show(chest);
            yield return WaitUntil(() => InventoryGui.IsVisible() && Chest != null && Chest.Menu.gameObject.activeInHierarchy, "the chest and its menu");

            StorageMaster.InventoryButtons._chestMode.onClick.Invoke();
            string label = StorageMaster.InventoryButtons._chestMode.GetComponentInChildren<TMP_Text>().text;
            Assert.Equal(label, StorageMaster.InventoryButtons._mode.GetComponentInChildren<TMP_Text>().text, "Both By buttons show the same order");
            Plugin.SortMode.Value = Sorter.Mode.NameAsc;

            StorageMaster.InventoryButtons._sortChest.onClick.Invoke();
            Assert.Equal("Stone x4 @0,0, Wood x12 @1,0", Describe(chest.GetInventory()), "Chest after Sort chest");
        }

        [Test]
        private static IEnumerator OptionsShowAndSetTheBadgePosition()
        {
            yield return ShowInventory();
            Inventory.MoveTo(new Vector2(-30f, 25f));
            InventoryGui.instance.Hide();
            OptionsWindow.Open();
            yield return Frames(2);

            TMP_InputField[] fields = OptionsWindow._window.Root.GetComponentsInChildren<TMP_InputField>()
                .Where(f => f.transform.parent.name == "set_Inventory menu button").ToArray();
            Assert.Equal(2, fields.Length, "X and Y fields");
            Assert.Equal("-30", fields[0].text, "X shown");
            Assert.Equal("25", fields[1].text, "Y shown");

            fields[0].text = "-80";
            fields[0].onEndEdit.Invoke(fields[0].text);
            Assert.Equal(-80, Plugin.InventoryBadgeX.Value, "X typed in");
            OptionsWindow.Close();
            yield return ShowInventory();
            Assert.Equal(Inventory.Home + new Vector2(-80f, 25f), Inventory.Rect.anchoredPosition, "Badge position");
        }

        // ------------------------------------------------------------------ helpers

        private static IEnumerator ShowInventory()
        {
            InventoryGui.instance.Show(null);
            yield return WaitUntil(() => InventoryGui.IsVisible() && Inventory != null, "the inventory and its badge");
            // Until the panels have slid in, the badge can seem off screen and be kept on it.
            yield return InventorySettled();
        }

        private static void Click(GameObject go)
        {
            ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }

        /// <summary>Where a rect is on screen, in the units of the UI's root canvas.</summary>
        private static Rect InRoot(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Transform root = UIKit.CanvasRoot;
            Vector2 min = root.InverseTransformPoint(corners[0]), max = root.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static string Path(Transform t) => t.parent == null ? t.name : t.parent.name + "/" + t.name;

        private static Rect Shrink(Rect r, float by) => Rect.MinMaxRect(r.xMin + by, r.yMin + by, r.xMax - by, r.yMax - by);

        private static bool Inside(Rect inner, Rect outer) =>
            inner.xMin >= outer.xMin - 0.5f && inner.yMin >= outer.yMin - 0.5f && inner.xMax <= outer.xMax + 0.5f && inner.yMax <= outer.yMax + 0.5f;
    }
}
