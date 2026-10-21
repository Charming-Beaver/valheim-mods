using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using StorageMaster.UI;
using UnityEngine;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    /// <summary>
    /// Storage Master's menus and windows take SeneaL UI's look while it draws the inventory its way, and the Valheim
    /// look otherwise (#2), also when SeneaL UI's inventory is switched off and on again in game.
    /// </summary>
    public static class ThemeTests
    {
        [Test, SkipWithMod(SeneaLCompat.Guid, "gives the menus and windows its own look; see the SeneaL tests here")]
        private static IEnumerator WithoutSeneaLEverythingKeepsTheValheimLook()
        {
            yield return OpenEverything(root => AssertLook(root, seneaL: false));
        }

        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static IEnumerator WithSeneaLEverythingTakesItsLook()
        {
            yield return BadgeTests.ShowInventory();
            Assert.True(SeneaLTheme.On, "SeneaL UI's look is on (its inventory panel is shown, its sprites and fonts were found)");
            yield return OpenEverything(root => AssertLook(root, seneaL: true));
        }

        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static IEnumerator SwitchingSeneaLsInventoryOffAndOnSwitchesTheLook()
        {
            InventoryGui gui = InventoryGui.instance;
            ConfigEntry<bool> inventory = SeneaLSwitch("Inventory"), chest = SeneaLSwitch("Container");
            Assert.True(inventory.Value && chest.Value, "SeneaL UI draws the inventory and chests at first");
            // TestKit.Reset puts them back on if this test stops halfway.
            _restore = true;
            inventory.Value = false;
            chest.Value = false;

            gui.Show(SpawnChest());
            yield return WaitUntil(() => InventoryGui.IsVisible() && SeneaLCompat.InventoryPanel == null && InventoryButtons._badge != null
                && InventoryButtons._badge.Panel == gui.m_player, "the game's inventory panel and its badge", 10f);
            yield return Frames(3);
            bool lookWhileOff = SeneaLTheme.On;
            bool chestOnGame = InventoryButtons._chestBadge != null && InventoryButtons._chestBadge.Panel == gui.m_container;
            gui.Hide();
            yield return OpenEverything(root => AssertLook(root, seneaL: false));

            inventory.Value = true;
            chest.Value = true;
            _restore = false;
            Assert.True(!lookWhileOff, "SeneaL UI's look is off with its inventory switched off");
            Assert.True(chestOnGame, "The chest badge is on the game's chest panel with SeneaL UI's chests switched off");

            yield return BadgeTests.ShowInventory();
            yield return WaitUntil(() => SeneaLCompat.InventoryPanel != null && InventoryButtons._badge.Panel == SeneaLCompat.InventoryPanel,
                "SeneaL UI's inventory panel and the badge on it again", 10f);
            Assert.True(SeneaLTheme.On, "SeneaL UI's look is on again");
            yield return OpenEverything(root => AssertLook(root, seneaL: true));
        }

        private static bool _restore;

        /// <summary>SeneaL UI's own switch for drawing one of its panels (Prefs.Inventory.On and so on).</summary>
        private static ConfigEntry<bool> SeneaLSwitch(string panel)
        {
            System.Type prefs = Chainloader.PluginInfos[SeneaLCompat.Guid].Instance.GetType().Assembly.GetType("SeneaLUI.Core.Prefs");
            object elem = prefs.GetField(panel).GetValue(null);
            return (ConfigEntry<bool>)elem.GetType().GetField("On").GetValue(elem);
        }

        /// <summary>Puts SeneaL UI's switches back on if a test stopped with them off.</summary>
        public static void RestoreSeneaL()
        {
            if (!_restore) return;
            _restore = false;
            SeneaLSwitch("Inventory").Value = true;
            SeneaLSwitch("Container").Value = true;
        }

        /// <summary>Opens the inventory with a chest (both menus), then the Storage, Info and Options windows, checking each.</summary>
        public static IEnumerator OpenEverything(System.Action<GameObject> check)
        {
            InventoryGui gui = InventoryGui.instance;
            gui.Show(SpawnChest());
            yield return WaitUntil(() => InventoryGui.IsVisible() && InventoryButtons._chestBadge != null
                && InventoryButtons._chestBadge.gameObject.activeInHierarchy && InventoryButtons._chestMenu.gameObject.activeInHierarchy, "the chest and its menu");
            yield return Frames(3);
            check(InventoryButtons._menu.gameObject);
            check(InventoryButtons._chestMenu.gameObject);
            gui.Hide();
            yield return Frames(2);

            StorageWindow.Open();
            yield return WaitUntil(() => StorageWindow._cards.All(c => c.Claimed), "the chests to open");
            yield return Frames(3);
            check(StorageWindow._root);
            StorageWindow.Close();

            InfoWindow.Open();
            yield return Frames(3);
            check(InfoWindow._window.Root);
            OptionsWindow.Open();
            yield return Frames(3);
            check(OptionsWindow._window.Root);
            OptionsWindow.Close();
        }

        /// <summary>Every marked piece has the given look, and every text is drawn.</summary>
        private static void AssertLook(GameObject root, bool seneaL)
        {
            Themed[] pieces = root.GetComponentsInChildren<Themed>(true);
            Assert.True(pieces.Length > 0, $"{root.name} has themed pieces");
            Themed wrong = pieces.FirstOrDefault(p => p.SeneaL != seneaL);
            Assert.True(wrong == null, $"{root.name}: {wrong?.name} ({wrong?.Kind}) has the {(seneaL ? "Valheim" : "SeneaL UI")} look ({pieces.Count(p => p.SeneaL != seneaL)} of {pieces.Length})");
            // The fonts too, which other code (an input field) can set again after the look was given.
            foreach (Themed piece in pieces.Where(p => p.Kind == Themed.Role.Text || p.Kind == Themed.Role.Heading))
            {
                TMPro.TMP_Text text = piece.GetComponent<TMPro.TMP_Text>();
                bool theirs = text.font == SeneaLTheme.TextFont || text.font == SeneaLTheme.HeadingFont;
                Assert.True(theirs == seneaL, $"{root.name}: \"{text.text}\" ({piece.name}) has the font {text.font?.name}");
            }
            if (root.activeInHierarchy) TextTests.AssertAllDrawn(root);
        }
    }
}
