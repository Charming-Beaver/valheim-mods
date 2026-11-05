using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VillageWorkers.Jobs;

namespace VillageWorkers.UI
{
    /// <summary>The village management window: villagers & their tasks, and statistics.</summary>
    public static partial class VillageWindow
    {
        private const float W = 1180f, H = 730f;

        private static GameObject _root;
        private static RectTransform _window;
        private static RectTransform _body;
        private static Button _tabWorkers, _tabStats;
        private static int _tab;
        private static ZDOID _selected = ZDOID.None;
        private static List<WorkerEntry> _entries = new List<WorkerEntry>();
        private static float _nextRefresh;
        private static string _signature;
        private static int _closedFrame = -10;
        private static bool _hiddenForText;
        private static Action _liveUpdate;

        public static bool IsOpen => _root != null && _root.activeSelf;
        public static bool BlocksInput => IsOpen || _hiddenForText;
        public static bool SwallowEscape => Time.frameCount <= _closedFrame + 1;

        public static bool IsManaging(WorkerAI w)
        {
            if (!(IsOpen || _hiddenForText) || _tab != 0 || w == null || w.NView == null || !w.NView.IsValid()) return false;
            return w.NView.GetZDO().m_uid == _selected;
        }

        public static void Open(WorkerAI w)
        {
            if (!UIKit.Init()) return;
            if (_root == null) Build();
            if (w != null && w.NView != null && w.NView.IsValid())
            {
                _selected = w.NView.GetZDO().m_uid;
                _tab = 0;
            }
            if (InventoryGui.instance != null && InventoryGui.instance.IsContainerOpen()) InventoryGui.instance.Hide();
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            _signature = null;
            Refresh(true);
        }

        public static void Close()
        {
            ClosePopup();
            if (_root != null) _root.SetActive(false);
            _hiddenForText = false;
            _closedFrame = Time.frameCount;
        }

