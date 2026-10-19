using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StorageMaster.UI
{
    /// <summary>
    /// Builds uGUI widgets out of the game's own assets (fonts, sprites, and clones of the inventory's
    /// buttons, tabs and scrollbars) so the village windows look and sound like part of Valheim.
    /// </summary>
    public static class UIKit
    {
        public static TMP_FontAsset Font;
        public static TMP_FontAsset Norse;
        public static Material FontMaterial;
        public static Sprite PanelSprite, ButtonSprite, FieldSprite, SlotSprite, CheckSprite, CheckMarkSprite, InteriorSprite;

        public static readonly Color Orange = new Color(1f, 0.631f, 0.235f);
        public static readonly Color Parchment = new Color(0.93f, 0.89f, 0.80f);
        public static readonly Color Grey = new Color(0.68f, 0.66f, 0.62f);
        public static readonly Color Dim = new Color(0f, 0f, 0f, 0.45f);
        public static readonly Color Red = new Color(1f, 0.38f, 0.32f);
        public static readonly Color Green = new Color(0.55f, 0.85f, 0.4f);

        private static GameObject _buttonTemplate, _tabTemplate, _scrollbarTemplate;
        public static Transform CanvasRoot;
        public static bool Ready;

        public static bool Init()
        {
            if (Ready && CanvasRoot != null) return true;
            InventoryGui gui = InventoryGui.instance;
            if (gui == null) return false;

            Font = gui.m_containerName.font;
            FontMaterial = gui.m_containerName.fontSharedMaterial;
            // Headers use the same font (Norse bold) with its plain material instead of the outlined one. Taken from the
            // game's own text, not searched by name, so a "Norse" font loaded by another mod can't end up in our windows.
            Norse = Font;

            Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && !sprites.ContainsKey(s.name)) sprites[s.name] = s;
            Sprite Get(params string[] names)
            {
                foreach (string n in names)
                    if (sprites.TryGetValue(n, out Sprite s)) return s;
                return null;
            }

            Image craftingBg = gui.m_crafting != null ? gui.m_crafting.GetComponent<Image>() : null;
            PanelSprite = Get("woodpanel_trophys", "woodpanel_settings", "woodpanel_512x512") ?? craftingBg?.sprite;
            ButtonSprite = Get("button");
            FieldSprite = Get("text_field");
            InteriorSprite = Get("panel_interior_bkg_128", "darken_blob");
            SlotSprite = Get("item_background_sharp", "item_background");
            CheckSprite = Get("checkbox");
            CheckMarkSprite = Get("checkbox_marker");

            _buttonTemplate = gui.m_takeAllButton != null ? gui.m_takeAllButton.gameObject : gui.m_craftButton?.gameObject;
            _tabTemplate = gui.m_tabCraft != null ? gui.m_tabCraft.gameObject : _buttonTemplate;
            _scrollbarTemplate = gui.m_recipeListScroll != null ? gui.m_recipeListScroll.gameObject : null;

            Canvas canvas = gui.GetComponentInParent<Canvas>();
            CanvasRoot = canvas != null ? canvas.rootCanvas.transform : gui.transform.parent;
            Ready = CanvasRoot != null;
            return Ready;
        }

        // ------------------------------------------------------------------ layout primitives

        public static RectTransform Rect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Anchor(this RectTransform rt, float minX, float minY, float maxX, float maxY,
            float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Fill(this RectTransform rt, float pad = 0) => rt.Anchor(0, 0, 1, 1, pad, pad, pad, pad);

        /// <summary>Anchors to the top-left corner with a fixed position and size.</summary>
        public static RectTransform TopLeft(this RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static LayoutElement Size(this Component c, float w = -1, float h = -1, float flexW = -1, float flexH = -1)
        {
            LayoutElement le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) { le.preferredWidth = w; le.minWidth = w; }
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static HorizontalLayoutGroup HRow(Transform parent, string name, float spacing = 6, int padding = 0)
        {
            RectTransform rt = Rect(parent, name);
            HorizontalLayoutGroup g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childAlignment = TextAnchor.MiddleLeft;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return g;
        }

        public static VerticalLayoutGroup VCol(Transform parent, string name, float spacing = 4, int padding = 0)
        {
            RectTransform rt = Rect(parent, name);
            VerticalLayoutGroup g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childAlignment = TextAnchor.UpperLeft;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            return g;
        }

        public static RectTransform Spacer(Transform parent, float w = -1, float h = -1, float flex = 1)
        {
            RectTransform rt = Rect(parent, "spacer");
            rt.Size(w, h, w < 0 ? flex : -1, h < 0 ? flex : -1);
            return rt;
        }

        // ------------------------------------------------------------------ visuals

        public static Image Panel(Transform parent, string name = "panel")
        {
            RectTransform rt = Rect(parent, name);
            Image img = rt.gameObject.AddComponent<Image>();
            if (PanelSprite != null)
            {
                img.sprite = PanelSprite;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
            }
            else img.color = new Color(0.22f, 0.16f, 0.1f, 0.97f);
            return img;
        }

        /// <summary>
        /// A panel with the inventory's own dark background (sprite, material and color copied from it), or the
        /// lighter wooden panel if that isn't there.
        /// </summary>
        public static Image DarkPanel(Transform parent, string name = "panel")
        {
            Image img = Panel(parent, name);
            InventoryGui gui = InventoryGui.instance;
            Transform bkg = gui != null && gui.m_player != null ? gui.m_player.Find("Bkg") : null;
            if (bkg != null && bkg.TryGetComponent(out Image inventory) && inventory.sprite != null)
            {
                img.sprite = inventory.sprite;
                img.type = inventory.type;
                img.material = inventory.material;
                img.color = inventory.color;
                img.pixelsPerUnitMultiplier = inventory.pixelsPerUnitMultiplier;
            }
            return img;
        }

        /// <summary>The darker recessed background used inside wooden panels.</summary>
        public static Image Inset(Transform parent, string name = "inset", float alpha = 0.55f)
        {
            RectTransform rt = Rect(parent, name);
            Image img = rt.gameObject.AddComponent<Image>();
            if (InteriorSprite != null)
            {
                img.sprite = InteriorSprite;
                img.type = Image.Type.Sliced;
                img.color = new Color(1, 1, 1, Mathf.Clamp01(alpha + 0.3f));
            }
            else img.color = new Color(0, 0, 0, alpha);
            return img;
        }

        public static Image Solid(Transform parent, Color color, string name = "solid")
        {
            RectTransform rt = Rect(parent, name);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image Icon(Transform parent, Sprite sprite, float size, bool slot = false)
        {
            RectTransform holder = Rect(parent, "icon");
            holder.Size(size, size);
            if (slot && SlotSprite != null)
            {
                Image bg = holder.gameObject.AddComponent<Image>();
                bg.sprite = SlotSprite;
                bg.type = Image.Type.Sliced;
                bg.color = new Color(1, 1, 1, 0.8f);
            }
            RectTransform irt = Rect(holder, "img").Fill(slot ? 3 : 0);
            Image img = irt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = sprite != null;
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size = 18, Color? color = null,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool norse = false, string name = "text")
        {
            RectTransform rt = Rect(parent, name);
            // Added while inactive, so the text has its font before it initializes. An active one starts up right away,
            // finds no font, and makes TextMeshPro look for its default font (not in the game) and log a warning.
            rt.gameObject.SetActive(false);
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = norse ? Norse : Font;
            if (!norse && FontMaterial != null) t.fontSharedMaterial = FontMaterial;
            rt.gameObject.SetActive(true);
            t.fontSize = size;
            t.color = color ?? Parchment;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = true;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        public static TextMeshProUGUI Wrap(this TextMeshProUGUI t)
        {
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Truncate;
            return t;
        }

        // ------------------------------------------------------------------ controls

        public static Button Button(Transform parent, string label, UnityAction onClick, float width = 140, float height = 34, bool tab = false)
        {
            GameObject template = tab ? _tabTemplate : _buttonTemplate;
            GameObject go;
            Button b;
            if (template != null)
            {
                go = UnityEngine.Object.Instantiate(template, parent, false);
                go.SetActive(true);
                foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true))
                {
                    if (pad.m_hint != null) UnityEngine.Object.Destroy(pad.m_hint);
                    UnityEngine.Object.Destroy(pad);
                }
                foreach (UITooltip tip in go.GetComponentsInChildren<UITooltip>(true)) UnityEngine.Object.Destroy(tip);
                b = go.GetComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent();
                b.interactable = true;
                TMP_Text t = go.GetComponentInChildren<TMP_Text>(true);
                if (t != null)
                {
                    t.text = label;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    t.enableAutoSizing = true;
                    t.fontSizeMin = 10;
                    t.fontSizeMax = Mathf.Max(14, t.fontSize);
                }
                else Text(go.transform, label, 16, Parchment, TextAlignmentOptions.Center).rectTransform.Fill();
            }
            else
            {
                RectTransform rt = Rect(parent, "button");
                go = rt.gameObject;
                Image img = go.AddComponent<Image>();
                img.sprite = ButtonSprite;
                img.type = Image.Type.Sliced;
                b = go.AddComponent<Button>();
                b.targetGraphic = img;
                Text(go.transform, label, 16, Parchment, TextAlignmentOptions.Center).rectTransform.Fill(4);
            }
            go.name = "btn_" + label;
            RectTransform r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(width, height);
            r.Size(width, height);
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        /// <summary>The game's button sounds on our own button (it must already have its Button).</summary>
        public static void ButtonSound(GameObject go)
        {
            ButtonSfx template = _buttonTemplate != null ? _buttonTemplate.GetComponent<ButtonSfx>() : null;
            if (template == null) return;
            ButtonSfx sfx = go.AddComponent<ButtonSfx>();
            sfx.m_sfxPrefab = template.m_sfxPrefab;
            sfx.m_selectSfxPrefab = template.m_selectSfxPrefab;
            sfx.m_enterSfxPrefab = template.m_enterSfxPrefab;
        }

        public static void SetLabel(this Button b, string label)
        {
            TMP_Text t = b.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = label;
        }

        /// <summary>A Valheim style checkbox with a label.</summary>
        public static Toggle Check(Transform parent, string label, bool value, UnityAction<bool> onChange, float width = 180)
        {
            HorizontalLayoutGroup row = HRow(parent, "check_" + label, 6);
            row.Size(width, 26);
            RectTransform boxRt = Rect(row.transform, "box");
            boxRt.Size(22, 22);
            Image box = boxRt.gameObject.AddComponent<Image>();
            box.sprite = CheckSprite;
            if (CheckSprite == null) box.color = new Color(0, 0, 0, 0.6f);
            RectTransform markRt = Rect(boxRt, "mark").Fill(CheckSprite == null ? 4 : 0);
            Image mark = markRt.gameObject.AddComponent<Image>();
            mark.sprite = CheckMarkSprite;
            mark.color = CheckMarkSprite == null ? Orange : Color.white;
            Toggle tog = row.gameObject.AddComponent<Toggle>();
            tog.targetGraphic = box;
            tog.graphic = mark;
            tog.isOn = value;
            if (onChange != null) tog.onValueChanged.AddListener(onChange);
            if (!string.IsNullOrEmpty(label)) Text(row.transform, label, 16).Size(-1, 22, 1);
            return tog;
        }

        /// <summary>A vertical scroll list; add rows to the returned content transform.</summary>
        public static RectTransform ScrollList(Transform parent, string name, out ScrollRect scroll, float spacing = 4, int padding = 4)
        {
            RectTransform root = Rect(parent, name);
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            // Valheim reports a mouse wheel notch as 0.05, so this is 150 px per notch, like the game's own lists.
            scroll.scrollSensitivity = 3000f;

            RectTransform viewport = Rect(root, "viewport").Anchor(0, 0, 1, 1, 0, 0, 16, 0);
            Image vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Rect(viewport, "content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            VerticalLayoutGroup v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;

            if (_scrollbarTemplate != null)
            {
                GameObject sbGo = UnityEngine.Object.Instantiate(_scrollbarTemplate, root, false);
                sbGo.SetActive(true);
                foreach (UIGamePad pad in sbGo.GetComponentsInChildren<UIGamePad>(true)) UnityEngine.Object.Destroy(pad);
                RectTransform sb = (RectTransform)sbGo.transform;
                sb.anchorMin = new Vector2(1, 0);
                sb.anchorMax = new Vector2(1, 1);
                sb.pivot = new Vector2(1, 0.5f);
                sb.offsetMin = new Vector2(-12, 0);
                sb.offsetMax = new Vector2(0, 0);
                Scrollbar bar = sbGo.GetComponent<Scrollbar>();
                bar.onValueChanged = new Scrollbar.ScrollEvent();
                bar.direction = Scrollbar.Direction.BottomToTop;
                scroll.verticalScrollbar = bar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
                scroll.verticalScrollbarSpacing = 2;
            }
            return content;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject c = t.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }
        }

        /// <summary>A label + value pair on one line ("Status: chopping").</summary>
        public static TextMeshProUGUI Field(Transform parent, string label, string value, float labelWidth = 110, float height = 24)
        {
            HorizontalLayoutGroup row = HRow(parent, "field_" + label, 4);
            row.Size(-1, height);
            Text(row.transform, label, 16, Orange).Size(labelWidth, height);
            TextMeshProUGUI v = Text(row.transform, value, 16);
            v.Size(-1, height, 1);
            return v;
        }

        public static void Tooltip(GameObject go, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            UITooltip tip = go.GetComponent<UITooltip>() ?? go.AddComponent<UITooltip>();
            tip.m_text = text;
            tip.m_tooltipPrefab = TooltipPrefab();
        }

        /// <summary>
        /// The game's tooltip, taken from an inventory slot. Other mods (SeneaL UI) can leave tooltips elsewhere in the
        /// inventory without one, which would make hovering ours fail.
        /// </summary>
        private static GameObject TooltipPrefab()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null) return null;
            UITooltip slot = gui.m_playerGrid != null && gui.m_playerGrid.m_elementPrefab != null ? gui.m_playerGrid.m_elementPrefab.GetComponent<UITooltip>() : null;
            if (slot != null && slot.m_tooltipPrefab != null) return slot.m_tooltipPrefab;
            foreach (UITooltip t in gui.GetComponentsInChildren<UITooltip>(true))
            {
                if (t.m_tooltipPrefab != null) return t.m_tooltipPrefab;
            }
            return null;
        }
            // ------------------------------------------------------------------ extras for PlayerCheats

        /// <summary>
        /// A clone of the build menu's search box, or a plain field built from game sprites if that isn't available.
        /// The clone's key hint (F, which focuses the build menu's search) is kept only with <paramref name="findKeyHint"/>,
        /// for a field that F focuses too.
        /// </summary>
        public static TMP_InputField InputField(Transform parent, string placeholder, float width, float height, bool plain = false, bool findKeyHint = false)
        {
            TMP_InputField field = null;
            GameObject template = plain ? null : Hud.instance != null && Hud.instance.m_buildUi != null && Hud.instance.m_buildUi.m_searchField != null
                ? Hud.instance.m_buildUi.m_searchField.gameObject : null;
            if (template != null)
            {
                GameObject go = UnityEngine.Object.Instantiate(template, parent, false);
                go.name = "input";
                go.SetActive(true);
                foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true))
                {
                    if (pad.m_hint != null) UnityEngine.Object.Destroy(pad.m_hint);
                    UnityEngine.Object.Destroy(pad);
                }
                if (!findKeyHint)
                {
                    foreach (UIInputHint hint in go.GetComponentsInChildren<UIInputHint>(true)) UnityEngine.Object.DestroyImmediate(hint.gameObject);
                }
                field = go.GetComponent<TMP_InputField>();
                field.onValueChanged = new TMP_InputField.OnChangeEvent();
                field.onSelect = new TMP_InputField.SelectionEvent();
                field.onDeselect = new TMP_InputField.SelectionEvent();
                field.onEndEdit = new TMP_InputField.SubmitEvent();
                field.onSubmit = new TMP_InputField.SubmitEvent();
                field.text = "";
            }
            else
            {
                RectTransform rt = Rect(parent, "input");
                Image bg = rt.gameObject.AddComponent<Image>();
                bg.sprite = FieldSprite;
                bg.type = Image.Type.Sliced;
                if (FieldSprite == null) bg.color = new Color(0, 0, 0, 0.6f);
                RectTransform area = Rect(rt, "area").Fill(6);
                area.gameObject.AddComponent<RectMask2D>();
                TextMeshProUGUI ph = Text(area, "", 18, Grey, name: "Placeholder");
                ph.rectTransform.Fill();
                TextMeshProUGUI tx = Text(area, "", 18, name: "Text");
                tx.rectTransform.Fill();
                // The field creates its text cursor when it's enabled, so it must be fully set up before that.
                bool wasActive = rt.gameObject.activeSelf;
                rt.gameObject.SetActive(false);
                field = rt.gameObject.AddComponent<TMP_InputField>();
                field.textViewport = area;
                field.textComponent = tx;
                field.placeholder = ph;
                field.fontAsset = Font;
                field.customCaretColor = true;
                field.caretColor = Parchment;
                field.caretWidth = 2;
                field.selectionColor = new Color(1f, 0.631f, 0.235f, 0.35f);
                rt.gameObject.SetActive(wasActive);
            }
            if (field.placeholder is TMP_Text phText) phText.text = placeholder;
            RectTransform r = (RectTransform)field.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(width, height);
            r.Size(width, height);
            return field;
        }

        /// <summary>Adds the game's scrollbar along the right edge of <paramref name="root"/> and hooks it to the scroll rect.</summary>
        public static void AttachScrollbar(RectTransform root, ScrollRect scroll)
        {
            if (_scrollbarTemplate == null) return;
            GameObject sbGo = UnityEngine.Object.Instantiate(_scrollbarTemplate, root, false);
            sbGo.SetActive(true);
            foreach (UIGamePad pad in sbGo.GetComponentsInChildren<UIGamePad>(true)) UnityEngine.Object.Destroy(pad);
            RectTransform sb = (RectTransform)sbGo.transform;
            sb.anchorMin = new Vector2(1, 0);
            sb.anchorMax = new Vector2(1, 1);
            sb.pivot = new Vector2(1, 0.5f);
            sb.offsetMin = new Vector2(-12, 0);
            sb.offsetMax = new Vector2(0, 0);
            Scrollbar bar = sbGo.GetComponent<Scrollbar>();
            bar.onValueChanged = new Scrollbar.ScrollEvent();
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        /// <summary>True while the player is typing into any text field of ours (or the game's).</summary>
        public static bool TextFieldFocused()
        {
            GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return sel != null && sel.TryGetComponent(out TMP_InputField f) && f.isFocused;
        }

        /// <summary>The crafting menu's recipe row (icon + name + selection highlight).</summary>
        public static GameObject RecipeRowTemplate => InventoryGui.instance != null ? InventoryGui.instance.m_recipeElementPrefab : null;
        public static float RecipeRowHeight => InventoryGui.instance != null ? InventoryGui.instance.m_recipeListSpace : 30f;
    }
}
