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
    /// Works a field in rounds: picks the ripe crops (up to <see cref="RoundSize"/>), then stores the harvest and takes
    /// the seeds for all of them in one trip to the chests, and replants them. With an area set, a round also plants
    /// empty cultivated ground in it, far enough apart for every crop to grow. Planting takes a cultivator, like for a
    /// player. Several farmers can share a field: each claims the spots it's about to work on (see
    /// <see cref="WorkerAI.SetClaims"/>), and the others leave them alone.
    /// </summary>
    public class FarmingJob : HarvestJob
    {
        public FarmingJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected override bool NeedsTool => true;
        protected override string ToolName => "a cultivator";
        protected override string Verb => "Harvesting";
        protected override float Reach => 1.4f;
        protected override int DeliverAtSlots => 6;

        /// <summary>A tool that plants crops, like the cultivator: one with saplings among its build pieces.</summary>
        protected override bool IsTool(ItemDrop.ItemData i) =>
            i.m_shared.m_buildPieces != null && i.m_shared.m_buildPieces.m_pieces.Any(p => p != null && WorkerPrefab.SaplingCrops.Keys.Any(s => s.name == p.name));

        // The least worn one, so the others last.
        protected override float ToolScore(ItemDrop.ItemData i) => i.m_durability;

        private static bool CanPlant(ItemDrop.ItemData tool, Piece sapling) =>
            tool.m_shared.m_buildPieces.m_pieces.Any(p => p != null && p.name == sapling.name);

        // Crops picked before going back to replant them, so a big field doesn't mean claiming all of it at once.
        private const int RoundSize = 20;
        // New spots planted in a round at most.
        private const int SeedBatch = 10;
        // Seconds a claim has to stand before picking or planting (see WorkerAI.ClaimAge).
        private const float ClaimSettle = 1f;
        // From a couple of meters, like a player: in a planted field the path finder often stops short of the spot.
        private const float PlantReach = 2f;
        // Planting spots checked in one search at most, nearest first.
        private const int MaxSpotChecks = 200;

        /// <summary>A crop to plant: where one was just picked, or on empty ground.</summary>
        private class Planting
        {
            public Piece Sapling;
            public Plant Plant;
            public string Seed;
            public int Amount;
            public Vector3 Pos;
            public Quaternion Rot;
            public bool New;
            public Pickable Picked;
            public int Tries;
        }

        private readonly List<Planting> _round = new List<Planting>();
        private Planting _next;
        private bool _stored;
        private float _actAt = -1f;
        private int _swing;
        private float _noSpotsUntil;
        private float _seedWaitUntil;
        private readonly List<KeyValuePair<Vector3, float>> _badSpots = new List<KeyValuePair<Vector3, float>>();
        private readonly List<Vector3> _claims = new List<Vector3>();

        protected override Target FindTarget(Vector3 from, float radius)
        {
            // A full round is replanted before picking more.
            if (_round.Count(p => !p.New) >= RoundSize) return null;
            return Nearest(WorldIndex.Current.Pickables, from, radius,
                p => p != null && p.m_itemPrefab != null && !p.GetPicked() && WorkerPrefab.IsCrop(Utils.GetPrefabName(p.gameObject)) && T.Allows(p.m_itemPrefab.name)
                     && !W.ClaimedByOther(p.transform.position, 0.3f, false) && !_round.Any(r => Utils.DistanceXZ(r.Pos, p.transform.position) < 0.3f),
                p => Items.Nice(p.m_itemPrefab.name).ToLower());
        }

        public override void Start()
        {
            base.Start();
            // What's left of a round (the villager went to bed, or fought) is still planted.
            _next = null;
            _stored = false;
            _actAt = -1f;
            _seedWaitUntil = 0f;
        }

        public override void Stop()
        {
            base.Stop();
            _next = null;
            W.ReleaseClaims();
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            // Let the other farmers know which spots are taken.
            _claims.Clear();
            foreach (Planting p in _round) _claims.Add(p.Pos);
            if (Cur != null && (P == Phase.Approach || P == Phase.Work)) _claims.Add(Cur.Pos);
            W.SetClaims(_claims);
        }

        protected override R Work(float dt)
        {
            Pickable p = Cur.C as Pickable;
            if (!Alive(p) || p.GetPicked()) return R.Failed;
            W.Stop();
            W.Face(p.transform.position);
            if (_actAt < 0f)
            {
                if (W.ClaimAge(Cur.Pos) < ClaimSettle) return R.Running;
                // Another farmer went for the same crop at the same moment, and goes first.
                if (W.ClaimedByOther(p.transform.position, 0.3f, true)) return R.Failed;
                W.Anim.SetTrigger("interact");
                _actAt = Time.time + 0.6f / Plugin.WorkSpeed.Value;
                return R.Running;
            }
            if (Time.time < _actAt) return R.Running;
            _actAt = -1f;

            // Replanted with the rest of the round.
            if (WorkerPrefab.CropToPlant.TryGetValue(Utils.GetPrefabName(p.gameObject), out Piece sapling))
            {
                Planting replant = NewPlanting(sapling, Cur.Pos, p.transform.rotation, false);
                if (replant != null)
                {
                    replant.Picked = p;
                    _round.Add(replant);
                }
            }
            p.m_nview.InvokeRPC("RPC_Pick", 0);
            W.RaiseSkill(Type, 0.6f);
            Collector.Begin(Cur.Pos, 3f, 4f, 1f);
            return R.Done;
        }

        private static Planting NewPlanting(Piece sapling, Vector3 pos, Quaternion rot, bool isNew)
        {
            Piece.Requirement req = Seed(sapling);
            if (req == null) return null;
            return new Planting
            {
                Sapling = sapling, Plant = sapling.GetComponent<Plant>(), Seed = req.m_resItem.gameObject.name, Amount = Mathf.Max(1, req.m_amount),
                Pos = pos, Rot = rot, New = isNew,
            };
        }

        private static Piece.Requirement Seed(Piece sapling)
        {
            Piece.Requirement req = sapling != null && sapling.m_resources.Length > 0 ? sapling.m_resources[0] : null;
            return req != null && req.m_resItem != null ? req : null;
        }

        protected override bool HasOtherWork() => _round.Count > 0 || NextFreeSpot(W.transform.position, out _, out _);

        /// <summary>Nothing (more) to pick: plant the round, with new spots added to it.</summary>
        protected override bool StartOtherWork()
        {
            PlanNewSpots();
            if (_round.Count == 0) return false;
            P = Phase.After;
            _next = null;
            _stored = false;
            _seedWaitUntil = 0f;
            return true;
        }

        private Dictionary<string, int> SeedsForRound()
        {
            var seeds = new Dictionary<string, int>();
            foreach (Planting p in _round)
            {
                seeds.TryGetValue(p.Seed, out int n);
                seeds[p.Seed] = n + p.Amount;
            }
            return seeds;
        }

        protected override Phase AfterTick(float dt)
        {
            if (_round.Count == 0) return Phase.Search;
            // Search fetches or mends the cultivator, then comes back to the round.
            ItemDrop.ItemData tool = BestToolInInventory();
            if (tool == null) return Phase.Search;
            W.Equip(tool);

            // All the seeds for the round in one trip, storing the harvest on the way.
            Dictionary<string, int> seeds = SeedsForRound();
            if (Fetcher.Missing(W, seeds).Count > 0)
            {
                if (!_stored)
                {
                    R d = Delivery.Tick(W, OutputChests, i => !seeds.ContainsKey(Items.PrefabName(i)));
                    if (d == R.Running) return Phase.After;
                    // Full chests don't stop the planting.
                    _stored = true;
                }
                R f = Fetcher.Tick(W, AllChests, seeds, "seeds");
                if (f == R.Running) return Phase.After;
                Dictionary<string, int> missing = Fetcher.Missing(W, seeds);
                if (missing.Count > 0)
                {
                    // The seeds are there, but a chest can't be reached for a moment (another villager in the way makes
                    // the fetcher give up on it for a while): try again.
                    if (missing.Any(kv => Chests.CountAvailable(AllChests, kv.Key) > 0))
                    {
                        if (_seedWaitUntil <= 0f) _seedWaitUntil = Time.time + 40f;
                        if (Time.time < _seedWaitUntil)
                        {
                            W.SetStatus("Waiting to get seeds");
                            return Phase.After;
                        }
                    }
                    PlantOnlyWhatSeedsAllow();
                    if (_round.Count == 0) return Phase.Search;
                }
            }
            _seedWaitUntil = 0f;

            // The nearest one next.
            if (_next == null || !_round.Contains(_next))
                _next = _round.OrderBy(p => Utils.DistanceXZ(p.Pos, W.transform.position)).First();
            Planting next = _next;
            W.SetStatus((next.New ? "Planting " : "Replanting ") + Items.Nice(next.Seed));
            R m = W.MoveNear(next.Pos, PlantReach, false);
            if (m == R.Running) return Phase.After;
            if (m == R.Failed)
            {
                Plugin.Debug($"{W.GetName()} can't get to {next.Pos} to plant");
                // A gap in a planted field is worse than a skipped new spot: try a few more times (someone may have
                // been in the way).
                if (!next.New && ++next.Tries < 3) return Phase.After;
                if (next.New) _badSpots.Add(new KeyValuePair<Vector3, float>(next.Pos, Time.time + 300f));
                Drop(next);
                return Phase.After;
            }
            if (W.ClaimAge(next.Pos) < ClaimSettle) return Phase.After;

            bool taken = next.New
                ? W.ClaimedByOther(next.Pos, PlantSpace.Spacing(next.Plant, next.Plant) - PlantSpace.Tolerance, true) || !PlantSpace.Free(next.Pos, next.Plant)
                // Replanting where a crop just grew: only make sure nobody replanted it already.
                : W.ClaimedByOther(next.Pos, 0.3f, true) || PlantSpace.Occupied(next.Pos, next.Picked);
            if (taken)
            {
                Plugin.Debug($"{W.GetName()} finds {next.Pos} taken");
                if (next.New) _badSpots.Add(new KeyValuePair<Vector3, float>(next.Pos, Time.time + 60f));
                Drop(next);
                return Phase.After;
            }
            if (!CanPlant(tool, next.Sapling))
            {
                W.Ask("tool_" + next.Sapling.name, $"My {Localization.instance.Localize(tool.m_shared.m_name)} can't plant {Items.Nice(next.Seed)}.");
                Drop(next);
                return Phase.After;
            }

            W.Face(next.Pos);
            W.Anim.SetTrigger(SwingTrigger(tool.m_shared.m_attack, ref _swing) ?? "interact");
            GameObject planted = Object.Instantiate(next.Sapling.gameObject, next.Pos, next.Rot);
            next.Sapling.m_placeEffect.Create(next.Pos, next.Rot, planted.transform);
            tool.m_shared.m_buildEffect.Create(W.transform.position, Quaternion.identity);
            Items.Remove(W.Inv, next.Seed, next.Amount);
            W.RaiseSkill(Type, 0.3f);
            if (next.New) W.LogEvent("planted " + Items.Nice(next.Seed));
            WearTool(tool);
            Drop(next);
            return Phase.After;
        }

        private void Drop(Planting p)
        {
            _round.Remove(p);
            if (_next == p) _next = null;
        }

        /// <summary>Out of some seed: keep as much of the round as the seeds at hand cover, replanting first.</summary>
        private void PlantOnlyWhatSeedsAllow()
        {
            foreach (IGrouping<string, Planting> group in _round.GroupBy(p => p.Seed).ToList())
            {
                int have = Items.Count(W.Inv, group.Key);
                int need = group.Sum(p => p.Amount);
                if (have >= need) continue;
                W.Ask("seeds_" + group.Key, $"I'm out of {Items.Nice(group.Key)} to plant the field.");
                Plugin.Debug($"{W.GetName()} has {have} {group.Key} for {need}");
                foreach (Planting p in group.OrderBy(p => p.New ? 0 : 1).ToList())
                {
                    if (have >= need) break;
                    Drop(p);
                    need -= p.Amount;
                }
                // Don't come straight back for new spots.
                _noSpotsUntil = Time.time + 15f;
            }
        }

        // ------------------------------------------------------------------ planting new crops

        /// <summary>Adds the nearest empty spots in the area to the round, one after the other, as far as the seeds go.</summary>
        private void PlanNewSpots()
        {
            if (!HasArea || Time.time < _noSpotsUntil) return;
            ItemDrop.ItemData tool = BestToolInInventory();
            if (tool == null) return;
            int added = 0;
            Vector3 from = W.transform.position;
            while (_round.Count(p => p.New) < SeedBatch && NextFreeSpot(from, out Piece sapling, out Vector3 spot, tool))
            {
                _round.Add(NewPlanting(sapling, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), true));
                from = spot;
                added++;
            }
            // Nothing to plant: don't search the whole field again for a while.
            if (added == 0)
            {
                _noSpotsUntil = Time.time + 15f;
                Plugin.Debug($"{W.GetName()} finds nothing to plant");
            }
        }

        /// <summary>
        /// The nearest empty spot in the area where one of the crops this task may grow can be planted, with seeds left
        /// for it (and the tool, when given, can plant it).
        /// </summary>
        private bool NextFreeSpot(Vector3 from, out Piece sapling, out Vector3 spot, ItemDrop.ItemData tool = null)
        {
            sapling = null;
            spot = Vector3.zero;
            if (!HasArea || Time.time < _noSpotsUntil) return false;
            _badSpots.RemoveAll(kv => Time.time > kv.Value);
            float best = float.MaxValue;
            foreach (Piece p in PlantableSaplings())
            {
                if (tool != null && !CanPlant(tool, p)) continue;
                if (!NearestFreeSpot(p.GetComponent<Plant>(), from, out Vector3 s)) continue;
                float d = Utils.DistanceXZ(from, s);
                if (d >= best) continue;
                best = d;
                sapling = p;
                spot = s;
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
                // Seeds for replanting come first: keep one for every ripe crop in the field, one for every spot another
                // farmer is at work on, and the ones this round already needs.
                int amount = Mathf.Max(1, req.m_amount);
                int ripe = WorldIndex.Current.Pickables.Count(p => p != null && !p.GetPicked() && T.Area.Contains(p.transform.position)
                                                                   && WorkerPrefab.CropToPlant.TryGetValue(Utils.GetPrefabName(p.gameObject), out Piece s) && s == kv.Key);
                int working = WorkerAI.All.Where(w => w != W).Sum(w => w.Claims.Count(c => T.Area.Contains(c)));
                int planned = _round.Where(p => p.Seed == seed).Sum(p => p.Amount);
                int supply = Items.Count(W.Inv, seed) + Chests.CountAvailable(AllChests, seed);
                if (supply - planned >= amount * (1 + ripe + working)) yield return kv.Key;
            }
        }

        /// <summary>
        /// Lays a grid over the area, as far apart as this crop needs (lined up with a box's sides), and returns the nearest
        /// point on cultivated ground where it has room to grow, isn't planned in this round and no other farmer is at work.
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
                if (_round.Any(r => Utils.DistanceXZ(r.Pos, p) < PlantSpace.Spacing(plant, r.Plant) - PlantSpace.Tolerance)) continue;
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
