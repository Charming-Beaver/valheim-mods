using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VillageWorkers.Jobs;

namespace VillageWorkers.UI
{
    public static partial class VillageWindow
    {
        private static float _dismissArmed;

        private static void BuildWorkersTab()
        {
            float bw = W - 40, bh = H - 86;

            // ---- villager list
            Image listBg = UIKit.Inset(_body, "listBg");
            listBg.rectTransform.TopLeft(0, 0, 290, bh);
            RectTransform list = UIKit.ScrollList(listBg.transform, "list", out _, 4, 6);
            ((RectTransform)list.parent.parent).Fill(2);

            if (_entries.Count == 0)
            {
                TextMeshProUGUI t = UIKit.Text(list, "No villagers yet.\n\nMeet wanderers in the Meadows and Black Forest and press <color=yellow>E</color> to invite them.", 16, UIKit.Grey).Wrap();
                t.Size(-1, 140);
            }

            List<Action> live = new List<Action>();
            foreach (WorkerEntry e in _entries)
            {
                WorkerEntry entry = e;
                bool sel = e.Id == _selected;
                RectTransform row = UIKit.Rect(list, "worker");
                row.Size(-1, 58);
                Image bg = row.gameObject.AddComponent<Image>();
                bg.color = sel ? new Color(1f, 0.63f, 0.24f, 0.22f) : new Color(0, 0, 0, 0.25f);
                Button b = row.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.onClick.AddListener(() =>
                {
                    _selected = entry.Id;
                    ClosePopup();
                    Refresh(true);
                });
                UIKit.Solid(row, e.Color, "swatch").rectTransform.TopLeft(0, 0, 5, 58);
                UIKit.Text(row, e.Name, 20, sel ? UIKit.Orange : UIKit.Parchment).rectTransform.TopLeft(12, 4, 260, 26);
                TextMeshProUGUI sub = UIKit.Text(row, "", 14, UIKit.Grey);
                sub.rectTransform.TopLeft(12, 30, 260, 22);
                live.Add(() => sub.text = Subtitle(entry));
            }

            // ---- detail
            RectTransform detail = UIKit.Rect(_body, "detail").TopLeft(305, 0, bw - 305, bh);
            WorkerEntry s = Selected();
            if (s != null) BuildDetail(detail, s, live);

            _liveUpdate = () =>
            {
                foreach (Action a in live) a();
            };
        }

        private static string Subtitle(WorkerEntry e)
        {
            if (e.State == WorkerState.Recruit) return "<color=#e0c060>Recruit</color> - needs a bed";
            string job = e.Job;
            string status = e.Status;
            if (!e.Loaded) return "Far away";
            return string.IsNullOrEmpty(job) ? status : $"{job}: {status}";
        }

