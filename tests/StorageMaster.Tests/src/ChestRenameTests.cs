using System.Collections;
using System.Linq;
using StorageMaster.UI;
using TMPro;
using UnityEngine;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    /// <summary>A chest can be named in the chest window, over its title: the game's, or SeneaL UI's own panel (#72).</summary>
    public static class ChestRenameTests
    {
        [Test, SkipWithMod(SeneaLCompat.Guid, "draws its own chest panel; see ChestCanBeNamedInSeneaLsChestPanel")]
        private static IEnumerator ChestCanBeNamedInTheChestWindow()
        {
            InventoryGui gui = InventoryGui.instance;
            yield return NameAChest(() => gui.m_container, () => gui.m_containerName);
        }

        [Test, RequiresMod(SeneaLCompat.Guid)]
        private static IEnumerator ChestCanBeNamedInSeneaLsChestPanel()
        {
            yield return NameAChest(() => SeneaLCompat.ChestShown ? SeneaLCompat.ChestPanel : null,
                () => SeneaLCompat.ChestPanel?.Find("title")?.GetComponent<TMP_Text>());
        }

        private static IEnumerator NameAChest(System.Func<RectTransform> panelNow, System.Func<TMP_Text> titleNow)
        {
            Container chest = SpawnChest();
            InventoryGui.instance.Show(chest);
            yield return WaitUntil(() => InventoryGui.IsVisible() && panelNow() != null && ChestRename._field != null
                && ChestRename._field.gameObject.activeInHierarchy, "the chest panel and its name field");
            yield return Frames(3);
            RectTransform panel = panelNow();
            TMP_Text title = titleNow();
            TMP_InputField field = ChestRename._field;

            Assert.True(field.interactable, "The name field can be used (you own the chest)");
            CanvasGroup blocking = field.GetComponentsInParent<CanvasGroup>().FirstOrDefault(g => !g.interactable || !g.blocksRaycasts || g.alpha < 0.5f);
            Assert.True(blocking == null, $"The name field isn't in a hidden or unclickable group ({blocking?.name})");
            Rect f = BadgeTests.InRoot((RectTransform)field.transform), p = BadgeTests.InRoot(panel);
            Assert.True(p.Contains(f.min) && p.Contains(f.max), $"The name field is on the chest panel ({f} in {p})");
            Assert.True(title != null && !title.enabled, "The title is hidden, the name field shows instead");
            Assert.True(f.Overlaps(BadgeTests.InRoot(title.rectTransform)), $"The name field is where the title was ({f}, title {BadgeTests.InRoot(title.rectTransform)})");
            // Nothing of the panel's own that can be clicked is covered (SeneaL UI's header buttons).
            foreach (UIInputHandler other in panel.GetComponentsInChildren<UIInputHandler>().Where(h => !h.transform.IsChildOf(field.transform)))
            {
                Rect o = BadgeTests.InRoot((RectTransform)other.transform);
                Assert.True(!BadgeTests.Shrink(f, 2f).Overlaps(BadgeTests.Shrink(o, 2f)), $"The name field covers {other.name} ({f}, {o})");
            }

            // The chest's type shows in the empty field, and then the name typed in.
            Assert.True(field.placeholder is TMP_Text ph && ph.text.Length > 0, "The field shows the chest's type while it has no name");
            AssertDrawn((TMP_Text)field.placeholder);
            field.text = "Ore chest";
            field.onEndEdit.Invoke(field.text);
            Assert.Equal("Ore chest", ChestAccess.GetLabel(chest), "The chest's name");
            yield return Frames(2);
            AssertDrawn(field.textComponent);
        }

        /// <summary>The text has its letters drawn (a line too tall for its box isn't drawn at all).</summary>
        private static void AssertDrawn(TMP_Text text)
        {
            text.ForceMeshUpdate();
            Rect r = text.rectTransform.rect;
            Assert.True(text.textInfo.characterCount > 0 && text.textBounds.size.x > 0f && text.color.a > 0.3f,
                $"\"{text.text}\" is drawn (box {r.width:0}x{r.height:0}, font size {text.fontSize}, letters {text.textInfo.characterCount}, overflowing {text.isTextOverflowing})");
        }
    }
}
