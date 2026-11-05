using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VillageWorkers.UI
{
    /// <summary>Line and bar charts drawn with plain uGUI images.</summary>
    public static class Charts
    {
        // Categorical palette, validated (CVD + contrast) against the dark wood panel surface #2b211a.
        // Assigned in fixed order to villagers by hire order, so a villager keeps its colour everywhere.
        public static readonly Color[] Series =
        {
            Hex("3987e5"), Hex("d95926"), Hex("199e70"), Hex("c98500"),
            Hex("d55181"), Hex("008300"), Hex("9085e9"), Hex("e66767")
        };

        public static readonly Color Grid = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Axis = new Color(1f, 1f, 1f, 0.25f);
        public static readonly Color BarColor = Hex("3987e5");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }

        public class Line
        {
            public string Name;
            public Color Color;
            public float[] Values;
        }

        public class Bar
        {
            public string Label;
            public float Value;
            public Sprite Icon;
            public Color Color;
            public string Tip;
        }

        private static float NiceMax(float max)
        {
            if (max <= 0) return 5;
            float exp = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(max)));
            foreach (float m in new[] { 1f, 2f, 2.5f, 5f, 10f })
                if (m * exp >= max) return m * exp;
            return 10 * exp;
        }

        private static RectTransform Seg(Transform parent, Vector2 a, Vector2 b, float thickness, Color color)
        {
            RectTransform rt = UIKit.Rect(parent, "seg");
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            Vector2 d = b - a;
            rt.sizeDelta = new Vector2(d.magnitude, thickness);
            rt.anchoredPosition = (a + b) / 2f;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            return rt;
        }

        /// <summary>Multi-series line chart with gridlines, a crosshair-style hover readout and a legend.</summary>
        public static void LineChart(RectTransform area, string title, List<Line> lines, string[] xLabels)
        {
            UIKit.Clear(area);
            float w = area.rect.width, h = area.rect.height;
            if (w < 10 || h < 10) { w = 560; h = 240; }
            const float left = 44, bottom = 26, top = 30, right = 12;
            float pw = w - left - right, ph = h - top - bottom - 22;

            TextMeshProUGUI head = UIKit.Text(area, title, 18, UIKit.Orange);
            head.rectTransform.TopLeft(0, 0, w, 24);
            TextMeshProUGUI readout = UIKit.Text(area, "", 14, UIKit.Grey, TextAlignmentOptions.MidlineRight);
            readout.rectTransform.TopLeft(w * 0.35f, 0, w * 0.65f, 24);

            RectTransform plot = UIKit.Rect(area, "plot");
            plot.anchorMin = plot.anchorMax = new Vector2(0, 1);
            plot.pivot = Vector2.zero;
            plot.sizeDelta = new Vector2(pw, ph);
            plot.anchoredPosition = new Vector2(left, -top - ph);

            int n = xLabels.Length;
            float max = NiceMax(lines.Count == 0 ? 0 : lines.Max(l => l.Values.Length == 0 ? 0 : l.Values.Max()));

            for (int g = 0; g <= 4; g++)
            {
                float y = ph * g / 4f;
                Seg(plot, new Vector2(0, y), new Vector2(pw, y), 1, g == 0 ? Axis : Grid);
                TextMeshProUGUI t = UIKit.Text(plot, (max * g / 4f).ToString("0"), 12, UIKit.Grey, TextAlignmentOptions.MidlineRight);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
                t.rectTransform.pivot = new Vector2(1, 0.5f);
                t.rectTransform.sizeDelta = new Vector2(40, 16);
                t.rectTransform.anchoredPosition = new Vector2(-4, y);
            }

            float X(int i) => n <= 1 ? pw / 2 : pw * i / (n - 1);
            int step = Mathf.Max(1, Mathf.CeilToInt(n / 7f));
            for (int i = 0; i < n; i += step)
            {
                TextMeshProUGUI t = UIKit.Text(plot, xLabels[i], 12, UIKit.Grey, TextAlignmentOptions.Center);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
                t.rectTransform.sizeDelta = new Vector2(50, 16);
                t.rectTransform.anchoredPosition = new Vector2(X(i), -12);
            }

            if (lines.Count == 0 || max <= 0)
            {
                TextMeshProUGUI empty = UIKit.Text(plot, "No production recorded yet", 16, UIKit.Grey, TextAlignmentOptions.Center);
                empty.rectTransform.Fill();
            }

            foreach (Line l in lines)
            {
                Vector2 prev = Vector2.zero;
                for (int i = 0; i < l.Values.Length && i < n; i++)
                {
                    Vector2 p = new Vector2(X(i), ph * Mathf.Clamp01(l.Values[i] / max));
                    if (i > 0) Seg(plot, prev, p, 2, l.Color);
                    prev = p;
                }
                // Direct label at the line end when there are few series.
                if (lines.Count <= 4 && l.Values.Length > 0)
                {
                    TextMeshProUGUI lbl = UIKit.Text(plot, l.Name, 12, UIKit.Parchment, TextAlignmentOptions.MidlineRight);
                    lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = Vector2.zero;
                    lbl.rectTransform.pivot = new Vector2(1, 0);
                    lbl.rectTransform.sizeDelta = new Vector2(120, 16);
                    lbl.rectTransform.anchoredPosition = new Vector2(pw - 2, prev.y + 3);
                }
            }

            // Hover columns: a vertical crosshair plus a readout of every series at that day.
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                float colW = n <= 1 ? pw : pw / (n - 1);
                RectTransform hit = UIKit.Rect(plot, "hover");
                hit.anchorMin = hit.anchorMax = Vector2.zero;
                hit.pivot = new Vector2(0.5f, 0);
                hit.sizeDelta = new Vector2(colW, ph);
                hit.anchoredPosition = new Vector2(X(i), 0);
                Image hitImg = hit.gameObject.AddComponent<Image>();
                hitImg.color = new Color(1, 1, 1, 0f);
                RectTransform cross = Seg(hit, new Vector2(colW / 2, 0), new Vector2(colW / 2, ph), 1, new Color(1, 1, 1, 0.35f));
                cross.gameObject.SetActive(false);
                List<GameObject> dots = new List<GameObject>();
                foreach (Line l in lines)
                {
                    if (idx >= l.Values.Length) continue;
                    RectTransform dot = UIKit.Rect(hit, "dot");
                    Image di = dot.gameObject.AddComponent<Image>();
                    di.color = l.Color;
                    di.raycastTarget = false;
                    dot.anchorMin = dot.anchorMax = Vector2.zero;
                    dot.sizeDelta = new Vector2(8, 8);
                    dot.anchoredPosition = new Vector2(colW / 2, ph * Mathf.Clamp01(l.Values[idx] / max));
                    dot.gameObject.SetActive(false);
                    dots.Add(dot.gameObject);
                }
                string text = xLabels[idx] + ":  " + string.Join("   ", lines.Where(l => idx < l.Values.Length)
                    .OrderByDescending(l => l.Values[idx]).Take(5).Select(l => $"{l.Name} {l.Values[idx]:0}"));
                Hover hv = hit.gameObject.AddComponent<Hover>();
                hv.Enter = () =>
                {
                    readout.text = text;
                    cross.gameObject.SetActive(true);
                    foreach (GameObject d in dots) d.SetActive(true);
                };
                hv.Exit = () =>
                {
                    readout.text = "";
                    cross.gameObject.SetActive(false);
                    foreach (GameObject d in dots) d.SetActive(false);
                };
            }

            // Legend
            if (lines.Count >= 2)
            {
                HorizontalLayoutGroup legend = UIKit.HRow(area, "legend", 14);
                RectTransform lrt = (RectTransform)legend.transform;
                lrt.anchorMin = new Vector2(0, 0);
                lrt.anchorMax = new Vector2(1, 0);
                lrt.pivot = new Vector2(0, 0);
                lrt.offsetMin = new Vector2(left, 0);
                lrt.offsetMax = new Vector2(0, 20);
                foreach (Line l in lines)
                {
                    HorizontalLayoutGroup item = UIKit.HRow(legend.transform, "item", 4);
                    UIKit.Solid(item.transform, l.Color).Size(12, 4);
                    UIKit.Text(item.transform, l.Name, 13).Size(-1, 18);
                }
            }
        }

        /// <summary>Horizontal bars, largest first, with value labels and hover tooltips.</summary>
        public static void BarChart(RectTransform area, string title, List<Bar> bars, int maxBars = 8)
        {
            UIKit.Clear(area);
            float w = area.rect.width, h = area.rect.height;
            if (w < 10 || h < 10) { w = 560; h = 240; }
            TextMeshProUGUI head = UIKit.Text(area, title, 18, UIKit.Orange);
            head.rectTransform.TopLeft(0, 0, w, 24);

            List<Bar> shown = bars.OrderByDescending(b => b.Value).Take(maxBars).ToList();
            if (bars.Count > maxBars)
            {
                float rest = bars.OrderByDescending(b => b.Value).Skip(maxBars).Sum(b => b.Value);
                shown.Add(new Bar { Label = "Other", Value = rest, Color = UIKit.Grey });
            }
            if (shown.Count == 0)
            {
                TextMeshProUGUI empty = UIKit.Text(area, "Nothing yet", 16, UIKit.Grey, TextAlignmentOptions.Center);
                empty.rectTransform.TopLeft(0, 30, w, h - 30);
                return;
            }

            float max = Mathf.Max(1, shown.Max(b => b.Value));
            float rowH = Mathf.Min(26, (h - 32) / shown.Count);
            float labelW = 150, valueW = 60, iconW = shown.Any(b => b.Icon != null) ? rowH : 0;
            float barMax = w - labelW - valueW - iconW - 12;
            for (int i = 0; i < shown.Count; i++)
            {
                Bar b = shown[i];
                float y = 30 + i * rowH;
                RectTransform row = UIKit.Rect(area, "row").TopLeft(0, y, w, rowH - 2);
                Image hitImg = row.gameObject.AddComponent<Image>();
                hitImg.color = new Color(1, 1, 1, 0f);
                float x = 0;
                if (iconW > 0)
                {
                    Image ic = UIKit.Icon(row, b.Icon, rowH - 4);
                    ((RectTransform)ic.transform.parent).TopLeft(0, 1, rowH - 4, rowH - 4);
                    x += iconW;
                }
                UIKit.Text(row, b.Label, 14).rectTransform.TopLeft(x, 0, labelW - 4, rowH - 2);
                x += labelW;
                float bw = Mathf.Max(2, barMax * b.Value / max);
                Image bar = UIKit.Solid(row, b.Color == default ? BarColor : b.Color, "bar");
                bar.rectTransform.TopLeft(x, 3, bw, rowH - 8);
                UIKit.Text(row, b.Value.ToString("0"), 14, UIKit.Parchment).rectTransform.TopLeft(x + bw + 6, 0, valueW, rowH - 2);
                if (!string.IsNullOrEmpty(b.Tip)) UIKit.Tooltip(row.gameObject, b.Tip);
            }
        }

        /// <summary>Forwards pointer enter/exit to callbacks.</summary>
        public class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action Enter, Exit;
            public void OnPointerEnter(PointerEventData e) => Enter?.Invoke();
            public void OnPointerExit(PointerEventData e) => Exit?.Invoke();
        }
    }
}
