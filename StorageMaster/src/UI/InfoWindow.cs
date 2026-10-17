using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StorageMaster.UI
{
    /// <summary>
    /// Help for the mod: what every feature does, the hotkeys (click to change) and the main settings.
    /// </summary>
    public static class InfoWindow
    {
        private const float W = 940f, H = 820f;

        private class KeyRow
        {
            public ConfigEntry<KeyboardShortcut> Entry;
            public string Title;
            public Button Button;
            public TextMeshProUGUI Warning;
        }

        private static GameObject _root;
        private static readonly List<KeyRow> _keyRows = new List<KeyRow>();
        private static readonly List<Action> _refreshers = new List<Action>();
        private static TextMeshProUGUI _footer;
        private static int _swallowUntilFrame = -10;

        public static bool IsOpen => _root != null && _root.activeSelf;
        public static bool BlocksInput => IsOpen || KeyCapture.Active || Time.frameCount <= _swallowUntilFrame;

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public static void Open()
        {
            if (Player.m_localPlayer == null || !UIKit.Init()) return;
            if (_root == null) Build();
            StorageWindow.Close();
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            Refresh();
        }

        public static void Close()
        {
            if (!IsOpen) return;
            KeyCapture.Cancel();
            _root.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            _swallowUntilFrame = Time.frameCount + 1;
        }

        public static void Update()
        {
            KeyCapture.Update();
            if (!IsOpen) return;
            if (Player.m_localPlayer == null || (Input.GetKeyDown(KeyCode.Escape) && KeyCapture.EscapeUsedFrame != Time.frameCount))
            {
                Close();
            }
        }

        // ------------------------------------------------------------------ building

        private static void Build()
        {
            KeyCapture.Changed += () => { if (IsOpen) Refresh(); };

            _root = UIKit.Rect(UIKit.CanvasRoot, "StorageMasterInfo").Fill().gameObject;
            _root.AddComponent<Image>().color = new Color(0, 0, 0, 0.35f);

            RectTransform window = UIKit.Panel(_root.transform, "window").rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(W, H);

            UIKit.Text(window, "Storage Master", 40, UIKit.Orange, TextAlignmentOptions.Center, true).rectTransform.TopLeft(0, 12, W, 50);
            Button close = UIKit.Button(window, "Close", Close, 110, 36);
            ((RectTransform)close.transform).TopLeft(W - 134, 22, 110, 36);
            UIKit.Text(window, $"Version {Plugin.Version}  ·  sorting, locked slots, quick stack and all your chests in one window", 16, UIKit.Grey, TextAlignmentOptions.Center)
                .rectTransform.TopLeft(0, 64, W, 26);

            RectTransform inset = UIKit.Inset(window, "list").rectTransform.Anchor(0, 0, 1, 1, 24, 70, 24, 98);
            RectTransform c = UIKit.ScrollList(inset, "scroll", out _, 4, 14);
            ((RectTransform)c.parent.parent).Fill(4);

            Section(c, "Sorting");
            Para(c, "<color=#ffa13c>Sort</color> (next to your inventory) tidies everything below the hotbar. The hotbar row is never moved. " +
                    "Partial stacks of the same item are merged first, so three stacks of 30 wood become one of 90.");
            Para(c, "<color=#ffa13c>By: ...</color> chooses the order: Name A-Z, Name Z-A, Type (weapons, ammo, armor, tools, food, materials, trophies), " +
                    "Quality (best first), Weight (heaviest first) or Amount (biggest stacks first).");
            Para(c, "<color=#ffa13c>Sort chest</color> appears next to an open chest and sorts it the same way. " +
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

            Section(c, "Hotkeys");
            Para(c, "Click a key to change it, then press the new key (hold Ctrl, Alt or Shift for a combination). Esc cancels, Backspace removes the key.");
            foreach (var hk in Plugin.Hotkeys) _keyRows.Add(KeyRowFor(c, hk.entry, hk.title, hk.description));

            Section(c, "Settings");
            CheckRow(c, "Auto-sort on pickup", "Sort your inventory every time you pick something up.", Plugin.AutoSort);
            CheckRow(c, "Quick stack skips the hotbar", "Keep everything in the hotbar row when quick stacking.", Plugin.QuickStackSkipsHotbar);
            NumberRow(c, "Quick stack range", "How far away chests can be for quick stack.", () => $"{Plugin.QuickStackRange.Value:0} m",
                d => Plugin.QuickStackRange.Value = Mathf.Clamp(Plugin.QuickStackRange.Value + d * 5f, 5f, 50f));
            NumberRow(c, "Storage window range", "How far away chests can be for the Storage window.", () => $"{Plugin.StorageRange.Value:0} m",
                d => Plugin.StorageRange.Value = Mathf.Clamp(Plugin.StorageRange.Value + d * 5f, 5f, 50f));
            NumberRow(c, "Most chests in Storage", "The nearest chests are shown first.", () => Plugin.StorageMaxChests.Value.ToString(),
                d => Plugin.StorageMaxChests.Value = Mathf.Clamp(Plugin.StorageMaxChests.Value + d * 4, 4, 60));

            _footer = UIKit.Text(window, "", 16, UIKit.Grey, TextAlignmentOptions.Center).Wrap();
            RectTransform frt = _footer.rectTransform;
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(1, 0);
            frt.pivot = new Vector2(0.5f, 0);
            frt.offsetMin = new Vector2(24, 14);
            frt.offsetMax = new Vector2(-24, 60);

            _root.SetActive(false);
        }

        private static void Section(Transform parent, string title)
        {
            UIKit.Spacer(parent, -1, 6, 0);
            UIKit.Text(parent, title, 26, UIKit.Orange, norse: true).Size(-1, 34);
            UIKit.Solid(parent, new Color(1f, 0.631f, 0.235f, 0.35f), "rule").Size(-1, 2);
        }

        private static void Para(Transform parent, string text)
        {
            UIKit.Text(parent, text, 16).Wrap().Size(-1, -1);
        }

        private static KeyRow KeyRowFor(Transform parent, ConfigEntry<KeyboardShortcut> entry, string title, string description)
        {
            var row = new KeyRow { Entry = entry, Title = title };
            HorizontalLayoutGroup h = UIKit.HRow(parent, "key_" + title, 12);
            h.padding = new RectOffset(6, 6, 2, 2);
            h.Size(-1, 58);
            VerticalLayoutGroup text = UIKit.VCol(h.transform, "text", 1);
            text.childAlignment = TextAnchor.MiddleLeft;
            text.Size(-1, -1, 1);
            UIKit.Text(text.transform, title, 19).Size(-1, 22);
            UIKit.Text(text.transform, description, 15, UIKit.Grey).Size(-1, 18);
            row.Warning = UIKit.Text(text.transform, "", 14, UIKit.Red);
            row.Warning.Size(-1, 16);
            row.Warning.gameObject.SetActive(false);
            row.Button = UIKit.Button(h.transform, "", () => KeyCapture.Begin(entry), 190, 38);
            return row;
        }

        private static void CheckRow(Transform parent, string title, string description, ConfigEntry<bool> entry)
        {
            HorizontalLayoutGroup h = UIKit.HRow(parent, "set_" + title, 12);
            h.padding = new RectOffset(6, 6, 2, 2);
            h.Size(-1, 50);
            Toggle t = UIKit.Check(h.transform, "", entry.Value, v => { entry.Value = v; InventoryButtons.Refresh(); }, 30);
            Labels(h.transform, title, description);
            _refreshers.Add(() => t.SetIsOnWithoutNotify(entry.Value));
        }

        private static void NumberRow(Transform parent, string title, string description, Func<string> value, Action<int> change)
        {
            HorizontalLayoutGroup h = UIKit.HRow(parent, "set_" + title, 8);
            h.padding = new RectOffset(6, 6, 2, 2);
            h.Size(-1, 50);
            UIKit.Spacer(h.transform, 30, -1, 0);
            Labels(h.transform, title, description);
            UIKit.Button(h.transform, "-", () => { change(-1); Refresh(); }, 40, 36);
            TextMeshProUGUI v = UIKit.Text(h.transform, "", 19, UIKit.Parchment, TextAlignmentOptions.Center);
            v.Size(80, 36);
            UIKit.Button(h.transform, "+", () => { change(+1); Refresh(); }, 40, 36);
            _refreshers.Add(() => v.text = value());
        }

        private static void Labels(Transform parent, string title, string description)
        {
            VerticalLayoutGroup text = UIKit.VCol(parent, "text", 1);
            text.childAlignment = TextAnchor.MiddleLeft;
            text.Size(-1, -1, 1);
            UIKit.Text(text.transform, title, 19).Size(-1, 22);
            UIKit.Text(text.transform, description, 15, UIKit.Grey).Size(-1, 18);
        }

        private static void Refresh()
        {
            foreach (KeyRow row in _keyRows)
            {
                bool capturing = KeyCapture.Target == row.Entry;
                row.Button.SetLabel(capturing ? "<color=#ffa13c>Press a key...</color>" : KeyNames.Format(row.Entry.Value));
                string clash = capturing ? null : KeyNames.ValheimConflict(row.Entry.Value);
                row.Warning.text = clash == null ? "" : $"Valheim also uses this key for {clash}";
                row.Warning.gameObject.SetActive(clash != null);
            }
            foreach (Action r in _refreshers) r();
            _footer.text = KeyCapture.Active
                ? $"Press the new key for <color=#ffa13c>{Plugin.Hotkeys.Find(h => h.entry == KeyCapture.Target).title}</color>. Esc cancels, Backspace removes the key."
                : "Everything here is saved to BepInEx\\config\\" + Plugin.Guid + ".cfg";
        }
    }
}
