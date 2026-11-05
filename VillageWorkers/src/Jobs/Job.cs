using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillageWorkers.Jobs
{
    public abstract class Job
    {
        public readonly WorkerAI W;
        public readonly TaskConfig T;
        public JobType Type => T.Type;

        /// <summary>The job is in the middle of something that shouldn't be interrupted by a higher-priority job.</summary>
        public bool Busy;
        /// <summary>The job can't continue; the worker should pick another one.</summary>
        public bool Blocked;
        public string BlockReason;
        public float CooldownUntil;

        protected readonly Delivery Delivery = new Delivery();
        protected readonly Fetcher Fetcher = new Fetcher();
        protected readonly ItemFetcher ToolFetcher = new ItemFetcher();
        protected readonly Repairer Repair = new Repairer();
        protected readonly Collector Collector = new Collector();

        protected Job(WorkerAI w, TaskConfig t)
        {
            W = w;
            T = t;
        }

        public static Job Create(WorkerAI w, TaskConfig t)
        {
            switch (t.Type)
            {
                case JobType.Woodcutting: return new WoodcuttingJob(w, t);
                case JobType.Mining: return new MiningJob(w, t);
                case JobType.Gathering: return new GatheringJob(w, t);
                case JobType.Farming: return new FarmingJob(w, t);
                case JobType.Hunting: return new HuntingJob(w, t);
                case JobType.Guarding: return new GuardingJob(w, t);
                case JobType.Crafting: return new CraftingJob(w, t);
                case JobType.Smelting: return new SmeltingJob(w, t);
                case JobType.Cooking: return new CookingJob(w, t);
                case JobType.Brewing: return new BrewingJob(w, t);
                case JobType.Fueling: return new FuelingJob(w, t);
            }
            return null;
        }

        public abstract bool CanStart(out string reason);
        public abstract void Tick(float dt);

        public virtual void Start()
        {
            Blocked = false;
            Busy = false;
            Delivery.Reset();
            Fetcher.Reset();
            ToolFetcher.Reset();
            Repair.Reset();
        }

        public virtual void Stop()
        {
            Busy = false;
            W.Anim.SetInt("crafting", 0);
        }

        protected void Block(string reason, float cooldown = 20f)
        {
            Blocked = true;
            Busy = false;
            BlockReason = reason;
            CooldownUntil = Time.time + cooldown;
            W.SetStatus(JobInfo.Title(Type) + ": " + reason);
        }

        // ------------------------------------------------------------------ chests & area

        protected List<ObjRef> OutputChests => T.Chests != null && T.Chests.Count > 0 ? T.Chests : W.Config.Chests;

        /// <summary>Every chest this worker knows about - inputs may come from any of them.</summary>
        protected List<ObjRef> AllChests
        {
            get
            {
                WorkerConfig cfg = W.Config;
                List<ObjRef> all = new List<ObjRef>(T.Chests);
                foreach (ObjRef r in cfg.Chests.Concat(cfg.Tasks.SelectMany(t => t.Chests)))
                    if (!all.Any(a => a.Same(r))) all.Add(r);
                return all;
            }
        }

        protected Vector3 Center => T.Area != null ? T.Area.Center : W.Home;
        protected bool HasArea => T.Area != null;

        protected float StationRange => HasArea ? T.Area.R : Plugin.StationRadius.Value;

        protected bool InRange(Vector3 p, float radius) => Utils.DistanceXZ(p, Center) <= radius;

        // ------------------------------------------------------------------ tools

        public virtual ItemDrop.ItemData BestToolInInventory() => null;
        protected virtual bool IsTool(ItemDrop.ItemData item) => false;
        protected virtual float ToolScore(ItemDrop.ItemData item) => item.m_shared.m_toolTier * 100f + item.GetDamage().GetTotalDamage();

        protected ItemDrop.ItemData BestTool(Func<ItemDrop.ItemData, bool> match)
        {
            return W.Inv.GetAllItems().Where(i => match(i) && WorkerAI.Usable(i)).OrderByDescending(ToolScore).FirstOrDefault();
        }

        protected bool ToolAvailableInChests() =>
            ItemFetcher.Find(AllChests, i => IsTool(i) && WorkerAI.Usable(i), ToolScore, out _) != null;

        /// <summary>A broken tool for this job that a nearby station could fix.</summary>
        protected bool ToolRepairable() => Repairer.NextBroken(W, IsTool, out _) != null;

        /// <summary>Damages a destructible as if the worker swung the tool at it; also wears the tool.</summary>
        protected void ApplyToolHit(IDestructible target, ItemDrop.ItemData tool, Vector3 point, Collider col)
        {
            HitData hit = new HitData();
            hit.m_damage = tool.GetDamage();
            hit.m_damage.Modify(W.SkillFactor(Type) * Plugin.WorkSpeed.Value);
            hit.m_toolTier = (short)tool.m_shared.m_toolTier;
            hit.m_point = point;
            hit.m_dir = (point - W.transform.position).normalized;
            hit.m_hitCollider = col;
            hit.m_skill = tool.m_shared.m_skillType;
            hit.m_hitType = HitData.HitType.PlayerHit;
            hit.SetAttacker(W.Human);
            target.Damage(hit);
            W.RaiseSkill(Type, 0.25f);
            WearTool(tool);
        }

        /// <summary>
        /// Animator trigger for one swing, named like Attack.Start does it: combo weapons (axes) only have
        /// "swing_axe0/1/2" and random-animation weapons "name0..n" - the bare name doesn't exist.
        /// </summary>
        protected static string SwingTrigger(Attack attack, ref int chain)
        {
            if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation)) return null;
            if (attack.m_attackChainLevels > 1)
            {
                if (chain >= attack.m_attackChainLevels) chain = 0;
                return attack.m_attackAnimation + chain++;
            }
            if (attack.m_attackRandomAnimations >= 2)
                return attack.m_attackAnimation + UnityEngine.Random.Range(0, attack.m_attackRandomAnimations);
            return attack.m_attackAnimation;
        }

        protected void WearTool(ItemDrop.ItemData tool)
        {
            if (!Plugin.ToolDurability.Value || !tool.m_shared.m_useDurability) return;
            tool.m_durability = Mathf.Max(0f, tool.m_durability - tool.m_shared.m_useDurabilityDrain);
            W.Inv.Changed();
            if (tool.m_durability <= 0f)
            {
                string name = Localization.instance.Localize(tool.m_shared.m_name);
                W.Human.UnequipItem(tool, true);
                CraftingStation station = Repairer.FindStation(W, tool);
                if (station != null) W.Say($"My {name} broke! I'll go mend it at the {Localization.instance.Localize(station.m_name)}.");
                else W.Say($"My {name} broke and there's nowhere near to repair it. I need a new one, or a workbench nearby.");
                W.LogEvent($"broke their {name}");
            }
        }

        // ------------------------------------------------------------------ misc

        protected static Collider NearestCollider(Component c, Vector3 from, out Vector3 point)
        {
            point = c.transform.position;
            Collider best = null;
            float bestD = float.MaxValue;
            // MineRock only takes hits on its hit areas.
            IEnumerable<Collider> cols = c is MineRock mr && mr.m_hitAreas != null ? mr.m_hitAreas : c.GetComponentsInChildren<Collider>();
            foreach (Collider col in cols)
            {
                if (col == null || !col.enabled || (col.isTrigger && !(c is MineRock)) || !col.gameObject.activeInHierarchy) continue;
                Vector3 p = col.ClosestPointOnBounds(from);
                float d = Utils.DistanceXZ(p, from);
                if (d < bestD)
                {
                    bestD = d;
                    best = col;
                    point = p;
                }
            }
            return best;
        }

        protected static bool Alive(Component c) => c != null && c.gameObject.activeInHierarchy && (!c.TryGetComponent(out ZNetView nv) || nv.IsValid());

        protected static IEnumerable<string> DropNames(DropTable table)
        {
            if (table?.m_drops == null) yield break;
            foreach (DropTable.DropData d in table.m_drops)
                if (d.m_item != null) yield return d.m_item.name;
        }
    }
}
