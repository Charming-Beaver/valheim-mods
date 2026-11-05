using System.Collections.Generic;
using UnityEngine;

namespace VillageWorkers
{
    /// <summary>
    /// Periodically indexes loaded networked objects by type, spread over several frames,
    /// so dozens of workers can search for work without scanning the scene themselves.
    /// </summary>
    public static class WorldIndex
    {
        public class Snapshot
        {
            public readonly List<TreeBase> Trees = new List<TreeBase>();
            public readonly List<TreeLog> Logs = new List<TreeLog>();
            public readonly List<Destructible> Destructibles = new List<Destructible>();
            public readonly List<MineRock> MineRocks = new List<MineRock>();
            public readonly List<MineRock5> MineRocks5 = new List<MineRock5>();
            public readonly List<Pickable> Pickables = new List<Pickable>();
            public readonly List<Beehive> Beehives = new List<Beehive>();
            public readonly List<SapCollector> SapCollectors = new List<SapCollector>();
            public readonly List<Smelter> Smelters = new List<Smelter>();
            public readonly List<CookingStation> CookingStations = new List<CookingStation>();
            public readonly List<Fermenter> Fermenters = new List<Fermenter>();
            public readonly List<Fireplace> Fireplaces = new List<Fireplace>();
            public readonly List<Container> Containers = new List<Container>();
            public readonly List<Bed> Beds = new List<Bed>();

            public void Clear()
            {
                Trees.Clear(); Logs.Clear(); Destructibles.Clear(); MineRocks.Clear(); MineRocks5.Clear();
                Pickables.Clear(); Beehives.Clear(); SapCollectors.Clear(); Smelters.Clear();
                CookingStations.Clear(); Fermenters.Clear(); Fireplaces.Clear(); Containers.Clear(); Beds.Clear();
            }
        }

        public static Snapshot Current = new Snapshot();
        private static Snapshot _building = new Snapshot();
        private static readonly List<ZNetView> _queue = new List<ZNetView>();
        private static int _cursor;
        private static float _nextCycle;
        private const int PerFrame = 1500;

        public static void Tick()
        {
            if (ZNetScene.instance == null || WorkerAI.All.Count == 0)
            {
                _queue.Clear();
                _cursor = 0;
                return;
            }

            if (_queue.Count == 0)
            {
                if (Time.time < _nextCycle) return;
                _building.Clear();
                _queue.AddRange(ZNetScene.instance.m_instances.Values);
                _cursor = 0;
            }

            int end = Mathf.Min(_cursor + PerFrame, _queue.Count);
            for (; _cursor < end; _cursor++)
            {
                ZNetView nv = _queue[_cursor];
                if (nv == null) continue;
                Classify(nv.gameObject, _building);
            }

            if (_cursor >= _queue.Count)
            {
                Snapshot done = _building;
                _building = Current;
                Current = done;
                _queue.Clear();
                _cursor = 0;
                _nextCycle = Time.time + 2.5f;
            }
        }

        private static void Classify(GameObject go, Snapshot s)
        {
            // Most objects are pieces or creatures; check the cheap, common cases first.
            if (go.TryGetComponent(out Pickable pickable)) { s.Pickables.Add(pickable); return; }
            if (go.TryGetComponent(out TreeBase tree)) { s.Trees.Add(tree); return; }
            if (go.TryGetComponent(out TreeLog log)) { s.Logs.Add(log); return; }
            if (go.TryGetComponent(out MineRock5 mr5)) { s.MineRocks5.Add(mr5); return; }
            if (go.TryGetComponent(out MineRock mr)) { s.MineRocks.Add(mr); return; }
            if (go.TryGetComponent(out Container container) && go.GetComponent<Piece>() != null) { s.Containers.Add(container); return; }
            if (go.TryGetComponent(out Smelter smelter)) { s.Smelters.Add(smelter); return; }
            if (go.TryGetComponent(out CookingStation cooking)) { s.CookingStations.Add(cooking); return; }
            if (go.TryGetComponent(out Fermenter fermenter)) { s.Fermenters.Add(fermenter); return; }
            if (go.TryGetComponent(out Bed bed)) { s.Beds.Add(bed); return; }
            if (go.TryGetComponent(out Beehive hive)) { s.Beehives.Add(hive); return; }
            if (go.TryGetComponent(out SapCollector sap)) { s.SapCollectors.Add(sap); return; }
            if (go.TryGetComponent(out Fireplace fire)) { s.Fireplaces.Add(fire); return; }
            if (go.TryGetComponent(out Destructible d) && go.GetComponent<Character>() == null && go.GetComponent<Piece>() == null)
                s.Destructibles.Add(d);
        }

        public static IEnumerable<CraftingStation> CraftingStations => CraftingStation.m_allStations;
        public static IEnumerable<ItemDrop> ItemDrops => ItemDrop.s_instances;
    }
}
