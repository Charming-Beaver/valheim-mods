using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StorageMaster.UI
{
    /// <summary>
    /// A window with a title, a Close button, a scrolling list and a footer line, in front of everything (the Info and
    /// Options windows). Only one is open at a time, and none together with the Storage window or the inventory.
    /// </summary>
    public class ScrollWindow
    {
        private const float W = 940f, H = 820f;

        private static readonly List<ScrollWindow> All = new List<ScrollWindow>();

        private readonly string _name, _title, _subtitle;
        private readonly Action<RectTransform> _fill;
        private int _swallowUntilFrame = -10;

        public GameObject Root;
        public TextMeshProUGUI Footer;
        /// <summary>Called whenever the window opens, after it's shown.</summary>
        public Action Opened;
        /// <summary>Called whenever the window closes.</summary>
        public Action Closed;

        public ScrollWindow(string name, string title, string subtitle, Action<RectTransform> fill)
        {
            _name = name;
            _title = title;
            _subtitle = subtitle;
            _fill = fill;
            All.Add(this);
        }

        public bool IsOpen => Root != null && Root.activeSelf;
        /// <summary>Open, or closed this frame or the last (so the key or click that closed it doesn't reach the game).</summary>
        public bool BlocksInput => IsOpen || Time.frameCount <= _swallowUntilFrame;

        public static bool AnyOpen
        {
            get
            {
                foreach (ScrollWindow w in All) if (w.IsOpen) return true;
                return false;
            }
        }

        public static bool AnyBlocksInput
        {
            get
            {
                if (KeyCapture.Active) return true;
                foreach (ScrollWindow w in All) if (w.BlocksInput) return true;
                return false;
            }
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (Player.m_localPlayer == null || !UIKit.Init()) return;
            if (Root == null) Build();
            StorageWindow.Close();
            foreach (ScrollWindow other in All) if (other != this) other.Close();
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            Root.SetActive(true);
            Root.transform.SetAsLastSibling();
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            Root.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            _swallowUntilFrame = Time.frameCount + 1;
            Closed?.Invoke();
        }

        /// <summary>Throws the window away, so it's built again on the next Open (used by the tests).</summary>
        public void Destroy()
        {
            Close();
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
        }

        public void Update()
        {
            if (!IsOpen) return;
            if (Player.m_localPlayer == null || (Input.GetKeyDown(KeyCode.Escape) && KeyCapture.EscapeUsedFrame != Time.frameCount))
            {
                Close();
            }
        }

        private void Build()
        {
            Root = UIKit.Rect(UIKit.CanvasRoot, _name).Fill().gameObject;
            Root.AddComponent<Image>().color = new Color(0, 0, 0, 0.35f);

            RectTransform window = UIKit.DarkPanel(Root.transform, "window").rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(W, H);

            UIKit.Text(window, _title, 40, UIKit.Orange, TextAlignmentOptions.Center, true).rectTransform.TopLeft(0, 12, W, 50);
            Button close = UIKit.Button(window, "Close", Close, 110, 36);
            ((RectTransform)close.transform).TopLeft(W - 134, 22, 110, 36);
            UIKit.Text(window, _subtitle, 16, UIKit.Grey, TextAlignmentOptions.Center).rectTransform.TopLeft(0, 64, W, 26);

            RectTransform inset = UIKit.Inset(window, "list").rectTransform.Anchor(0, 0, 1, 1, 24, 70, 24, 98);
            RectTransform content = UIKit.ScrollList(inset, "scroll", out _, 4, 14);
            ((RectTransform)content.parent.parent).Fill(4);

            Footer = UIKit.Text(window, "", 16, UIKit.Grey, TextAlignmentOptions.Center).Wrap();
            RectTransform frt = Footer.rectTransform;
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(1, 0);
            frt.pivot = new Vector2(0.5f, 0);
            frt.offsetMin = new Vector2(24, 14);
            frt.offsetMax = new Vector2(-24, 60);

            _fill(content);
            Root.SetActive(false);
        }

        // ------------------------------------------------------------------ rows

        public static void Section(Transform parent, string title)
        {
            UIKit.Spacer(parent, -1, 6, 0);
            UIKit.Text(parent, title, 26, UIKit.Orange, norse: true).Size(-1, 34);
            UIKit.Solid(parent, new Color(1f, 0.631f, 0.235f, 0.35f), "rule").Size(-1, 2);
        }

        public static TextMeshProUGUI Para(Transform parent, string text)
        {
            TextMeshProUGUI t = UIKit.Text(parent, text, 16).Wrap();
            t.Size(-1, -1);
            return t;
        }

        /// <summary>A title with a grey description under it, taking the row's free width.</summary>
        public static void Labels(Transform parent, string title, string description)
        {
            VerticalLayoutGroup text = UIKit.VCol(parent, "text", 1);
            text.childAlignment = TextAnchor.MiddleLeft;
            text.Size(-1, -1, 1);
            UIKit.Text(text.transform, title, 19).Size(-1, 22);
            UIKit.Text(text.transform, description, 15, UIKit.Grey).Size(-1, 18);
        }
    }
}
