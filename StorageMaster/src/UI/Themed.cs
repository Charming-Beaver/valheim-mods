using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StorageMaster.UI
{
    /// <summary>
    /// Marks a piece UIKit built (a panel, a button, a text and so on) and switches it between the Valheim look it was
    /// built with and SeneaL UI's look (SeneaLTheme), so the menus and windows match whichever the inventory has.
    /// </summary>
    public sealed class Themed : MonoBehaviour
    {
        public enum Role { Panel, Inset, Button, Tab, Check, CheckMark, Text, Heading, Field, ScrollTrack, ScrollHandle, Slot, Tint }

        public Role Kind;

        private bool? _seneaL;
        private Selectable _selectable;
        // The Valheim look, as built.
        private Sprite _sprite;
        private Image.Type _type;
        private Color _color;
        private Material _material;
        private float _pixelsPerUnit;
        private Selectable.Transition _transition;
        private SpriteState _sprites;
        private ColorBlock _colors;
        private TMP_FontAsset _font;
        private Material _fontMaterial;
        private Vector4 _margin;
        private GameObject _fill;

        /// <summary>
        /// Marks a piece (its Image, or its text for Text and Heading) and gives it the current look. A button's or tab's
        /// <paramref name="selectable"/> (if not on the same object) gets SeneaL UI's hover and pressed boxes.
        /// </summary>
        public static T Mark<T>(T piece, Role role, Selectable selectable = null) where T : Component
        {
            if (piece == null) return piece;
            Themed themed = piece.gameObject.AddComponent<Themed>();
            themed.Kind = role;
            themed._selectable = selectable != null ? selectable : piece.GetComponent<Selectable>();
            if (piece.TryGetComponent(out Image image))
            {
                themed._sprite = image.sprite;
                themed._type = image.type;
                themed._color = image.color;
                themed._material = image.material == image.defaultMaterial ? null : image.material;
                themed._pixelsPerUnit = image.pixelsPerUnitMultiplier;
            }
            if (themed._selectable != null)
            {
                themed._transition = themed._selectable.transition;
                themed._sprites = themed._selectable.spriteState;
                themed._colors = themed._selectable.colors;
            }
            if (piece.TryGetComponent(out TMP_Text text))
            {
                themed._font = text.font;
                themed._fontMaterial = text.fontSharedMaterial;
                themed._margin = text.margin;
            }
            themed.Apply(SeneaLTheme.On);
            return piece;
        }

        /// <summary>Gives every marked piece under <paramref name="root"/> the current look (only what changed).</summary>
        public static void Sync(Transform root)
        {
            if (root == null) return;
            bool on = SeneaLTheme.On;
            foreach (Themed themed in root.GetComponentsInChildren<Themed>(true)) themed.Apply(on);
        }

        /// <summary>The look a piece has now (null before it got one), for tests.</summary>
        public bool? SeneaL => _seneaL;

        public void Apply(bool seneaL)
        {
            if (_seneaL == seneaL) return;
            _seneaL = seneaL;
            if (Kind == Role.Text || Kind == Role.Heading)
            {
                ApplyText(seneaL);
                return;
            }
            if (!TryGetComponent(out Image image)) return;
            if (Kind == Role.Tint)
            {
                image.color = UIKit.Recolor(image.color, seneaL);
                return;
            }
            if (Kind == Role.Panel) Fill(seneaL);
            if (!seneaL)
            {
                image.sprite = _sprite;
                image.type = _type;
                image.color = _color;
                image.material = _material;
                image.pixelsPerUnitMultiplier = _pixelsPerUnit;
                if (_selectable != null)
                {
                    _selectable.transition = _transition;
                    _selectable.spriteState = _sprites;
                    _selectable.colors = _colors;
                }
                return;
            }

            if (Kind == Role.ScrollTrack || Kind == Role.ScrollHandle)
            {
                // Flat like SeneaL UI's own scroll bars: a faint brass track and a brass thumb.
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.material = null;
                Color brass = SeneaLTheme.Brass;
                image.color = new Color(brass.r, brass.g, brass.b, Kind == Role.ScrollTrack ? 0.12f : 0.55f);
                return;
            }

            Sprite sprite;
            switch (Kind)
            {
                case Role.Panel: sprite = SeneaLTheme.Panel; break;
                case Role.Inset: sprite = SeneaLTheme.Inset; break;
                case Role.Button: sprite = SeneaLTheme.Button; break;
                case Role.Tab: sprite = SeneaLTheme.Tab; break;
                case Role.Check: sprite = SeneaLTheme.Check; break;
                case Role.CheckMark: sprite = SeneaLTheme.CheckMark; break;
                case Role.Field: sprite = SeneaLTheme.Field; break;
                case Role.Slot: sprite = SeneaLTheme.Slot; break;
                default: return;
            }
            if (sprite == null) return;
            image.sprite = sprite;
            image.type = Kind == Role.CheckMark ? Image.Type.Simple : Image.Type.Sliced;
            image.preserveAspect = Kind == Role.CheckMark;
            image.material = null;
            image.pixelsPerUnitMultiplier = 1f;
            // The check mark is an icon, drawn in SeneaL UI's brass; boxes keep their own transparency (an inset's alpha).
            image.color = Kind == Role.CheckMark ? SeneaLTheme.Brass : new Color(1f, 1f, 1f, Kind == Role.Inset ? Mathf.Clamp01(_color.a + 0.2f) : 1f);

            if ((Kind == Role.Button || Kind == Role.Tab) && _selectable != null)
            {
                Selectable selectable = _selectable;
                // Without the color tint from now on, so take off what it last left on the box.
                image.canvasRenderer.SetColor(Color.white);
                // Hovered and pressed boxes are SeneaL UI's lit ones; a tab that is the open one (not interactable) is lit.
                selectable.transition = Selectable.Transition.SpriteSwap;
                Sprite lit = Kind == Role.Tab ? SeneaLTheme.TabOn : SeneaLTheme.ButtonHover;
                selectable.spriteState = new SpriteState
                {
                    highlightedSprite = Kind == Role.Tab ? SeneaLTheme.TabHover : SeneaLTheme.ButtonHover,
                    pressedSprite = lit,
                    selectedSprite = sprite,
                    disabledSprite = Kind == Role.Tab ? lit : sprite,
                };
            }
        }

        private void ApplyText(bool seneaL)
        {
            if (!TryGetComponent(out TMP_Text text)) return;
            bool heading = Kind == Role.Heading;
            if (seneaL)
            {
                text.font = heading ? SeneaLTheme.HeadingFont : SeneaLTheme.TextFont;
                Material material = heading ? SeneaLTheme.HeadingMaterial : SeneaLTheme.TextMaterial;
                if (material != null) text.fontSharedMaterial = material;
            }
            else
            {
                text.font = _font;
                if (_fontMaterial != null) text.fontSharedMaterial = _fontMaterial;
            }
            text.color = UIKit.Recolor(text.color, seneaL);
            // SeneaL UI's fonts have taller lines than the game's. A one-line text that no longer fits its height would
            // draw nothing at all (it's cut off with an ellipsis), so it may reach a little above and below its box.
            float room = seneaL && text.textWrappingMode == TextWrappingModes.NoWrap ? Mathf.Ceil(text.fontSize * 0.35f) : 0f;
            text.margin = new Vector4(_margin.x, _margin.y - room, _margin.z, _margin.w - room);
            // Our rich text colors (highlighted keys and buttons, on/off, warnings) as SeneaL UI's closest, also in
            // texts set later on.
            text.textPreprocessor = seneaL ? Accents.Instance : null;
            text.SetAllDirty();
        }

        /// <summary>
        /// SeneaL UI's panels let a little of what's behind them through, which works next to its HUD but not for our
        /// windows over it: a second layer of the same box makes ours (nearly) solid.
        /// </summary>
        private void Fill(bool seneaL)
        {
            if (_fill == null)
            {
                if (!seneaL) return;
                RectTransform rt = UIKit.Rect(transform, "seneal_fill").Fill();
                rt.SetAsFirstSibling();
                Image fill = rt.gameObject.AddComponent<Image>();
                fill.sprite = SeneaLTheme.Panel;
                fill.type = Image.Type.Sliced;
                fill.raycastTarget = false;
                // Not one of the panel's rows, if it lays out its children.
                rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                _fill = rt.gameObject;
            }
            _fill.SetActive(seneaL);
        }

        /// <summary>Swaps our rich text colors for SeneaL UI's before a text is drawn.</summary>
        private sealed class Accents : ITextPreprocessor
        {
            public static readonly Accents Instance = new Accents();

            private static string _gold, _green, _grey, _red;

            public string PreprocessText(string text)
            {
                if (string.IsNullOrEmpty(text) || text.IndexOf("<color=#", System.StringComparison.Ordinal) < 0) return text;
                if (_gold == null)
                {
                    _gold = Hex(SeneaLTheme.Gold);
                    _green = Hex(SeneaLTheme.Moss);
                    _grey = Hex(SeneaLTheme.Ash);
                    _red = Hex(SeneaLTheme.Blood);
                }
                return text.Replace("<color=#ffa13c>", _gold).Replace("<color=#8fd16a>", _green)
                    .Replace("<color=#a8a49e>", _grey).Replace("<color=#a9a49c>", _grey).Replace("<color=#ff7a6a>", _red);
            }

            private static string Hex(Color c) => "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">";
        }
    }
}
