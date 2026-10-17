using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using StorageMaster.UI;
using UnityEngine;

namespace StorageMaster
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "charmingbeaver.valheim.storagemaster";
        public const string Name = "Storage Master";
        public const string Version = "1.0.0";

        public static ConfigEntry<Sorter.Mode> SortMode;
        public static ConfigEntry<bool> AutoSort;
        public static ConfigEntry<float> QuickStackRange;
        public static ConfigEntry<bool> QuickStackSkipsHotbar;

        public static ConfigEntry<float> StorageRange;
        public static ConfigEntry<int> StorageMaxChests;
        public static ConfigEntry<bool> StorageShowInventory;

        public static ConfigEntry<KeyboardShortcut> StorageKey;
        public static ConfigEntry<KeyboardShortcut> SortKey;
        public static ConfigEntry<KeyboardShortcut> QuickStackKey;
        public static ConfigEntry<KeyboardShortcut> AutoSortKey;
        public static ConfigEntry<KeyboardShortcut> InfoKey;

        /// <summary>All hotkeys with a name and description, for the Info window.</summary>
        public static readonly List<(ConfigEntry<KeyboardShortcut> entry, string title, string description)> Hotkeys =
            new List<(ConfigEntry<KeyboardShortcut>, string, string)>();

        private Harmony _harmony;

        private void Awake()
        {
            SortMode = Config.Bind("Sorting", "SortMode", Sorter.Mode.NameAsc, "What Sort orders by.");
            AutoSort = Config.Bind("Sorting", "AutoSort", false, "Sort the inventory every time you pick something up.");
            QuickStackRange = Config.Bind("QuickStack", "Range", 10f, "Meters around you in which chests are used by quick stack.");
            QuickStackSkipsHotbar = Config.Bind("QuickStack", "SkipHotbar", true, "Quick stack leaves the hotbar row alone.");

            StorageRange = Config.Bind("Storage", "Range", 15f, "Meters around you in which chests are opened by the Storage window.");
            StorageMaxChests = Config.Bind("Storage", "MaxChests", 24, "Most chests the Storage window opens at once (nearest first).");
            StorageShowInventory = Config.Bind("Storage", "ShowInventory", true, "Show your inventory in the Storage window. Off gives the chests the whole window.");

            StorageKey = Config.Bind("Hotkeys", "Storage", new KeyboardShortcut(KeyCode.F1, KeyCode.LeftShift), "Open all nearby chests in one window.");
            SortKey = Config.Bind("Hotkeys", "SortInventory", new KeyboardShortcut(KeyCode.F4, KeyCode.LeftShift), "Sort your inventory (not the hotbar).");
            QuickStackKey = Config.Bind("Hotkeys", "QuickStack", new KeyboardShortcut(KeyCode.F3, KeyCode.LeftShift), "Put items into nearby chests that already hold the same kind of item.");
            AutoSortKey = Config.Bind("Hotkeys", "ToggleAutoSort", KeyboardShortcut.Empty, "Switch auto-sort on pickup on or off.");
            InfoKey = Config.Bind("Hotkeys", "Info", KeyboardShortcut.Empty, "Open the Storage Master info window.");

            Hotkeys.Add((StorageKey, "Storage window", "Open all nearby chests in one window."));
            Hotkeys.Add((SortKey, "Sort inventory", "Sort everything below the hotbar."));
            Hotkeys.Add((QuickStackKey, "Quick stack", "Put items into nearby chests that already hold the same kind."));
            Hotkeys.Add((AutoSortKey, "Auto-sort on/off", "Switch sorting on pickup on or off."));
            Hotkeys.Add((InfoKey, "Info", "Open this window."));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            if (Player.m_localPlayer == null)
            {
                return;
            }
            bool capturing = KeyCapture.Active;
            InventoryButtons.Ensure();
            QuickStack.Update();
            StorageWindow.Update();
            InfoWindow.Update();

            if (IsTyping())
            {
                return;
            }
            // Also skip the frame a key change finishes, so the new key doesn't fire straight away.
            if (capturing || KeyCapture.Active) return;
            if (InfoKey.Value.IsDown()) InfoWindow.Toggle();
            if (StorageKey.Value.IsDown()) StorageWindow.Toggle();
            if (StorageWindow.IsOpen || InfoWindow.IsOpen) return;
            if (SortKey.Value.IsDown()) Sorter.SortPlayer(silent: false);
            if (QuickStackKey.Value.IsDown()) QuickStack.Run();
            if (AutoSortKey.Value.IsDown()) ToggleAutoSort();
        }

        public static void ToggleAutoSort()
        {
            AutoSort.Value = !AutoSort.Value;
            Show($"Auto-sort on pickup: {(AutoSort.Value ? "<color=#8fd16a>ON</color>" : "<color=#ff7a6a>OFF</color>")}");
            InventoryButtons.Refresh();
            if (AutoSort.Value) Sorter.SortPlayer(silent: true);
        }

        private static bool IsTyping()
        {
            return UIKit.TextFieldFocused() || (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() || Menu.IsVisible();
        }

        public static void Show(string text)
        {
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, text);
        }
    }
}
