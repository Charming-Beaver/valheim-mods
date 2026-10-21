using System;
using System.Reflection;
using BepInEx.Bootstrap;
using TMPro;
using UnityEngine;

namespace StorageMaster.UI
{
    /// <summary>
    /// SeneaL UI's look: the sprites it draws its boxes with, its fonts and its colors. While SeneaL UI draws the
    /// inventory its way, Storage Master's menus and windows take this look (see Themed). Read through reflection from
    /// SeneaL UI's theme classes, so it isn't needed when SeneaL UI isn't installed.
    /// </summary>
    public static class SeneaLTheme
    {
        public static Sprite Panel, Button, ButtonHover, Tab, TabHover, TabOn, Inset, Field, Check, CheckMark, Slot;
        public static TMP_FontAsset TextFont, HeadingFont;
        public static Material TextMaterial, HeadingMaterial;
        public static Color Parchment, Brass, Gold, Ash, Blood, Moss;

        private static bool _looked, _loaded;
        private static int _onFrame = -1;
        private static bool _on;

        /// <summary>True while SeneaL UI draws its own inventory panel (asked once per frame).</summary>
        public static bool On
        {
            get
            {
                if (_onFrame == Time.frameCount) return _on;
                _onFrame = Time.frameCount;
                // Only once SeneaL UI shows its panel: its fonts and sprites are ready by then.
                _on = SeneaLCompat.InventoryPanel != null && Load();
                return _on;
            }
        }

        private static bool Load()
        {
            if (_looked) return _loaded;
            _looked = true;
            try
            {
                Assembly assembly = Chainloader.PluginInfos[SeneaLCompat.Guid].Instance.GetType().Assembly;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                Type styles = assembly.GetType("SeneaLUI.Theme.Styles", true), skin = assembly.GetType("SeneaLUI.Theme.Skin", true);
                Type boxStyle = assembly.GetType("SeneaLUI.Theme.BoxStyle", true), textStyles = assembly.GetType("SeneaLUI.Theme.TextStyles", true);
                Type palette = assembly.GetType("SeneaLUI.Core.Palette", true), icons = assembly.GetType("SeneaLUI.Theme.Icons", true);
                MethodInfo sliced = skin.GetMethod("Sliced", flags, null, new[] { typeof(int), boxStyle, typeof(int) }, null);
                MethodInfo icon = icons.GetMethod("Get", flags, null, new[] { typeof(string) }, null);
                // Without SeneaL UI's padding for shadows and glows, so a sprite fills its box like ours do.
                Sprite Box(int height, string style) =>
                    (Sprite)sliced.Invoke(null, new[] { height, styles.GetField(style, flags).GetValue(null), (object)0 });
                Color Colour(string name) => (Color)palette.GetField(name, flags).GetValue(null);
                object Text(string name) => textStyles.GetField(name, flags).GetValue(null);

                Panel = Box(64, "Panel");
                Button = Box(36, "Tb");
                ButtonHover = Box(36, "TbHover");
                Tab = Box(36, "TabBox");
                TabHover = Box(36, "TabBoxHover");
                TabOn = Box(36, "TabBoxOn");
                Inset = Box(32, "Card");
                Field = Box(36, "Search");
                Check = Box(22, "Tool");
                Slot = Box(64, "SlotEmpty");
                CheckMark = (Sprite)icon.Invoke(null, new object[] { "check" });

                object text = Text("PanelDesc"), heading = Text("Heading");
                Type textStyle = text.GetType();
                TextFont = (TMP_FontAsset)textStyle.GetField("Font").GetValue(text);
                TextMaterial = (Material)textStyle.GetField("Material").GetValue(text);
                HeadingFont = (TMP_FontAsset)textStyle.GetField("Font").GetValue(heading);
                HeadingMaterial = (Material)textStyle.GetField("Material").GetValue(heading);

                Parchment = Colour("Parchment");
                Brass = Colour("Brass");
                Gold = Colour("Gold");
                Ash = Colour("Ash");
                Blood = Colour("Blood");
                Moss = Colour("Moss");
                _loaded = Panel != null && Button != null && TextFont != null && HeadingFont != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Storage Master] SeneaL UI's look wasn't found ({e.GetType().Name}: {e.Message}). Storage Master's windows keep the Valheim look.");
                _loaded = false;
            }
            return _loaded;
        }
    }
}
