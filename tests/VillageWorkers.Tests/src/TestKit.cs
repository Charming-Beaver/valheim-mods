using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VillageWorkers.Jobs;
using Object = UnityEngine.Object;

namespace VillageWorkers.Tests
{
    /// <summary>Setting up scenes (villagers, chests, fields) and checking the results.</summary>
    public static class TestKit
    {
        public const string ChestPrefab = "piece_chest_wood";
        public const string BedPrefab = "bed";

        /// <summary>Everything the tests set up is this close to the player, and cleaned up within it.</summary>
        public const float TestRadius = 60f;

        public static string ScreenshotPath;

        private static readonly List<GameObject> _spawned = new List<GameObject>();

        public static Player Me => Player.m_localPlayer;

        // ------------------------------------------------------------------ setup

        /// <summary>
        /// Back to a clean state: no villagers, chests, beds, crops or loose items around, default settings, noon,
        /// no windows or messages.
        /// </summary>
        public static IEnumerator Reset()
        {
            if (WorldPicker.Active) WorldPicker.Finish(false);
            WorldPicker.AimOverride = null;
            UI.VillageWindow.Close();
            foreach (GameObject go in _spawned) Destroy(go);
            _spawned.Clear();
            yield return ClearArea();

            // Only the local values: on a server, the server's settings stay in use.
            foreach (var key in Plugin.Instance.Config.Keys.ToList())
            {
                var entry = Plugin.Instance.Config[key];
                entry.BoxedValue = entry.DefaultValue;
            }
            // Villagers only work in the daytime.
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.5f;
            // Messages would cover the screenshots.
            MessageHud.instance.m_msgQeue.Clear();
            MessageHud.instance.ClearUnlockQueue();
            MessageHud.instance.HideAll();
            yield return null;
        }

        /// <summary>
        /// Removes villagers, crops, chests, beds and loose items near the player: left over from a run that crashed,
        /// from -Play, or planted and dropped by the last test.
        /// </summary>
        public static IEnumerator ClearArea()
        {
            Vector3 me = Me.transform.position;
            bool Near(Component c) => c != null && Utils.DistanceXZ(c.transform.position, me) < TestRadius;
            foreach (WorkerAI w in WorkerAI.All.Where(Near).ToList()) Destroy(w.gameObject);
            foreach (Plant p in Object.FindObjectsByType<Plant>(FindObjectsSortMode.None).Where(Near)) Destroy(p.gameObject);
            foreach (Pickable p in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None).Where(Near)
                         .Where(p => WorkerPrefab.IsCrop(Utils.GetPrefabName(p.gameObject))))
                Destroy(p.gameObject);
            foreach (Container c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(Near).Where(c => c.GetComponent<Piece>() != null))
                Destroy(c.gameObject);
            foreach (Bed b in Object.FindObjectsByType<Bed>(FindObjectsSortMode.None).Where(Near)) Destroy(b.gameObject);
            foreach (ItemDrop d in ItemDrop.s_instances.Where(Near).ToList()) Destroy(d.gameObject);
            yield return null;
        }

        /// <summary>Started by run-tests.ps1 -Small: a small window and the lowest graphics, which need less memory.</summary>
        public static bool LowGraphicsWanted => Environment.GetCommandLineArgs().Contains("-test-low-graphics");

        /// <summary>
        /// Lowers the graphics for this session only (run-tests.ps1 puts the game's saved settings back afterwards).
        /// The game applies its own settings when the menu and the world load, so this runs again after both.
        /// </summary>
        public static void LowGraphics()
        {
            if (!LowGraphicsWanted) return;
            Screen.SetResolution(800, 450, FullScreenMode.Windowed);
            QualitySettings.SetQualityLevel(0, true);
            QualitySettings.globalTextureMipmapLimit = 2;
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowDistance = 0f;
            QualitySettings.lodBias = 0.3f;
            QualitySettings.maximumLODLevel = 1;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;
        }

        /// <summary>Leaves what was set up in the world (for -Play), instead of removing it in the next Reset.</summary>
        public static void KeepSpawned() => _spawned.Clear();

