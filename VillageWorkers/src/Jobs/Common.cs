using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillageWorkers.Jobs
{
    public static class Items
    {
        private static readonly Dictionary<string, string> SharedNames = new Dictionary<string, string>();

        public static GameObject Prefab(string prefabName) => ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;

        public static string PrefabName(ItemDrop.ItemData item)
        {
            if (item == null) return "";
            if (item.m_dropPrefab != null) return item.m_dropPrefab.name;
            return item.m_shared.m_name;
        }

        public static string Shared(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return "";
            if (SharedNames.TryGetValue(prefabName, out string s)) return s;
            GameObject p = Prefab(prefabName);
            ItemDrop d = p != null ? p.GetComponent<ItemDrop>() : null;
            s = d != null ? d.m_itemData.m_shared.m_name : prefabName;
            if (ObjectDB.instance != null) SharedNames[prefabName] = s;
            return s;
        }

        public static string Nice(string prefabName)
        {
            string shared = Shared(prefabName);
            if (shared.StartsWith("$") && Localization.instance != null) return Localization.instance.Localize(shared);
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            if (go != null)
            {
                Character c = go.GetComponent<Character>();
                if (c != null) return Localization.instance.Localize(c.m_name);
                Piece piece = go.GetComponent<Piece>();
                if (piece != null) return Localization.instance.Localize(piece.m_name);
            }
            return prefabName;
        }

        public static Sprite Icon(string prefabName)
        {
            GameObject p = Prefab(prefabName);
            ItemDrop d = p != null ? p.GetComponent<ItemDrop>() : null;
            if (d != null) return d.m_itemData.GetIcon();
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            Piece piece = go != null ? go.GetComponent<Piece>() : null;
            return piece != null ? piece.m_icon : null;
        }

        public static int Count(Inventory inv, string prefabName) => inv == null ? 0 : inv.CountItems(Shared(prefabName), -1, false);

        public static int SpaceFor(Inventory inv, ItemDrop.ItemData proto)
        {
            int max = Mathf.Max(1, proto.m_shared.m_maxStackSize);
            int free = proto.m_shared.m_maxStackSize > 1 ? inv.FindFreeStackSpace(proto.m_shared.m_name, proto.m_worldLevel) : 0;
            return free + inv.GetEmptySlots() * max;
        }

        /// <summary>Moves up to <paramref name="amount"/> of <paramref name="item"/> between inventories. Returns the amount moved.</summary>
        public static int Move(Inventory from, Inventory to, ItemDrop.ItemData item, int amount)
        {
            amount = Mathf.Min(amount, item.m_stack, SpaceFor(to, item));
            if (amount <= 0) return 0;
            ItemDrop.ItemData copy = item.Clone();
            copy.m_stack = amount;
            copy.m_equipped = false;
            if (!to.AddItem(copy)) return 0;
            if (amount >= item.m_stack) from.RemoveItem(item);
            else from.RemoveItem(item, amount);
            return amount;
        }

        /// <summary>Removes <paramref name="amount"/> items by prefab name. Returns how many were removed.</summary>
        public static int Remove(Inventory inv, string prefabName, int amount)
        {
            string shared = Shared(prefabName);
            int have = inv.CountItems(shared, -1, false);
            int n = Mathf.Min(have, amount);
            if (n > 0) inv.RemoveItem(shared, n, -1, false);
            return n;
        }

        public static string Summary(Dictionary<string, int> moved)
        {
            return string.Join(", ", moved.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Value} {Nice(kv.Key)}"))
                   + (moved.Count > 4 ? ", ..." : "");
        }
    }

    public static class Chests
    {
        public static IEnumerable<Container> Resolve(IEnumerable<ObjRef> refs)
        {
            if (refs == null) yield break;
            foreach (ObjRef r in refs)
            {
                Container c = r.Resolve<Container>();
                if (c != null && c.GetInventory() != null) yield return c;
            }
        }

        public static bool Accessible(Container c)
        {
            if (c == null || c.m_nview == null || !c.m_nview.IsValid() || c.GetInventory() == null) return false;
            if (c.m_nview.IsOwner()) return !c.IsInUse();
            return c.m_nview.GetZDO().GetInt(ZDOVars.s_inUse) == 0;
        }

        /// <summary>Takes network ownership so the change is saved and synced.</summary>
        public static Inventory Claim(Container c)
        {
            if (!c.m_nview.IsOwner()) c.m_nview.ClaimOwnership();
            // Our copy may be up to a second old; reload before changing it so nothing is overwritten.
            if (c.m_nview.GetZDO().DataRevision != c.m_lastRevision) c.Load();
            return c.GetInventory();
        }

        public static int CountAvailable(IEnumerable<ObjRef> refs, string prefabName)
        {
            int n = 0;
            foreach (Container c in Resolve(refs)) n += Items.Count(c.GetInventory(), prefabName);
            return n;
        }

        public static bool HasSpace(IEnumerable<ObjRef> refs, out string reason)
        {
            List<ObjRef> list = refs?.ToList() ?? new List<ObjRef>();
            if (list.Count == 0)
            {
                reason = "no chest assigned";
                return false;
            }
            bool any = false;
            foreach (Container c in Resolve(list))
            {
                any = true;
                if (c.GetInventory().GetEmptySlots() > 0)
                {
                    reason = null;
                    return true;
                }
            }
            reason = any ? "chests are full" : "chests are out of reach";
            return false;
        }
    }

    /// <summary>Brings everything the worker doesn't need to the nearest chest with room.</summary>
    public class Delivery
    {
        private ObjRef _target;
        private int _failures;
        public string FailReason;

        public void Reset()
        {
            _target = null;
            _failures = 0;
        }

        public R Tick(WorkerAI w, List<ObjRef> chests, Func<ItemDrop.ItemData, bool> filter)
        {
            HashSet<ItemDrop.ItemData> keep = w.ReservedItems();
            List<ItemDrop.ItemData> loot = w.Inv.GetAllItems().Where(i => !keep.Contains(i) && (filter == null || filter(i))).ToList();
            if (loot.Count == 0)
            {
                Reset();
                return R.Done;
            }
            if (chests == null || chests.Count == 0)
            {
                FailReason = "no chest assigned";
                w.Ask("chest", "Where should I put my things? Please assign me a chest.");
                return R.Failed;
            }

            Container chest = _target?.Resolve<Container>();
            if (chest == null)
            {
                _target = null;
                float best = float.MaxValue;
                bool anyLoaded = false;
                foreach (Container c in Chests.Resolve(chests))
                {
                    anyLoaded = true;
                    if (!Chests.Accessible(c) || w.IsBlacklisted(c)) continue;
                    Inventory inv = c.GetInventory();
                    if (!loot.Any(i => Items.SpaceFor(inv, i) > 0)) continue;
                    float d = Vector3.Distance(w.transform.position, c.transform.position);
                    if (d < best)
                    {
                        best = d;
                        chest = c;
                    }
                }
                if (chest == null)
                {
                    FailReason = anyLoaded ? "chests are full" : "chests are out of reach";
                    if (anyLoaded) w.Ask("chestfull", "My chests are full! I can't store anything more.");
                    return R.Failed;
                }
                _target = new ObjRef(chest.m_nview);
            }

            w.SetStatus("Bringing things to a chest");
            R m = w.MoveNear(chest.transform.position, 1.9f, false);
            if (m == R.Failed)
            {
                w.Blacklist(chest, 30f);
                _target = null;
                if (++_failures > 3)
                {
                    FailReason = "can't reach my chests";
                    return R.Failed;
                }
                return R.Running;
            }
            if (m == R.Running) return R.Running;

            w.Stop();
            w.Face(chest.transform.position);
            w.Anim.SetTrigger("interact");
            Inventory target = Chests.Claim(chest);
            Dictionary<string, int> moved = new Dictionary<string, int>();
            foreach (ItemDrop.ItemData item in loot)
            {
                string name = Items.PrefabName(item);
                int n = Items.Move(w.Inv, target, item, item.m_stack);
                if (n <= 0) continue;
                moved.TryGetValue(name, out int prev);
                moved[name] = prev + n;
            }
            if (moved.Count > 0) w.LogEvent("stored " + Items.Summary(moved));
            else w.Blacklist(chest, 20f);
            _target = null;
            return R.Running;
        }
    }

    /// <summary>Collects specific items (by prefab name) from chests into the worker's inventory.</summary>
    public class Fetcher
    {
        private ObjRef _target;
        private int _failures;

        public void Reset()
        {
            _target = null;
            _failures = 0;
        }

        public static Dictionary<string, int> Missing(WorkerAI w, Dictionary<string, int> want)
        {
            Dictionary<string, int> missing = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> kv in want)
            {
                int have = Items.Count(w.Inv, kv.Key);
                if (have < kv.Value) missing[kv.Key] = kv.Value - have;
            }
            return missing;
        }

        public R Tick(WorkerAI w, List<ObjRef> chests, Dictionary<string, int> want, string what)
        {
            Dictionary<string, int> missing = Missing(w, want);
            if (missing.Count == 0)
            {
                Reset();
                return R.Done;
            }

            Container chest = _target?.Resolve<Container>();
            if (chest == null)
            {
                _target = null;
                float best = float.MaxValue;
                foreach (Container c in Chests.Resolve(chests))
                {
                    if (!Chests.Accessible(c) || w.IsBlacklisted(c)) continue;
                    if (!missing.Keys.Any(k => Items.Count(c.GetInventory(), k) > 0)) continue;
                    float d = Vector3.Distance(w.transform.position, c.transform.position);
                    if (d < best)
                    {
                        best = d;
                        chest = c;
                    }
                }
                if (chest == null) return R.Failed;
                _target = new ObjRef(chest.m_nview);
            }

            w.SetStatus("Fetching " + what);
            R m = w.MoveNear(chest.transform.position, 1.9f, false);
            if (m == R.Failed)
            {
                w.Blacklist(chest, 30f);
                _target = null;
                return ++_failures > 3 ? R.Failed : R.Running;
            }
            if (m == R.Running) return R.Running;

            w.Stop();
            w.Face(chest.transform.position);
            w.Anim.SetTrigger("interact");
            Inventory inv = Chests.Claim(chest);
            bool tookAny = false;
            foreach (KeyValuePair<string, int> kv in missing)
            {
                string shared = Items.Shared(kv.Key);
                int need = kv.Value;
                foreach (ItemDrop.ItemData item in inv.GetAllItems().Where(i => i.m_shared.m_name == shared).ToList())
                {
                    if (need <= 0) break;
                    int n = Items.Move(inv, w.Inv, item, need);
                    need -= n;
                    tookAny |= n > 0;
                }
            }
            _target = null;
            if (!tookAny)
            {
                // Inventory full or the items vanished - don't loop forever on this chest.
                w.Blacklist(chest, 20f);
                if (w.Inv.GetEmptySlots() == 0) return R.Failed;
            }
            return R.Running;
        }
    }

    /// <summary>Fetches a single specific item (e.g. a tool) from a chest.</summary>
    public class ItemFetcher
    {
        private ObjRef _chest;

        public void Reset() => _chest = null;

        public static ItemDrop.ItemData Find(IEnumerable<ObjRef> chests, Func<ItemDrop.ItemData, bool> match, Func<ItemDrop.ItemData, float> score, out Container chest)
        {
            chest = null;
            ItemDrop.ItemData best = null;
            float bestScore = float.MinValue;
            foreach (Container c in Chests.Resolve(chests))
            {
                if (!Chests.Accessible(c)) continue;
                foreach (ItemDrop.ItemData i in c.GetInventory().GetAllItems())
                {
                    if (!match(i)) continue;
                    float s = score(i);
                    if (s > bestScore)
                    {
                        bestScore = s;
                        best = i;
                        chest = c;
                    }
                }
            }
            return best;
        }

        public R Tick(WorkerAI w, IEnumerable<ObjRef> chests, Func<ItemDrop.ItemData, bool> match, Func<ItemDrop.ItemData, float> score, string what)
        {
            if (w.Inv.GetAllItems().Any(match)) return R.Done;
            Container chest = _chest?.Resolve<Container>();
            if (chest == null)
            {
                if (Find(chests, match, score, out chest) == null) return R.Failed;
                _chest = new ObjRef(chest.m_nview);
            }
            w.SetStatus("Fetching " + what);
            R m = w.MoveNear(chest.transform.position, 1.9f, false);
            if (m == R.Failed)
            {
                _chest = null;
                return R.Failed;
            }
            if (m == R.Running) return R.Running;

            w.Stop();
            w.Face(chest.transform.position);
            w.Anim.SetTrigger("interact");
            Inventory inv = Chests.Claim(chest);
            ItemDrop.ItemData item = inv.GetAllItems().Where(match).OrderByDescending(score).FirstOrDefault();
            _chest = null;
            if (item == null) return R.Running;
            int stack = string.IsNullOrEmpty(item.m_shared.m_ammoType) || item.IsWeapon() ? 1 : item.m_stack;
            if (Items.Move(inv, w.Inv, item, Math.Max(1, stack)) <= 0) return R.Failed;
            w.LogEvent("took " + Localization.instance.Localize(item.m_shared.m_name) + " from a chest");
            return R.Done;
        }
    }

    /// <summary>
    /// Takes broken tools to a nearby crafting station and mends them, with the same rules as the player's
    /// repair button (right station type, high enough level).
    /// </summary>
    public class Repairer
    {
        private CraftingStation _station;
        private float _doneAt = -1f;

        public void Reset()
        {
            _station = null;
            _doneAt = -1f;
        }

        public static bool Broken(ItemDrop.ItemData i) => i.m_shared.m_useDurability && i.m_durability <= 0f;

        public static bool CanRepair(ItemDrop.ItemData item, CraftingStation s)
        {
            if (item == null || s == null || !item.m_shared.m_canBeReparied) return false;
            if (s.m_craftRequireFire && !s.m_haveFire) return false;
            Recipe r = ObjectDB.instance.GetRecipe(item);
            if (r == null || (r.m_craftingStation == null && r.m_repairStation == null)) return false;
            bool right = (r.m_repairStation != null && r.m_repairStation.m_name == s.m_name) ||
                         (r.m_craftingStation != null && r.m_craftingStation.m_name == s.m_name) ||
                         item.m_worldLevel < Game.m_worldLevel;
            return right && Mathf.Min(s.GetLevel(), 4) >= r.m_minStationLevel;
        }

        /// <summary>Nearest station near the worker or their home that can repair the item.</summary>
        public static CraftingStation FindStation(WorkerAI w, ItemDrop.ItemData item)
        {
            float range = Plugin.StationRadius.Value;
            CraftingStation best = null;
            float bestD = float.MaxValue;
            foreach (CraftingStation s in WorldIndex.CraftingStations)
            {
                if (s == null || s.m_nview == null || !s.m_nview.IsValid() || w.IsBlacklisted(s)) continue;
                float d = Utils.DistanceXZ(s.transform.position, w.transform.position);
                if (d > range && Utils.DistanceXZ(s.transform.position, w.Home) > range) continue;
                if (d < bestD && CanRepair(item, s))
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>A broken tool in the worker's pockets that some nearby station can fix.</summary>
        public static ItemDrop.ItemData NextBroken(WorkerAI w, Func<ItemDrop.ItemData, bool> isTool, out CraftingStation station)
        {
            station = null;
            foreach (ItemDrop.ItemData i in w.Inv.GetAllItems())
            {
                if (!Broken(i) || !isTool(i)) continue;
                station = FindStation(w, i);
                if (station != null) return i;
            }
            return null;
        }

        /// <summary>Done when nothing repairable is left, Failed when the station can't be reached.</summary>
        public R Tick(WorkerAI w, Func<ItemDrop.ItemData, bool> isTool)
        {
            ItemDrop.ItemData item = NextBroken(w, isTool, out CraftingStation found);
            if (item == null)
            {
                if (_doneAt > 0f) w.Anim.SetInt("crafting", 0);
                Reset();
                return R.Done;
            }
            if (_station == null || !_station.m_nview.IsValid() || !CanRepair(item, _station))
            {
                if (_doneAt > 0f) w.Anim.SetInt("crafting", 0);
                _station = found;
                _doneAt = -1f;
            }
            string name = Localization.instance.Localize(item.m_shared.m_name);

            if (_doneAt < 0f)
            {
                w.SetStatus($"Taking the {name} to the {Localization.instance.Localize(_station.m_name)} for repair");
                R m = w.MoveNear(_station.transform.position, Mathf.Max(1.2f, _station.m_useDistance), false);
                if (m == R.Failed)
                {
                    w.Blacklist(_station, 60f);
                    _station = null;
                    return R.Failed;
                }
                if (m == R.Running) return R.Running;
                w.Stop();
                w.UnequipHands();
                w.Face(_station.transform.position);
                w.Anim.SetInt("crafting", _station.m_useAnimation);
                _doneAt = Time.time + 2.5f;
                return R.Running;
            }

            w.SetStatus("Repairing " + name);
            _station.PokeInUse();
            if (Time.time < _doneAt) return R.Running;

            w.Anim.SetInt("crafting", 0);
            _doneAt = -1f;
            item.m_durability = item.GetMaxDurability();
            w.Inv.Changed();
            _station.m_repairItemDoneEffects.Create(_station.transform.position, Quaternion.identity);
            w.LogEvent($"repaired their {name}");
            // The next tick picks up the next broken tool, or reports Done.
            return R.Running;
        }
    }

    /// <summary>Picks up item drops around a point (loot from trees, rocks, animals, stations).</summary>
    public class Collector
    {
        private Vector3 _center;
        private float _radius;
        private float _until;
        private float _minUntil;
        private ItemDrop _target;
        private float _targetSince;

        /// <summary>
        /// Collects everything lying within the radius. maxTime is a safety cap on top of the walking: the
        /// collector normally stops when nothing is left (after waiting at least minTime for drops to spawn).
        /// </summary>
        public void Begin(Vector3 center, float radius, float maxTime, float minTime = 0f)
        {
            _center = center;
            _radius = radius;
            _until = Time.time + maxTime + 60f;
            _minUntil = Time.time + minTime;
            _target = null;
        }

        public static bool AnyDropsNear(Vector3 center, float radius)
        {
            foreach (ItemDrop d in WorldIndex.ItemDrops)
                if (d != null && Vector3.Distance(d.transform.position, center) < radius) return true;
            return false;
        }

        /// <summary>Drops the worker could still pick up (not given up on, room in the pockets).</summary>
        public static bool AnyDropsNear(WorkerAI w, Vector3 center, float radius)
        {
            foreach (ItemDrop d in WorldIndex.ItemDrops)
                if (d != null && !w.IsBlacklisted(d) && Vector3.Distance(d.transform.position, center) < radius && w.Inv.CanAddItem(d.m_itemData))
                    return true;
            return false;
        }

        public R Tick(WorkerAI w, JobType job)
        {
            if (Time.time > _until) return R.Done;

            if (_target == null || _target.m_nview == null || !_target.m_nview.IsValid())
            {
                _target = null;
                float best = float.MaxValue;
                foreach (ItemDrop d in WorldIndex.ItemDrops)
                {
                    if (d == null || w.IsBlacklisted(d)) continue;
                    float dc = Vector3.Distance(d.transform.position, _center);
                    if (dc > _radius) continue;
                    float dw = Vector3.Distance(d.transform.position, w.transform.position);
                    if (dw < best)
                    {
                        best = dw;
                        _target = d;
                    }
                }
                _targetSince = Time.time;
                if (_target == null)
                {
                    if (Time.time > _minUntil) return R.Done;
                    w.Stop();
                    w.SetStatus("Waiting for the loot");
                    return R.Running;
                }
            }

            _target.Load();
            if (!w.Inv.CanAddItem(_target.m_itemData)) return R.Done;

            w.SetStatus("Picking up " + Localization.instance.Localize(_target.m_itemData.m_shared.m_name));
            R m = w.MoveNear(_target.transform.position, 1.1f, false);
            if (m == R.Failed || Time.time - _targetSince > 12f)
            {
                w.Blacklist(_target, 60f);
                _target = null;
                return R.Running;
            }
            if (m == R.Running) return R.Running;

            if (!_target.CanPickup(false))
            {
                _target.RequestOwn();
                return R.Running;
            }
            string name = Items.PrefabName(_target.m_itemData);
            if (string.IsNullOrEmpty(name) || name.StartsWith("$")) name = Utils.GetPrefabName(_target.gameObject);
            int stack = _target.m_itemData.m_stack;
            if (w.Human.Pickup(_target.gameObject, false, false))
            {
                w.Record(job, name, stack);
                w.RaiseSkill(job, 0.15f);
            }
            else
            {
                w.Blacklist(_target, 30f);
            }
            _target = null;
            return R.Running;
        }
    }
}
