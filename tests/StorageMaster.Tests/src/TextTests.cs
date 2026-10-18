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
        private static IEnumerator InfoWindowTextRenders()
        {
            InfoWindow.Open();
            yield return Frames(3);

            AssertAllRender(InfoWindow._root);
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
                         StorageMaster.InventoryButtons._sortChest,
                     })
            {
                Assert.True(button != null, "Button created");
                AssertAllRender(button.gameObject);
            }
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
            if (text.font.atlasTexture == null) return $"font {text.font.name} has no atlas";
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
