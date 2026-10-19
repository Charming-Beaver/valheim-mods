using System.Collections;
using System.Linq;
using BepInEx.Configuration;
using StorageMaster.UI;
using TMPro;
using UnityEngine;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    public static class HotkeyTests
    {
        /// <summary>No default hotkey is one Valheim also reacts to (Shift + F2 would open its network stats).</summary>
        [Test]
        private static void DefaultHotkeysDontClashWithValheim()
        {
            var clashes = Plugin.Hotkeys
                .Select(h => (h.title, key: (KeyboardShortcut)h.entry.DefaultValue))
                .Select(h => (h.title, h.key, clash: KeyNames.ValheimConflict(h.key)))
                .Where(h => h.clash != null)
                .Select(h => $"{h.title} ({KeyNames.Format(h.key)}) is also {h.clash}")
                .ToList();
            Assert.True(clashes.Count == 0, string.Join("; ", clashes));
        }

        /// <summary>The warning knows Valheim's own keys, both fixed ones (F2) and bindings (F5, the console).</summary>
        [Test]
        private static void KnowsValheimsKeys()
        {
            foreach (KeyCode key in new[] { KeyCode.F2, KeyCode.F5 })
            {
                Assert.True(KeyNames.ValheimConflict(new KeyboardShortcut(key, KeyCode.LeftShift)) != null, $"Shift + {key} is known as Valheim's");
            }
        }

        /// <summary>Options, where a menu button moved out of reach is reset, can always be opened with a key.</summary>
        [Test]
        private static void OptionsHaveADefaultHotkey()
        {
            Assert.Equal(new KeyboardShortcut(KeyCode.F6, KeyCode.LeftShift).ToString(), ((KeyboardShortcut)Plugin.OptionsKey.DefaultValue).ToString(), "Default Options key");
            Assert.True(!Plugin.OptionsKey.Value.Equals(KeyboardShortcut.Empty), "The Options key is set");
        }

        /// <summary>The Info window lists the hotkeys as they're set (the #8 reporter looked for them there).</summary>
        [Test]
        private static IEnumerator InfoWindowListsTheHotkeys()
        {
            InfoWindow.Open();
            yield return Frames(2);

            string shown = string.Join("\n", InfoWindow._window.Root.GetComponentsInChildren<TMP_Text>().Select(t => t.text));
            foreach (var hk in Plugin.Hotkeys.Where(h => h.entry.Value.MainKey != KeyCode.None))
            {
                Assert.True(shown.Contains($"{hk.title}: <color=#ffa13c>{KeyNames.Format(hk.entry.Value)}</color>"), $"Info shows the key for {hk.title}");
            }
        }

        [Test]
        private static IEnumerator OptionsButtonOpensOptions()
        {
            InventoryGui.instance.Show(null);
            yield return WaitUntil(() => InventoryGui.IsVisible() && StorageMaster.InventoryButtons._options != null, "the inventory menu");
            StorageMaster.InventoryButtons._options.onClick.Invoke();
            yield return Frames(2);

            Assert.True(OptionsWindow.IsOpen, "Options open");
            // IsVisible() says yes while Options is open (it keeps the player from moving), so check what it checks itself.
            Assert.True(!InfoWindow.IsOpen && InventoryGui.instance.m_hiddenFrames > 1, "Info and the inventory are closed");
        }
    }
}
