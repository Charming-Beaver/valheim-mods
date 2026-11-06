using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillageWorkers.Jobs
{
    /// <summary>
    /// Shared flow for jobs that go out into the world: find target -> walk there -> work it -> pick up the loot ->
    /// bring it home when the pockets are full. Without an assigned area the search starts close to home and
    /// widens step by step.
    /// </summary>
    public abstract class HarvestJob : Job
    {
        protected enum Phase { Search, Tool, Repair, Approach, Work, Collect, Deliver, After }

        protected class Target
        {
            public Component C;
            public Vector3 Pos;
            public string Name;
        }

        protected Phase P;
        protected Target Cur;
        protected float Radius;
        protected Vector3 LastWorkPos;
        protected float WorkStarted;
        private float _nextSearch;

        protected HarvestJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected abstract bool NeedsTool { get; }
        protected abstract string ToolName { get; }
        protected abstract string Verb { get; }
        protected abstract Target FindTarget(Vector3 from, float radius);
        protected abstract R Work(float dt);
        protected virtual float Reach => 1.6f;
        protected virtual int DeliverAtSlots => 8;
        protected virtual float MaxWorkTime => 90f;
        protected virtual Vector3 ApproachPoint(Target t) => t.Pos;
        /// <summary>After unloading, go back for loot still lying around the last work spot (0 = don't).</summary>
        protected virtual float LeftoverRadius => 0f;

        protected float MaxRadius => HasArea ? T.Area.R : Plugin.MaxSearchRadius.Value;
        protected float StartRadius => HasArea ? T.Area.R : Mathf.Min(Plugin.StartSearchRadius.Value, MaxRadius);

        public override ItemDrop.ItemData BestToolInInventory() => NeedsTool ? BestTool(IsTool) : null;

        public override bool CanStart(out string reason)
        {
            if (NeedsTool && BestToolInInventory() == null && !ToolAvailableInChests() && !ToolRepairable())
            {
                reason = "needs " + ToolName;
                W.Ask(Type + "_tool", $"I could do some {JobInfo.Title(Type).ToLower()}, but I need {ToolName}. Could you bring me one?");
                return false;
            }
            if (!Chests.HasSpace(OutputChests, out reason))
            {
                if (reason == "no chest assigned") W.Ask("chest", "Where should I put my things? Please assign me a chest.");
                return false;
            }
            if (FindTarget(W.transform.position, MaxRadius) == null && !HasOtherWork())
            {
                reason = $"nothing to {Verb.ToLower()} {(HasArea ? "in my area" : "nearby")}";
                return false;
            }
            reason = null;
            return true;
        }

        public override void Start()
        {
            base.Start();
            P = Phase.Search;
            Cur = null;
            Radius = StartRadius;
        }

        public override void Stop()
        {
            base.Stop();
            Cur = null;
        }

        public override void Tick(float dt)
        {
            if (P != Phase.Deliver && P != Phase.Work && P != Phase.After &&
                (W.LootSlots() >= DeliverAtSlots || W.Inv.GetEmptySlots() == 0))
            {
                P = Phase.Deliver;
                Delivery.Reset();
            }

            switch (P)
            {
                case Phase.Deliver:
                {
                    R r = Delivery.Tick(W, OutputChests, null);
                    if (r == R.Done)
                    {
                        P = Phase.Search;
                        if (LeftoverRadius > 0f && LastWorkPos != Vector3.zero && Collector.AnyDropsNear(W, LastWorkPos, LeftoverRadius))
                        {
                            Collector.Begin(LastWorkPos, LeftoverRadius, 10f);
                            P = Phase.Collect;
                        }
                    }
                    else if (r == R.Failed) Block(Delivery.FailReason, 30f);
                    break;
                }
                case Phase.Tool:
                {
                    R r = ToolFetcher.Tick(W, AllChests, i => IsTool(i) && WorkerAI.Usable(i), ToolScore, ToolName);
                    if (r == R.Done) P = Phase.Search;
                    else if (r == R.Failed)
                    {
                        W.Ask(Type + "_tool", $"I need {ToolName} to keep working!");
                        Block("needs " + ToolName, 30f);
                    }
                    break;
                }
                case Phase.Repair:
                {
                    // Done or unreachable - either way Search() decides what's next (work, or fetch a tool).
                    if (Repair.Tick(W, IsTool) != R.Running) P = Phase.Search;
                    break;
                }
                case Phase.Search:
                    Search();
                    break;
                case Phase.Approach:
                {
                    if (Cur == null || !Alive(Cur.C))
                    {
                        P = Phase.Search;
                        break;
                    }
                    W.SetStatus($"Going to {Verb.ToLower()} {Cur.Name}");
                    R r = W.MoveNear(ApproachPoint(Cur), Reach, false);
                    if (r == R.Failed)
                    {
                        W.Blacklist(Cur.C, 180f);
                        P = Phase.Search;
                    }
                    else if (r == R.Done)
                    {
                        P = Phase.Work;
                        WorkStarted = Time.time;
                        Busy = true;
                    }
                    break;
                }
                case Phase.Work:
                {
                    W.SetStatus($"{Verb} {Cur?.Name}");
                    R r = Cur == null ? R.Failed : Work(dt);
                    if (r == R.Running && Time.time - WorkStarted > MaxWorkTime) r = R.Failed;
                    if (r == R.Done)
                    {
                        Busy = false;
                        P = Phase.Collect;
                    }
                    else if (r == R.Failed)
                    {
                        Busy = false;
                        if (Cur != null) W.Blacklist(Cur.C, 120f);
                        P = Phase.Search;
                    }
                    break;
                }
                case Phase.Collect:
                {
                    R r = Collector.Tick(W, Type);
                    if (r != R.Running) P = OnCollected();
                    break;
                }
                case Phase.After:
                    P = AfterTick(dt);
                    break;
            }
        }

        protected virtual Phase OnCollected() => Phase.Search;
        protected virtual Phase AfterTick(float dt) => Phase.Search;

        /// <summary>
        /// Work that isn't a target in the world, such as a farmer planting empty ground, done when there's nothing left
        /// to harvest. Starting it sets the phase.
        /// </summary>
        protected virtual bool HasOtherWork() => false;
        protected virtual bool StartOtherWork() => false;

        private void Search()
        {
            if (NeedsTool)
            {
                // Mend broken tools first, even when a spare still works.
                if (ToolRepairable())
                {
                    P = Phase.Repair;
                    Repair.Reset();
                    return;
                }
                ItemDrop.ItemData tool = BestToolInInventory();
                if (tool == null)
                {
                    P = Phase.Tool;
                    ToolFetcher.Reset();
                    return;
                }
                W.Equip(tool);
            }
            else
            {
                W.UnequipHands();
            }

            if (Time.time < _nextSearch)
            {
                W.Stop();
                return;
            }

            Cur = FindTarget(W.transform.position, Radius);
            if (Cur != null)
            {
                P = Phase.Approach;
                return;
            }

            if (Radius < MaxRadius)
            {
                Radius = Mathf.Min(Radius + 20f, MaxRadius);
                W.SetStatus($"Looking further away for something to {Verb.ToLower()} ({Radius:0} m)");
                _nextSearch = Time.time + 1f;
                return;
            }

            Radius = StartRadius;
            if (StartOtherWork()) return;
            if (W.HasLoot())
            {
                P = Phase.Deliver;
                return;
            }
            Block($"nothing to {Verb.ToLower()} {(HasArea ? "in my area" : "nearby")}", 45f);
        }

        /// <summary>Generic target scan: nearest valid candidate (from the worker) that lies inside the search circle.</summary>
        protected Target Nearest<TC>(IEnumerable<TC> candidates, Vector3 from, float radius, System.Func<TC, bool> valid,
            System.Func<TC, string> name, System.Func<TC, float> bias = null) where TC : Component
        {
            TC best = null;
            float bestScore = float.MaxValue;
            foreach (TC c in candidates)
            {
                if (c == null || !InRange(c.transform.position, radius) || W.IsBlacklisted(c) || !valid(c)) continue;
                float score = Vector3.Distance(from, c.transform.position) + (bias?.Invoke(c) ?? 0f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best == null ? null : new Target { C = best, Pos = best.transform.position, Name = name(best) };
        }

        // ------------------------------------------------------------------ striking (trees and rocks)

        private float _nextSwing;
        private float _hitAt = -1f;
        private Vector3 _hitPoint;
        private Collider _hitCollider;
        private int _chain;

        protected R Strike(IDestructible target, Component comp)
        {
            if (!Alive(comp))
            {
                _hitAt = -1f;
                return R.Done;
            }
            ItemDrop.ItemData tool = BestToolInInventory();
            if (tool == null)
            {
                // Broke mid-job: Search() repairs it or fetches another.
                P = Phase.Search;
                Busy = false;
                ToolFetcher.Reset();
                return R.Running;
            }
            W.Equip(tool);

            Collider col = NearestCollider(comp, W.transform.position, out Vector3 point);
            if (col == null) return comp is MineRock ? R.Done : R.Failed;
            LastWorkPos = comp.transform.position;

            float reach = 1.9f;
            if (Utils.DistanceXZ(W.transform.position, point) > reach)
            {
                if (W.MoveNear(point, reach * 0.75f, false) == R.Failed) return R.Failed;
                return R.Running;
            }

            W.Stop();
            W.Face(point + Vector3.up * 0.8f);

            if (_hitAt > 0f && Time.time >= _hitAt)
            {
                _hitAt = -1f;
                if (_hitCollider != null) ApplyToolHit(target, tool, _hitPoint, _hitCollider);
            }

            if (_hitAt < 0f && Time.time >= _nextSwing && W.Facing(point, 35f))
            {
                string anim = SwingTrigger(tool.m_shared.m_attack, ref _chain);
                if (anim != null) W.Anim.SetTrigger(anim);
                _hitPoint = point;
                _hitCollider = col;
                _hitAt = Time.time + 0.45f;
                float speed = W.SkillFactor(Type) * Plugin.WorkSpeed.Value;
                _nextSwing = Time.time + 1.7f / Mathf.Max(0.25f, speed);
            }
            return R.Running;
        }
    }

    // ====================================================================== woodcutting

    public class WoodcuttingJob : HarvestJob
    {
        public WoodcuttingJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected override bool NeedsTool => true;
        protected override string ToolName => "an axe";
        protected override string Verb => "Chopping";
        protected override bool IsTool(ItemDrop.ItemData i) => i.m_shared.m_skillType == Skills.SkillType.Axes && i.GetDamage().m_chop > 0f;
        protected override float ToolScore(ItemDrop.ItemData i) => i.m_shared.m_toolTier * 100f + i.GetDamage().m_chop;

        private int Tier => BestToolInInventory()?.m_shared.m_toolTier ?? 99;

        public static IEnumerable<string> Drops(Component c)
        {
            switch (c)
            {
                case TreeLog log:
                    foreach (string n in LogDrops(log, 0)) yield return n;
                    break;
                case TreeBase tree:
                    foreach (string n in DropNames(tree.m_dropWhenDestroyed)) yield return n;
                    TreeLog l = tree.m_logPrefab != null ? tree.m_logPrefab.GetComponent<TreeLog>() : null;
                    if (l != null) foreach (string n in LogDrops(l, 0)) yield return n;
                    break;
                default:
                    DropOnDestroyed dod = c.GetComponent<DropOnDestroyed>();
                    if (dod != null) foreach (string n in DropNames(dod.m_dropWhenDestroyed)) yield return n;
                    break;
            }
        }

        private static IEnumerable<string> LogDrops(TreeLog log, int depth)
        {
            foreach (string n in DropNames(log.m_dropWhenDestroyed)) yield return n;
            if (depth < 3 && log.m_subLogPrefab != null)
            {
                TreeLog sub = log.m_subLogPrefab.GetComponent<TreeLog>();
                if (sub != null) foreach (string n in LogDrops(sub, depth + 1)) yield return n;
            }
        }

        private bool Wanted(Component c) => T.Filter.Count == 0 || Drops(c).Any(T.Allows);

        protected override Vector3 ApproachPoint(Target t)
        {
            if (!Alive(t.C)) return t.Pos;
            NearestCollider(t.C, W.transform.position, out Vector3 p);
            return p;
        }

        protected override float Reach => 1.8f;

        protected override Target FindTarget(Vector3 from, float radius)
        {
            int tier = Tier;
            WorldIndex.Snapshot s = WorldIndex.Current;

            // Finish fallen logs first - they are right here and otherwise rot away.
            Target log = Nearest(LogsNear(s), from, radius, l => l.m_minToolTier <= tier && Wanted(l), l => "a log",
                l => Vector3.Distance(from, l.transform.position) < 25f ? -30f : 0f);
            if (log != null && Vector3.Distance(from, log.Pos) < 30f) return log;

            Target tree = Nearest(s.Trees, from, radius, t => t.m_minToolTier <= tier && Wanted(t),
                t => Items.Nice(Utils.GetPrefabName(t.gameObject)).Replace("_", " "));
            Target stump = Nearest(s.Destructibles, from, radius,
                d => d.GetDestructibleType() == DestructibleType.Tree && d.m_minToolTier <= tier && Wanted(d), d => "a stump", d => 15f);

            Target best = null;
            foreach (Target t in new[] { log, tree, stump })
                if (t != null && (best == null || Vector3.Distance(from, t.Pos) < Vector3.Distance(from, best.Pos))) best = t;
            if (best != null && best.C is TreeBase) best.Name = TreeName(best.C);
            return best;
        }

        private IEnumerable<TreeLog> LogsNear(WorldIndex.Snapshot s)
        {
            foreach (TreeLog l in s.Logs) yield return l;
            // Freshly felled logs may not be indexed yet.
            if (LastWorkPos != Vector3.zero)
                foreach (Collider c in Physics.OverlapSphere(LastWorkPos, 14f))
                {
                    TreeLog l = c.GetComponentInParent<TreeLog>();
                    if (l != null) yield return l;
                }
        }

        private static string TreeName(Component c)
        {
            string n = Utils.GetPrefabName(c.gameObject);
            if (n.Contains("Birch")) return "a birch";
            if (n.Contains("Oak")) return "an oak";
            if (n.Contains("Pine")) return "a pine";
            if (n.Contains("Fir")) return "a fir";
            if (n.Contains("Beech")) return "a beech";
            if (n.Contains("Yggdrasil") || n.Contains("Ygga")) return "a yggdrasil tree";
            if (n.Contains("Ash")) return "an ashwood tree";
            return "a tree";
        }

        protected override float LeftoverRadius => 14f;

        protected override R Work(float dt)
        {
            Component c = Cur.C;
            // Logs roll, so collect around where it was last hit rather than where it was found.
            Vector3 at = LastWorkPos != Vector3.zero ? LastWorkPos : Cur.Pos;
            if (!Alive(c))
            {
                Collector.Begin(at, LeftoverRadius, 10f, 3f);
                return R.Done;
            }
            R r = Strike(c as IDestructible, c);
            if (r == R.Done)
            {
                Collector.Begin(LastWorkPos != Vector3.zero ? LastWorkPos : Cur.Pos, LeftoverRadius, 10f, 3f);
                W.RaiseSkill(Type, 1f);
            }
            return r;
        }
    }

    // ====================================================================== mining

    public class MiningJob : HarvestJob
    {
        public MiningJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected override bool NeedsTool => true;
        protected override string ToolName => "a pickaxe";
        protected override string Verb => "Mining";
        protected override float MaxWorkTime => 240f;
        protected override bool IsTool(ItemDrop.ItemData i) => i.m_shared.m_skillType == Skills.SkillType.Pickaxes && i.GetDamage().m_pickaxe > 0f;
        protected override float ToolScore(ItemDrop.ItemData i) => i.m_shared.m_toolTier * 100f + i.GetDamage().m_pickaxe;

        public static IEnumerable<string> Drops(Component c)
        {
            switch (c)
            {
                case MineRock5 m5: return DropNames(m5.m_dropItems);
                case MineRock m: return DropNames(m.m_dropItems);
                default:
                    DropOnDestroyed dod = c.GetComponent<DropOnDestroyed>();
                    return dod != null ? DropNames(dod.m_dropWhenDestroyed) : Enumerable.Empty<string>();
            }
        }

        public static bool IsMineable(Destructible d)
        {
            if (d.GetDestructibleType() == DestructibleType.Tree || d.GetDestructibleType() == DestructibleType.Character) return false;
            HitData.DamageModifiers m = d.m_damages;
            bool pick = m.m_pickaxe != HitData.DamageModifier.Immune && m.m_pickaxe != HitData.DamageModifier.Ignore;
            bool chopImmune = m.m_chop == HitData.DamageModifier.Immune || m.m_chop == HitData.DamageModifier.Ignore;
            return pick && chopImmune && d.GetComponent<DropOnDestroyed>() != null;
        }

        private bool Wanted(Component c) => T.Filter.Count == 0 || Drops(c).Any(T.Allows);

        protected override Target FindTarget(Vector3 from, float radius)
        {
            int tier = BestToolInInventory()?.m_shared.m_toolTier ?? 99;
            WorldIndex.Snapshot s = WorldIndex.Current;
            Target a = Nearest(s.MineRocks5, from, radius, r => r.m_minToolTier <= tier && Wanted(r), r => RockName(r));
            Target b = Nearest(s.MineRocks, from, radius, r => r.m_minToolTier <= tier && Wanted(r), r => RockName(r));
            Target c = Nearest(s.Destructibles, from, radius, d => d.m_minToolTier <= tier && IsMineable(d) && Wanted(d), d => RockName(d));
            Target best = null;
            foreach (Target t in new[] { a, b, c })
                if (t != null && (best == null || Vector3.Distance(from, t.Pos) < Vector3.Distance(from, best.Pos))) best = t;
            return best;
        }

        private static string RockName(Component c)
        {
            string main = Drops(c).FirstOrDefault(n => n != "Stone") ?? Drops(c).FirstOrDefault();
            return main != null ? Items.Nice(main).ToLower() + " deposit" : "a rock";
        }

        protected override Vector3 ApproachPoint(Target t)
        {
            NearestCollider(t.C, W.transform.position, out Vector3 p);
            return p;
        }

        protected override float Reach => 1.8f;
        protected override float LeftoverRadius => 9f;

        protected override R Work(float dt)
        {
            Component c = Cur.C;
            if (!Alive(c))
            {
                Collector.Begin(LastWorkPos != Vector3.zero ? LastWorkPos : Cur.Pos, 8f, 6f, 1.5f);
                return R.Done;
            }
            R r = Strike(c as IDestructible, c);
            if (r == R.Done) Collector.Begin(Cur.Pos, 8f, 6f, 1.5f);
            return r;
        }
    }

    // ====================================================================== gathering

    public class GatheringJob : HarvestJob
    {
        public GatheringJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected override bool NeedsTool => false;
        protected override string ToolName => "";
        protected override string Verb => "Picking";
        protected override float Reach => 1.5f;

        private float _actAt = -1f;

        public static bool IsGatherable(Pickable p)
        {
            if (p == null || p.m_itemPrefab == null || p.GetPicked() || p.m_enabled == 0) return false;
            if (p.m_aggravateRange > 0f) return false;
            return !WorkerPrefab.IsCrop(Utils.GetPrefabName(p.gameObject));
        }

        protected override Target FindTarget(Vector3 from, float radius)
        {
            WorldIndex.Snapshot s = WorldIndex.Current;
            Target a = Nearest(s.Pickables, from, radius, p => IsGatherable(p) && T.Allows(p.m_itemPrefab.name),
                p => Items.Nice(p.m_itemPrefab.name));
            Target b = Nearest(s.Beehives, from, radius, h => h.GetHoneyLevel() > 0 && h.m_honeyItem != null && T.Allows(h.m_honeyItem.name),
                h => "the beehive");
            Target c = Nearest(s.SapCollectors, from, radius, x => x.GetLevel() > 0 && x.m_spawnItem != null && T.Allows(x.m_spawnItem.name),
                x => "the sap collector");
            Target best = null;
            foreach (Target t in new[] { a, b, c })
                if (t != null && (best == null || Vector3.Distance(from, t.Pos) < Vector3.Distance(from, best.Pos))) best = t;
            return best;
        }

        protected override R Work(float dt)
        {
            Component c = Cur.C;
            if (!Alive(c))
            {
                _actAt = -1f;
                return R.Failed;
            }
            W.Stop();
            W.Face(c.transform.position);
            if (_actAt < 0f)
            {
                W.Anim.SetTrigger("interact");
                _actAt = Time.time + 0.6f / Plugin.WorkSpeed.Value;
                return R.Running;
            }
            if (Time.time < _actAt) return R.Running;
            _actAt = -1f;

            Vector3 spawn = c.transform.position;
            switch (c)
            {
                case Pickable p:
                    if (p.GetPicked()) return R.Failed;
                    p.m_nview.InvokeRPC("RPC_Pick", 0);
                    break;
                case Beehive h:
                    h.m_nview.InvokeRPC("RPC_Extract");
                    spawn = h.m_spawnPoint.position;
                    break;
                case SapCollector x:
                    x.m_nview.InvokeRPC("RPC_Extract");
                    spawn = x.m_spawnPoint.position;
                    break;
            }
            W.RaiseSkill(Type, 0.5f);
            Collector.Begin(spawn, 3.5f, 4f, 0.8f);
            return R.Done;
        }
    }

    // ====================================================================== farming

    /// <summary>
    /// Harvests ripe crops and replants them. With an area set, also plants empty cultivated ground in it with seeds from
    /// the chests, far enough apart for every crop to grow. Several farmers can share a field: each claims the spot it's
    /// about to work on (see <see cref="WorkerAI.Claim"/>), and the others leave it alone.
    /// </summary>
    public class FarmingJob : HarvestJob
    {
        public FarmingJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected override bool NeedsTool => false;
        protected override string ToolName => "";
        protected override string Verb => "Harvesting";
        protected override float Reach => 1.4f;
        protected override int DeliverAtSlots => 6;

        // Seconds a claim has to stand before picking or planting (see WorkerAI.ClaimAge).
        private const float ClaimSettle = 1f;
        // Seeds fetched at once, so planting a field doesn't mean a walk to the chest for every seed.
        private const int SeedBatch = 10;
        private const float PlantReach = 2f;
        // Planting spots checked in one search at most, nearest first.
        private const int MaxSpotChecks = 200;

        private float _actAt = -1f;
        // What to plant next: the same crop where one was just picked, or a new one on empty ground.
        private Piece _plant;
        private Vector3 _plantPos;
        private Quaternion _plantRot;
        private bool _newSpot;
        private Pickable _picked;
        private float _noSpotsUntil;
        private float _seedWaitUntil;
        private int _moveTries;
        private readonly List<KeyValuePair<Vector3, float>> _badSpots = new List<KeyValuePair<Vector3, float>>();

        protected override Target FindTarget(Vector3 from, float radius)
        {
            return Nearest(WorldIndex.Current.Pickables, from, radius,
                p => p != null && p.m_itemPrefab != null && !p.GetPicked() && WorkerPrefab.IsCrop(Utils.GetPrefabName(p.gameObject)) && T.Allows(p.m_itemPrefab.name)
                     && !W.ClaimedByOther(p.transform.position, 0.3f, false),
                p => Items.Nice(p.m_itemPrefab.name).ToLower());
        }

        public override void Start()
        {
            base.Start();
            _plant = null;
            _actAt = -1f;
        }

        public override void Stop()
        {
            base.Stop();
            _plant = null;
            W.ReleaseClaim();
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            // Let the other farmers know which spot is taken.
            if (_plant != null && (P == Phase.Collect || P == Phase.After)) W.Claim(_plantPos);
            else if (Cur != null && (P == Phase.Approach || P == Phase.Work)) W.Claim(Cur.Pos);
            else W.ReleaseClaim();
        }

        protected override R Work(float dt)
        {
            Pickable p = Cur.C as Pickable;
            if (!Alive(p) || p.GetPicked()) return R.Failed;
            W.Stop();
            W.Face(p.transform.position);
            if (_actAt < 0f)
            {
                if (W.ClaimAge < ClaimSettle) return R.Running;
                // Another farmer went for the same crop at the same moment, and goes first.
                if (W.ClaimedByOther(p.transform.position, 0.3f, true)) return R.Failed;
                W.Anim.SetTrigger("interact");
                _actAt = Time.time + 0.6f / Plugin.WorkSpeed.Value;
                return R.Running;
            }
            if (Time.time < _actAt) return R.Running;
            _actAt = -1f;

            WorkerPrefab.CropToPlant.TryGetValue(Utils.GetPrefabName(p.gameObject), out _plant);
            _plantPos = p.transform.position;
            _plantRot = p.transform.rotation;
            _newSpot = false;
            _picked = p;
            _seedWaitUntil = 0f;
            _moveTries = 0;
            p.m_nview.InvokeRPC("RPC_Pick", 0);
            W.RaiseSkill(Type, 0.6f);
            Collector.Begin(_plantPos, 3f, 4f, 1f);
            return R.Done;
        }

        protected override Phase OnCollected() => _plant != null ? Phase.After : Phase.Search;

        protected override bool HasOtherWork() => FindPlantSpot(out _, out _);

        protected override bool StartOtherWork()
        {
            if (!FindPlantSpot(out Piece sapling, out Vector3 spot)) return false;
            _plant = sapling;
            _plantPos = spot;
            _plantRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            _newSpot = true;
            _picked = null;
            _seedWaitUntil = 0f;
            _moveTries = 0;
            P = Phase.After;
            return true;
        }

        private static Piece.Requirement Seed(Piece sapling)
        {
            Piece.Requirement req = sapling != null && sapling.m_resources.Length > 0 ? sapling.m_resources[0] : null;
            return req != null && req.m_resItem != null ? req : null;
        }

        protected override Phase AfterTick(float dt)
        {
            // Plant the crop with its seed.
            Piece.Requirement req = Seed(_plant);
            if (req == null)
            {
                _plant = null;
                return Phase.Search;
            }
            string seed = req.m_resItem.gameObject.name;
            int amount = Mathf.Max(1, req.m_amount);
            int have = Items.Count(W.Inv, seed);
            if (have < amount)
            {
                // A batch for planting a field. For replanting just one, so a farmer sharing the field with others
                // doesn't take seeds they need to replant what they pick.
                int want = _newSpot ? Mathf.Min(amount * SeedBatch, have + Chests.CountAvailable(AllChests, seed)) : amount;
                R f = want >= amount ? Fetcher.Tick(W, AllChests, new Dictionary<string, int> { { seed, want } }, Items.Nice(seed)) : R.Failed;
                if (f == R.Running) return Phase.After;
                if (Items.Count(W.Inv, seed) < amount)
                {
                    // The seeds are there, but the chest can't be reached for a moment (another villager in the way
                    // makes the fetcher give up on it for a while): keep the spot and try again.
                    if (Chests.CountAvailable(AllChests, seed) >= amount)
                    {
                        if (_seedWaitUntil <= 0f) _seedWaitUntil = Time.time + 40f;
                        if (Time.time < _seedWaitUntil)
                        {
                            W.SetStatus("Waiting to get " + Items.Nice(seed));
                            return Phase.After;
                        }
                    }
                    else
                    {
                        W.Ask("seeds_" + seed, $"I'm out of {Items.Nice(seed)} to {(_newSpot ? "plant" : "replant")} the field.");
                    }
                    Plugin.Debug($"{W.GetName()} has no {seed} to plant at {_plantPos}");
                    // Don't come straight back to the same spot.
                    if (_newSpot) _noSpotsUntil = Time.time + 15f;
                    _plant = null;
                    return Phase.Search;
                }
            }
            _seedWaitUntil = 0f;

            W.SetStatus((_newSpot ? "Planting " : "Replanting ") + Items.Nice(seed));
            // From a couple of meters, like a player: in a planted field the path finder often stops short of the spot.
            R m = W.MoveNear(_plantPos, PlantReach, false);
            if (m == R.Running) return Phase.After;
            if (m == R.Failed)
            {
                Plugin.Debug($"{W.GetName()} can't get to {_plantPos} to plant");
                // A gap in a planted field is worse than a skipped new spot: try a few more times (someone may have
                // been in the way).
                if (!_newSpot && ++_moveTries < 3) return Phase.After;
                if (_newSpot) _badSpots.Add(new KeyValuePair<Vector3, float>(_plantPos, Time.time + 300f));
                _plant = null;
                return Phase.Search;
            }
            if (W.ClaimAge < ClaimSettle) return Phase.After;

            Plant plant = _plant.GetComponent<Plant>();
            bool taken = _newSpot
                ? W.ClaimedByOther(_plantPos, PlantSpace.Spacing(plant, plant) - PlantSpace.Tolerance, true) || !PlantSpace.Free(_plantPos, plant, _picked)
                // Replanting where a crop just grew: only make sure nobody replanted it already.
                : W.ClaimedByOther(_plantPos, 0.3f, true) || PlantSpace.Occupied(_plantPos, _picked);
            if (taken)
            {
                Plugin.Debug($"{W.GetName()} finds {_plantPos} taken");
                if (_newSpot) _badSpots.Add(new KeyValuePair<Vector3, float>(_plantPos, Time.time + 60f));
                _plant = null;
                return Phase.Search;
            }

            W.Face(_plantPos);
            W.Anim.SetTrigger("interact");
            GameObject planted = Object.Instantiate(_plant.gameObject, _plantPos, _plantRot);
            _plant.m_placeEffect.Create(_plantPos, _plantRot, planted.transform);
            Items.Remove(W.Inv, seed, amount);
            if (_newSpot)
            {
                W.RaiseSkill(Type, 0.3f);
                W.LogEvent("planted " + Items.Nice(seed));
            }
            _plant = null;
            return Phase.Search;
        }

        // ------------------------------------------------------------------ planting new crops

        /// <summary>The nearest empty spot in the area where one of the crops this task may grow can be planted, with seeds to hand.</summary>
        private bool FindPlantSpot(out Piece sapling, out Vector3 spot)
        {
            sapling = null;
            spot = Vector3.zero;
            if (!HasArea || Time.time < _noSpotsUntil) return false;
            _badSpots.RemoveAll(kv => Time.time > kv.Value);
            Vector3 from = W.transform.position;
            float best = float.MaxValue;
            foreach (Piece p in PlantableSaplings())
            {
                if (!NearestFreeSpot(p.GetComponent<Plant>(), from, out Vector3 s)) continue;
                float d = Utils.DistanceXZ(from, s);
                if (d >= best) continue;
                best = d;
                sapling = p;
                spot = s;
            }
            // Nothing to plant: don't search the whole field again for a while.
            if (sapling == null)
            {
                _noSpotsUntil = Time.time + 15f;
                Plugin.Debug($"{W.GetName()} finds nothing to plant");
            }
            return sapling != null;
        }

        private static bool IsFood(ItemDrop item) => item.m_itemData.m_shared.m_food > 0f || item.m_itemData.m_shared.m_foodStamina > 0f;

        private IEnumerable<Piece> PlantableSaplings()
        {
            foreach (KeyValuePair<Piece, string> kv in WorkerPrefab.SaplingCrops)
            {
                Plant plant = kv.Key.GetComponent<Plant>();
                if (plant == null || !(plant.m_needCultivatedGround || kv.Key.m_cultivatedGroundOnly) || !T.Allows(kv.Value)) continue;
                Piece.Requirement req = Seed(kv.Key);
                if (req == null) continue;
                string seed = req.m_resItem.gameObject.name;
                // Planting a carrot grows a seed carrot: don't plant food that's the harvest of another crop unless the
                // task's options ask for that crop. Seeds, and crops planted with their own harvest (barley, Jotun puffs), are fine.
                if (T.Filter.Count == 0 && IsFood(req.m_resItem) && WorkerPrefab.SaplingCrops.Any(o => o.Key != kv.Key && o.Value == seed)) continue;
                // Seeds for replanting what's ripe in the field come first: keep one for every ripe crop, and one for every
                // other farmer at work here (who may be about to replant).
                int amount = Mathf.Max(1, req.m_amount);
                int ripe = WorldIndex.Current.Pickables.Count(p => p != null && !p.GetPicked() && T.Area.Contains(p.transform.position)
                                                                   && WorkerPrefab.CropToPlant.TryGetValue(Utils.GetPrefabName(p.gameObject), out Piece s) && s == kv.Key);
                int working = WorkerAI.All.Count(w => w != W && w.TryGetClaim(out Vector3 c) && T.Area.Contains(c));
                if (Items.Count(W.Inv, seed) + Chests.CountAvailable(AllChests, seed) >= amount * (1 + ripe + working)) yield return kv.Key;
            }
        }

        /// <summary>
        /// Lays a grid over the area, as far apart as this crop needs (lined up with a box's sides), and returns the nearest
        /// point on cultivated ground where it has room to grow and no other farmer is at work.
        /// </summary>
        private bool NearestFreeSpot(Plant plant, Vector3 from, out Vector3 spot)
        {
            spot = Vector3.zero;
            AreaDef area = T.Area;
            float step = PlantSpace.Spacing(plant, plant);
            float halfW = area.IsBox ? area.W * 0.5f : area.R;
            float halfL = area.IsBox ? area.L * 0.5f : area.R;
            int nx = Mathf.Max(1, Mathf.FloorToInt(2f * halfW / step));
            int nz = Mathf.Max(1, Mathf.FloorToInt(2f * halfL / step));
            // Centered, so the leftover margin is the same on both sides.
            float x0 = -(nx - 1) * step * 0.5f, z0 = -(nz - 1) * step * 0.5f;

            List<KeyValuePair<float, Vector3>> candidates = new List<KeyValuePair<float, Vector3>>();
            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < nz; j++)
                {
                    Vector3 p = area.ToWorld(x0 + i * step, z0 + j * step);
                    if (!area.Contains(p) || !PlantSpace.Cultivated(p)) continue;
                    if (_badSpots.Any(kv => Utils.DistanceXZ(kv.Key, p) < 0.3f)) continue;
                    candidates.Add(new KeyValuePair<float, Vector3>(Utils.DistanceXZ(from, p), p));
                }
            }
            candidates.Sort((a, b) => a.Key.CompareTo(b.Key));
            int checks = 0;
            foreach (KeyValuePair<float, Vector3> c in candidates)
            {
                Vector3 p = c.Value;
                if (W.ClaimedByOther(p, step - PlantSpace.Tolerance, false)) continue;
                p.y = ZoneSystem.instance.GetGroundHeight(p);
                if (!PlantSpace.Grows(plant, p)) continue;
                if (PlantSpace.Free(p, plant))
                {
                    spot = p;
                    return true;
                }
                if (++checks >= MaxSpotChecks) break;
            }
            return false;
        }
    }
}