        private static void Destroy(GameObject go)
        {
            if (go == null) return;
            ZNetView nv = go.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid())
            {
                if (nv == null) Object.Destroy(go);
                return;
            }
            nv.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        /// <summary>A point on the ground, <paramref name="distance"/> meters north of the player.</summary>
        public static Vector3 Ahead(float distance) => Ground(Me.transform.position + Vector3.forward * distance);

        public static Vector3 Ground(Vector3 p)
        {
            p.y = ZoneSystem.instance.GetGroundHeight(p);
            return p;
        }

        public static GameObject Spawn(string prefab, Vector3 pos, Quaternion? rot = null)
        {
            GameObject p = ZNetScene.instance.GetPrefab(prefab);
            Assert.True(p != null, $"No prefab called {prefab}");
            GameObject go = Object.Instantiate(p, Ground(pos), rot ?? Quaternion.identity);
            _spawned.Add(go);
            return go;
        }

        public static Container SpawnChest(Vector3 pos) => Spawn(ChestPrefab, pos).GetComponent<Container>();

        public static Bed SpawnBed(Vector3 pos) => Spawn(BedPrefab, pos).GetComponent<Bed>();

        /// <summary>Adds items by prefab name (like "CarrotSeeds"), in as many stacks as needed.</summary>
        public static void Put(Inventory inv, string prefab, int amount)
        {
            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(prefab);
            Assert.True(itemPrefab != null, $"No item prefab called {prefab}");
            ItemDrop.ItemData proto = itemPrefab.GetComponent<ItemDrop>().m_itemData;
            while (amount > 0)
            {
                int n = Mathf.Min(amount, proto.m_shared.m_maxStackSize);
                ItemDrop.ItemData item = proto.Clone();
                item.m_dropPrefab = itemPrefab;
                item.m_stack = n;
                Assert.True(inv.AddItem(item), $"No room for {n} {prefab}");
                amount -= n;
            }
        }

        public static int Count(Inventory inv, string prefab) => Items.Count(inv, prefab);

        /// <summary>
        /// A villager living in the village (hired, with the bed and chest when given), as if it had been recruited and
        /// given a bed through the village window.
        /// </summary>
        public static IEnumerator Villager(Vector3 pos, Bed bed, Container chest, Action<WorkerAI> done)
        {
            GameObject go = Spawn(WorkerPrefab.PrefabName, pos);
            // Awake and Start set it up (name, looks, clothes).
            yield return Frames(2);
            WorkerAI w = go.GetComponent<WorkerAI>();
            Assert.True(w != null && w.NView.IsValid() && w.NView.IsOwner(), "The villager was spawned");
            WorkerConfig cfg = new WorkerConfig();
            if (bed != null) cfg.Bed = new ObjRef(bed.m_nview);
            if (chest != null) cfg.Chests.Add(new ObjRef(chest.m_nview));
            w.NView.GetZDO().Set(WorkerAI.K_State, (int)WorkerState.Villager);
            w.NView.GetZDO().Set(WorkerAI.K_Cfg, cfg.ToJson());
            done(w);
        }

        /// <summary>
        /// Waits until villagers know about crops just spawned: they look in WorldIndex, which is refreshed every few
        /// seconds. In the game, crops have been there long before.
        /// </summary>
        public static IEnumerator Indexed(IEnumerable<Pickable> crops)
        {
            List<Pickable> list = crops.ToList();
            yield return WaitUntil(() => list.All(c => WorldIndex.Current.Pickables.Contains(c)), "the villagers to know about the crops", 15f);
        }

        /// <summary>Gives a villager a farming task in the area, as the village window does.</summary>
        public static void Farming(WorkerAI w, AreaDef area, params string[] only)
        {
            WorkerConfig cfg = w.Config.Clone();
            cfg.Tasks.Add(new TaskConfig { Type = JobType.Farming, Area = area, Filter = only.ToList() });
            w.NView.GetZDO().Set(WorkerAI.K_Cfg, cfg.ToJson());
        }

        // ------------------------------------------------------------------ waiting

