using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VillageWorkers.Jobs;

namespace VillageWorkers.UI
{
    /// <summary>A villager as seen by the UI - read from the ZDO, so far-away villagers show up too.</summary>
    public class WorkerEntry
    {
        public ZDO Zdo;
        public WorkerAI Ai;
        public int ColorIndex;

        private string _cfgJson, _statsJson;
        private WorkerConfig _cfg;
        private WorkerStats _stats;

        public ZDOID Id => Zdo.m_uid;
        public string Name => string.IsNullOrEmpty(Zdo.GetString(WorkerAI.K_Name)) ? "Villager" : Zdo.GetString(WorkerAI.K_Name);
        public WorkerState State => (WorkerState)Zdo.GetInt(WorkerAI.K_State);
        public string Status => Ai != null ? Ai.GetStatus() : Zdo.GetString(WorkerAI.K_Status);
        public bool Loaded => Ai != null && Ai.NView != null && Ai.NView.IsValid();
        public Color Color => Charts.Series[ColorIndex % Charts.Series.Length];

        public string Job
        {
            get
            {
                int j = Zdo.GetInt(WorkerAI.K_Job, -1);
                return j < 0 ? "" : JobInfo.Profession((JobType)j);
            }
        }

        public WorkerConfig Cfg
        {
            get
            {
                string j = Zdo.GetString(WorkerAI.K_Cfg);
                if (_cfg == null || j != _cfgJson)
                {
                    _cfg = WorkerConfig.FromJson(j);
                    _cfgJson = j;
                }
                return _cfg;
            }
        }

        public string CfgJson => Zdo.GetString(WorkerAI.K_Cfg);

        public WorkerStats Stats
        {
            get
            {
                if (Loaded && Ai.NView.IsOwner()) return Ai.Stats;
                string j = Zdo.GetString(WorkerAI.K_Stats);
                if (_stats == null || j != _statsJson)
                {
                    _stats = WorkerStats.FromJson(j);
                    _statsJson = j;
                }
                return _stats;
            }
        }
    }

    public static class Village
    {
        private static readonly Dictionary<ZDOID, WorkerEntry> Cache = new Dictionary<ZDOID, WorkerEntry>();

        private static List<WorkerEntry> _last = new List<WorkerEntry>();
        private static float _lastTime = -10f;

        public static List<WorkerEntry> Collect(bool force = false)
        {
            // A full ZDO scan is not free; the list rarely changes.
            if (!force && Time.unscaledTime - _lastTime < 2f)
            {
                foreach (WorkerEntry e in _last)
                    e.Ai = WorkerAI.All.FirstOrDefault(a => a != null && a.NView != null && a.NView.IsValid() && a.NView.GetZDO() == e.Zdo);
                return _last;
            }
            _lastTime = Time.unscaledTime;
            List<ZDO> zdos = new List<ZDO>();
            if (ZDOMan.instance != null)
            {
                int index = 0;
                int guard = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(WorkerPrefab.PrefabName, zdos, ref index) && guard++ < 100000) { }
            }
            // Loaded villagers are always known, even if the sector scan missed them.
            foreach (WorkerAI ai in WorkerAI.All)
            {
                ZDO z = ai.NView != null && ai.NView.IsValid() ? ai.NView.GetZDO() : null;
                if (z != null && !zdos.Contains(z)) zdos.Add(z);
            }

            List<WorkerEntry> list = new List<WorkerEntry>();
            foreach (ZDO z in zdos.Distinct())
            {
                if (z == null || !z.IsValid()) continue;
                if ((WorkerState)z.GetInt(WorkerAI.K_State) == WorkerState.Wild) continue;
                if (!Cache.TryGetValue(z.m_uid, out WorkerEntry e) || e.Zdo != z)
                {
                    e = new WorkerEntry { Zdo = z };
                    Cache[z.m_uid] = e;
                }
                e.Ai = WorkerAI.All.FirstOrDefault(a => a.NView != null && a.NView.IsValid() && a.NView.GetZDO() == z);
                list.Add(e);
            }

            list = list.OrderBy(e => e.Stats.Hired < 0 ? int.MaxValue : e.Stats.Hired).ThenBy(e => e.Id.UserID).ThenBy(e => e.Id.ID).ToList();
            for (int i = 0; i < list.Count; i++) list[i].ColorIndex = i;
            _last = list;
            return list;
        }

        public static string ItemLabel(string key)
        {
            if (key.StartsWith("kill:")) return Items.Nice(key.Substring(5)) + " (hunted)";
            if (key.StartsWith("fuel:")) return Items.Nice(key.Substring(5)) + " (fed to fires)";
            if (key.StartsWith("brew:")) return Items.Nice(key.Substring(5)) + " (set to ferment)";
            return Items.Nice(key);
        }

        public static Sprite ItemIcon(string key)
        {
            int colon = key.IndexOf(':');
            string name = colon >= 0 ? key.Substring(colon + 1) : key;
            if (key.StartsWith("kill:"))
            {
                // Show the creature's trophy if it has one.
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
                CharacterDrop drop = prefab != null ? prefab.GetComponent<CharacterDrop>() : null;
                GameObject trophy = drop?.m_drops.Select(d => d.m_prefab).FirstOrDefault(p => p != null && p.name.StartsWith("Trophy"));
                return trophy != null ? Items.Icon(trophy.name) : null;
            }
            return Items.Icon(name);
        }

        public static string FormatTime(double seconds)
        {
            if (EnvMan.instance == null) return "";
            double len = EnvMan.instance.m_dayLengthSec;
            int day = (int)(seconds / len);
            double frac = (seconds - day * len) / len;
            int minutes = (int)(frac * 24 * 60);
            return $"Day {day}, {minutes / 60:00}:{minutes % 60:00}";
        }
    }
}
