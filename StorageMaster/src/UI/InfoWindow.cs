using System.Text;
using TMPro;
using UnityEngine;
using static StorageMaster.UI.ScrollWindow;

namespace StorageMaster.UI
{
    /// <summary>Help for the mod: what every feature does and which keys open what. Settings are in the Options window.</summary>
    public static class InfoWindow
    {
        private static readonly ScrollWindow _window = new ScrollWindow("StorageMasterInfo", "Storage Master",
            $"Version {Plugin.Version}  ·  sorting, locked slots, quick stack and all your chests in one window", Fill)
        {
            Opened = Refresh,
        };

        private static TextMeshProUGUI _hotkeys;

        public static bool IsOpen => _window.IsOpen;
        public static void Toggle() => _window.Toggle();
        public static void Open() => _window.Open();
        public static void Close() => _window.Close();
        public static void Update() => _window.Update();

        private static void Fill(RectTransform c)
        {
            Section(c, "Sorting");
            Para(c, "<color=#ffa13c>Sort</color> (in the menu next to your inventory) tidies everything below the hotbar. The hotbar row is never moved. " +
                    "Partial stacks of the same item are merged first, so three stacks of 30 wood become one of 90.");
            Para(c, "<color=#ffa13c>By: ...</color> chooses the order: Name A-Z, Name Z-A, Type (weapons, ammo, armor, tools, food, materials, trophies), " +
                    "Quality (best first), Weight (heaviest first) or Amount (biggest stacks first).");
            Para(c, "<color=#ffa13c>Sort chest</color> is in the menu next to an open chest and sorts it the same way. " +
                    "<color=#ffa13c>Auto-sort</color> sorts your inventory every time you pick something up.");

            Section(c, "Locked slots");
            Para(c, "<color=#ffa13c>Alt + click</color> a slot in your inventory to lock or unlock it. Locked slots are tinted orange. " +
                    "Sort, auto-sort and quick stack never move what's in them, and neither does the game's own Place stacks button. " +
                    "Locks are saved with your character.");

            Section(c, "Quick stack");
            Para(c, "<color=#ffa13c>Quick stack</color> puts your items into nearby chests that already hold the same kind of item. " +
                    "Your hotbar, equipped items and locked slots stay with you, and chests marked Skip in the Storage window are left out. " +
                    "Each chest is handed over by whoever controls it first, so it's safe while your friend plays, and a chest they have open is skipped.");

            Section(c, "Storage window");
            Para(c, "<color=#ffa13c>Storage</color> opens every chest around you at once, next to your inventory.");
            Para(c, "<color=#ffa13c>Click</color> picks up and puts down, like the normal chest window.  <color=#ffa13c>Shift + click</color> takes half a stack.  " +
                    "<color=#ffa13c>Ctrl + click</color> moves an item between you and the chests.  <color=#ffa13c>Alt + click</color> locks one of your slots.  " +
                    "<color=#ffa13c>Esc</color> puts a carried item back, or closes the window.");
            Para(c, "<color=#ffa13c>Right-click</color> an item to choose it: every slot with it lights up and you see how many you have and where. " +
                    "Then <color=#ffa13c>Gather here</color> on a chest pulls all of it into that chest; what doesn't fit goes into the next chests.");
            Para(c, "<color=#ffa13c>Find item</color> highlights matches in every chest.  <color=#ffa13c>Tidy up</color> brings each item that's spread over several chests " +
                    "into the chest holding most of it, then sorts all chests.  Click a chest's title to <color=#ffa13c>name it</color>, " +
                    "here or in the normal chest window (E); the name shows when you look at the chest in the world.  " +
                    "<color=#ffa13c>Settings</color> sets how far away and how many chests are opened (up to 20 m and about 30 chests is recommended).");
            Para(c, "Tick <color=#ffa13c>Skip</color> on a chest (for example your friend's) and Quick stack, Gather here, Tidy up and Sort all " +
                    "leave it alone, here and with the Quick stack button. You can still move items in and out by hand. Only you see this setting.");
            Para(c, "<color=#ffa13c>Hide inventory</color> gives the chests the whole window, so more of them fit side by side. " +
                    "Ctrl + click on a chest item still moves it to you.");

            Section(c, "Menu buttons");
            Para(c, "The buttons are in two menus, one next to your inventory and one next to an open chest. " +
                    "Click the box with three bars (above the weight) to open or close a menu; it stays the way you leave it. " +
                    "Hold the drag key (<color=#ffa13c>Ctrl</color> unless changed in Options) and drag the box to move it, for example when it covers another mod's window. " +
                    "If one ends up out of reach, <color=#ffa13c>Reset</color> in Options puts it back.");

            Section(c, "Hotkeys");
            _hotkeys = Para(c, "");
        }

        /// <summary>The hotkeys as they're set now; they're changed in Options.</summary>
        private static void Refresh()
        {
            var sb = new StringBuilder();
            foreach (var hk in Plugin.Hotkeys)
            {
                string key = hk.entry.Value.MainKey == KeyCode.None ? "<color=#a8a49e>not set</color>" : $"<color=#ffa13c>{KeyNames.Format(hk.entry.Value)}</color>";
                sb.Append($"{hk.title}: {key}\n");
            }
            sb.Append("Change them in <color=#ffa13c>Options</color> (in the menu next to your inventory).");
            _hotkeys.text = sb.ToString();
            string optionsKey = Plugin.OptionsKey.Value.MainKey == KeyCode.None ? "" : $"<color=#ffa13c>{KeyNames.Format(Plugin.OptionsKey.Value)}</color> or ";
            _window.Footer.text = $"Options: {optionsKey}the Options button in the menu next to your inventory";
        }
    }
}
