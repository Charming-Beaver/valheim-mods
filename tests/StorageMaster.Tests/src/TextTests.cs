using System.Collections;
using System.Collections.Generic;
using System.Linq;
using StorageMaster.UI;
using TMPro;
using UnityEngine;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    /// <summary>
    /// Every text in the mod's windows can be drawn. Text whose font or material is missing or can't render shows up as
    /// magenta blocks (#1); other mods replacing the game's fonts are the usual cause, so this matters most with -With.
    /// </summary>
    public static class TextTests
    {
        [Test]
        private static IEnumerator StorageWindowTextRenders()
        {
            Container chest = SpawnChest();
            Put(chest.GetInventory(), "Wood", 10, 0, 0);
            StorageWindow.Open();
            yield return WaitUntil(() => StorageWindow._cards.All(c => c.Claimed), "the chest to open");
            // Choosing an item fills the item name and info texts too.
            StorageWindow._selected = Name("Wood");
            StorageWindow.ToggleSettings();
            yield return Frames(3);

            AssertAllRender(StorageWindow._root);
        }

        [Test]
        private static IEnumerator InfoAndOptionsTextRenders()
        {
            InfoWindow.Open();
            yield return Frames(3);
            AssertAllRender(InfoWindow._window.Root);
            OptionsWindow.Open();
            yield return Frames(3);
            AssertAllRender(OptionsWindow._window.Root);
        }

        /// <summary>
        /// The Info and Options windows' rows are laid out one below the other (#8: in one player's setup with 1.0.0
        /// they all stayed on top of each other at the top of the window). Most useful with -With.
        /// </summary>
        [Test]
        private static IEnumerator InfoAndOptionsLayOutTheirRows()
        {
            InfoWindow.Open();
            yield return Frames(3);
            AssertRowsLaidOut(InfoWindow._window.Root);
            OptionsWindow.Open();
            yield return Frames(3);
            AssertRowsLaidOut(OptionsWindow._window.Root);
        }

        /// <summary>
        /// Every text with something to say is actually drawn: it has room, it isn't see-through, and its letters show.
        /// (With SeneaL UI's look the Options window's labels once had none.)
        /// </summary>
        [Test]
        private static IEnumerator InfoAndOptionsTextsAreDrawn()
        {
            InfoWindow.Open();
            yield return Frames(3);
            AssertAllDrawn(InfoWindow._window.Root);
            OptionsWindow.Open();
            yield return Frames(3);
            AssertAllDrawn(OptionsWindow._window.Root);
        }

        public static void AssertAllDrawn(GameObject root)
        {
            var problems = new List<string>();
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(includeInactive: false))
            {
                // An empty input field holds a zero width space.
                if (string.IsNullOrWhiteSpace(text.text.Replace("​", "")) || !text.enabled) continue;
                text.ForceMeshUpdate();
                Rect r = text.rectTransform.rect;
                string problem = r.width < 4f || r.height < 4f ? $"no room ({r.width:0}x{r.height:0})"
                    : text.color.a < 0.3f ? $"see-through (color {text.color})"
                    : text.textInfo.characterCount == 0 || text.textBounds.size.x <= 0f ? "no letters drawn" : null;
                if (problem != null)
                {
                    problems.Add($"\"{Shorten(text.text)}\" ({Path(text.transform, root.transform)}): {problem}, font {text.font?.name}, size {text.fontSize}, "
                        + $"rect {r.width:0}x{r.height:0}, letters {text.textInfo.characterCount}, overflow {text.overflowMode}/{text.isTextOverflowing}, "
                        + $"ellipsis in font {text.font != null && text.font.HasCharacter('…')}");
                }
            }
            Assert.True(problems.Count == 0, $"{problems.Count} texts in {root.name} aren't drawn:\n" + string.Join("\n", problems.Take(10)));
        }

        private static void AssertRowsLaidOut(GameObject root)
        {
            UnityEngine.UI.ScrollRect scroll = root.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
            RectTransform content = scroll.content;
            Assert.True(content.childCount > 10, $"Rows in {root.name}: {content.childCount}");
            Assert.True(content.rect.width > scroll.viewport.rect.width * 0.9f, $"{root.name}: rows are as wide as the window (rows {content.rect.width:0}, window {scroll.viewport.rect.width:0})");
            Assert.True(content.rect.height > scroll.viewport.rect.height, $"{root.name}: the rows are taller than the window (rows {content.rect.height:0}, window {scroll.viewport.rect.height:0})");
            float last = float.MaxValue;
            for (int i = 0; i < content.childCount; i++)
            {
                var row = (RectTransform)content.GetChild(i);
                if (!row.gameObject.activeSelf) continue;
                Assert.True(row.anchoredPosition.y < last, $"{root.name}: row {i} ({row.name}) is below the one before (y {row.anchoredPosition.y:0}, before {last:0})");
                last = row.anchoredPosition.y;
            }
        }

        [Test]
        private static IEnumerator InventoryButtonTextRenders()
        {
            InventoryGui.instance.Show(SpawnChest());
            yield return WaitUntil(InventoryGui.IsVisible, "the chest to open");
            yield return Frames(3);

            foreach (var button in new[]
                     {
                         StorageMaster.InventoryButtons._sort, StorageMaster.InventoryButtons._mode, StorageMaster.InventoryButtons._quickStack,
                         StorageMaster.InventoryButtons._autoSort, StorageMaster.InventoryButtons._storage, StorageMaster.InventoryButtons._info,
                         StorageMaster.InventoryButtons._options, StorageMaster.InventoryButtons._sortChest, StorageMaster.InventoryButtons._chestMode,
                     })
            {
                Assert.True(button != null, "Button created");
                AssertAllRender(button.gameObject);
            }
        }

        /// <summary>
        /// Another mod loading its own "Norse" font, with a material that can't render, doesn't break our headers (#1).
        /// </summary>
        [Test]
        private static IEnumerator TextRendersWhenAnotherModLoadsABrokenNorseFont()
        {
            var fake = ScriptableObject.CreateInstance<TMP_FontAsset>();
            fake.name = "OtherMod-Norsebold";
            fake.material = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "OtherMod-Norsebold Material" };
            try
            {
                yield return RebuildWindows();
                Container chest = SpawnChest();
                StorageWindow.Open();
                yield return WaitUntil(() => StorageWindow._cards.All(c => c.Claimed), "the chest to open");
                yield return Frames(3);
                AssertAllRender(StorageWindow._root);
                InfoWindow.Open();
                yield return Frames(3);
                AssertAllRender(InfoWindow._window.Root);
                OptionsWindow.Open();
                yield return Frames(3);
                AssertAllRender(OptionsWindow._window.Root);
            }
            finally
            {
                Object.Destroy(fake.material);
                Object.Destroy(fake);
            }
            // The next tests get windows built with the real fonts again.
            yield return RebuildWindows();
        }

        /// <summary>Building the mod's windows and buttons logs no TextMeshPro warnings about missing fonts (#20).</summary>
        [Test]
        private static IEnumerator BuildingTheUILogsNoFontWarnings()
        {
            var warnings = new List<string>();
            void Listen(string message, string stack, LogType type)
            {
                if (message.Contains("Font Asset")) warnings.Add(message);
            }
            Application.logMessageReceived += Listen;
            try
            {
                yield return RebuildWindows();
                Container chest = SpawnChest();
                InventoryGui.instance.Show(chest);
                yield return WaitUntil(() => StorageMaster.InventoryButtons._sortChest != null, "the inventory buttons");
                InventoryGui.instance.Hide();
                StorageWindow.Open();
                yield return WaitUntil(() => StorageWindow._cards.All(c => c.Claimed), "the chest to open");
                StorageWindow.ToggleSettings();
                InfoWindow.Open();
                yield return Frames(3);
                OptionsWindow.Open();
                yield return Frames(3);
            }
            finally
            {
                Application.logMessageReceived -= Listen;
            }
            Assert.True(warnings.Count == 0, $"{warnings.Count} font warnings, e.g. \"{warnings.FirstOrDefault()}\"");
        }

        /// <summary>Throws away the windows, buttons and the fonts and templates they're built from, so they're built again.</summary>
        private static IEnumerator RebuildWindows()
        {
            foreach (Badge badge in new[] { StorageMaster.InventoryButtons._badge, StorageMaster.InventoryButtons._chestBadge })
            {
                if (badge == null) continue;
                Object.Destroy(badge.Menu.gameObject);
                Object.Destroy(badge.gameObject);
            }
            StorageMaster.InventoryButtons._badge = StorageMaster.InventoryButtons._chestBadge = null;
            StorageMaster.InventoryButtons._sort = StorageMaster.InventoryButtons._mode = StorageMaster.InventoryButtons._quickStack =
                StorageMaster.InventoryButtons._autoSort = StorageMaster.InventoryButtons._storage = StorageMaster.InventoryButtons._info =
                    StorageMaster.InventoryButtons._options = StorageMaster.InventoryButtons._sortChest = StorageMaster.InventoryButtons._chestMode = null;
            StorageWindow.Close();
            if (StorageWindow._root != null) Object.Destroy(StorageWindow._root);
            StorageWindow._root = null;
            InfoWindow._window.Destroy();
            OptionsWindow._window.Destroy();
            UIKit.Ready = false;
            yield return null;
        }

        private static void AssertAllRender(GameObject root)
        {
            var problems = new List<string>();
            int count = 0;
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(includeInactive: false))
            {
                count++;
                string problem = Problem(text);
                if (problem != null) problems.Add($"\"{Shorten(text.text)}\" ({Path(text.transform, root.transform)}): {problem}");
            }
            Assert.True(count > 0, $"Texts found in {root.name}");
            Assert.True(problems.Count == 0, $"{problems.Count} of {count} texts can't render:\n" + string.Join("\n", problems.Take(10)));
        }

        private static string Problem(TMP_Text text)
        {
            if (text.font == null) return "no font";
            if (text.font.atlasTextures == null || text.font.atlasTextures.Length == 0 || text.font.atlasTexture == null)
            {
                return $"font {text.font.name} has no atlas";
            }
            Material material = text.fontSharedMaterial;
            if (material == null) return "no material";
            Shader shader = material.shader;
            if (shader == null || !shader.isSupported || shader.name.Contains("InternalErrorShader"))
            {
                return $"shader {(shader == null ? "(none)" : shader.name)} of material {material.name} can't render";
            }
            // A material made for another font draws pieces of that font's atlas instead of these letters.
            if (material.HasProperty(ShaderUtilities.ID_MainTex) && material.GetTexture(ShaderUtilities.ID_MainTex) != text.font.atlasTexture)
            {
                return $"material {material.name} belongs to another font than {text.font.name}";
            }
            return null;
        }

        private static string Shorten(string s)
        {
            s = (s ?? "").Replace("\n", " ");
            return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
        }

        private static string Path(Transform t, Transform root) => t == root || t.parent == null ? t.name : Path(t.parent, root) + "/" + t.name;
    }
}
