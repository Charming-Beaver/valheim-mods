using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static StorageMaster.UI.ScrollWindow;

namespace StorageMaster.UI
{
    /// <summary>The hotkeys (click to change), the main settings and the menu buttons' drag key and positions.</summary>
    public static class OptionsWindow
    {
        private class KeyRow
        {
            public ConfigEntry<KeyboardShortcut> Entry;
            public Button Button;
            public TextMeshProUGUI Warning;
        }

        private static readonly ScrollWindow _window = new ScrollWindow("StorageMasterOptions", "Options",
            "Storage Master  ·  hotkeys, settings and the menu buttons", Fill)
        {
            Opened = Refresh,
            Closed = KeyCapture.Cancel,
        };

        private static readonly List<KeyRow> _keyRows = new List<KeyRow>();
        private static readonly List<Action> _refreshers = new List<Action>();
        private static bool _listening;

        public static bool IsOpen => _window.IsOpen;
        public static void Toggle() => _window.Toggle();
        public static void Open() => _window.Open();
        public static void Close() => _window.Close();

        public static void Update()
        {
            KeyCapture.Update();
            _window.Update();
        }

        private static void Fill(RectTransform c)
        {
            if (!_listening)
            {
                KeyCapture.Changed += () => { if (IsOpen) Refresh(); };
                _listening = true;
            }
            // Built again after the window was thrown away (tests); the old rows are gone.
            _keyRows.Clear();
            _refreshers.Clear();

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

            Section(c, "Menu buttons");
            Para(c, "Hold the drag key and drag a menu button (the box with three bars) to move it. " +
                    "<color=#ffa13c>Reset</color> puts it back in its usual place, for example when it ended up out of reach.");
            ChoiceRow(c, "Drag key", "Hold it to move a menu button with the mouse.", () => Plugin.DragKey.Value.ToString(),
                () => Plugin.DragKey.Value = (Plugin.DragKeys)(((int)Plugin.DragKey.Value + 1) % 3));
            PositionRow(c, "Inventory menu button", "How far it's moved: right (X) and up (Y).", Plugin.InventoryBadgeX, Plugin.InventoryBadgeY);
            PositionRow(c, "Chest menu button", "How far it's moved: right (X) and up (Y).", Plugin.ChestBadgeX, Plugin.ChestBadgeY);
        }

        private static KeyRow KeyRowFor(Transform parent, ConfigEntry<KeyboardShortcut> entry, string title, string description)
        {
            var row = new KeyRow { Entry = entry };
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

        private static void ChoiceRow(Transform parent, string title, string description, Func<string> value, Action next)
        {
            HorizontalLayoutGroup h = UIKit.HRow(parent, "set_" + title, 8);
            h.padding = new RectOffset(6, 6, 2, 2);
            h.Size(-1, 50);
            UIKit.Spacer(h.transform, 30, -1, 0);
            Labels(h.transform, title, description);
            Button b = UIKit.Button(h.transform, "", () => { next(); Refresh(); }, 176, 36);
            _refreshers.Add(() => b.SetLabel(value()));
        }

        /// <summary>X and Y of a menu button, kept in step with dragging it, and a button to put it back.</summary>
        private static void PositionRow(Transform parent, string title, string description, ConfigEntry<int> x, ConfigEntry<int> y)
        {
            HorizontalLayoutGroup h = UIKit.HRow(parent, "set_" + title, 8);
            h.padding = new RectOffset(6, 6, 2, 2);
            h.Size(-1, 50);
            UIKit.Spacer(h.transform, 30, -1, 0);
            Labels(h.transform, title, description);
            foreach (ConfigEntry<int> entry in new[] { x, y })
            {
                UIKit.Text(h.transform, entry == x ? "X" : "Y", 19, UIKit.Orange, TextAlignmentOptions.Center).Size(16, 36);
                TMP_InputField field = UIKit.InputField(h.transform, "0", 72, 36);
                field.contentType = TMP_InputField.ContentType.IntegerNumber;
                field.onEndEdit.AddListener(text =>
                {
                    if (int.TryParse(text, out int value)) entry.Value = value;
                    InventoryButtons.ApplyBadges();
                    Refresh();
                });
                _refreshers.Add(() => { if (!field.isFocused) field.SetTextWithoutNotify(entry.Value.ToString()); });
            }
            UIKit.Button(h.transform, "Reset", () =>
            {
                x.Value = y.Value = 0;
                InventoryButtons.ApplyBadges();
                Refresh();
            }, 80, 36);
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
            _window.Footer.text = KeyCapture.Active
                ? $"Press the new key for <color=#ffa13c>{Plugin.Hotkeys.Find(h => h.entry == KeyCapture.Target).title}</color>. Esc cancels, Backspace removes the key."
                : "Everything here is saved to BepInEx\\config\\" + Plugin.Guid + ".cfg";
        }
    }
}
