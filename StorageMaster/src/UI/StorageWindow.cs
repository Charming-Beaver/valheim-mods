using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StorageMaster.UI
{
    /// <summary>
    /// Opens every nearby chest at once, next to your own inventory, using the game's own inventory grids.
    /// Items can be moved by hand, searched, gathered into one chest, sorted and tidied up.
    /// </summary>
    public static class StorageWindow
    {
        // Taller with Adventure Backpacks, for "Also gather from my backpack".
        private static float SelH = 250f;
        private const float GridTop = 160f;
        private const float CardPad = 10f, CardGap = 8f, CardHeader = 92f, MinCardW = 380f;
        private const float HighlightAlpha = 0.32f;
        private const string HighlightName = "StorageMaster_Highlight";

        private class Card
        {
            public Container Chest;
            public RectTransform Root;
            public InventoryGrid Grid;
            public TMP_InputField Label;
            public Button ResetName;
            public TextMeshProUGUI Status;
            public TextMeshProUGUI Distance;
            public Image Background;
            public Button Gather;
            public Toggle Skip;
            public Vector2i Size;
            public float W, H;
            public bool Claimed;
            public bool Denied;
            public bool Excluded => Exclusions.IsExcluded(Chest);
            public Inventory Inv => Chest.GetInventory();
        }

        private static GameObject _root;
        private static float W, H, LeftW, Space;
        private static Vector2 _builtFor;
        private static RectTransform _chestsInset, _leftTitle, _selPanel;
        private static Button _toggleInventory;
        private static string _summaryBase = "";

        // Settings popup. Above these values opening gets slow and many of your friend's chests get locked while the window is open.
        private const float SafeRange = 20f;
        private const int SafeChests = 30;
        private static RectTransform _settings;
        private static TextMeshProUGUI _rangeValue, _chestsValue;
        private static bool _settingsChanged;
        private static RectTransform _cardList;
        private static RectTransform _playerArea, _playerContent;
        private static InventoryGrid _playerGrid;
        // With Adventure Backpacks, the backpack you're wearing is shown under your inventory (#60).
        private static InventoryGrid _backpackGrid;
        private static TextMeshProUGUI _backpackTitle;
        private const float BackpackTitleH = 40f;
        // Nearest first. A card without a Root is still waiting to be built (see BuildSome).
        private static readonly List<Card> _cards = new List<Card>();
        // Cards of closed chests, hidden and kept for the next chest of the same size: building one is the slow part.
        private static readonly List<Card> _pool = new List<Card>();
        // Building cards stops for the frame after this long, so opening with many chests doesn't freeze the game (#16).
        private const double BuildBudgetMs = 6;
        private static TMP_InputField _search;
        private static Image _selIcon;
        private static TextMeshProUGUI _selName, _selInfo, _summary;
        private static Toggle _includeMe, _includeBackpack;

        private static Inventory _heldInv;
        private static ItemDrop.ItemData _heldItem;
        private static int _heldAmount;
        private static RectTransform _follower;
        private static Image _followerIcon;
        private static TextMeshProUGUI _followerAmount;

        private static string _selected;   // m_shared.m_name of the chosen item type
        private static int _swallowUntilFrame = -10;

        public static bool IsOpen => _root != null && _root.activeSelf;
        public static bool BlocksInput => IsOpen || Time.frameCount <= _swallowUntilFrame;
        public static InventoryGrid PlayerGrid => IsOpen ? _playerGrid : null;

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public static void Open()
        {
            Player player = Player.m_localPlayer;
            if (player == null || InventoryGui.instance == null || !UIKit.Init()) return;
            // Built to fill the screen, so build again if the resolution changed.
            if (_root != null && CanvasSize() != _builtFor)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            if (_root == null) Build();
            InfoWindow.Close();
            OptionsWindow.Close();
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            // In SeneaL UI's look while it draws the inventory its way.
            Themed.Sync(_root.transform);
            _selected = null;
            _search.SetTextWithoutNotify("");
            ClearHeld();
            _settings.gameObject.SetActive(false);
            ApplyInventoryVisibility();
            Scan();
        }

        private static Vector2 CanvasSize() => ((RectTransform)UIKit.CanvasRoot).rect.size;

        public static void Close()
        {
            if (!IsOpen) return;
            ClearHeld();
            ReleaseAll();
            _root.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            _swallowUntilFrame = Time.frameCount + 1;
        }

        // ------------------------------------------------------------------ chests

        private static void Scan()
        {
            ReleaseAll();
            List<Container> chests = ChestAccess.FindNearby(Plugin.StorageRange.Value);
            int overLimit = Mathf.Max(0, chests.Count - Plugin.StorageMaxChests.Value);
            if (overLimit > 0) chests.RemoveRange(Plugin.StorageMaxChests.Value, overLimit);
            foreach (Container chest in chests) _cards.Add(new Card { Chest = chest });
            _summaryBase = chests.Count == 0
                ? $"No chests within {Plugin.StorageRange.Value:0} m that you can open."
                : $"{chests.Count} chest{(chests.Count == 1 ? "" : "s")} within {Plugin.StorageRange.Value:0} m";
            // Only the nearest ones are opened; say so, or the rest silently don't take part in tidying up and gathering.
            if (overLimit > 0) _summaryBase += $"  <color=#ffa13c>{overLimit} more over the Max chests limit (Settings)</color>";
            BuildSome();
        }

        /// <summary>
        /// Builds the cards still waiting, nearest first, and asks for their chests, until the frame's budget is used up;
        /// the rest follow in the next frames. Until then those chests count as "still opening".
        /// </summary>
        private static void BuildSome()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool built = false;
            for (int i = 0; i < _cards.Count && watch.Elapsed.TotalMilliseconds < BuildBudgetMs; i++)
            {
                if (_cards[i].Root != null) continue;
                Card card = TakeCard(_cards[i].Chest);
                _cards[i] = card;
                built = true;
                ChestAccess.Request(card.Chest, granted => OnAnswer(card, granted));
            }
            if (built) LayoutCards();
        }

        /// <summary>A card for the chest: a hidden one of the same size if there is one, otherwise a new one.</summary>
        private static Card TakeCard(Container chest)
        {
            Inventory inv = chest.GetInventory();
            var size = new Vector2i(inv.GetWidth(), inv.GetHeight());
            int pooled = _pool.FindIndex(c => c.Size == size);
            Card card;
            if (pooled >= 0)
            {
                card = _pool[pooled];
                _pool.RemoveAt(pooled);
            }
            else
            {
                card = BuildCard(size);
            }
            Bind(card, chest);
            return card;
        }

        /// <summary>Hides a card and keeps it for the next chest of the same size.</summary>
        private static void Recycle(Card card)
        {
            if (card.Root == null) return;
            card.Root.gameObject.SetActive(false);
            card.Chest = null;
            _pool.Add(card);
        }

        private static void OnAnswer(Card card, bool granted)
        {
            if (!IsOpen || !_cards.Contains(card) || card.Chest == null) return;
            if (!granted)
            {
                card.Denied = true;
                card.Status.text = "In use by someone else";
                return;
            }
            card.Claimed = true;
            card.Chest.SetInUse(true);
            card.Status.gameObject.SetActive(false);
            card.Grid.gameObject.SetActive(true);
            card.Label.SetTextWithoutNotify(ChestAccess.GetLabel(card.Chest));
        }

        private static void ReleaseAll()
        {
            foreach (Card card in _cards)
            {
                Release(card);
                Recycle(card);
            }
            _cards.Clear();
        }

        private static void Release(Card card)
        {
            if (card.Chest == null) return;
            ChestAccess.Forget(card.Chest);
            if (card.Claimed && card.Chest.m_nview != null && card.Chest.m_nview.IsValid() && card.Chest.IsOwner())
            {
                card.Chest.SetInUse(false);
            }
        }

        private static List<Card> Ready => _cards.Where(c => c.Claimed && c.Chest != null).ToList();

        /// <summary>Open chests that aren't marked Skip: the only ones gather, tidy up, sort all and quick stack use.</summary>
        private static List<Card> Usable => Ready.Where(c => !c.Excluded).ToList();

        // ------------------------------------------------------------------ frame

        public static void FocusSearch()
        {
            _search.Select();
            _search.ActivateInputField();
        }

        public static void Update()
        {
            if (!IsOpen) return;
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead() || player.IsTeleporting())
            {
                Close();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_heldItem != null) ClearHeld();
                else if (_settings.gameObject.activeSelf) CloseSettings();
                else Close();
                _swallowUntilFrame = Time.frameCount + 1;
                return;
            }
            // F jumps into Find item, as in the build menu (unless you're typing somewhere already).
            if (Input.GetKeyDown(KeyCode.F) && !UIKit.TextFieldFocused()) FocusSearch();

            // Drop chests that were destroyed or that you walked away from.
            bool removed = false;
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                Card card = _cards[i];
                bool gone = card.Chest == null || card.Chest.m_nview == null || !card.Chest.m_nview.IsValid();
                float dist = gone ? 0f : Vector3.Distance(card.Chest.transform.position, player.transform.position);
                if (gone || dist > Plugin.StorageRange.Value + 5f)
                {
                    if (_heldInv != null && !gone && _heldInv == card.Inv) ClearHeld();
                    Release(card);
                    Recycle(card);
                    _cards.RemoveAt(i);
                    removed = true;
                    continue;
                }
                if (card.Root == null) continue;
                card.Distance.text = $"{dist:0} m";
                card.ResetName.gameObject.SetActive(card.Claimed && ChestAccess.GetLabel(card.Chest).Length > 0);
            }
            if (removed) LayoutCards();
            BuildSome();

            Inventory backpack = AdventureBackpacksCompat.Backpack(player);
            if (_includeBackpack != null && _includeBackpack.interactable != (backpack != null)) _includeBackpack.interactable = backpack != null;
            // Also when the item was taken from a backpack that you've taken off since.
            if (_heldItem != null && (_heldInv == null || !_heldInv.ContainsItem(_heldItem) ||
                                      (_heldInv != player.GetInventory() && _heldInv != backpack && _cards.All(c => c.Chest == null || _heldInv != c.Inv))))
            {
                ClearHeld();
            }

            if (Plugin.StorageShowInventory.Value)
            {
                Inventory mine = player.GetInventory();
                FitPlayerGrids(mine, backpack);
                _playerGrid.UpdateInventory(mine, player, _heldItem);
                HideSlotRows(mine);
                Highlight(_playerGrid, mine);
                Locks.DrawOverlays(_playerGrid);
                if (backpack != null)
                {
                    _backpackGrid.UpdateInventory(backpack, null, _heldItem);
                    Highlight(_backpackGrid, backpack);
                }
            }
            foreach (Card card in _cards)
            {
                if (!card.Claimed) continue;
                card.Grid.UpdateInventory(card.Inv, null, _heldItem);
                Highlight(card.Grid, card.Inv);
            }

            if (_heldItem != null)
            {
                _follower.gameObject.SetActive(true);
                _follower.position = Input.mousePosition;
                _followerIcon.sprite = _heldItem.GetIcon();
                _followerAmount.text = _heldAmount > 1 ? _heldAmount.ToString() : "";
            }
            else
            {
                _follower.gameObject.SetActive(false);
            }
            UpdateSelectionInfo();
        }

        // ------------------------------------------------------------------ building

        private static void Build()
        {
            // The hidden cards went with the old window.
            _pool.Clear();
            // The window fills the screen, so as many chests as possible fit side by side.
            _builtFor = CanvasSize();
            W = Mathf.Max(1000f, _builtFor.x - 60f);
            H = Mathf.Max(700f, _builtFor.y - 140f);
            Space = InventoryGui.instance.ContainerGrid.m_elementSpace;
            // Room for 8 slots and the scroll bar, which shows when a backpack under the inventory doesn't fit.
            LeftW = Mathf.Max(520f, 8 * Space + 28f);
            SelH = AdventureBackpacksCompat.Active ? 280f : 250f;

            _root = UIKit.Rect(UIKit.CanvasRoot, "StorageMasterStorage").Fill().gameObject;
            _root.AddComponent<Image>().color = new Color(0, 0, 0, 0.35f);

            RectTransform window = UIKit.DarkPanel(_root.transform, "window").rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(W, H);
            window.anchoredPosition = new Vector2(0, 20);

            UIKit.Text(window, "Storage", 40, UIKit.Orange, TextAlignmentOptions.Center, true).rectTransform.TopLeft(0, 12, W, 50);
            Place(UIKit.Button(window, "Close", Close, 110, 36), W - 134, 22, 110, 36);
            Place(UIKit.Button(window, "Refresh", Scan, 110, 36), W - 250, 22, 110, 36);
            Button settingsButton = UIKit.Button(window, "Settings", ToggleSettings, 110, 36);
            Place(settingsButton, W - 366, 22, 110, 36);
            UIKit.Tooltip(settingsButton.gameObject, "How far away and how many chests the Storage window opens.");

            // Top bar.
            // F focuses it, like the build menu's search it's copied from (and whose F hint it keeps).
            _search = UIKit.InputField(window, "Find item...", 300, 38, findKeyHint: true);
            Place(_search.transform, 24, 72, 300, 38);
            float x = 340;
            x = TopButton(window, "Tidy up", TidyUp, x, 120,
                "Collects every item that is split over several chests into the chest that already has the most of it, then sorts all chests.\nChests marked Skip are left alone.");
            x = TopButton(window, "Sort all", SortAll, x, 110, "Sorts every open chest, except the ones marked Skip.");
            x = TopButton(window, "Quick stack", QuickStackHere, x, 140,
                "Puts your items into the chests that already hold the same kind. Hotbar, equipped items, locked slots and chests marked Skip are left alone.");
            _toggleInventory = UIKit.Button(window, "", ToggleInventory, 170, 38);
            Place(_toggleInventory, x, 72, 170, 38);
            UIKit.Tooltip(_toggleInventory.gameObject, "Hide your inventory to give the chests the whole window, or show it again.");
            x += 170 + 16;
            _summary = UIKit.Text(window, "", 16, UIKit.Grey);
            _summary.rectTransform.TopLeft(x, 76, W - x - 24, 30);

            // Left: your inventory and the selected item.
            _leftTitle = UIKit.Text(window, "Your inventory", 24, UIKit.Orange, norse: true).rectTransform.TopLeft(24, 124, LeftW, 32);
            _playerArea = UIKit.Rect(window, "player").TopLeft(24, GridTop, LeftW, H - GridTop - SelH - 40);
            // Your inventory and, with Adventure Backpacks, your backpack under it. Scrolls when they don't both fit.
            _playerContent = UIKit.ScrollList(_playerArea, "scroll", out _, 0, 0);
            ((RectTransform)_playerContent.parent.parent).Fill();
            UnityEngine.Object.DestroyImmediate(_playerContent.GetComponent<ContentSizeFitter>());
            UnityEngine.Object.DestroyImmediate(_playerContent.GetComponent<VerticalLayoutGroup>());
            _playerGrid = MakeGrid(_playerContent);
            ((RectTransform)_playerGrid.transform).TopLeft(0, 0, 8 * Space, 4 * Space);
            _backpackTitle = UIKit.Text(_playerContent, "", 24, UIKit.Orange, norse: true);
            _backpackGrid = MakeGrid(_playerContent);
            ((RectTransform)_backpackGrid.transform).TopLeft(0, 0, 3 * Space, Space);

            _selPanel = UIKit.Inset(window, "selection").rectTransform;
            _selPanel.anchorMin = _selPanel.anchorMax = new Vector2(0, 0);
            _selPanel.pivot = new Vector2(0, 0);
            _selPanel.anchoredPosition = new Vector2(24, 24);
            _selPanel.sizeDelta = new Vector2(LeftW, SelH);
            _selIcon = UIKit.Icon(_selPanel, null, 64, slot: true);
            ((RectTransform)_selIcon.transform.parent).TopLeft(14, 14, 64, 64);
            _selName = UIKit.Text(_selPanel, "", 24, UIKit.Orange, norse: true);
            _selName.rectTransform.TopLeft(90, 12, LeftW - 104, 32);
            _selInfo = UIKit.Text(_selPanel, "", 16).Wrap();
            _selInfo.alignment = TextAlignmentOptions.TopLeft;
            _selInfo.rectTransform.TopLeft(90, 46, LeftW - 104, 60);
            _includeMe = UIKit.Check(_selPanel, "Also gather from my inventory (not hotbar or locked slots)", false, null, LeftW - 28);
            ((RectTransform)_includeMe.transform).TopLeft(14, 108, LeftW - 28, 26);
            float helpTop = 144;
            if (AdventureBackpacksCompat.Active)
            {
                _includeBackpack = UIKit.Check(_selPanel, "Also gather from my backpack", false, null, LeftW - 28);
                ((RectTransform)_includeBackpack.transform).TopLeft(14, 138, LeftW - 28, 26);
                helpTop += 30;
            }
            UIKit.Text(_selPanel,
                "<color=#ffa13c>Click</color> pick up / put down    <color=#ffa13c>Shift+click</color> take half\n" +
                "<color=#ffa13c>Ctrl+click</color> move between you and the chests\n" +
                "<color=#ffa13c>Right-click</color> choose an item to find and gather\n" +
                "<color=#ffa13c>Alt+click</color> lock a slot of your inventory    <color=#ffa13c>Esc</color> drop / close",
                15, UIKit.Grey).Wrap().rectTransform.TopLeft(14, helpTop, LeftW - 28, SelH - helpTop - 8);

            // Right: the chests, laid out side by side and wrapping into rows.
            _chestsInset = UIKit.Inset(window, "chests").rectTransform.Anchor(0, 0, 1, 1, 24, 24, 24, 124);
            _cardList = UIKit.ScrollList(_chestsInset, "scroll", out ScrollRect scroll, 0, 0);
            ((RectTransform)_cardList.parent.parent).Fill(4);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            UnityEngine.Object.DestroyImmediate(_cardList.GetComponent<ContentSizeFitter>());
            UnityEngine.Object.DestroyImmediate(_cardList.GetComponent<VerticalLayoutGroup>());

            BuildSettings(window);

            // Item following the mouse while carried.
            _follower = UIKit.Rect(_root.transform, "held");
            _follower.sizeDelta = new Vector2(64, 64);
            _followerIcon = _follower.gameObject.AddComponent<Image>();
            _followerIcon.raycastTarget = false;
            _followerIcon.preserveAspect = true;
            _followerAmount = UIKit.Text(_follower, "", 18, Color.white, TextAlignmentOptions.BottomRight);
            _followerAmount.rectTransform.Fill();
            _follower.gameObject.SetActive(false);

            _root.SetActive(false);
        }

        private static void BuildSettings(RectTransform window)
        {
            const float sw = 480f, sh = 330f;
            _settings = UIKit.DarkPanel(window, "settings").rectTransform.TopLeft(W - 24 - sw, 64, sw, sh);
            UIKit.Text(_settings, "Settings", 30, UIKit.Orange, TextAlignmentOptions.Center, true).rectTransform.TopLeft(0, 14, sw, 40);

            _rangeValue = SettingRow(_settings, "Range", 70, d =>
                Plugin.StorageRange.Value = Mathf.Clamp(Plugin.StorageRange.Value + d * 5f, 5f, 50f));
            _chestsValue = SettingRow(_settings, "Most chests", 116, d =>
                Plugin.StorageMaxChests.Value = Mathf.Clamp(Plugin.StorageMaxChests.Value + d * 4, 4, 60));

            UIKit.Text(_settings,
                $"<color=#ffa13c>Recommended: up to {SafeRange:0} m and about {SafeChests} chests.</color>\n" +
                "More works, but the window takes longer to open, and every chest it shows is locked for your friend " +
                "until you close it (they see \"in use\"). The nearest chests are always shown first.",
                15, UIKit.Grey).Wrap().rectTransform.TopLeft(28, 166, sw - 56, 100);

            Button done = UIKit.Button(_settings, "Done", CloseSettings, 120, 36);
            Place(done, (sw - 120) / 2f, sh - 56, 120, 36);
            _settings.gameObject.SetActive(false);
        }

        private static TextMeshProUGUI SettingRow(RectTransform parent, string label, float y, System.Action<int> change)
        {
            UIKit.Text(parent, label, 20).rectTransform.TopLeft(28, y, 200, 36);
            Place(UIKit.Button(parent, "-", () => { change(-1); SettingsChanged(); }, 40, 36), 250, y, 40, 36);
            TextMeshProUGUI value = UIKit.Text(parent, "", 20, UIKit.Parchment, TextAlignmentOptions.Center);
            value.rectTransform.TopLeft(294, y, 110, 36);
            Place(UIKit.Button(parent, "+", () => { change(+1); SettingsChanged(); }, 40, 36), 408, y, 40, 36);
            return value;
        }

        private static void SettingsChanged()
        {
            _settingsChanged = true;
            RefreshSettings();
        }

        private static void RefreshSettings()
        {
            float range = Plugin.StorageRange.Value;
            int chests = Plugin.StorageMaxChests.Value;
            _rangeValue.text = range > SafeRange ? $"<color=#ffa13c>{range:0} m</color>" : $"{range:0} m";
            _chestsValue.text = chests > SafeChests ? $"<color=#ffa13c>{chests}</color>" : chests.ToString();
        }

        private static void ToggleSettings()
        {
            if (_settings.gameObject.activeSelf) CloseSettings();
            else
            {
                _settingsChanged = false;
                RefreshSettings();
                _settings.gameObject.SetActive(true);
                _settings.SetAsLastSibling();
            }
        }

        /// <summary>Closes the popup and opens the chests again if the range or count changed.</summary>
        private static void CloseSettings()
        {
            _settings.gameObject.SetActive(false);
            if (_settingsChanged)
            {
                _settingsChanged = false;
                ClearHeld();
                Scan();
            }
        }

        private static void ToggleInventory()
        {
            Plugin.StorageShowInventory.Value = !Plugin.StorageShowInventory.Value;
            Player player = Player.m_localPlayer;
            if (!Plugin.StorageShowInventory.Value && player != null && _heldInv == player.GetInventory()) ClearHeld();
            ApplyInventoryVisibility();
        }

        private static void ApplyInventoryVisibility()
        {
            bool show = Plugin.StorageShowInventory.Value;
            _leftTitle.gameObject.SetActive(show);
            _playerArea.gameObject.SetActive(show);
            _selPanel.gameObject.SetActive(show);
            _chestsInset.offsetMin = new Vector2(show ? 24 + LeftW + 24 : 24, 24);
            _toggleInventory.SetLabel(show ? "Hide inventory" : "Show inventory");
            LayoutCards();
        }

        /// <summary>Width available for chest cards inside the scroll list.</summary>
        private static float ChestsWidth()
        {
            float insetW = W - (Plugin.StorageShowInventory.Value ? 24 + LeftW + 24 : 24) - 24;
            return insetW - 8 - 16;
        }

        /// <summary>Places the cards left to right, starting a new row when the next one doesn't fit.</summary>
        private static void LayoutCards()
        {
            if (_cardList == null) return;
            float avail = ChestsWidth();
            float x = CardGap, y = CardGap, rowH = 0;
            foreach (Card card in _cards)
            {
                if (card.Root == null) continue;
                if (x > CardGap && x + card.W > avail)
                {
                    x = CardGap;
                    y += rowH + CardGap;
                    rowH = 0;
                }
                card.Root.anchoredPosition = new Vector2(x, -y);
                x += card.W + CardGap;
                rowH = Mathf.Max(rowH, card.H);
            }
            _cardList.sizeDelta = new Vector2(0, y + rowH + CardGap);
        }

        private static float TopButton(RectTransform window, string label, UnityEngine.Events.UnityAction onClick, float x, float w, string tip)
        {
            Button b = UIKit.Button(window, label, onClick, w, 38);
            Place(b.transform, x, 72, w, 38);
            UIKit.Tooltip(b.gameObject, tip);
            return x + w + 6;
        }

        private static void Place(Component c, float x, float y, float w, float h) => ((RectTransform)c.transform).TopLeft(x, y, w, h);

        /// <summary>A card for chests with this many slots; <see cref="Bind"/> puts a chest in it.</summary>
        private static Card BuildCard(Vector2i size)
        {
            float gridW = size.x * Space;
            float gridH = size.y * Space;
            var card = new Card { Size = size };
            card.W = Mathf.Max(MinCardW, gridW + 2 * CardPad);
            card.H = CardHeader + gridH + CardPad;
            float inner = card.W - 2 * CardPad;

            card.Root = UIKit.Rect(_cardList, "chest").TopLeft(0, 0, card.W, card.H);
            card.Background = card.Root.gameObject.AddComponent<Image>();

            // First line: name, distance and the Skip box.
            card.Label = UIKit.InputField(card.Root, "", inner - 190, 34, plain: true);
            Place(card.Label.transform, CardPad, 8, inner - 190, 34);
            card.ResetName = UIKit.Button(card.Root, "X", () =>
            {
                ChestAccess.SetLabel(card.Chest, "");
                card.Label.SetTextWithoutNotify("");
            }, 34, 34);
            Place(card.ResetName, CardPad + inner - 186, 8, 34, 34);
            UIKit.Tooltip(card.ResetName.gameObject, "Remove the name.");
            card.ResetName.gameObject.SetActive(false);
            card.Label.characterLimit = 32;
            card.Label.onEndEdit.AddListener(text => ChestAccess.SetLabel(card.Chest, text));
            UIKit.Tooltip(card.Label.gameObject, "Click to name this chest. The name shows when you look at it.");

            card.Distance = UIKit.Text(card.Root, "", 15, UIKit.Grey, TextAlignmentOptions.Center);
            card.Distance.rectTransform.TopLeft(CardPad + inner - 144, 8, 52, 34);

            card.Skip = UIKit.Check(card.Root, "Skip", false, v =>
            {
                Exclusions.Set(card.Chest, v);
                RefreshCardLook(card);
            }, 84);
            ((RectTransform)card.Skip.transform).TopLeft(card.W - CardPad - 84, 12, 84, 26);
            UIKit.Tooltip(card.Skip.gameObject,
                "Leave this chest alone: Quick stack, Gather here, Tidy up and Sort all won't put anything in or take anything out " +
                "(handy for your friend's chests). You can still move items by hand. Only affects you.");

            // Second line: actions.
            card.Gather = UIKit.Button(card.Root, "Gather here", () => GatherInto(card), 124, 34);
            Place(card.Gather, CardPad, 50, 124, 34);
            UIKit.Tooltip(card.Gather.gameObject, "Moves every item of the chosen kind (right-click an item) into this chest. What doesn't fit goes into the next chests.");
            Button sort = UIKit.Button(card.Root, "Sort", () => { if (card.Claimed) Sorter.SortContainer(card.Chest); }, 70, 34);
            Place(sort, CardPad + 130, 50, 70, 34);
            Button take = UIKit.Button(card.Root, "Take all", () => TakeAll(card), 92, 34);
            Place(take, CardPad + 206, 50, 92, 34);

            RectTransform gridArea = UIKit.Rect(card.Root, "grid").TopLeft((card.W - gridW) / 2f, CardHeader, gridW, gridH);
            card.Grid = MakeGrid(gridArea);
            ((RectTransform)card.Grid.transform).TopLeft(0, 0, gridW, gridH);

            card.Status = UIKit.Text(card.Root, "", 18, UIKit.Grey, TextAlignmentOptions.Center);
            card.Status.rectTransform.Anchor(0, 0, 1, 1, CardPad, CardPad, CardPad, CardHeader);
            return card;
        }

        /// <summary>Shows a chest on a card, new or reused, as "Opening..." until the chest answers.</summary>
        private static void Bind(Card card, Container chest)
        {
            card.Chest = chest;
            card.Claimed = false;
            card.Denied = false;
            ((TMP_Text)card.Label.placeholder).text = Localization.instance.Localize(chest.m_name);
            card.Label.SetTextWithoutNotify("");
            card.ResetName.gameObject.SetActive(false);
            card.Distance.text = "";
            card.Skip.SetIsOnWithoutNotify(Exclusions.IsExcluded(chest));
            card.Status.text = "Opening...";
            card.Status.gameObject.SetActive(true);
            // The grid makes its slots the first time it shows an inventory of its size, which is slow; a reused card
            // already has them.
            card.Grid.UpdateInventory(chest.GetInventory(), null, null);
            card.Grid.gameObject.SetActive(false);
            RefreshCardLook(card);
            card.Root.gameObject.SetActive(true);
        }

        private static void RefreshCardLook(Card card)
        {
            bool excluded = card.Excluded;
            card.Background.color = excluded ? new Color(0.3f, 0.06f, 0.03f, 0.4f) : new Color(0, 0, 0, 0.25f);
            card.Gather.interactable = !excluded;
        }

        /// <summary>A copy of the chest window's grid, wired to our own click handlers.</summary>
        private static InventoryGrid MakeGrid(Transform parent)
        {
            InventoryGrid template = InventoryGui.instance.ContainerGrid;
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            go.name = "grid";
            go.SetActive(true);
            InventoryGrid grid = go.GetComponent<InventoryGrid>();
            // The copy brings the original's slots along; remove them so the grid builds its own.
            for (int i = grid.m_gridRoot.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(grid.m_gridRoot.GetChild(i).gameObject);
            grid.m_width = 0;
            grid.m_height = 0;
            grid.m_onSelected = OnSelected;
            grid.m_onRightClick = OnRightClick;
            grid.m_onReleased = null;
            grid.CanDropDragOntoItem = _ => true;
            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) UnityEngine.Object.Destroy(pad);
            foreach (ScrollRect sr in go.GetComponentsInChildren<ScrollRect>(true)) sr.enabled = false;
            // The chest window clips its grid to its own size; ours is always sized to fit, so no clipping.
            foreach (RectMask2D m in go.GetComponentsInChildren<RectMask2D>(true)) UnityEngine.Object.Destroy(m);
            foreach (Mask m in go.GetComponentsInChildren<Mask>(true)) UnityEngine.Object.Destroy(m);
            if (grid.m_scrollbar != null) grid.m_scrollbar.gameObject.SetActive(false);
            RectTransform root = grid.m_gridRoot;
            root.anchorMin = new Vector2(0, 1);
            root.anchorMax = new Vector2(1, 1);
            root.pivot = new Vector2(0.5f, 1);
            root.anchoredPosition = Vector2.zero;
            return grid;
        }

        /// <summary>
        /// Sizes your inventory grid (and your backpack's) to exactly its slots (the game lays slots out from the grid's
        /// width when they're created, so this must happen before it updates). Alone, your inventory shrinks when it has
        /// many rows; with a backpack under it, both keep their size and the area scrolls when they don't fit.
        /// </summary>
        private static void FitPlayerGrids(Inventory inv, Inventory backpack)
        {
            float w = inv.GetWidth() * Space;
            float h = PlayerSlots.Rows(inv) * Space;
            float available = H - GridTop - SelH - 40;
            float contentH;
            if (backpack == null)
            {
                float scale = Mathf.Min(1f, available / h, LeftW / w);
                Fit(_playerGrid, 0f, w, h, scale);
                contentH = h * scale;
            }
            else
            {
                float bw = backpack.GetWidth() * Space, bh = backpack.GetHeight() * Space;
                // The scroll bar takes some of the width when it shows.
                float width = h + BackpackTitleH + bh > available ? LeftW - 18f : LeftW;
                float scale = Mathf.Min(1f, width / w);
                Fit(_playerGrid, 0f, w, h, scale);
                float y = h * scale;
                _backpackTitle.rectTransform.TopLeft(0, y + 6, width, BackpackTitleH - 10);
                string title = Localization.instance.Localize(backpack.GetName());
                if (_backpackTitle.text != title) _backpackTitle.text = title;
                y += BackpackTitleH;
                float backpackScale = Mathf.Min(1f, width / bw);
                Fit(_backpackGrid, y, bw, bh, backpackScale);
                contentH = y + bh * backpackScale;
            }
            if (_backpackGrid.gameObject.activeSelf != (backpack != null))
            {
                _backpackGrid.gameObject.SetActive(backpack != null);
                _backpackTitle.gameObject.SetActive(backpack != null);
            }
            if (!Mathf.Approximately(_playerContent.sizeDelta.y, contentH)) _playerContent.sizeDelta = new Vector2(0, contentH);
        }

        private static void Fit(InventoryGrid grid, float y, float w, float h, float scale)
        {
            RectTransform rt = (RectTransform)grid.transform;
            rt.localScale = new Vector3(scale, scale, 1);
            rt.TopLeft(0, y, w, h);
        }

        /// <summary>
        /// Other mods' slots (ExtraSlots, SeneaL UI) are rows below the regular inventory. They're shown in those mods' own
        /// panels, and dropping things into them here would skip their checks, so the window only shows the regular slots
        /// (without SeneaL UI's unusable ones).
        /// </summary>
        private static void HideSlotRows(Inventory inv)
        {
            if (!PlayerSlots.OtherModsSlots) return;
            foreach (InventoryElement element in _playerGrid.m_elements)
            {
                bool slot = !PlayerSlots.IsRegular(inv, element.Position);
                if (element.gameObject.activeSelf == slot) element.gameObject.SetActive(!slot);
            }
        }

        // ------------------------------------------------------------------ clicks

        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private static bool Alt => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        private static void OnSelected(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos, InventoryGrid.Modifier mod)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            Inventory inv = grid.GetInventory();

            if (Alt && grid == _playerGrid)
            {
                Locks.Toggle(pos);
                return;
            }
            if (_heldItem != null)
            {
                DropHeld(grid, pos);
                return;
            }
            if (item == null) return;
            if (Ctrl)
            {
                QuickMove(inv, item);
                return;
            }
            if (inv == player.GetInventory() && player.IsItemEquiped(item))
            {
                Plugin.Show("Unequip it first");
                return;
            }
            _heldInv = inv;
            _heldItem = item;
            _heldAmount = Shift && item.m_stack > 1 ? Mathf.CeilToInt(item.m_stack / 2f) : item.m_stack;
        }

        private static void OnRightClick(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos)
        {
            if (_heldItem != null)
            {
                ClearHeld();
                return;
            }
            if (item == null) return;
            _selected = _selected == item.m_shared.m_name ? null : item.m_shared.m_name;
        }

        private static void DropHeld(InventoryGrid grid, Vector2i pos)
        {
            Player player = Player.m_localPlayer;
            Inventory target = grid.GetInventory();
            if (!_heldInv.ContainsItem(_heldItem))
            {
                ClearHeld();
                return;
            }
            if (_heldItem.m_shared.m_questItem && target != _heldInv) return;
            ItemDrop.ItemData there = target.GetItemAt(pos.x, pos.y);
            if (there != null && there != _heldItem && target == player.GetInventory() && player.IsItemEquiped(there) && _heldInv != target)
            {
                Plugin.Show("Unequip it first");
                return;
            }
            if (grid.DropItem(_heldInv, _heldItem, _heldAmount, pos))
            {
                InventoryGui.instance.m_moveItemEffects.Create(player.transform.position, Quaternion.identity);
                ClearHeld();
            }
        }

        private static void ClearHeld()
        {
            _heldInv = null;
            _heldItem = null;
            _heldAmount = 0;
            if (_follower != null) _follower.gameObject.SetActive(false);
        }

        /// <summary>
        /// Ctrl + click: chest item goes to you; your item (or your backpack's) goes to a chest that already has it (or
        /// any with room).
        /// </summary>
        private static void QuickMove(Inventory from, ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            Inventory mine = player.GetInventory();
            if (item.m_shared.m_questItem) return;
            int before = item.m_stack;
            if (from == mine || from == AdventureBackpacksCompat.Backpack(player))
            {
                if (from == mine && player.IsItemEquiped(item))
                {
                    Plugin.Show("Unequip it first");
                    return;
                }
                List<Card> ready = Usable;
                Card dest = ready.FirstOrDefault(c => c.Inv.ContainsItemByName(item.m_shared.m_name) && c.Inv.CanAddItem(item, 1))
                            ?? ready.FirstOrDefault(c => c.Inv.CanAddItem(item, 1));
                if (dest == null)
                {
                    Plugin.Show("No room in the open chests");
                    return;
                }
                dest.Inv.MoveItemToThis(from, item);
            }
            else
            {
                if (!mine.CanAddItem(item, 1))
                {
                    Plugin.Show("$inventory_full");
                    return;
                }
                mine.MoveItemToThis(from, item);
            }
            if (!from.ContainsItem(item) || item.m_stack != before)
            {
                InventoryGui.instance.m_moveItemEffects.Create(player.transform.position, Quaternion.identity);
            }
        }

        private static void TakeAll(Card card)
        {
            if (!card.Claimed) return;
            Inventory mine = Player.m_localPlayer.GetInventory();
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(card.Inv.GetAllItems()))
            {
                if (!mine.CanAddItem(item, 1)) continue;
                mine.MoveItemToThis(card.Inv, item);
            }
        }

        // ------------------------------------------------------------------ gather / tidy

        private static void GatherInto(Card target)
        {
            if (!target.Claimed) return;
            if (target.Excluded)
            {
                Plugin.Show("This chest is skipped. Untick Skip to gather into it.");
                return;
            }
            if (_selected == null)
            {
                Plugin.Show("Right-click an item first to choose what to gather");
                return;
            }
            List<Card> ready = Usable;
            int start = ready.IndexOf(target);
            // This chest first, then the following ones in the list, wrapping around.
            var order = new List<Card>();
            for (int i = 0; i < ready.Count; i++) order.Add(ready[(start + i) % ready.Count]);

            Inventory backpack = IncludedBackpack();
            int moved = Gather(_selected, order, _includeMe.isOn, out int intoTarget, out int chestsUsed, backpack);
            string name = Localization.instance.Localize(_selected);
            int remaining = TotalEverywhere(_selected, _includeMe.isOn, backpack) - target.Inv.CountItems(_selected);
            Plugin.Show(moved == 0
                ? $"Nothing to gather: all {name} already in place"
                : $"Gathered {moved} {name}" + (chestsUsed > 1 ? $"\n<size=70%>{intoTarget} into this chest, the rest into {chestsUsed - 1} more</size>" : "") +
                  (remaining > 0 && chestsUsed <= 1 ? $"\n<size=70%>{remaining} left elsewhere</size>" : "") + LeftOut());
        }

        /// <summary>
        /// A note on chests that couldn't take part (still opening, or open for someone else), for the summary of
        /// Gather here, Tidy up, Sort all and Quick stack. Empty when all chests in the window took part.
        /// </summary>
        private static string LeftOut()
        {
            int opening = _cards.Count(c => !c.Claimed && !c.Denied);
            int inUse = _cards.Count(c => c.Denied);
            if (opening == 0 && inUse == 0) return "";
            var parts = new List<string>();
            if (opening > 0) parts.Add($"{opening} still opening");
            if (inUse > 0) parts.Add($"{inUse} in use by someone else");
            int total = opening + inUse;
            return $"\n<size=70%><color=#ffa13c>{total} chest{(total == 1 ? " was" : "s were")} left out: {string.Join(", ", parts)}</color></size>";
        }

        /// <summary>The backpack you're wearing, when "Also gather from my backpack" is ticked.</summary>
        private static Inventory IncludedBackpack() =>
            _includeBackpack != null && _includeBackpack.isOn ? AdventureBackpacksCompat.Backpack(Player.m_localPlayer) : null;

        /// <summary>
        /// Fills the chests in <paramref name="order"/> one after another with the given item, taking it from
        /// chests later in the order (and optionally from your inventory and your backpack). Returns how many items moved.
        /// </summary>
        private static int Gather(string name, List<Card> order, bool includeMe, out int intoFirst, out int chestsUsed, Inventory backpack = null)
        {
            Player player = Player.m_localPlayer;
            Inventory mine = player.GetInventory();
            int moved = 0;
            intoFirst = 0;
            chestsUsed = 0;
            for (int d = 0; d < order.Count; d++)
            {
                Inventory dest = order[d].Inv;
                int before = dest.CountItems(name);
                var sources = new List<Inventory>();
                if (includeMe) sources.Add(mine);
                if (backpack != null) sources.Add(backpack);
                for (int s = order.Count - 1; s > d; s--) sources.Add(order[s].Inv);

                foreach (Inventory src in sources)
                {
                    foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(src.GetAllItems()))
                    {
                        if (item.m_shared.m_name != name) continue;
                        if (src == mine && !QuickStack.CanMove(player, item)) continue;
                        if (!dest.CanAddItem(item, 1)) break;
                        MoveInto(dest, src, item);
                    }
                }
                dest.Changed();
                int gained = dest.CountItems(name) - before;
                if (gained > 0 || (d == 0 && before > 0)) chestsUsed++;
                if (d == 0) intoFirst = gained;
                moved += gained;
                if (!AnyLeft(name, sources, mine, includeMe, player)) break;
            }
            if (moved > 0) InventoryGui.instance.m_moveItemEffects.Create(player.transform.position, Quaternion.identity);
            return moved;
        }

        /// <summary>
        /// Moves as much of a stack as fits. The game's AddItem also handles a stack that only partly fits (room on
        /// partial stacks, but no empty slot), but then logs "Trying to add item to occupied slot", so it only gets what fits.
        /// </summary>
        private static void MoveInto(Inventory dest, Inventory src, ItemDrop.ItemData item)
        {
            int room = int.MaxValue;
            if (!dest.HaveEmptySlot())
            {
                room = 0;
                foreach (ItemDrop.ItemData other in dest.GetAllItems())
                {
                    if (other.m_shared.m_name == item.m_shared.m_name && other.m_quality == item.m_quality && other.m_worldLevel == item.m_worldLevel)
                    {
                        room += Mathf.Max(0, other.m_shared.m_maxStackSize - other.m_stack);
                    }
                }
            }
            if (item.m_stack <= room)
            {
                if (dest.AddItem(item)) src.RemoveItem(item);
                else src.Changed();
                return;
            }
            // Only partial stacks have room: top them up with part of the stack, the rest stays where it is.
            ItemDrop.ItemData part = item.Clone();
            part.m_stack = room;
            dest.AddItem(part);
            item.m_stack -= room;
            src.Changed();
        }

        private static bool AnyLeft(string name, List<Inventory> sources, Inventory mine, bool includeMe, Player player)
        {
            foreach (Inventory src in sources)
            {
                foreach (ItemDrop.ItemData item in src.GetAllItems())
                {
                    if (item.m_shared.m_name == name && (src != mine || QuickStack.CanMove(player, item))) return true;
                }
            }
            return false;
        }

        private static int TotalEverywhere(string name, bool includeMe, Inventory backpack)
        {
            int total = Usable.Sum(c => c.Inv.CountItems(name));
            if (includeMe) total += Player.m_localPlayer.GetInventory().CountItems(name);
            if (backpack != null) total += backpack.CountItems(name);
            return total;
        }

        /// <summary>Every item spread over several chests goes to the chest holding the most of it; then everything is sorted.</summary>
        private static void TidyUp()
        {
            List<Card> ready = Usable;
            if (ready.Count < 2)
            {
                SortAll();
                return;
            }
            // Merging partial stacks first frees slots in full chests, so more can be brought together.
            foreach (Card c in ready) Sorter.SortContainer(c.Chest, silent: true);
            var names = new HashSet<string>(ready.SelectMany(c => c.Inv.GetAllItems().Select(i => i.m_shared.m_name)));
            int moved = 0;
            var kinds = new HashSet<string>();
            // Gathering one kind can make room for another that was already done (its chest was full then), so repeat
            // until nothing moves. Items only ever move towards the chest holding the most of them, so this settles quickly.
            for (int pass = 0; pass < 10; pass++)
            {
                int movedThisPass = 0;
                foreach (string name in names)
                {
                    var holders = ready.Where(c => c.Inv.CountItems(name) > 0).OrderByDescending(c => c.Inv.CountItems(name)).ToList();
                    if (holders.Count < 2) continue;
                    // Overflow goes first to chests that already hold some, then to any other chest.
                    var order = holders.Concat(ready.Where(c => !holders.Contains(c))).ToList();
                    int m = Gather(name, order, false, out _, out _);
                    if (m > 0)
                    {
                        movedThisPass += m;
                        kinds.Add(name);
                    }
                }
                moved += movedThisPass;
                if (movedThisPass == 0) break;
            }
            foreach (Card c in ready) Sorter.SortContainer(c.Chest, silent: true);
            Plugin.Show((moved > 0 ? $"Tidied up: {kinds.Count} kind{(kinds.Count == 1 ? "" : "s")} of items brought together, all chests sorted" : "All chests sorted (nothing was spread out)") + LeftOut());
        }

        private static void SortAll()
        {
            List<Card> ready = Usable;
            foreach (Card c in ready) Sorter.SortContainer(c.Chest, silent: true);
            Plugin.Show($"Sorted {ready.Count} chest{(ready.Count == 1 ? "" : "s")} by {Sorter.ModeLabel}" + LeftOut());
        }

        private static void QuickStackHere()
        {
            int total = 0;
            foreach (Card c in Usable) total += QuickStack.StackInto(c.Inv);
            Plugin.Show((total > 0 ? $"Quick stacked {total} item{(total == 1 ? "" : "s")}" : "Nothing of yours matches these chests") + LeftOut());
        }

        // ------------------------------------------------------------------ highlighting

        private static void UpdateSelectionInfo()
        {
            _summary.text = _summaryBase;
            if (_selected == null)
            {
                _selIcon.enabled = false;
                _selName.text = "No item chosen";
                _selInfo.text = "Right-click any item to see where it is and to gather it into one chest.";
                return;
            }
            ItemDrop.ItemData sample = FindSample(_selected);
            _selIcon.sprite = sample?.GetIcon();
            _selIcon.enabled = sample != null;
            _selName.text = Localization.instance.Localize(_selected);
            int inChests = Ready.Sum(c => c.Inv.CountItems(_selected));
            int chests = Ready.Count(c => c.Inv.CountItems(_selected) > 0);
            Inventory backpack = AdventureBackpacksCompat.Backpack(Player.m_localPlayer);
            int onMe = Player.m_localPlayer.GetInventory().CountItems(_selected) + (backpack?.CountItems(_selected) ?? 0);
            _selInfo.text = $"{inChests} in {chests} chest{(chests == 1 ? "" : "s")}, {onMe} on you.\nClick <color=#ffa13c>Gather here</color> on a chest to bring them together.";
            if (!Plugin.StorageShowInventory.Value)
            {
                _summary.text = $"{_summaryBase}    <color=#ffa13c>{_selName.text}</color>: {inChests} in {chests} chest{(chests == 1 ? "" : "s")}, {onMe} on you";
            }
        }

        private static ItemDrop.ItemData FindSample(string name)
        {
            foreach (ItemDrop.ItemData i in Player.m_localPlayer.GetInventory().GetAllItems()) if (i.m_shared.m_name == name) return i;
            Inventory backpack = AdventureBackpacksCompat.Backpack(Player.m_localPlayer);
            if (backpack != null)
                foreach (ItemDrop.ItemData i in backpack.GetAllItems())
                    if (i.m_shared.m_name == name) return i;
            foreach (Card c in Ready)
                foreach (ItemDrop.ItemData i in c.Inv.GetAllItems())
                    if (i.m_shared.m_name == name) return i;
            return null;
        }

        private static void Highlight(InventoryGrid grid, Inventory inv)
        {
            string search = _search.text.Trim().ToLowerInvariant();
            bool searching = search.Length > 0;
            foreach (InventoryElement element in grid.m_elements)
            {
                if (element == null) continue;
                ItemDrop.ItemData item = inv.GetItemAt(element.Position.x, element.Position.y);
                bool match = item != null && (item.m_shared.m_name == _selected ||
                             (searching && Localization.instance.Localize(item.m_shared.m_name).ToLowerInvariant().Contains(search)));
                if (item != null && searching && !match && item != _heldItem)
                {
                    element.m_icon.color = new Color(1, 1, 1, 0.25f);
                }
                Transform hl = element.transform.Find(HighlightName);
                if (hl == null)
                {
                    if (!match) continue;
                    RectTransform rt = UIKit.Rect(element.transform, HighlightName).Fill();
                    // First child: drawn over the slot's background but under the item, so the item keeps its colors.
                    rt.SetSiblingIndex(0);
                    Image img = rt.gameObject.AddComponent<Image>();
                    img.color = new Color(1f, 0.631f, 0.235f, HighlightAlpha);
                    img.raycastTarget = false;
                    hl = rt;
                }
                hl.gameObject.SetActive(match);
            }
        }

        // ------------------------------------------------------------------ input blocking

        [HarmonyPatch]
        private static class Patches
        {
            // Pretending the inventory is open frees the mouse and stops the player from moving or attacking.
            [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.IsVisible)), HarmonyPostfix]
            private static void InventoryVisible(ref bool __result)
            {
                if (BlocksInput || ScrollWindow.AnyBlocksInput) __result = true;
            }

            // Other mods (SeneaL UI) blank the game's item tooltips on inventory grids they don't draw themselves, so our
            // slots would show none. Fill it in the way the game does, right before it decides whether there's a tooltip to show.
            [HarmonyPatch(typeof(UITooltip), nameof(UITooltip.OnHoverStart)), HarmonyPrefix]
            private static void FillItemTooltip(UITooltip __instance)
            {
                if (!IsOpen) return;
                InventoryElement element = __instance.GetComponentInParent<InventoryElement>();
                InventoryGrid grid = element != null ? element.GetComponentInParent<InventoryGrid>() : null;
                if (grid == null || grid.m_inventory == null || (grid != _playerGrid && grid != _backpackGrid && !_cards.Any(c => c.Grid == grid))) return;
                ItemDrop.ItemData item = grid.m_inventory.GetItemAt(element.Position.x, element.Position.y);
                if (item == null) return;
                __instance.m_topic = item.m_shared.m_name;
                __instance.m_text = item.GetTooltip();
                __instance.m_anchor = grid.m_tooltipAnchor;
            }

            [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel)), HarmonyPrefix]
            private static bool BlockScroll(ref float __result)
            {
                if (!BlocksInput && !ScrollWindow.AnyBlocksInput) return true;
                __result = 0f;
                return false;
            }

            // Typing a search or a chest name must not open the map, chat and so on.
            [HarmonyPatch]
            private static class BlockKeys
            {
                private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
                {
                    var names = new HashSet<string> { "GetButtonDown", "GetButtonUp", "GetButton", "GetKey", "GetKeyDown", "GetKeyUp", "GetMouseButton", "GetMouseButtonDown", "GetMouseButtonUp" };
                    foreach (System.Reflection.MethodInfo m in AccessTools.GetDeclaredMethods(typeof(ZInput)))
                    {
                        if (names.Contains(m.Name) && m.IsStatic && m.ReturnType == typeof(bool)) yield return m;
                    }
                }

                private static bool Prefix(ref bool __result)
                {
                    if (!BlocksInput && !ScrollWindow.AnyBlocksInput && !ChestRename.BlocksInput) return true;
                    __result = false;
                    return false;
                }
            }
        }
    }
}
