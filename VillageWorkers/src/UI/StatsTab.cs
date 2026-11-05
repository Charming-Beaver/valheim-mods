using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VillageWorkers.UI
{
    public static partial class VillageWindow
    {
        private enum StatsMode { ByVillager, ByItem, ByJob }

        private static StatsMode _statsMode = StatsMode.ByVillager;
        private static ZDOID _statsFocus = ZDOID.None;   // a villager picked in the ranking
        private const int ChartDays = 14;

        private static void BuildStatsTab()
        {
            float bw = W - 40, bh = H - 86;
            int today = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
            List<WorkerEntry> villagers = _entries.Where(e => e.State == WorkerState.Villager || e.Stats.E.Count > 0).ToList();
            WorkerEntry focus = villagers.FirstOrDefault(v => v.Id == _statsFocus);

            // ---- summary tiles
            int totalToday = villagers.Sum(v => v.Stats.TotalOn(today));
            int totalAll = villagers.Sum(v => v.Stats.Total());
            WorkerEntry best = villagers.OrderByDescending(v => v.Stats.TotalOn(today)).FirstOrDefault();
            WorkerEntry bestAll = villagers.OrderByDescending(v => PerDay(v, today)).FirstOrDefault();
            string[,] tiles =
            {
                { villagers.Count.ToString(), "Villagers" },
                { totalToday.ToString(), "Produced today" },
                { totalAll.ToString(), "Produced in total" },
                { best != null && best.Stats.TotalOn(today) > 0 ? best.Name : "-", "Hardest worker today" },
                { bestAll != null && PerDay(bestAll, today) > 0 ? bestAll.Name : "-", "Best average per day" },
            };
            float tw = (bw - 4 * 10) / 5f;
            for (int i = 0; i < 5; i++)
            {
                Image tile = UIKit.Inset(_body, "tile");
                tile.rectTransform.TopLeft(i * (tw + 10), 0, tw, 70);
                UIKit.Text(tile.transform, tiles[i, 0], 30, UIKit.Orange, TextAlignmentOptions.Center, true).rectTransform.TopLeft(0, 4, tw, 40);
                UIKit.Text(tile.transform, tiles[i, 1], 15, UIKit.Grey, TextAlignmentOptions.Center).rectTransform.TopLeft(0, 44, tw, 22);
            }

            // ---- ranking table (left)
            float lw = 470, top = 82;
            UIKit.Text(_body, "Ranking", 22, UIKit.Orange, TextAlignmentOptions.MidlineLeft, true).rectTransform.TopLeft(0, top, 200, 30);
            UIKit.Text(_body, "click a villager to see their details", 14, UIKit.Grey, TextAlignmentOptions.MidlineRight).rectTransform.TopLeft(200, top, lw - 200, 30);
            Image tableBg = UIKit.Inset(_body, "table");
            tableBg.rectTransform.TopLeft(0, top + 32, lw, 270);
            float[] cols = { 26, 150, 62, 62, 70, 72 };
            string[] heads = { "#", "Villager", "Today", "7 days", "Total", "Per day" };
            float cx = 8;
            for (int i = 0; i < cols.Length; i++)
            {
                UIKit.Text(tableBg.transform, heads[i], 15, UIKit.Orange, i <= 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight)
                    .rectTransform.TopLeft(cx, 4, cols[i], 24);
                cx += cols[i] + 4;
            }
            RectTransform rows = UIKit.ScrollList(tableBg.transform, "rows", out _, 2, 2);
            ((RectTransform)rows.parent.parent).Anchor(0, 0, 1, 1, 2, 2, 2, 30);

            List<WorkerEntry> ranked = villagers.OrderByDescending(v => v.Stats.E.Where(x => x.D > today - 7).Sum(x => x.N)).ThenByDescending(v => v.Stats.Total()).ToList();
            if (ranked.Count == 0)
                UIKit.Text(rows, "No villagers yet.", 15, UIKit.Grey).Size(-1, 26);
            for (int i = 0; i < ranked.Count; i++)
            {
                WorkerEntry v = ranked[i];
                bool sel = v.Id == _statsFocus;
                RectTransform row = UIKit.Rect(rows, "rank");
                row.Size(-1, 28);
                Image bg = row.gameObject.AddComponent<Image>();
                bg.color = sel ? new Color(1f, 0.63f, 0.24f, 0.25f) : new Color(0, 0, 0, i % 2 == 0 ? 0.18f : 0.05f);
                Button b = row.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.onClick.AddListener(() =>
                {
                    _statsFocus = _statsFocus == v.Id ? ZDOID.None : v.Id;
                    if (_statsFocus != ZDOID.None && _statsMode == StatsMode.ByVillager) _statsMode = StatsMode.ByItem;
                    _signature = null;
                    Refresh(true);
                });
                string[] vals =
                {
                    (i + 1).ToString(), v.Name, v.Stats.TotalOn(today).ToString(),
                    v.Stats.E.Where(x => x.D > today - 7).Sum(x => x.N).ToString(), v.Stats.Total().ToString(), PerDay(v, today).ToString("0.0")
                };
                cx = 6;
                for (int c = 0; c < cols.Length; c++)
                {
                    if (c == 1) UIKit.Solid(row, v.Color, "swatch").rectTransform.TopLeft(cx, 9, 10, 10);
                    float off = c == 1 ? 14 : 0;
                    UIKit.Text(row, vals[c], 15, c == 1 && sel ? UIKit.Orange : UIKit.Parchment, c <= 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight)
                        .rectTransform.TopLeft(cx + off, 0, cols[c] - off, 28);
                    cx += cols[c] + 4;
                }
            }

            // ---- activity log (left, below)
            float ly = top + 32 + 270 + 10;
            UIKit.Text(_body, focus != null ? $"What {focus.Name} did" : "Recent activity", 22, UIKit.Orange, TextAlignmentOptions.MidlineLeft, true)
                .rectTransform.TopLeft(0, ly, lw, 30);
            Image logBg = UIKit.Inset(_body, "log");
            logBg.rectTransform.TopLeft(0, ly + 32, lw, bh - ly - 32);
            RectTransform log = UIKit.ScrollList(logBg.transform, "logList", out _, 1, 6);
            ((RectTransform)log.parent.parent).Fill(2);
            var events = (focus != null ? new[] { focus } : villagers.ToArray())
                .SelectMany(v => v.Stats.L.Select(l => new { v, l }))
                .OrderByDescending(x => x.l.T).Take(80).ToList();
            if (events.Count == 0) UIKit.Text(log, "Nothing has happened yet.", 15, UIKit.Grey).Size(-1, 24);
            foreach (var ev in events)
            {
                TextMeshProUGUI t = UIKit.Text(log, $"<color=#a8a090>{Village.FormatTime(ev.l.T)}</color>  <color=#ffa13c>{ev.v.Name}</color> {ev.l.M}", 14).Wrap();
                t.Size(-1, 20);
            }

            // ---- charts (right)
            float rx = lw + 16, rw = bw - rx;
            RectTransform line = UIKit.Rect(_body, "lineChart").TopLeft(rx, top, rw, 290);
            int[] days = Enumerable.Range(today - ChartDays + 1, ChartDays).ToArray();
            string[] labels = days.Select(d => "Day " + d).ToArray();
            List<Charts.Line> lines = new List<Charts.Line>();
            IEnumerable<WorkerEntry> lineSet = focus != null ? new[] { focus } : villagers.OrderByDescending(v => v.Stats.Total()).Take(Charts.Series.Length);
            foreach (WorkerEntry v in lineSet.OrderBy(v => v.ColorIndex))
                lines.Add(new Charts.Line { Name = v.Name, Color = v.Color, Values = days.Select(d => (float)v.Stats.TotalOn(d)).ToArray() });
            Canvas.ForceUpdateCanvases();
            Charts.LineChart(line, focus != null ? $"{focus.Name}'s daily output" : "Daily output per villager", lines, labels);

            float by = top + 300;
            HorizontalLayoutGroup modes = UIKit.HRow(_body, "modes", 4);
            ((RectTransform)modes.transform).TopLeft(rx + rw - 3 * 134, by, 3 * 134, 30);
            void Mode(string label, StatsMode m, bool enabled)
            {
                Button b = UIKit.Button(modes.transform, label, () =>
                {
                    _statsMode = m;
                    _signature = null;
                    Refresh(true);
                }, 130, 30, tab: true);
                b.interactable = _statsMode != m && enabled;
            }
            Mode("Per villager", StatsMode.ByVillager, focus == null);
            Mode("Per item", StatsMode.ByItem, true);
            Mode("Per task", StatsMode.ByJob, true);

            RectTransform barArea = UIKit.Rect(_body, "barChart").TopLeft(rx, by, rw, bh - by);
            List<Charts.Bar> bars = new List<Charts.Bar>();
            IEnumerable<StatEntry> pool = (focus != null ? new[] { focus } : villagers.ToArray()).SelectMany(v => v.Stats.E);
            string scope = focus != null ? focus.Name : "the village";
            string title;
            switch (focus != null && _statsMode == StatsMode.ByVillager ? StatsMode.ByItem : _statsMode)
            {
                case StatsMode.ByVillager:
                    title = "Total output per villager";
                    foreach (WorkerEntry v in villagers)
                        bars.Add(new Charts.Bar
                        {
                            Label = v.Name, Value = v.Stats.Total(), Color = v.Color,
                            Tip = string.Join("\n", Enum.GetValues(typeof(JobType)).Cast<JobType>()
                                .Where(j => v.Stats.TotalFor(j) > 0).Select(j => $"{JobInfo.Title(j)}: {v.Stats.TotalFor(j)}"))
                        });
                    break;
                case StatsMode.ByItem:
                    title = $"What {scope} produced";
                    foreach (var g in pool.GroupBy(x => x.I))
                        bars.Add(new Charts.Bar { Label = Village.ItemLabel(g.Key), Value = g.Sum(x => x.N), Icon = Village.ItemIcon(g.Key), Color = Charts.BarColor });
                    break;
                default:
                    title = $"Output of {scope} per task";
                    foreach (var g in pool.GroupBy(x => x.J))
                        bars.Add(new Charts.Bar { Label = JobInfo.Title((JobType)g.Key), Value = g.Sum(x => x.N), Color = Charts.BarColor });
                    break;
            }
            Charts.BarChart(barArea, title, bars, 9);
        }

        private static float PerDay(WorkerEntry v, int today)
        {
            int hired = v.Stats.Hired < 0 ? today : v.Stats.Hired;
            return v.Stats.Total() / (float)Mathf.Max(1, today - hired + 1);
        }
    }
}