        private static void Send(WorkerEntry e, Action<WorkerConfig> change)
        {
            if (!e.Loaded)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"{e.Name} is too far away to receive orders.");
                return;
            }
            WorkerConfig cfg = e.Cfg.Clone();
            change(cfg);
            e.Ai.SendConfig(cfg);
            _signature = null;
            _nextRefresh = 0;
        }

        private static void BuildDetail(RectTransform detail, WorkerEntry e, List<Action> live)
        {
            float dw = detail.rect.width > 10 ? detail.rect.width : W - 40 - 305;
            WorkerConfig cfg = e.Cfg;
            bool loaded = e.Loaded;
            WorkerAI ai = e.Ai;

            // Header: name + actions
            HorizontalLayoutGroup head = UIKit.HRow(detail, "head", 8);
            ((RectTransform)head.transform).TopLeft(0, 0, dw, 44);
            UIKit.Text(head.transform, e.Name, 32, UIKit.Orange, TextAlignmentOptions.MidlineLeft, true).Size(-1, 44, 1);
            Button rename = UIKit.Button(head.transform, "Rename", () =>
            {
                if (loaded) TextInput.instance.RequestText(ai, "$hud_rename", 20);
            }, 110, 34);
            Button inv = UIKit.Button(head.transform, "Inventory", () =>
            {
                if (!loaded) return;
                Close();
                ai.Box.Interact(Player.m_localPlayer, false, false);
            }, 120, 34);
            UIKit.Tooltip(inv.gameObject, "Give the villager tools, weapons, armour and ammo, or take things from them.");
            bool following = cfg.FollowPlayer != 0;
            Button follow = UIKit.Button(head.transform, following ? "Stop following" : "Follow me", () =>
            {
                long me = Player.m_localPlayer.GetPlayerID();
                Send(e, c => c.FollowPlayer = c.FollowPlayer != 0 && e.State == WorkerState.Villager ? 0 : me);
            }, 150, 34);
            follow.interactable = loaded && e.State == WorkerState.Villager;
            Button dismiss = null;
            dismiss = UIKit.Button(head.transform, "Dismiss", () =>
            {
                if (!loaded) return;
                if (Time.unscaledTime > _dismissArmed)
                {
                    _dismissArmed = Time.unscaledTime + 4f;
                    dismiss.SetLabel("Really?");
                    return;
                }
                ai.SendDismiss();
                _selected = ZDOID.None;
                _signature = null;
                _nextRefresh = 0;
            }, 110, 34);
            rename.interactable = inv.interactable = dismiss.interactable = loaded;

            // Info block
            VerticalLayoutGroup info = UIKit.VCol(detail, "info", 2);
            ((RectTransform)info.transform).TopLeft(0, 50, dw, 160);

            TextMeshProUGUI status = UIKit.Field(info.transform, "Status", "");
            live.Add(() => status.text = !loaded ? "<color=#a0a0a0>Too far away - their area is not loaded</color>" : e.Status);

            TextMeshProUGUI health = UIKit.Field(info.transform, "Health", "");
            live.Add(() =>
            {
                if (!loaded) { health.text = "-"; return; }
                Character c = ai.Character;
                float hp = c.GetHealth(), max = c.GetMaxHealth();
                string col = hp < max * 0.35f ? "#ff6050" : "#e8e0cc";
                string skills = string.Join("   ", e.Stats.S.Where(kv => kv.Value >= 1f).OrderByDescending(kv => kv.Value).Take(3)
                    .Select(kv => $"{JobInfo.Title((JobType)Enum.Parse(typeof(JobType), kv.Key))} {kv.Value:0}"));
                health.text = $"<color={col}>{hp:0} / {max:0}</color>" + (skills.Length > 0 ? $"      <color=#ffa13c>Skills</color>  {skills}" : "");
            });

            // Bed
            HorizontalLayoutGroup bedRow = UIKit.HRow(info.transform, "bed", 6);
            bedRow.Size(-1, 32);
            UIKit.Text(bedRow.transform, "Bed", 16, UIKit.Orange).Size(110, 30);
            string bedText;
            if (cfg.Bed == null) bedText = "<color=#ff7060>None - assign a bed to make them a villager</color>";
            else
            {
                Bed bed = cfg.Bed.Resolve<Bed>();
                float dist = Player.m_localPlayer != null ? Vector3.Distance(Player.m_localPlayer.transform.position, cfg.Bed.Pos) : 0;
                bedText = bed != null || dist > 60 ? $"Assigned ({dist:0} m from you)" : "<color=#ff7060>The bed was destroyed</color>";
            }
            UIKit.Text(bedRow.transform, bedText, 16).Size(-1, 30, 1);
            Button bedBtn = UIKit.Button(bedRow.transform, "Assign bed", () => WorldPicker.Begin(ai, WorldPicker.Mode.Bed), 140, 30);
            bedBtn.interactable = loaded;

            // Combat
            HorizontalLayoutGroup combat = UIKit.HRow(info.transform, "combat", 6);
            combat.Size(-1, 32);
            UIKit.Text(combat.transform, "When attacked", 16, UIKit.Orange).Size(110, 30);
            Button fight = UIKit.Button(combat.transform, "Fight back", () => Send(e, c => c.Combat = CombatMode.FightBack), 130, 30, tab: true);
            Button flee = UIKit.Button(combat.transform, "Flee", () => Send(e, c => c.Combat = CombatMode.Flee), 130, 30, tab: true);
            fight.interactable = loaded && cfg.Combat != CombatMode.FightBack;
            flee.interactable = loaded && cfg.Combat != CombatMode.Flee;
            UIKit.Tooltip(fight.gameObject, "Fight with the best weapon in their inventory (or bare fists).");
            UIKit.Tooltip(flee.gameObject, "Run away from danger and come back to work later.");

            // Default chests
            HorizontalLayoutGroup chestRow = UIKit.HRow(info.transform, "chests", 6);
            chestRow.Size(-1, 32);
            UIKit.Text(chestRow.transform, "Chests", 16, UIKit.Orange).Size(110, 30);
            UIKit.Text(chestRow.transform, cfg.Chests.Count == 0
                ? "<color=#ff7060>None</color> - where tasks without their own chests store things and look for materials"
                : $"{cfg.Chests.Count} chest{(cfg.Chests.Count == 1 ? "" : "s")} for storing loot and taking materials", 16).Size(-1, 30, 1);
            Button addChest = UIKit.Button(chestRow.transform, "Choose chests", () => WorldPicker.Begin(ai, WorldPicker.Mode.Chests), 140, 30);
            Button clrChest = UIKit.Button(chestRow.transform, "Clear", () => Send(e, c => c.Chests.Clear()), 70, 30);
            addChest.interactable = loaded;
            clrChest.interactable = loaded && cfg.Chests.Count > 0;

            // Tasks
            float ty = 220;
            HorizontalLayoutGroup taskHead = UIKit.HRow(detail, "taskHead", 8);
            ((RectTransform)taskHead.transform).TopLeft(0, ty, dw, 34);
            UIKit.Text(taskHead.transform, "Tasks", 24, UIKit.Orange, TextAlignmentOptions.MidlineLeft, true).Size(80, 32);
            UIKit.Text(taskHead.transform, "Worked top to bottom - when a task can't go on (chest full, no tool, nothing left), the next one is done.", 14, UIKit.Grey).Wrap().Size(-1, 32, 1);
            Button add = UIKit.Button(taskHead.transform, "Add task", () => ShowAddTask(e), 130, 32);
            add.interactable = loaded && e.State == WorkerState.Villager;

            Image taskBg = UIKit.Inset(detail, "taskBg");
            taskBg.rectTransform.TopLeft(0, ty + 40, dw, (H - 86) - ty - 40);
            RectTransform tasks = UIKit.ScrollList(taskBg.transform, "tasks", out _, 6, 6);
            ((RectTransform)tasks.parent.parent).Fill(2);

            if (e.State == WorkerState.Recruit)
                UIKit.Text(tasks, $"{e.Name} follows you around until you <color=yellow>assign a bed</color>. Then they move into the village and you can give them tasks.", 17, UIKit.Parchment).Wrap().Size(-1, 60);
            else if (cfg.Tasks.Count == 0)
                UIKit.Text(tasks, "No tasks yet. Use <color=yellow>Add task</color> - for example Woodcutting with an axe in their inventory and a chest to store the wood.", 17, UIKit.Parchment).Wrap().Size(-1, 60);

            for (int i = 0; i < cfg.Tasks.Count; i++) BuildTaskRow(tasks, e, i, loaded);
        }

        private static string AreaText(TaskConfig t)
        {
            if (t.Area != null) return $"Area: {t.Area.R:0} m circle";
            bool station = t.Type == JobType.Crafting || t.Type == JobType.Smelting || t.Type == JobType.Cooking ||
                           t.Type == JobType.Brewing || t.Type == JobType.Fueling;
            return station ? $"Area: within {Plugin.StationRadius.Value:0} m of home" : $"Area: automatic (starts near home, searches up to {Plugin.MaxSearchRadius.Value:0} m)";
        }

        private static void BuildTaskRow(Transform parent, WorkerEntry e, int index, bool loaded)
        {
            WorkerConfig cfg = e.Cfg;
            TaskConfig t = cfg.Tasks[index];
            WorkerAI ai = e.Ai;

            RectTransform row = UIKit.Rect(parent, "task");
            row.Size(-1, 84);
            Image bg = row.gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, t.Enabled ? 0.3f : 0.15f);

            Toggle en = UIKit.Check(row, "", t.Enabled, v => Send(e, c => c.Tasks[index].Enabled = v), 30);
            ((RectTransform)en.transform).TopLeft(8, 8, 30, 26);
            en.interactable = loaded;

            float skill = e.Stats.Skill(t.Type);
            string head = $"{index + 1}. {JobInfo.Title(t.Type)}" + (skill >= 1 ? $"  <size=14><color=#b0a890>skill {skill:0}</color></size>" : "");
            UIKit.Text(row, head, 20, t.Enabled ? UIKit.Orange : UIKit.Grey).rectTransform.TopLeft(42, 4, 360, 28);

            List<string> parts = new List<string> { AreaText(t) };
            if (JobInfo.ProducesItems(t.Type) || JobInfo.UsesOrders(t.Type))
                parts.Add(t.Chests.Count > 0 ? $"Chests: {t.Chests.Count} own" : "Chests: villager's chests");
            if (t.Filter.Count > 0) parts.Add("Only: " + string.Join(", ", t.Filter.Take(4).Select(Village.ItemLabel)) + (t.Filter.Count > 4 ? "..." : ""));
            if (t.Orders.Count > 0) parts.Add("Orders: " + string.Join(", ", t.Orders.Take(3).Select(o => $"{Items.Nice(o.Item)} x{o.Keep}")) + (t.Orders.Count > 3 ? "..." : ""));
            // Description takes the space left of the two button lines (about 450 px wide).
            UIKit.Text(row, string.Join("\n", parts), 13, UIKit.Grey).Wrap().rectTransform.Anchor(0, 1, 1, 1, 42, -82, 460, 30);

            // Buttons, right aligned
            float x = 0;
            void Btn(string label, Action a, float w, bool enabled = true, string tip = null)
            {
                Button b = UIKit.Button(row, label, () => a(), w, 28);
                RectTransform r = (RectTransform)b.transform;
                r.anchorMin = r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(1, 1);
                r.anchoredPosition = new Vector2(-8 - x, -8);
                x += w + 4;
                b.interactable = loaded && enabled;
                UIKit.Tooltip(b.gameObject, tip);
            }

            Btn("X", () => Send(e, c => c.Tasks.RemoveAt(index)), 34, true, "Remove this task");
            Btn("▼", () => Send(e, c => Swap(c.Tasks, index, index + 1)), 34, index < cfg.Tasks.Count - 1, "Lower priority");
            Btn("▲", () => Send(e, c => Swap(c.Tasks, index, index - 1)), 34, index > 0, "Higher priority");
            bool hasOptions = FilterOptions.For(t.Type).Count > 0 || JobInfo.UsesOrders(t.Type);
            Btn("Options", () => ShowOptions(e, index), 90, hasOptions, "What exactly to work on");

            // Second line of buttons
            float x2 = 0;
            void Btn2(string label, Action a, float w, bool enabled = true, string tip = null)
            {
                Button b = UIKit.Button(row, label, () => a(), w, 28);
                RectTransform r = (RectTransform)b.transform;
                r.anchorMin = r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(1, 1);
                r.anchoredPosition = new Vector2(-8 - x2, -40);
                x2 += w + 4;
                b.interactable = loaded && enabled;
                UIKit.Tooltip(b.gameObject, tip);
            }

            if (JobInfo.ProducesItems(t.Type) || JobInfo.UsesOrders(t.Type))
            {
                Btn2("Clear chests", () => Send(e, c => c.Tasks[index].Chests.Clear()), 110, t.Chests.Count > 0, "Use the villager's chests again");
                Btn2("Own chests", () => WorldPicker.Begin(ai, WorldPicker.Mode.Chests, index), 110, true, "Chests only for this task. When they are full, the villager moves on to the next task.");
            }
            Btn2("Auto area", () => Send(e, c => c.Tasks[index].Area = null), 100, t.Area != null, "Search automatically around home");
            Btn2("Set area", () => WorldPicker.Begin(ai, WorldPicker.Mode.Area, index), 100, true, "Mark a circle in the world to work in");
        }

        private static void Swap<T>(List<T> list, int a, int b)
        {
            if (a < 0 || b < 0 || a >= list.Count || b >= list.Count) return;
            (list[a], list[b]) = (list[b], list[a]);
        }

        // ------------------------------------------------------------------ add task popup

        private static void ShowAddTask(WorkerEntry e)
        {
            RectTransform content = Popup("Add a task", 760, 560);
            GridLayoutGroup grid = UIKit.Rect(content, "grid").Fill().gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(350, 76);
            grid.spacing = new Vector2(12, 8);
            foreach (JobType job in Enum.GetValues(typeof(JobType)))
            {
                JobType j = job;
                RectTransform cell = UIKit.Rect(grid.transform, "job");
                Image bg = cell.gameObject.AddComponent<Image>();
                bg.color = new Color(0, 0, 0, 0.3f);
                Button b = cell.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                ColorBlock cb = b.colors;
                cb.highlightedColor = new Color(1f, 0.8f, 0.5f, 1f);
                b.colors = cb;
                b.onClick.AddListener(() =>
                {
                    ClosePopup();
                    Send(e, c => c.Tasks.Add(new TaskConfig { Type = j }));
                });
                UIKit.Text(cell, JobInfo.Title(j), 20, UIKit.Orange).rectTransform.TopLeft(10, 4, 330, 26);
                UIKit.Text(cell, JobInfo.Description(j), 14, UIKit.Parchment).Wrap().rectTransform.TopLeft(10, 30, 330, 42);
            }
        }

        // ------------------------------------------------------------------ options popup

        private static void ShowOptions(WorkerEntry e, int index)
        {
            TaskConfig t = e.Cfg.Tasks[index];
            RectTransform content = Popup(JobInfo.Title(t.Type) + " options", 900, 620);
            List<FilterOptions.Option> options = FilterOptions.For(t.Type);
            bool orders = JobInfo.UsesOrders(t.Type);
            float filterH = options.Count == 0 ? 0 : (orders ? 250 : 530);

            if (options.Count > 0)
            {
                string what = t.Type == JobType.Hunting ? "Hunt only these animals" :
                    t.Type == JobType.Smelting ? "Operate only these stations" :
                    t.Type == JobType.Cooking || t.Type == JobType.Brewing ? "Use only these ingredients" : "Only collect these";
                UIKit.Text(content, $"{what} <size=14><color=#a8a090>(nothing selected = everything{(t.Type == JobType.Hunting ? " in the default list" : "")})</color></size>", 18, UIKit.Orange)
                    .rectTransform.TopLeft(0, 0, 860, 26);
                Image fbg = UIKit.Inset(content, "filters");
                fbg.rectTransform.TopLeft(0, 30, 860, filterH - 34);
                RectTransform list = UIKit.ScrollList(fbg.transform, "flist", out _, 4, 6);
                ((RectTransform)list.parent.parent).Fill(2);
                VerticalLayoutGroup vl = list.GetComponent<VerticalLayoutGroup>();
                // Immediate: a deferred Destroy leaves the vertical layout in place, and Unity refuses a second layout group.
                UnityEngine.Object.DestroyImmediate(vl);
                GridLayoutGroup grid = list.gameObject.AddComponent<GridLayoutGroup>();
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 4;
                grid.cellSize = new Vector2(200, 34);
                grid.spacing = new Vector2(6, 4);
                grid.padding = new RectOffset(6, 6, 6, 6);
                foreach (FilterOptions.Option o in options)
                {
                    FilterOptions.Option opt = o;
                    bool on = t.Filter.Contains(o.Id);
                    RectTransform cell = UIKit.Rect(grid.transform, "opt");
                    Image bg = cell.gameObject.AddComponent<Image>();
                    bg.color = on ? new Color(1f, 0.63f, 0.24f, 0.35f) : new Color(0, 0, 0, 0.3f);
                    Button b = cell.gameObject.AddComponent<Button>();
                    b.targetGraphic = bg;
                    b.interactable = e.Loaded;
                    b.onClick.AddListener(() =>
                    {
                        Send(e, c =>
                        {
                            List<string> f = c.Tasks[index].Filter;
                            if (f.Contains(opt.Id)) f.Remove(opt.Id);
                            else f.Add(opt.Id);
                        });
                        ShowOptionsLater(e, index);
                    });
                    Image ic = UIKit.Icon(cell, o.Icon, 28);
                    ((RectTransform)ic.transform.parent).TopLeft(3, 3, 28, 28);
                    UIKit.Text(cell, o.Label, 15, on ? UIKit.Orange : UIKit.Parchment).rectTransform.TopLeft(36, 0, 160, 34);
                }
            }

            if (orders)
            {
                float oy = filterH;
                HorizontalLayoutGroup oh = UIKit.HRow(content, "ordersHead", 8);
                ((RectTransform)oh.transform).TopLeft(0, oy, 860, 32);
                UIKit.Text(oh.transform, "Keep in stock", 18, UIKit.Orange).Size(150, 30);
                UIKit.Text(oh.transform, "The villager crafts until the chests hold this many.", 14, UIKit.Grey).Size(-1, 30, 1);
                Button addR = UIKit.Button(oh.transform, "Add recipe", () => ShowRecipes(e, index), 140, 30);
                addR.interactable = e.Loaded;

                Image obg = UIKit.Inset(content, "orders");
                obg.rectTransform.TopLeft(0, oy + 36, 860, 564 - oy - 36 - 20);
                RectTransform list = UIKit.ScrollList(obg.transform, "olist", out _, 4, 6);
                ((RectTransform)list.parent.parent).Fill(2);
                if (t.Orders.Count == 0)
                    UIKit.Text(list, "No orders yet.", 16, UIKit.Grey).Size(-1, 30);
                for (int i = 0; i < t.Orders.Count; i++)
                {
                    int oi = i;
                    CraftOrder o = t.Orders[i];
                    HorizontalLayoutGroup r = UIKit.HRow(list, "order", 8, 2);
                    r.Size(-1, 40);
                    UIKit.Icon(r.transform, Items.Icon(o.Item), 34, true);
                    Recipe rec = CraftWork.RecipeFor(o.Item);
                    string station = rec?.m_craftingStation != null ? Localization.instance.Localize(rec.m_craftingStation.m_name) : "by hand";
                    UIKit.Text(r.transform, $"{Items.Nice(o.Item)}  <size=13><color=#a8a090>({station})</color></size>", 17).Size(-1, 34, 1);
                    void Adjust(int delta)
                    {
                        Send(e, c => c.Tasks[index].Orders[oi].Keep = Mathf.Clamp(c.Tasks[index].Orders[oi].Keep + delta, 1, 999));
                        ShowOptionsLater(e, index);
                    }
                    UIKit.Button(r.transform, "-10", () => Adjust(-10), 50, 30).interactable = e.Loaded;
                    UIKit.Button(r.transform, "-", () => Adjust(-1), 38, 30).interactable = e.Loaded;
                    UIKit.Text(r.transform, o.Keep.ToString(), 18, UIKit.Orange, TextAlignmentOptions.Center).Size(50, 30);
                    UIKit.Button(r.transform, "+", () => Adjust(1), 38, 30).interactable = e.Loaded;
                    UIKit.Button(r.transform, "+10", () => Adjust(10), 50, 30).interactable = e.Loaded;
                    UIKit.Button(r.transform, "Remove", () =>
                    {
                        Send(e, c => c.Tasks[index].Orders.RemoveAt(oi));
                        ShowOptionsLater(e, index);
                    }, 90, 30).interactable = e.Loaded;
                }
            }
        }

        /// <summary>Re-opens the options popup after the config round trip.</summary>
        private static void ShowOptionsLater(WorkerEntry e, int index)
        {
            Plugin.Instance.StartCoroutine(Later());
            System.Collections.IEnumerator Later()
            {
                yield return null;
                yield return null;
                if (IsOpen && e.Cfg.Tasks.Count > index) ShowOptions(e, index);
            }
        }

        // ------------------------------------------------------------------ recipe picker

        private static string _recipeStation;

        private static void ShowRecipes(WorkerEntry e, int index)
        {
            TaskConfig t = e.Cfg.Tasks[index];
            RectTransform content = Popup("Choose a recipe", 900, 640);
            List<Recipe> all = FilterOptions.RecipesFor(t.Type);
            List<string> stations = all.Select(StationKey).Distinct().OrderBy(s => s == "" ? "" : Localization.instance.Localize(s)).ToList();
            if (_recipeStation == null || !stations.Contains(_recipeStation)) _recipeStation = stations.FirstOrDefault() ?? "";

            GridLayoutGroup tabs = UIKit.Rect(content, "stations").TopLeft(0, 0, 860, 68).gameObject.AddComponent<GridLayoutGroup>();
            tabs.cellSize = new Vector2(120, 30);
            tabs.spacing = new Vector2(3, 4);
            foreach (string s in stations.Take(14))
            {
                string st = s;
                Button b = UIKit.Button(tabs.transform, s == "" ? "By hand" : Localization.instance.Localize(s), () =>
                {
                    _recipeStation = st;
                    ShowRecipes(e, index);
                }, 120, 30, tab: true);
                b.interactable = s != _recipeStation;
            }

            Image bg = UIKit.Inset(content, "recipes");
            bg.rectTransform.TopLeft(0, 74, 860, 490);
            RectTransform list = UIKit.ScrollList(bg.transform, "rlist", out _, 3, 6);
            ((RectTransform)list.parent.parent).Fill(2);
            foreach (Recipe r in all.Where(r => StationKey(r) == _recipeStation).OrderBy(r => r.m_minStationLevel).ThenBy(r => Items.Nice(r.m_item.gameObject.name)))
            {
                Recipe rec = r;
                string item = r.m_item.gameObject.name;
                bool have = t.Orders.Any(o => o.Item == item);
                RectTransform row = UIKit.Rect(list, "recipe");
                row.Size(-1, 40);
                Image rbg = row.gameObject.AddComponent<Image>();
                rbg.color = new Color(0, 0, 0, have ? 0.1f : 0.28f);
                Button b = row.gameObject.AddComponent<Button>();
                b.targetGraphic = rbg;
                b.interactable = !have && e.Loaded;
                b.onClick.AddListener(() =>
                {
                    Send(e, c =>
                    {
                        if (!c.Tasks[index].Orders.Any(o => o.Item == item))
                            c.Tasks[index].Orders.Add(new CraftOrder { Item = item, Keep = Mathf.Max(1, rec.m_amount * 5) });
                    });
                    ShowOptionsLater(e, index);
                });
                Image ic = UIKit.Icon(row, rec.m_item.m_itemData.GetIcon(), 34, true);
                ((RectTransform)ic.transform.parent).TopLeft(4, 3, 34, 34);
                string mats = string.Join(", ", CraftWork.Materials(rec).Select(m => $"{m.Value} {Items.Nice(m.Key)}"));
                string lvl = rec.m_minStationLevel > 1 ? $" (level {rec.m_minStationLevel})" : "";
                UIKit.Text(row, Items.Nice(item) + (rec.m_amount > 1 ? $" x{rec.m_amount}" : "") + lvl, 17, have ? UIKit.Grey : UIKit.Parchment).rectTransform.TopLeft(46, 0, 330, 40);
                UIKit.Text(row, mats, 14, UIKit.Grey).rectTransform.TopLeft(380, 0, 460, 40);
            }
        }

        private static string StationKey(Recipe r) => r.m_craftingStation != null ? r.m_craftingStation.m_name : "";
    }

    /// <summary>What a task can be limited to (item, creature or station prefab names), discovered from the game's prefabs.</summary>
    public static class FilterOptions
    {
        public class Option
        {
            public string Id;
            public string Label;
            public Sprite Icon;
        }

        private static readonly Dictionary<JobType, List<Option>> Cache = new Dictionary<JobType, List<Option>>();

        public static List<Option> For(JobType job)
        {
            if (Cache.TryGetValue(job, out List<Option> list)) return list;
            list = new List<Option>();
            if (ZNetScene.instance == null || ObjectDB.instance == null) return list;
            HashSet<string> ids = new HashSet<string>();
            IEnumerable<GameObject> prefabs = ZNetScene.instance.m_prefabs.Where(p => p != null);

            switch (job)
            {
                case JobType.Woodcutting:
                    foreach (GameObject p in prefabs)
                    {
                        Component c = (Component)p.GetComponent<TreeBase>() ?? p.GetComponent<TreeLog>();
                        if (c == null)
                        {
                            Destructible d = p.GetComponent<Destructible>();
                            if (d != null && d.m_destructibleType == DestructibleType.Tree) c = d;
                        }
                        if (c != null) foreach (string n in WoodcuttingJob.Drops(c)) ids.Add(n);
                    }
                    break;
                case JobType.Mining:
                    foreach (GameObject p in prefabs)
                    {
                        Component c = (Component)p.GetComponent<MineRock5>() ?? p.GetComponent<MineRock>();
                        if (c == null)
                        {
                            Destructible d = p.GetComponent<Destructible>();
                            if (d != null && p.GetComponent<Piece>() == null && MiningJob.IsMineable(d)) c = d;
                        }
                        if (c != null) foreach (string n in MiningJob.Drops(c)) ids.Add(n);
                    }
                    break;
                case JobType.Gathering:
                    foreach (GameObject p in prefabs)
                    {
                        Pickable pk = p.GetComponent<Pickable>();
                        if (pk != null && pk.m_itemPrefab != null && pk.m_aggravateRange <= 0 && !WorkerPrefab.IsCrop(p.name)) ids.Add(pk.m_itemPrefab.name);
                        Beehive h = p.GetComponent<Beehive>();
                        if (h != null && h.m_honeyItem != null) ids.Add(h.m_honeyItem.name);
                        SapCollector s = p.GetComponent<SapCollector>();
                        if (s != null && s.m_spawnItem != null) ids.Add(s.m_spawnItem.name);
                    }
                    break;
                case JobType.Farming:
                    foreach (string crop in WorkerPrefab.CropToPlant.Keys)
                    {
                        GameObject p = ZNetScene.instance.GetPrefab(crop);
                        Pickable pk = p != null ? p.GetComponent<Pickable>() : null;
                        if (pk != null && pk.m_itemPrefab != null) ids.Add(pk.m_itemPrefab.name);
                    }
                    break;
                case JobType.Hunting:
                    foreach (string n in HuntingJob.DefaultTargets) if (ZNetScene.instance.GetPrefab(n) != null) ids.Add(n);
                    foreach (GameObject p in prefabs)
                    {
                        Character c = p.GetComponent<Character>();
                        if (c != null && c.m_faction == Character.Faction.AnimalsVeg && p.GetComponent<BaseAI>() != null && !c.m_boss) ids.Add(p.name);
                    }
                    foreach (string id in ids)
                        list.Add(new Option { Id = id, Label = Items.Nice(id), Icon = Village.ItemIcon("kill:" + id) });
                    break;
                case JobType.Smelting:
                    foreach (GameObject p in WorkerPrefab.SmelterPrefabs)
                        list.Add(new Option { Id = p.name, Label = Localization.instance.Localize(p.GetComponent<Piece>().m_name), Icon = p.GetComponent<Piece>().m_icon });
                    break;
                case JobType.Cooking:
                    foreach (GameObject p in prefabs)
                    {
                        CookingStation cs = p.GetComponent<CookingStation>();
                        if (cs != null) foreach (CookingStation.ItemConversion x in cs.m_conversion) if (x.m_from != null) ids.Add(x.m_from.gameObject.name);
                    }
                    break;
                case JobType.Brewing:
                    foreach (GameObject p in prefabs)
                    {
                        Fermenter f = p.GetComponent<Fermenter>();
                        if (f != null) foreach (Fermenter.ItemConversion x in f.m_conversion) if (x.m_from != null) ids.Add(x.m_from.gameObject.name);
                    }
                    break;
            }

            if (job != JobType.Hunting && job != JobType.Smelting)
                foreach (string id in ids)
                    if (Items.Prefab(id) != null) list.Add(new Option { Id = id, Label = Items.Nice(id), Icon = Items.Icon(id) });

            list = list.GroupBy(o => o.Id).Select(g => g.First()).OrderBy(o => o.Label).ToList();
            Cache[job] = list;
            return list;
        }

        public static List<Recipe> RecipesFor(JobType job)
        {
            if (ObjectDB.instance == null) return new List<Recipe>();
            IEnumerable<Recipe> all = ObjectDB.instance.m_recipes.Where(r => r != null && r.m_enabled && r.m_item != null && r.m_resources.Length > 0);
            switch (job)
            {
                case JobType.Cooking:
                    return all.Where(r => r.m_item.m_itemData.m_shared.m_food > 0f || r.m_item.m_itemData.m_shared.m_foodStamina > 0f).ToList();
                case JobType.Brewing:
                    return all.Where(r => r.m_item.gameObject.name.Contains("Base")).ToList();
                default:
                    return all.ToList();
            }
        }

        public static void ClearCache() => Cache.Clear();
    }
}