        public static void GlobalUpdate()
        {
            if (Player.m_localPlayer == null)
            {
                if (IsOpen) Close();
                return;
            }

            // Keep out of the way of the rename dialog, then come back.
            if (_hiddenForText && !TextInput.IsVisible())
            {
                _hiddenForText = false;
                if (_root != null) _root.SetActive(true);
                _signature = null;
            }
            if (IsOpen && TextInput.IsVisible())
            {
                _root.SetActive(false);
                _hiddenForText = true;
                return;
            }

            bool typing = (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() || Menu.IsVisible();
            if (!typing && !WorldPicker.Active && Plugin.VillageKey.Value.IsDown())
            {
                if (IsOpen) Close();
                else Open(null);
                return;
            }
            if (!IsOpen) return;

            if (ZInput.GetKeyDown(KeyCode.Escape) && !WorldPicker.SwallowEscapeThisFrame)
            {
                if (_popup != null) ClosePopup();
                else Close();
                _closedFrame = Time.frameCount;
                return;
            }

            if (Time.unscaledTime > _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                Refresh(false);
            }
        }

        // ------------------------------------------------------------------ frame

        private static void Build()
        {
            _root = UIKit.Rect(UIKit.CanvasRoot, "VillageWorkersWindow").Fill().gameObject;
            Image blocker = _root.AddComponent<Image>();
            blocker.color = new Color(0, 0, 0, 0.35f);

            Image panel = UIKit.Panel(_root.transform, "window");
            _window = panel.rectTransform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(W, H);

            TextMeshProUGUI title = UIKit.Text(_window, "Village", 36, UIKit.Orange, TextAlignmentOptions.Center, true);
            title.rectTransform.TopLeft(0, 8, W, 46);

            _tabWorkers = UIKit.Button(_window, "Villagers", () => SwitchTab(0), 150, 36, tab: true);
            ((RectTransform)_tabWorkers.transform).TopLeft(24, 18, 150, 36);
            _tabStats = UIKit.Button(_window, "Statistics", () => SwitchTab(1), 150, 36, tab: true);
            ((RectTransform)_tabStats.transform).TopLeft(180, 18, 150, 36);

            Button close = UIKit.Button(_window, "Close", Close, 110, 36);
            ((RectTransform)close.transform).TopLeft(W - 134, 18, 110, 36);

            _body = UIKit.Rect(_window, "body").Anchor(0, 0, 1, 1, 20, 20, 20, 66);
            _root.SetActive(false);
        }

        private static void SwitchTab(int tab)
        {
            ClosePopup();
            _tab = tab;
            _signature = null;
            Refresh(true);
        }

        private static void Refresh(bool force)
        {
            _entries = Village.Collect(force);
            if (_tabWorkers != null)
            {
                _tabWorkers.interactable = _tab != 0;
                _tabStats.interactable = _tab != 1;
            }

            string sig = _tab + "|" + _selected + "|" + string.Join(",", _entries.Select(e => e.Id + ":" + (int)e.State + ":" + e.Name + ":" + e.Loaded));
            if (_tab == 0)
            {
                WorkerEntry sel = Selected();
                if (sel != null) sig += "|" + sel.CfgJson.GetHashCode();
            }
            else
            {
                sig += "|" + _statsMode + "|" + _statsFocus + "|" + (int)(Time.unscaledTime / 10f);
            }

            if (force || sig != _signature)
            {
                _signature = sig;
                _liveUpdate = null;
                UIKit.Clear(_body);
                if (_tab == 0) BuildWorkersTab();
                else BuildStatsTab();
            }
            _liveUpdate?.Invoke();
        }

        private static WorkerEntry Selected()
        {
            WorkerEntry e = _entries.FirstOrDefault(x => x.Id == _selected);
            if (e == null && _entries.Count > 0)
            {
                e = _entries[0];
                _selected = e.Id;
            }
            return e;
        }

        // ------------------------------------------------------------------ popups

        private static GameObject _popup;

        private static RectTransform Popup(string title, float w, float h)
        {
            ClosePopup();
            RectTransform dim = UIKit.Rect(_window, "popupDim").Fill();
            Image dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0, 0, 0, 0.55f);
            _popup = dim.gameObject;

            Image panel = UIKit.Panel(dim, "popup");
            RectTransform rt = panel.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            UIKit.Text(rt, title, 26, UIKit.Orange, TextAlignmentOptions.Center, true).rectTransform.TopLeft(0, 10, w, 36);
            Button close = UIKit.Button(rt, "Close", ClosePopup, 100, 32);
            ((RectTransform)close.transform).TopLeft(w - 120, 12, 100, 32);
            RectTransform content = UIKit.Rect(rt, "content").Anchor(0, 0, 1, 1, 20, 20, 20, 56);
            return content;
        }

        private static void ClosePopup()
        {
            if (_popup != null) UnityEngine.Object.Destroy(_popup);
            _popup = null;
        }

        // ------------------------------------------------------------------ hint (world picker)
    }

    /// <summary>Small instruction panel shown at the bottom of the screen while picking things in the world.</summary>
    public static class Hint
    {
        private static GameObject _go;
        private static TextMeshProUGUI _title, _text;

        public static void Show(string title, string text)
        {
            if (_go == null)
            {
                if (!UIKit.Init()) return;
                Image panel = UIKit.Panel(UIKit.CanvasRoot, "VillageWorkersHint");
                RectTransform rt = panel.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(760, 86);
                rt.anchoredPosition = new Vector2(0, 150);
                panel.raycastTarget = false;
                _title = UIKit.Text(rt, "", 22, UIKit.Orange, TextAlignmentOptions.Center, true);
                _title.rectTransform.TopLeft(0, 8, 760, 32);
                _text = UIKit.Text(rt, "", 17, UIKit.Parchment, TextAlignmentOptions.Center);
                _text.rectTransform.TopLeft(10, 44, 740, 30);
                _go = panel.gameObject;
            }
            _go.SetActive(true);
            _title.text = title;
            _text.text = text;
        }

        public static void Hide()
        {
            if (_go != null && _go.activeSelf) _go.SetActive(false);
        }
    }
}