        /// <summary>Waits until a condition holds, failing after a timeout.</summary>
        public static IEnumerator WaitUntil(Func<bool> condition, string what, float timeout = 5f, Func<string> state = null)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) throw new AssertionException($"Timed out waiting for: {what}" + (state != null ? $" ({state()})" : ""));
                yield return null;
            }
        }

        /// <summary>What a villager is doing and carrying, and what lies around, for failure messages.</summary>
        public static string Describe(WorkerAI w, Container chest = null)
        {
            string Items(Inventory inv) => string.Join(", ", inv.GetAllItems().Select(i => $"{i.m_dropPrefab?.name ?? i.m_shared.m_name} x{i.m_stack}"));
            string drops = string.Join(", ", ItemDrop.s_instances.Where(d => d != null && Utils.DistanceXZ(d.transform.position, w.transform.position) < 20f)
                .Select(d => $"{d.m_itemData.m_dropPrefab?.name ?? d.name} {Utils.DistanceXZ(d.transform.position, w.transform.position):0.0} m"));
            string got = string.Join(", ", w.Stats.E.Select(e => $"{e.I} x{e.N}"));
            string log = string.Join(" / ", w.Stats.L.Skip(Math.Max(0, w.Stats.L.Count - 6)).Select(e => e.M));
            return $"{w.GetName()}: \"{w.GetStatus()}\", carrying [{Items(w.Inv)}]" + (chest != null ? $"; chest [{Items(chest.GetInventory())}]" : "")
                   + $"; on the ground [{drops}]; got [{got}]; log [{log}]; the player carries [{Items(Me.GetInventory())}]";
        }

        /// <summary>
        /// Waits until a count stops changing for <paramref name="quiet"/> seconds (villagers are done), failing if it
        /// never does within the timeout.
        /// </summary>
        public static IEnumerator WaitUntilSettled(Func<int> count, string what, float quiet, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            int last = count();
            float changed = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - changed < quiet)
            {
                if (Time.realtimeSinceStartup > end) throw new AssertionException($"Timed out waiting for {what} to settle (at {last})");
                yield return new WaitForSeconds(0.5f);
                int now = count();
                if (now == last) continue;
                last = now;
                changed = Time.realtimeSinceStartup;
            }
        }

        public static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        /// <summary>Saves a screenshot to BepInEx/VillageWorkers.Tests.screenshots/name.png (after the frame is drawn).</summary>
        public static IEnumerator Screenshot(string name)
        {
            yield return Frames(3);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(ScreenshotPath, name + ".png"));
            yield return Frames(2);
        }

        /// <summary>
        /// A screenshot of a spot in the world, taken from a few meters away (hills may hide it from where the player
        /// stands). The player goes back afterwards, so the tests' cleanup still finds everything around it.
        /// </summary>
        public static IEnumerator ScreenshotOf(Vector3 target, string name)
        {
            Vector3 home = Me.transform.position;
            MovePlayer(Ground(target + new Vector3(0f, 0f, -6f)) + Vector3.up * 0.2f);
            yield return Frames(10);
            yield return LookAt(target);
            yield return Screenshot(name);
            MovePlayer(home);
            yield return Frames(10);
        }

        // The physics body would put the player back where it was.
        private static void MovePlayer(Vector3 p)
        {
            Me.m_body.position = p;
            Me.m_body.linearVelocity = Vector3.zero;
            Me.transform.position = p;
        }

        /// <summary>Turns the player (and so the camera) to look at a point.</summary>
        public static IEnumerator LookAt(Vector3 point)
        {
            Camera cam = GameCamera.instance.GetComponent<Camera>();
            // The camera sits behind and above the player, so aim from where it is and correct a few times.
            for (int i = 0; i < 6; i++)
            {
                Me.SetLookDir(point - cam.transform.position);
                yield return Frames(3);
            }
        }

        // ------------------------------------------------------------------ crops

        /// <summary>Plants of a crop near the player (saplings, not grown yet).</summary>
        public static List<Plant> Saplings(Farm farm) => Object.FindObjectsByType<Plant>(FindObjectsSortMode.None)
            .Where(p => p != null && p.m_nview != null && p.m_nview.IsValid() && Utils.GetPrefabName(p.gameObject) == farm.Sapling.name
                        && Utils.DistanceXZ(p.transform.position, Me.transform.position) < TestRadius)
            .ToList();

        /// <summary>Ripe crops near the player.</summary>
        public static List<Pickable> Crops(Farm farm) => Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None)
            .Where(p => p != null && p.m_nview != null && p.m_nview.IsValid() && !p.GetPicked() && Utils.GetPrefabName(p.gameObject) == farm.CropPrefab.name
                        && Utils.DistanceXZ(p.transform.position, Me.transform.position) < TestRadius)
            .ToList();

        /// <summary>
        /// Why nothing was planted on the spots of a farmer's grid (FarmingJob.NearestFreeSpot) that are still empty, for
        /// failure messages.
        /// </summary>
        public static string ExplainEmptySpots(Farm farm, List<Plant> planted)
        {
            AreaDef area = farm.Area;
            Plant plant = farm.Plant;
            float step = PlantSpace.Spacing(plant, plant);
            int nx = Mathf.Max(1, Mathf.FloorToInt(area.W / step)), nz = Mathf.Max(1, Mathf.FloorToInt(area.L / step));
            float x0 = -(nx - 1) * step * 0.5f, z0 = -(nz - 1) * step * 0.5f;
            var reasons = new List<string>();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    Vector3 p = Ground(area.ToWorld(x0 + i * step, z0 + j * step));
                    if (planted.Any(s => Utils.DistanceXZ(s.transform.position, p) < 0.3f)) continue;
                    string why;
                    if (!PlantSpace.Cultivated(p)) why = "not cultivated";
                    else if (!PlantSpace.Grows(plant, p)) why = "doesn't grow here";
                    else if (Physics.Raycast(p, Vector3.up, out RaycastHit roof, 100f, LayerMask.GetMask("Default", "static_solid", "piece"))) why = $"roof {roof.collider.name}";
                    else
                    {
                        Collider block = Physics.OverlapSphere(p, plant.m_growRadius, PlantSpace.SpaceMask).FirstOrDefault();
                        Plant near = planted.OrderBy(s => Utils.DistanceXZ(s.transform.position, p)).FirstOrDefault();
                        why = block != null ? $"{block.name} ({LayerMask.LayerToName(block.gameObject.layer)}) within {plant.m_growRadius:0.00} m"
                            : !PlantSpace.Free(p, plant) ? $"too close to a crop {Utils.DistanceXZ(near.transform.position, p):0.00} m away"
                            : "free";
                    }
                    reasons.Add($"({i},{j}) {why}");
                }
            return $"spacing {step:0.00} m (grow radius {plant.m_growRadius:0.00}, footprint {PlantSpace.Footprint(plant):0.00}); empty: " + string.Join("; ", reasons);
        }

        /// <summary>The plant's health as the game judges it once the plant is older than its first 10 seconds.</summary>
        public static Plant.Status Health(Plant p)
        {
            p.UpdateHealth(1000.0);
            return p.GetStatus();
        }

        /// <summary>Checks that crops are far enough apart for all of them to grow, and that each one does.</summary>
        public static void AssertAllGrow(List<Plant> plants, string who)
        {
            for (int i = 0; i < plants.Count; i++)
            {
                for (int j = i + 1; j < plants.Count; j++)
                {
                    float d = Utils.DistanceXZ(plants[i].transform.position, plants[j].transform.position);
                    float need = PlantSpace.Spacing(plants[i], plants[j]);
                    Assert.True(d >= need - PlantSpace.Tolerance - 0.001f, $"{who}: two crops are {d:0.00} m apart, they need {need:0.00} m (at {plants[i].transform.position})");
                }
            }
            foreach (Plant p in plants)
            {
                Plant.Status status = Health(p);
                Assert.Equal(Plant.Status.Healthy, status, $"{who}: the crop at {p.transform.position}");
            }
        }

        /// <summary>Grows every other plant into its crop now, then checks the ones still growing have room.</summary>
        public static IEnumerator GrowHalf(List<Plant> plants, string who)
        {
            List<Plant> rest = new List<Plant>();
            for (int i = 0; i < plants.Count; i++)
            {
                if (i % 2 == 0)
                {
                    Assert.Equal(Plant.Status.Healthy, Health(plants[i]), $"{who}: the crop at {plants[i].transform.position} before growing");
                    Assert.True(plants[i].Grow() != null, $"{who}: the crop at {plants[i].transform.position} grew");
                }
                else rest.Add(plants[i]);
            }
            yield return Frames(3);
            foreach (Plant p in rest)
                Assert.Equal(Plant.Status.Healthy, Health(p), $"{who}: the crop at {p.transform.position}, next to grown ones");
        }
    }

    /// <summary>
    /// A cultivated field for the farming tests: a box near the player, cleared and cultivated, with cultivated ground a
    /// few meters beyond its sides (so the tests can check villagers stay inside their area).
    /// </summary>
    public class Farm
    {
        public const string SeedName = "CarrotSeeds";

        public AreaDef Area;
        public string Seed = SeedName;
        public Piece Sapling;
        public Plant Plant;
        public GameObject CropPrefab;

        private static Vector3? _spot;

        /// <summary>Where the test farms go: the same place in every test, found the first time.</summary>
        public static Vector3 Spot => (_spot ?? (_spot = FindSpot(16f))).Value;

        /// <summary>
        /// Sets up a field <paramref name="width"/> by <paramref name="length"/> meters, turned <paramref name="yaw"/>
        /// degrees, around <paramref name="offset"/> from the test farm's spot.
        /// </summary>
        public static IEnumerator Create(Vector3 offset, float width, float length, float yaw, Action<Farm> done, float margin = 2f)
        {
            var farm = new Farm { Area = AreaDef.Box(TestKit.Ground(Spot + offset), width, length, yaw) };
            foreach (KeyValuePair<Piece, string> kv in WorkerPrefab.SaplingCrops)
            {
                Piece.Requirement seed = kv.Key.m_resources.FirstOrDefault();
                if (seed?.m_resItem == null || seed.m_resItem.gameObject.name != SeedName) continue;
                farm.Sapling = kv.Key;
                farm.Plant = kv.Key.GetComponent<Plant>();
                farm.CropPrefab = ZNetScene.instance.GetPrefab(WorkerPrefab.CropToPlant.First(c => c.Value == kv.Key).Key);
            }
            Assert.True(farm.Sapling != null && farm.CropPrefab != null, $"Found the crop that grows from {SeedName}");

            float w = width + 2f * margin, l = length + 2f * margin;
            ClearObstacles(farm.Area.Center, w, l, yaw);
            yield return Terrain(farm.Area, w, l, op => op.m_settings.m_paintCleared && op.m_settings.m_paintType == TerrainModifier.PaintType.Cultivate, "cultivating");
            // The terrain changes reach this game a moment later when another game (the server) owns the terrain.
            yield return TestKit.WaitUntil(() => Grid(farm.Area, farm.Area.W, farm.Area.L, 1f).All(PlantSpace.Cultivated), "the field to be cultivated", 20f);
            // Moving the ground drops whatever stood on it.
            yield return TestKit.ClearArea();
            done(farm);
        }

        /// <summary>
        /// Somewhere near the player that's out of the start temple, dry, fairly flat and in the Meadows: the test
        /// world's terrain depends on its random seed.
        /// </summary>
        private static Vector3 FindSpot(float size)
        {
            Vector3 me = TestKit.Me.transform.position;
            for (float d = 25f; d <= 45f; d += 10f)
            {
                for (int a = 0; a < 16; a++)
                {
                    Vector3 c = TestKit.Ground(me + Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward * d);
                    if (Heightmap.FindBiome(c) != Heightmap.Biome.Meadows) continue;
                    var heights = Grid(new AreaDef(c, 0f) { W = size, L = size }, size, size, 2f).Select(p => p.y).ToList();
                    if (heights.Min() < ZoneSystem.instance.m_waterLevel + 1f || heights.Max() - heights.Min() > 3f) continue;
                    // Location buildings and stones have no ZNetView, so they can't be cleared away.
                    if (Physics.OverlapBox(c + Vector3.up * 20f, new Vector3(size, 40f, size) * 0.5f, Quaternion.identity, PlantSpace.SpaceMask)
                        .Any(col => col.GetComponentInParent<ZNetView>() == null)) continue;
                    TestPlugin.Log.LogInfo($"Test farm at {c} ({d:0} m from the player)");
                    return c;
                }
            }
            throw new AssertionException("Found no dry, flat spot in the Meadows near the player for the test farm");
        }

        /// <summary>Points of a grid over the area, in its own axes, <paramref name="step"/> apart, on the ground.</summary>
        public static IEnumerable<Vector3> Grid(AreaDef area, float width, float length, float step)
        {
            for (float x = -width * 0.5f; x <= width * 0.5f + 0.01f; x += step)
                for (float z = -length * 0.5f; z <= length * 0.5f + 0.01f; z += step)
                    yield return TestKit.Ground(area.ToWorld(x, z));
        }

        /// <summary>Removes trees, rocks, bushes and pieces standing on the field (anything the game saves).</summary>
        private static void ClearObstacles(Vector3 center, float width, float length, float yaw)
        {
            var roots = new HashSet<ZNetView>();
            foreach (Collider c in Physics.OverlapBox(center + Vector3.up * 20f, new Vector3(width, 80f, length) * 0.5f, Quaternion.Euler(0f, yaw, 0f)))
            {
                ZNetView nv = c.GetComponentInParent<ZNetView>();
                if (nv != null && nv.IsValid() && nv.GetComponent<Character>() == null) roots.Add(nv);
            }
            foreach (ZNetView nv in roots)
            {
                nv.ClaimOwnership();
                ZNetScene.instance.Destroy(nv.gameObject);
            }
        }

        /// <summary>Applies one of the game's terrain tools (like the cultivator) all over the area.</summary>
        private static IEnumerator Terrain(AreaDef area, float width, float length, Func<TerrainOp, bool> which, string what)
        {
            // The hoe's and cultivator's pieces, found through the tools' piece tables.
            List<TerrainOp> ops = ObjectDB.instance.m_items
                .Select(i => i != null ? i.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null)
                .Where(t => t != null)
                .SelectMany(t => t.m_pieces)
                .Concat(ZNetScene.instance.m_prefabs).Concat(ZNetScene.instance.m_nonNetViewPrefabs)
                .Select(p => p != null ? p.GetComponent<TerrainOp>() : null)
                .Where(o => o != null)
                .Distinct()
                .ToList();
            TerrainOp op = ops.Where(which).OrderBy(o => o.m_settings.GetRadius()).FirstOrDefault();
            Assert.True(op != null, $"Found the game's terrain tool for {what}, among: " + string.Join(", ", ops.Select(o =>
                $"{o.name} (level {o.m_settings.m_level}, raise {o.m_settings.m_raise}, smooth {o.m_settings.m_smooth}, paint {(o.m_settings.m_paintCleared ? o.m_settings.m_paintType.ToString() : "-")})")));
            float step = Mathf.Max(0.5f, op.m_settings.GetRadius());
            float y = area.Center.y;
            foreach (Vector3 p in Grid(area, width, length, step))
            {
                Object.Instantiate(op.gameObject, new Vector3(p.x, y, p.z), Quaternion.identity);
            }
            yield return TestKit.Frames(5);
        }

        /// <summary>Ripe crops on a grid in the field, <paramref name="perSide"/> by <paramref name="perSide"/>, far enough apart to have grown.</summary>
        public List<Pickable> PlantCrops(int perSide)
        {
            var crops = new List<Pickable>();
            float step = PlantSpace.Spacing(Plant, Plant) + 0.5f;
            for (int i = 0; i < perSide; i++)
                for (int j = 0; j < perSide; j++)
                {
                    Vector3 p = Area.ToWorld((i - (perSide - 1) * 0.5f) * step, (j - (perSide - 1) * 0.5f) * step);
                    crops.Add(TestKit.Spawn(CropPrefab.name, p, Quaternion.Euler(0f, 45f * (i + j), 0f)).GetComponent<Pickable>());
                }
            return crops;
        }
    }

    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new AssertionException($"{what}: expected {expected}, got {actual}");
        }
    }
}
