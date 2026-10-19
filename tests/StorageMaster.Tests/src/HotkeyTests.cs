using System.Linq;
using BepInEx.Configuration;
using StorageMaster.UI;
using UnityEngine;

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

        /// <summary>The Info window, where a menu button moved out of reach is reset, can always be opened with a key.</summary>
        [Test]
        private static void InfoWindowHasADefaultHotkey()
        {
            Assert.Equal(new KeyboardShortcut(KeyCode.F6, KeyCode.LeftShift).ToString(), ((KeyboardShortcut)Plugin.InfoKey.DefaultValue).ToString(), "Default Info key");
            Assert.True(!Plugin.InfoKey.Value.Equals(KeyboardShortcut.Empty), "The Info key is set");
        }
    }
}
