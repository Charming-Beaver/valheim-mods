using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace VillageWorkers
{
    public enum JobType
    {
        Woodcutting,
        Mining,
        Gathering,
        Farming,
        Hunting,
        Crafting,
        Smelting,
        Cooking,
        Brewing,
        Fueling,
        Guarding
    }

    public enum CombatMode
    {
        FightBack,
        Flee
    }

    public enum WorkerState
    {
        Wild = 0,
        Recruit = 1,
        Villager = 2
    }

    /// <summary>Reference to a networked object (bed, chest) that survives reloads.</summary>
    public class ObjRef
    {
        public long U;
        public uint I;
        public float X, Y, Z;

        public ObjRef() { }

        public ObjRef(ZNetView nview)
        {
            ZDOID id = nview.GetZDO().m_uid;
            U = id.UserID;
            I = id.ID;
            Vector3 p = nview.transform.position;
            X = p.x;
            Y = p.y;
            Z = p.z;
        }

        [JsonIgnore] public Vector3 Pos => new Vector3(X, Y, Z);
        [JsonIgnore] public ZDOID Id => new ZDOID(U, I);

        // ZDO ids are renumbered every time a world loads, so pieces are identified by position
        // (pieces never move); the id is only a fast path within one session.
        private const float Tolerance = 0.3f;

        public bool Same(ObjRef o) => o != null && (o.Pos - Pos).sqrMagnitude < Tolerance * Tolerance;

        public bool Matches(ZNetView nv) => nv != null && nv.IsValid() && (nv.transform.position - Pos).sqrMagnitude < Tolerance * Tolerance;

        /// <summary>Returns the loaded instance, or null if it is unloaded or destroyed.</summary>
        public T Resolve<T>() where T : Component
        {
            if (ZNetScene.instance == null) return null;
            GameObject go = ZNetScene.instance.FindInstance(Id);
            if (go != null && (go.transform.position - Pos).sqrMagnitude < Tolerance * Tolerance && go.TryGetComponent(out T byId))
                return byId;
            foreach (Collider c in Physics.OverlapSphere(Pos, 1.5f))
            {
                T t = c.GetComponentInParent<T>();
                if (t == null) continue;
                ZNetView nv = t.GetComponent<ZNetView>();
                if (Matches(nv))
                {
                    ZDOID id = nv.GetZDO().m_uid;
                    U = id.UserID;
                    I = id.ID;
                    return t;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// A work area: a circle (radius R), or a box W wide and L long, turned A degrees around the up axis.
    /// For a box, R is the distance from the center to a corner, so range checks that only know circles still cover it.
    /// </summary>
    public class AreaDef
    {
        public float X, Y, Z, R = 20f;
        public float W, L, A;
        [JsonIgnore] public Vector3 Center => new Vector3(X, Y, Z);
        [JsonIgnore] public bool IsBox => W > 0f && L > 0f;
        [JsonIgnore] public Quaternion Rotation => Quaternion.Euler(0f, A, 0f);

        public AreaDef() { }

        public AreaDef(Vector3 c, float r)
        {
            X = c.x;
            Y = c.y;
            Z = c.z;
            R = r;
        }

        public static AreaDef Box(Vector3 center, float width, float length, float yaw)
        {
            return new AreaDef(center, 0.5f * Mathf.Sqrt(width * width + length * length)) { W = width, L = length, A = yaw };
        }

        /// <summary>Whether a point lies inside, looking from above (height doesn't matter).</summary>
        public bool Contains(Vector3 p)
        {
            Vector3 d = p - Center;
            d.y = 0f;
            if (!IsBox) return d.sqrMagnitude <= R * R;
            Vector3 local = Quaternion.Inverse(Rotation) * d;
            return Mathf.Abs(local.x) <= W * 0.5f && Mathf.Abs(local.z) <= L * 0.5f;
        }

        /// <summary>A point given in the area's own axes (x across the width, z along the length) relative to its center.</summary>
        public Vector3 ToWorld(float x, float z) => Center + Rotation * new Vector3(x, 0f, z);

        public string Describe() => IsBox ? $"{W:0} x {L:0} m box" : $"{R:0} m circle";
    }

    public class CraftOrder
    {
        /// <summary>Prefab name of the item to produce.</summary>
        public string Item;
        /// <summary>Keep this many in the chests.</summary>
        public int Keep = 10;
    }

    public class TaskConfig
    {
        public JobType Type;
        public bool Enabled = true;
        public AreaDef Area;
        public List<ObjRef> Chests = new List<ObjRef>();
        public List<CraftOrder> Orders = new List<CraftOrder>();
        /// <summary>Allowed sub-targets (item / creature / station prefab names). Empty = everything.</summary>
        public List<string> Filter = new List<string>();

        public bool Allows(string name) => Filter == null || Filter.Count == 0 || Filter.Contains(name);
    }

    public class WorkerConfig
    {
        public List<TaskConfig> Tasks = new List<TaskConfig>();
        public List<ObjRef> Chests = new List<ObjRef>();
        public ObjRef Bed;
        public CombatMode Combat = CombatMode.FightBack;
        public long FollowPlayer;
        public long Employer;

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static WorkerConfig FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return new WorkerConfig();
            try
            {
                WorkerConfig c = JsonConvert.DeserializeObject<WorkerConfig>(json) ?? new WorkerConfig();
                c.Tasks = c.Tasks ?? new List<TaskConfig>();
                c.Chests = c.Chests ?? new List<ObjRef>();
                foreach (TaskConfig t in c.Tasks)
                {
                    t.Chests = t.Chests ?? new List<ObjRef>();
                    t.Orders = t.Orders ?? new List<CraftOrder>();
                    t.Filter = t.Filter ?? new List<string>();
                }
                return c;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Bad worker config: " + e.Message);
                return new WorkerConfig();
            }
        }

        public WorkerConfig Clone() => FromJson(ToJson());
    }

    // ------------------------------------------------------------------ statistics

    public class StatEntry
    {
        public int D;      // day (-1 = merged lifetime totals)
        public int J;      // JobType
        public string I;   // item / creature prefab name
        public int N;      // amount
    }

    public class LogEntry
    {
        public double T;   // world time in seconds
        public string M;
    }

    public class WorkerStats
    {
        public int Hired = -1;
        public List<StatEntry> E = new List<StatEntry>();
        public List<LogEntry> L = new List<LogEntry>();
        public Dictionary<string, float> S = new Dictionary<string, float>();  // job skill 0..100
        public double WorkSec;  // seconds spent working

        public const int MaxLog = 60;

        public void Add(int day, JobType job, string item, int n)
        {
            if (n <= 0 || string.IsNullOrEmpty(item)) return;
            int j = (int)job;
            StatEntry e = E.FirstOrDefault(x => x.D == day && x.J == j && x.I == item);
            if (e == null)
            {
                e = new StatEntry { D = day, J = j, I = item };
                E.Add(e);
            }
            e.N += n;
            Prune(day);
        }

        public void Log(double time, string msg)
        {
            L.Add(new LogEntry { T = time, M = msg });
            if (L.Count > MaxLog) L.RemoveRange(0, L.Count - MaxLog);
        }

        private void Prune(int today)
        {
            int keep = Plugin.StatsDaysKept != null ? Plugin.StatsDaysKept.Value : 60;
            int cutoff = today - keep;
            if (!E.Any(x => x.D >= 0 && x.D < cutoff)) return;
            List<StatEntry> old = E.Where(x => x.D >= 0 && x.D < cutoff).ToList();
            foreach (StatEntry o in old)
            {
                E.Remove(o);
                StatEntry t = E.FirstOrDefault(x => x.D == -1 && x.J == o.J && x.I == o.I);
                if (t == null) E.Add(new StatEntry { D = -1, J = o.J, I = o.I, N = o.N });
                else t.N += o.N;
            }
        }

        public float Skill(JobType job) => S.TryGetValue(job.ToString(), out float v) ? v : 0f;

        public void RaiseSkill(JobType job, float amount)
        {
            float cur = Skill(job);
            // diminishing returns, like the player skill curve
            cur += amount * (1f - cur / 110f);
            S[job.ToString()] = Mathf.Clamp(cur, 0f, 100f);
        }

        public int Total() => E.Sum(x => x.N);
        public int TotalOn(int day) => E.Where(x => x.D == day).Sum(x => x.N);
        public int TotalFor(JobType job) => E.Where(x => x.J == (int)job).Sum(x => x.N);

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static WorkerStats FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return new WorkerStats();
            try
            {
                WorkerStats s = JsonConvert.DeserializeObject<WorkerStats>(json) ?? new WorkerStats();
                s.E = s.E ?? new List<StatEntry>();
                s.L = s.L ?? new List<LogEntry>();
                s.S = s.S ?? new Dictionary<string, float>();
                return s;
            }
            catch
            {
                return new WorkerStats();
            }
        }
    }

    public static class JobInfo
    {
        public static string Title(JobType t)
        {
            switch (t)
            {
                case JobType.Woodcutting: return "Woodcutting";
                case JobType.Mining: return "Mining";
                case JobType.Gathering: return "Gathering";
                case JobType.Farming: return "Farming";
                case JobType.Hunting: return "Hunting";
                case JobType.Crafting: return "Crafting";
                case JobType.Smelting: return "Smelting & Kilns";
                case JobType.Cooking: return "Cooking";
                case JobType.Brewing: return "Brewing";
                case JobType.Fueling: return "Tend fires";
                case JobType.Guarding: return "Guarding";
            }
            return t.ToString();
        }

        public static string Profession(JobType t)
        {
            switch (t)
            {
                case JobType.Woodcutting: return "Woodcutter";
                case JobType.Mining: return "Miner";
                case JobType.Gathering: return "Gatherer";
                case JobType.Farming: return "Farmer";
                case JobType.Hunting: return "Hunter";
                case JobType.Crafting: return "Crafter";
                case JobType.Smelting: return "Smelter";
                case JobType.Cooking: return "Cook";
                case JobType.Brewing: return "Brewer";
                case JobType.Fueling: return "Firekeeper";
                case JobType.Guarding: return "Guard";
            }
            return t.ToString();
        }

        public static string Description(JobType t)
        {
            switch (t)
            {
                case JobType.Woodcutting: return "Chops trees, logs and stumps. Needs an axe.";
                case JobType.Mining: return "Mines rocks and ore deposits. Needs a pickaxe.";
                case JobType.Gathering: return "Picks berries, mushrooms, flint, stones, branches, honey and sap.";
                case JobType.Farming: return "Harvests and replants crops, and plants empty fields in its area. Needs a cultivator.";
                case JobType.Hunting: return "Hunts animals and brings home the loot. Needs a weapon.";
                case JobType.Crafting: return "Crafts items at workbenches, forges, cauldrons etc. to keep a stock.";
                case JobType.Smelting: return "Fills smelters, kilns, furnaces, windmills and spinning wheels and collects the output.";
                case JobType.Cooking: return "Cooks on cooking stations and ovens. Can also craft cauldron food.";
                case JobType.Brewing: return "Fills and taps fermenters. Can also craft mead bases at the cauldron.";
                case JobType.Fueling: return "Keeps fires, hearths, braziers and torches burning.";
                case JobType.Guarding: return "Patrols and fights off monsters. Needs a weapon.";
            }
            return "";
        }

        public static bool UsesOrders(JobType t) => t == JobType.Crafting || t == JobType.Cooking || t == JobType.Brewing;

        public static bool ProducesItems(JobType t) => t != JobType.Guarding && t != JobType.Fueling;
    }
}
