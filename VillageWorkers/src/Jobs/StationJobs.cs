using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillageWorkers.Jobs
{
    /// <summary>
    /// Keeps a stock of crafted items (by recipe) in the output chests. Used by the crafting job and as an
    /// extra for cooks (cauldron food) and brewers (mead bases).
    /// </summary>
    public class CraftWork
    {
        private enum S { Choose, Fetch, Go, Craft, Deliver }

        private readonly Job _job;
        private readonly WorkerAI _w;
        private readonly TaskConfig _t;
        private readonly Fetcher _fetcher = new Fetcher();
        private readonly Delivery _delivery = new Delivery();
        private S _s = S.Choose;
        private Recipe _recipe;
        private CraftingStation _station;
        private Dictionary<string, int> _mats;
        private float _craftEnd;
        public string Reason;

        public CraftWork(Job job, WorkerAI w, TaskConfig t)
        {
            _job = job;
            _w = w;
            _t = t;
        }

        public bool Active => _s != S.Choose;

        public static Recipe RecipeFor(string itemPrefab)
        {
            if (ObjectDB.instance == null) return null;
            return ObjectDB.instance.m_recipes.FirstOrDefault(r => r != null && r.m_enabled && r.m_item != null && r.m_item.gameObject.name == itemPrefab);
        }

        private List<ObjRef> OutputChests => _t.Chests.Count > 0 ? _t.Chests : _w.Config.Chests;

        private List<ObjRef> AllChests
        {
            get
            {
                WorkerConfig cfg = _w.Config;
                List<ObjRef> all = new List<ObjRef>(_t.Chests);
                foreach (ObjRef r in cfg.Chests.Concat(cfg.Tasks.SelectMany(t => t.Chests)))
                    if (!all.Any(a => a.Same(r))) all.Add(r);
                return all;
            }
        }

        private float Range => _t.Area != null ? _t.Area.R : Plugin.StationRadius.Value;
        private Vector3 Center => _t.Area != null ? _t.Area.Center : _w.Home;

        public static Dictionary<string, int> Materials(Recipe recipe)
        {
            Dictionary<string, int> mats = new Dictionary<string, int>();
            foreach (Piece.Requirement req in recipe.m_resources)
            {
                if (req.m_resItem == null) continue;
                int amount = req.GetAmount(1);
                if (amount <= 0) continue;
                string name = req.m_resItem.gameObject.name;
                mats.TryGetValue(name, out int prev);
                mats[name] = prev + amount;
                if (recipe.m_requireOnlyOneIngredient) break;
            }
            return mats;
        }

        private CraftingStation FindStation(Recipe recipe, out string reason)
        {
            reason = null;
            CraftingStation need = recipe.m_craftingStation;
            if (need == null) return null;
            CraftingStation best = null;
            float bestD = float.MaxValue;
            bool any = false;
            foreach (CraftingStation s in WorldIndex.CraftingStations)
            {
                if (s == null || s.m_name != need.m_name || Utils.DistanceXZ(s.transform.position, Center) > Range) continue;
                any = true;
                if (s.GetLevel() < recipe.m_minStationLevel)
                {
                    reason = $"{Localization.instance.Localize(need.m_name)} level {recipe.m_minStationLevel} needed";
                    continue;
                }
                if (s.m_craftRequireFire && !s.m_haveFire)
                {
                    reason = $"the fire under the {Localization.instance.Localize(need.m_name)} is out";
                    continue;
                }
                float d = Vector3.Distance(_w.transform.position, s.transform.position);
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            if (!any) reason = $"no {Localization.instance.Localize(need.m_name)} nearby";
            return best;
        }

        /// <summary>Finds the next order that is below its stock level and can be crafted right now.</summary>
        public bool FindOrder(out Recipe recipe, out CraftingStation station, out string reason)
        {
            recipe = null;
            station = null;
            reason = _t.Orders.Count == 0 ? "no crafting orders" : "everything is in stock";
            foreach (CraftOrder order in _t.Orders)
            {
                Recipe r = RecipeFor(order.Item);
                if (r == null) continue;
                int stock = Chests.CountAvailable(OutputChests, order.Item) + Items.Count(_w.Inv, order.Item);
                if (stock >= order.Keep) continue;

                CraftingStation s = FindStation(r, out string sr);
                if (r.m_craftingStation != null && s == null)
                {
                    reason = sr;
                    continue;
                }
                bool haveAll = true;
                foreach (KeyValuePair<string, int> m in Materials(r))
                {
                    int have = Chests.CountAvailable(AllChests, m.Key) + Items.Count(_w.Inv, m.Key);
                    if (have < m.Value)
                    {
                        haveAll = false;
                        reason = $"not enough {Items.Nice(m.Key)} for {Items.Nice(order.Item)}";
                        break;
                    }
                }
                if (!haveAll) continue;
                recipe = r;
                station = s;
                return true;
            }
            return false;
        }

        public bool HasWork(out string reason)
        {
            if (Active) { reason = null; return true; }
            if (!Chests.HasSpace(OutputChests, out reason)) return false;
            return FindOrder(out _, out _, out reason);
        }

        public void Reset()
        {
            if (_s == S.Craft) _w.Anim.SetInt("crafting", 0);
            _s = S.Choose;
            _fetcher.Reset();
            _delivery.Reset();
        }

        public R Tick(float dt)
        {
            switch (_s)
            {
                case S.Choose:
                    if (!FindOrder(out _recipe, out _station, out Reason)) return R.Failed;
                    _mats = Materials(_recipe);
                    _s = S.Fetch;
                    _fetcher.Reset();
                    return R.Running;

                case S.Fetch:
                {
                    R r = _fetcher.Tick(_w, AllChests, _mats, "materials for " + Items.Nice(_recipe.m_item.gameObject.name));
                    if (r == R.Failed)
                    {
                        Reason = "couldn't get the materials";
                        _s = S.Choose;
                        return R.Failed;
                    }
                    if (r == R.Done) _s = S.Go;
                    return R.Running;
                }

                case S.Go:
                {
                    if (_recipe.m_craftingStation != null && (_station == null || !_station.m_nview.IsValid()))
                    {
                        _s = S.Choose;
                        return R.Running;
                    }
                    Vector3 target = _station != null ? _station.transform.position : _w.Home;
                    float dist = _station != null ? Mathf.Max(1.2f, _station.m_useDistance) : 3f;
                    _w.SetStatus($"Going to craft {Items.Nice(_recipe.m_item.gameObject.name)}");
                    R m = _w.MoveNear(target, dist, false);
                    if (m == R.Failed)
                    {
                        if (_station != null) _w.Blacklist(_station, 60f);
                        Reason = "can't reach the crafting station";
                        _s = S.Choose;
                        return R.Failed;
                    }
                    if (m == R.Done)
                    {
                        _w.Stop();
                        _w.UnequipHands();
                        _w.Face(target);
                        if (_station != null)
                        {
                            _w.Anim.SetInt("crafting", _station.m_useAnimation);
                            _station.m_craftItemEffects.Create(_w.transform.position, Quaternion.identity);
                        }
                        else _w.Anim.SetTrigger("interact");
                        float speed = _w.SkillFactor(_job.Type) * Plugin.WorkSpeed.Value;
                        _craftEnd = Time.time + Plugin.CraftSeconds.Value / Mathf.Max(0.25f, speed);
                        _s = S.Craft;
                    }
                    return R.Running;
                }

                case S.Craft:
                {
                    string product = _recipe.m_item.gameObject.name;
                    _w.SetStatus("Crafting " + Items.Nice(product));
                    _job.Busy = true;
                    if (_station != null) _station.PokeInUse();
                    if (Time.time < _craftEnd) return R.Running;
                    _w.Anim.SetInt("crafting", 0);
                    _job.Busy = false;

                    if (Fetcher.Missing(_w, _mats).Count > 0)
                    {
                        _s = S.Choose;
                        return R.Running;
                    }
                    foreach (KeyValuePair<string, int> m in _mats) Items.Remove(_w.Inv, m.Key, m.Value);
                    ItemDrop.ItemData made = _w.Inv.AddItem(product, _recipe.m_amount, 1, 0, _w.Config.Employer, _w.GetName(), false);
                    if (made == null)
                    {
                        ItemDrop drop = ItemDrop.DropItem(_recipe.m_item.m_itemData, _recipe.m_amount, _w.transform.position + Vector3.up, Quaternion.identity);
                        if (drop != null)
                        {
                            drop.m_itemData.m_crafterName = _w.GetName();
                        }
                    }
                    if (_station != null) _station.m_craftItemDoneEffects.Create(_w.transform.position, Quaternion.identity);
                    _w.Record(_job.Type, product, _recipe.m_amount);
                    _w.LogEvent($"crafted {_recipe.m_amount} {Items.Nice(product)}");
                    _w.RaiseSkill(_job.Type, 1.5f);
                    _s = S.Deliver;
                    _delivery.Reset();
                    return R.Running;
                }

                case S.Deliver:
                {
                    R r = _delivery.Tick(_w, OutputChests, null);
                    if (r == R.Running) return R.Running;
                    _s = S.Choose;
                    if (r == R.Failed)
                    {
                        Reason = _delivery.FailReason;
                        return R.Failed;
                    }
                    return R.Done;
                }
            }
            return R.Failed;
        }
    }

    public class CraftingJob : Job
    {
        private readonly CraftWork _craft;

        public CraftingJob(WorkerAI w, TaskConfig t) : base(w, t)
        {
            _craft = new CraftWork(this, w, t);
        }

        public override bool CanStart(out string reason) => _craft.HasWork(out reason);

        public override void Start()
        {
            base.Start();
            _craft.Reset();
        }

        public override void Stop()
        {
            _craft.Reset();
            base.Stop();
        }

        public override void Tick(float dt)
        {
            R r = _craft.Tick(dt);
            if (r == R.Failed) Block(_craft.Reason, 25f);
        }
    }

    /// <summary>
    /// Base for jobs that tend production stations: plan what a station needs, fetch inputs from the chests,
    /// walk over, feed it, collect the output, store it.
    /// </summary>
    public abstract class StationJob : Job
    {
        protected enum S { Plan, Fetch, Go, Use, Collect, Deliver, Wait, Craft }

        protected class Plan
        {
            public Component Station;
            public Dictionary<string, int> Inputs = new Dictionary<string, int>();
            public bool Collect;
            public string What;
        }

        protected S St = S.Plan;
        protected Plan Cur;
        protected readonly CraftWork Craft;
        private float _useAt;
        private float _waitUntil;

        protected StationJob(WorkerAI w, TaskConfig t) : base(w, t)
        {
            Craft = new CraftWork(this, w, t);
        }

        protected abstract IEnumerable<Component> Stations();
        protected abstract Plan MakePlan(Component station, bool checkChests);
        /// <summary>Feeds the station and/or triggers its output. Returns the point where output appears (or null).</summary>
        protected abstract Vector3? Use(Plan plan);
        protected virtual bool KeepsBusy(Component station) => false;
        protected virtual string Noun => "station";

        protected IEnumerable<TS> InRange<TS>(IEnumerable<TS> all) where TS : Component =>
            all.Where(s => s != null && InRange(s.transform.position, StationRange) && !W.IsBlacklisted(s) &&
                           (T.Filter.Count == 0 || T.Allows(Utils.GetPrefabName(s.gameObject)) || FilterIsInputs));

        protected virtual bool FilterIsInputs => false;

        protected int Available(string prefab) => Items.Count(W.Inv, prefab) + Chests.CountAvailable(AllChests, prefab);

        public override bool CanStart(out string reason)
        {
            if (JobInfo.ProducesItems(Type) && OutputChests.Count == 0)
            {
                reason = "no chest assigned";
                W.Ask("chest", "Where should I put my things? Please assign me a chest.");
                return false;
            }
            foreach (Component s in Stations())
                if (MakePlan(s, true) != null || KeepsBusy(s))
                {
                    reason = null;
                    return true;
                }
            if (T.Orders.Count > 0 && Craft.HasWork(out reason)) return true;
            reason = Stations().Any() ? $"no work at the {Noun}s" : $"no {Noun} nearby";
            return false;
        }

        public override void Start()
        {
            base.Start();
            St = S.Plan;
            Cur = null;
            Craft.Reset();
        }

        public override void Stop()
        {
            Craft.Reset();
            base.Stop();
        }

        public override void Tick(float dt)
        {
            switch (St)
            {
                case S.Plan:
                    DoPlan();
                    break;

                case S.Fetch:
                {
                    if (!Alive(Cur.Station)) { St = S.Plan; break; }
                    // Take what we can; a partially filled station is better than an empty one.
                    Dictionary<string, int> want = Cur.Inputs.ToDictionary(kv => kv.Key, kv => Mathf.Min(kv.Value, Available(kv.Key)));
                    R r = Fetcher.Tick(W, AllChests, want, Cur.What);
                    if (r == R.Done) St = S.Go;
                    else if (r == R.Failed)
                    {
                        if (Cur.Inputs.Keys.Any(k => Items.Count(W.Inv, k) > 0)) St = S.Go;
                        else
                        {
                            W.Blacklist(Cur.Station, 30f);
                            St = S.Plan;
                        }
                    }
                    break;
                }

                case S.Go:
                {
                    if (!Alive(Cur.Station)) { St = S.Plan; break; }
                    W.SetStatus($"Going to the {Localization.instance.Localize(StationName(Cur.Station))}");
                    R r = W.MoveNear(Cur.Station.transform.position, 2.2f, false);
                    if (r == R.Failed)
                    {
                        W.Blacklist(Cur.Station, 60f);
                        St = S.Plan;
                    }
                    else if (r == R.Done)
                    {
                        W.Stop();
                        W.UnequipHands();
                        W.Face(Cur.Station.transform.position);
                        W.Anim.SetTrigger("interact");
                        _useAt = Time.time + 0.5f;
                        St = S.Use;
                    }
                    break;
                }

                case S.Use:
                {
                    if (Time.time < _useAt) break;
                    if (!Alive(Cur.Station)) { St = S.Plan; break; }
                    Vector3? output = Use(Cur);
                    W.RaiseSkill(Type, 0.5f);
                    if (output.HasValue)
                    {
                        Collector.Begin(output.Value, 3f, 5f, 2f);
                        St = S.Collect;
                    }
                    else St = S.Plan;
                    break;
                }

                case S.Collect:
                    if (Collector.Tick(W, Type) != R.Running) St = S.Plan;
                    break;

                case S.Deliver:
                {
                    HashSet<string> keepInputs = KeepInputs();
                    R r = Delivery.Tick(W, OutputChests, i => !keepInputs.Contains(Items.PrefabName(i)));
                    if (r == R.Done) St = S.Plan;
                    else if (r == R.Failed) Block(Delivery.FailReason, 30f);
                    break;
                }

                case S.Wait:
                    Busy = true;
                    if (Time.time > _waitUntil)
                    {
                        Busy = false;
                        St = S.Plan;
                        break;
                    }
                    if (Cur != null && Cur.Station != null) W.Wander(dt, Cur.Station.transform.position, 3f);
                    break;

                case S.Craft:
                {
                    R r = Craft.Tick(dt);
                    if (r != R.Running) St = S.Plan;
                    if (r == R.Failed) Block(Craft.Reason, 20f);
                    break;
                }
            }
        }

        /// <summary>Inputs worth carrying around instead of putting them back.</summary>
        protected virtual HashSet<string> KeepInputs() => new HashSet<string>();

        private void DoPlan()
        {
            Busy = false;
            // Store the output before doing anything else when the pockets are getting full.
            if (W.LootSlots() >= 6 || W.Inv.GetEmptySlots() <= 1)
            {
                if (W.HasLoot() && St != S.Deliver)
                {
                    St = S.Deliver;
                    Delivery.Reset();
                    return;
                }
            }

            Plan best = null;
            float bestD = float.MaxValue;
            foreach (Component s in Stations())
            {
                Plan p = MakePlan(s, true);
                if (p == null) continue;
                float d = Vector3.Distance(W.transform.position, s.transform.position) - (p.Collect ? 15f : 0f);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
            if (best != null)
            {
                Cur = best;
                Fetcher.Reset();
                St = best.Inputs.Count > 0 ? S.Fetch : S.Go;
                return;
            }

            if (W.HasLoot())
            {
                St = S.Deliver;
                Delivery.Reset();
                return;
            }

            if (T.Orders.Count > 0 && Craft.HasWork(out _))
            {
                St = S.Craft;
                return;
            }

            Component busy = Stations().FirstOrDefault(KeepsBusy);
            if (busy != null)
            {
                Cur = new Plan { Station = busy };
                W.SetStatus($"Watching the {Localization.instance.Localize(StationName(busy))}");
                _waitUntil = Time.time + 3f;
                St = S.Wait;
                return;
            }

            Block($"no work at the {Noun}s", 20f);
        }

        protected static string StationName(Component c)
        {
            Piece p = c != null ? c.GetComponent<Piece>() : null;
            if (p != null) return p.m_name;
            Hoverable h = c as Hoverable;
            return h != null ? h.GetHoverName() : "station";
        }

        /// <summary>Picks the input with the most stock among the allowed ones.</summary>
        protected string PickInput(IEnumerable<string> candidates, bool checkChests)
        {
            string best = null;
            int bestN = 0;
            foreach (string c in candidates.Distinct())
            {
                int n = checkChests ? Available(c) : Items.Count(W.Inv, c);
                if (n > bestN)
                {
                    bestN = n;
                    best = c;
                }
            }
            return best;
        }
    }

    // ====================================================================== smelting

    public class SmeltingJob : StationJob
    {
        public SmeltingJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected override string Noun => "smelter";

        protected override IEnumerable<Component> Stations() => InRange(WorldIndex.Current.Smelters).Cast<Component>();

        protected override Plan MakePlan(Component c, bool checkChests)
        {
            Smelter s = (Smelter)c;
            if (s.m_nview == null || !s.m_nview.IsValid()) return null;
            Plan p = new Plan { Station = s, What = "supplies for the " + Localization.instance.Localize(StationName(s)) };

            p.Collect = s.GetProcessedQueueSize() > 0 || (s.m_outputPoint != null && OutputLying(s.m_outputPoint.position));
            if (p.Collect && W.IsBlacklisted(s)) p.Collect = false;

            int queue = s.GetQueueSize();
            int free = s.m_maxOre - queue;
            if (free > 0)
            {
                string ore = PickInput(s.m_conversion.Where(x => x.m_from != null).Select(x => x.m_from.gameObject.name), checkChests);
                if (ore != null) p.Inputs[ore] = free;
            }
            bool willWork = queue > 0 || p.Inputs.Count > 0;
            if (s.m_maxFuel > 0 && s.m_fuelItem != null && willWork)
            {
                int fuelFree = s.m_maxFuel - Mathf.CeilToInt(s.GetFuel());
                string fuel = s.m_fuelItem.gameObject.name;
                if (fuelFree > 0 && fuelFree >= s.m_maxFuel / 3 && Available(fuel) > 0) p.Inputs[fuel] = fuelFree;
            }
            // Don't start a smelter we can't fuel.
            if (s.m_maxFuel > 0 && s.GetFuel() <= 0f && !p.Inputs.ContainsKey(s.m_fuelItem != null ? s.m_fuelItem.gameObject.name : ""))
                p.Inputs.Clear();

            return p.Collect || p.Inputs.Count > 0 ? p : null;
        }

        private bool OutputLying(Vector3 point)
        {
            foreach (ItemDrop d in WorldIndex.ItemDrops)
                if (d != null && !W.IsBlacklisted(d) && Vector3.Distance(d.transform.position, point) < 2.5f) return true;
            return false;
        }

        protected override Vector3? Use(Plan plan)
        {
            Smelter s = (Smelter)plan.Station;
            int added = 0;
            foreach (KeyValuePair<string, int> kv in plan.Inputs)
            {
                bool isFuel = s.m_fuelItem != null && kv.Key == s.m_fuelItem.gameObject.name;
                int n = Mathf.Min(kv.Value, Items.Count(W.Inv, kv.Key));
                if (!isFuel) n = Mathf.Min(n, s.m_maxOre - s.GetQueueSize());
                for (int i = 0; i < n; i++)
                {
                    if (isFuel) s.m_nview.InvokeRPC("RPC_AddFuel");
                    else s.m_nview.InvokeRPC("RPC_AddOre", kv.Key, false);
                }
                Items.Remove(W.Inv, kv.Key, n);
                added += n;
                if (n > 0 && !isFuel) W.LogEvent($"put {n} {Items.Nice(kv.Key)} into the {Localization.instance.Localize(StationName(s))}");
            }
            if (s.GetProcessedQueueSize() > 0) s.m_nview.InvokeRPC("RPC_EmptyProcessed");
            if (s.m_outputPoint != null && (plan.Collect || s.GetProcessedQueueSize() > 0))
            {
                // Don't come straight back just for leftovers we can't pick up.
                if (plan.Inputs.Count == 0) W.Blacklist(s, 20f);
                return s.m_outputPoint.position;
            }
            return null;
        }

        protected override bool KeepsBusy(Component c) => false;
    }

    // ====================================================================== cooking

    public class CookingJob : StationJob
    {
        public CookingJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected override string Noun => "cooking station";
        protected override bool FilterIsInputs => true;

        protected override IEnumerable<Component> Stations() => InRange(WorldIndex.Current.CookingStations).Cast<Component>();

        private static int DoneSlots(CookingStation s)
        {
            int n = 0;
            for (int i = 0; i < s.m_slots.Length; i++)
            {
                s.GetSlot(i, out string item, out _, out _, out _);
                if (item != "" && s.IsItemDone(item)) n++;
            }
            return n;
        }

        private static int FreeSlots(CookingStation s)
        {
            int n = 0;
            for (int i = 0; i < s.m_slots.Length; i++)
                if (s.m_nview.GetZDO().GetString("slot" + i) == "") n++;
            return n;
        }

        private static bool Cooking(CookingStation s) => s.m_slots.Length - FreeSlots(s) - DoneSlots(s) > 0;

        protected override Plan MakePlan(Component c, bool checkChests)
        {
            CookingStation s = (CookingStation)c;
            if (s.m_nview == null || !s.m_nview.IsValid()) return null;
            Plan p = new Plan { Station = s, What = "food to cook" };
            p.Collect = DoneSlots(s) > 0;

            int free = FreeSlots(s);
            bool fuelOk = !s.m_useFuel || s.GetFuel() > 0f;
            bool fireOk = s.m_useFuel ? true : s.IsFireLit();
            if (free > 0 && fireOk)
            {
                string raw = PickInput(s.m_conversion.Where(x => x.m_from != null && T.Allows(x.m_from.gameObject.name))
                    .Select(x => x.m_from.gameObject.name), checkChests);
                if (raw != null)
                {
                    p.Inputs[raw] = free;
                    if (s.m_useFuel && s.m_fuelItem != null)
                    {
                        string fuel = s.m_fuelItem.gameObject.name;
                        int fuelFree = s.m_maxFuel - Mathf.CeilToInt(s.GetFuel());
                        if (fuelFree > 0 && Available(fuel) > 0) p.Inputs[fuel] = fuelFree;
                        else if (!fuelOk) p.Inputs.Remove(raw);
                    }
                }
            }
            return p.Collect || p.Inputs.Count > 0 ? p : null;
        }

        protected override bool KeepsBusy(Component c) => Cooking((CookingStation)c);

        protected override Vector3? Use(Plan plan)
        {
            CookingStation s = (CookingStation)plan.Station;
            Vector3 pos = W.transform.position;
            int done = DoneSlots(s);
            for (int i = 0; i < done; i++) s.m_nview.InvokeRPC("RPC_RemoveDoneItem", pos, 1);

            if (s.m_useFuel && s.m_fuelItem != null)
            {
                string fuel = s.m_fuelItem.gameObject.name;
                int n = Mathf.Min(Items.Count(W.Inv, fuel), s.m_maxFuel - Mathf.CeilToInt(s.GetFuel()));
                for (int i = 0; i < n; i++) s.m_nview.InvokeRPC("RPC_AddFuel");
                Items.Remove(W.Inv, fuel, n);
            }

            int free = FreeSlots(s);
            foreach (string raw in plan.Inputs.Keys.ToList())
            {
                if (s.m_fuelItem != null && raw == s.m_fuelItem.gameObject.name) continue;
                int n = Mathf.Min(free, Items.Count(W.Inv, raw));
                for (int i = 0; i < n; i++) s.m_nview.InvokeRPC("RPC_AddItem", raw, false);
                Items.Remove(W.Inv, raw, n);
                free -= n;
                if (n > 0) W.LogEvent($"started cooking {n} {Items.Nice(raw)}");
            }
            return done > 0 ? (Vector3?)(s.m_spawnPoint != null ? s.m_spawnPoint.position : s.transform.position) : null;
        }
    }

    // ====================================================================== brewing

    public class BrewingJob : StationJob
    {
        public BrewingJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected override string Noun => "fermenter";
        protected override bool FilterIsInputs => true;

        protected override IEnumerable<Component> Stations() => InRange(WorldIndex.Current.Fermenters).Cast<Component>();

        protected override Plan MakePlan(Component c, bool checkChests)
        {
            Fermenter f = (Fermenter)c;
            if (f.m_nview == null || !f.m_nview.IsValid()) return null;
            Fermenter.Status st = f.GetStatus();
            Plan p = new Plan { Station = f, What = "a mead base" };
            if (st == Fermenter.Status.Ready)
            {
                p.Collect = true;
                return p;
            }
            if (st != Fermenter.Status.Empty) return null;
            string input = PickInput(f.m_conversion.Where(x => x.m_from != null && T.Allows(x.m_from.gameObject.name))
                .Select(x => x.m_from.gameObject.name), checkChests);
            if (input == null) return null;
            p.Inputs[input] = 1;
            return p;
        }

        protected override Vector3? Use(Plan plan)
        {
            Fermenter f = (Fermenter)plan.Station;
            Fermenter.Status st = f.GetStatus();
            if (st == Fermenter.Status.Ready)
            {
                f.m_nview.InvokeRPC("RPC_Tap");
                W.LogEvent("tapped a fermenter");
                return f.m_outputPoint != null ? f.m_outputPoint.position : f.transform.position;
            }
            if (st == Fermenter.Status.Empty)
            {
                string input = plan.Inputs.Keys.FirstOrDefault(k => Items.Count(W.Inv, k) > 0);
                if (input != null)
                {
                    f.m_nview.InvokeRPC("RPC_AddItem", input.GetStableHashCode(), false);
                    Items.Remove(W.Inv, input, 1);
                    W.LogEvent($"started fermenting {Items.Nice(input)}");
                    W.Record(Type, "brew:" + input, 1);
                }
            }
            return null;
        }
    }

    // ====================================================================== fire keeping

    public class FuelingJob : StationJob
    {
        public FuelingJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected override string Noun => "fire";

        protected override IEnumerable<Component> Stations() => InRange(WorldIndex.Current.Fireplaces).Cast<Component>();

        private static float Fuel(Fireplace f) => f.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);

        public override bool CanStart(out string reason)
        {
            foreach (Component s in Stations())
                if (MakePlan(s, true) != null)
                {
                    reason = null;
                    return true;
                }
            reason = Stations().Any() ? "all fires are burning" : "no fires nearby";
            return false;
        }

        protected override Plan MakePlan(Component c, bool checkChests)
        {
            Fireplace f = (Fireplace)c;
            if (f.m_nview == null || !f.m_nview.IsValid() || f.m_infiniteFuel || f.m_fuelItem == null) return null;
            if (f.GetComponent<Piece>() == null) return null;
            float fuel = Fuel(f);
            if (fuel > f.m_maxFuel * 0.5f) return null;
            string item = f.m_fuelItem.gameObject.name;
            int need = Mathf.FloorToInt(f.m_maxFuel - fuel);
            if (need <= 0 || Available(item) <= 0) return null;
            Plan p = new Plan { Station = f, What = Items.Nice(item) + " for the fire" };
            p.Inputs[item] = need;
            return p;
        }

        protected override Vector3? Use(Plan plan)
        {
            Fireplace f = (Fireplace)plan.Station;
            foreach (KeyValuePair<string, int> kv in plan.Inputs)
            {
                int n = Mathf.Min(Items.Count(W.Inv, kv.Key), Mathf.FloorToInt(f.m_maxFuel - Fuel(f)));
                for (int i = 0; i < n; i++) f.m_nview.InvokeRPC("RPC_AddFuel");
                Items.Remove(W.Inv, kv.Key, n);
                if (n > 0) W.Record(Type, "fuel:" + kv.Key, n);
            }
            return null;
        }
    }
}
