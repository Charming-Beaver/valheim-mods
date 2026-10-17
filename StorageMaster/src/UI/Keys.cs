using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;

namespace StorageMaster.UI
{
    /// <summary>Waits for the next key press and assigns it to one of this mod's hotkeys.</summary>
    public static class KeyCapture
    {
        public static ConfigEntry<KeyboardShortcut> Target { get; private set; }
        public static bool Active => Target != null;
        public static int EscapeUsedFrame = -1;
        public static event Action Changed;

        private static int _startFrame;
        private static readonly KeyCode[] Candidates = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
            .Where(k => k != KeyCode.None && k < KeyCode.JoystickButton0).Distinct().ToArray();

        public static void Begin(ConfigEntry<KeyboardShortcut> entry)
        {
            Target = entry;
            _startFrame = Time.frameCount;
            Changed?.Invoke();
        }

        public static void Cancel()
        {
            if (Target == null) return;
            Target = null;
            Changed?.Invoke();
        }

        public static void Update()
        {
            if (Target == null || Time.frameCount <= _startFrame) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                EscapeUsedFrame = Time.frameCount;
                Cancel();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                Assign(KeyboardShortcut.Empty);
                return;
            }
            foreach (KeyCode key in Candidates)
            {
                if (KeyNames.IsModifier(key) || !Input.GetKeyDown(key)) continue;
                KeyCode[] mods = KeyNames.Modifiers.Where(Input.GetKey).ToArray();
                if ((key == KeyCode.Mouse0 || key == KeyCode.Mouse1) && mods.Length == 0)
                {
                    Plugin.Show("Left and right click need Ctrl, Alt or Shift held as well");
                    return;
                }
                Assign(new KeyboardShortcut(key, mods));
                return;
            }
        }

        private static void Assign(KeyboardShortcut shortcut)
        {
            ConfigEntry<KeyboardShortcut> entry = Target;
            Target = null;
            string note = "";
            if (shortcut.MainKey != KeyCode.None)
            {
                foreach (var other in Plugin.Hotkeys)
                {
                    if (other.entry != entry && KeyNames.Same(other.entry.Value, shortcut))
                    {
                        other.entry.Value = KeyboardShortcut.Empty;
                        note = $"\n<size=70%>(removed from {other.title})</size>";
                    }
                }
            }
            entry.Value = shortcut;
            string title = Plugin.Hotkeys.First(h => h.entry == entry).title;
            Plugin.Show($"{title}: {(shortcut.MainKey == KeyCode.None ? "no key" : KeyNames.Format(shortcut))}{note}");
            Changed?.Invoke();
        }
    }

    public static class KeyNames
    {
        public static readonly KeyCode[] Modifiers =
        {
            KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt,
        };

        public static bool IsModifier(KeyCode key) => Array.IndexOf(Modifiers, key) >= 0
            || key == KeyCode.LeftCommand || key == KeyCode.RightCommand || key == KeyCode.LeftWindows || key == KeyCode.RightWindows || key == KeyCode.AltGr;

        public static bool Same(KeyboardShortcut a, KeyboardShortcut b)
        {
            return a.MainKey == b.MainKey && new HashSet<KeyCode>(a.Modifiers).SetEquals(b.Modifiers);
        }

        public static string Format(KeyboardShortcut s)
        {
            if (s.MainKey == KeyCode.None)
            {
                return "—";
            }
            return string.Join(" + ", s.Modifiers.Select(Name).Concat(new[] { Name(s.MainKey) }));
        }

        public static string Name(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftControl:
                case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt: return "Alt";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
                case KeyCode.PageUp: return "Page Up";
                case KeyCode.PageDown: return "Page Down";
                case KeyCode.Mouse0: return "Left click";
                case KeyCode.Mouse1: return "Right click";
                case KeyCode.Mouse2: return "Middle click";
                case KeyCode.Mouse3: return "Mouse 4";
                case KeyCode.Mouse4: return "Mouse 5";
                case KeyCode.Mouse5: return "Mouse 6";
                case KeyCode.Mouse6: return "Mouse 7";
                case KeyCode.Return: return "Enter";
                case KeyCode.BackQuote: return "`";
            }
            string name = key.ToString();
            if (name.StartsWith("Alpha")) return name.Substring(5);
            if (name.StartsWith("Keypad")) return "Num " + name.Substring(6);
            return name;
        }

        // Keys Valheim checks directly in code rather than through its rebindable controls.
        private static readonly (KeyCode key, bool ctrl, string what)[] HardCoded =
        {
            (KeyCode.F1, true, "freeing the mouse"),
            (KeyCode.F2, false, "network stats"),
            (KeyCode.F3, true, "hiding the HUD"),
            (KeyCode.F9, false, "switching the controller layout"),
            (KeyCode.F11, false, "screenshots"),
            (KeyCode.Escape, false, "the menu"),
        };

        /// <summary>Name of a Valheim action that reacts to the same key, or null.</summary>
        public static string ValheimConflict(KeyboardShortcut s)
        {
            if (s.MainKey == KeyCode.None)
            {
                return null;
            }
            bool ctrl = s.Modifiers.Contains(KeyCode.LeftControl) || s.Modifiers.Contains(KeyCode.RightControl);
            foreach (var h in HardCoded)
            {
                if (h.key == s.MainKey && (!h.ctrl || ctrl))
                {
                    return h.what;
                }
            }

            ZInput input = ZInput.instance;
            if (input == null)
            {
                return null;
            }
            string path;
            try
            {
                path = ZInput.KeyCodeToPath(s.MainKey);
            }
            catch
            {
                return null;
            }
            if (string.IsNullOrEmpty(path) || path.EndsWith("/None"))
            {
                return null;
            }
            foreach (ZInput.ButtonDef def in input.m_buttons.Values)
            {
                if (def.Source == ZInput.InputSource.Gamepad || !IsGameplayButton(def.Name) || def.ButtonAction == null)
                {
                    continue;
                }
                foreach (var binding in def.ButtonAction.bindings)
                {
                    if (string.Equals(binding.effectivePath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        return Pretty(def.Name);
                    }
                }
            }
            return null;
        }

        private static bool IsGameplayButton(string name)
        {
            // Generic aliases and chat-only keys, which only matter while chat has focus.
            return !(name.StartsWith("Joy") || name.StartsWith("Mouse") || name.Contains("Chat") || name == "Tab" || name == "LShift" || name == "Escape"
                     || name.StartsWith("Radial") || name.StartsWith("Cam"));
        }

        private static string Pretty(string buttonName)
        {
            switch (buttonName)
            {
                case "GP": return "Forsaken power";
                case "Hide": return "Hide weapons";
                case "Use": return "Use";
                case "AltPlace": return "Alternative placement";
                case "BuildMenu": return "Build menu";
            }
            if (buttonName.StartsWith("Hotbar"))
            {
                return "Hotbar slot " + buttonName.Substring(6);
            }
            return Regex.Replace(buttonName, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
        }
    }
}
