using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VillageWorkers
{
    /// <summary>
    /// Builds the networked villager prefab out of the player prefab (so villagers look like vikings,
    /// can wear armour and hold tools) and registers it with the game.
    /// </summary>
    public static class WorkerPrefab
    {
        public const string PrefabName = "VW_Villager";
        public static readonly int PrefabHash = PrefabName.GetStableHashCode();

        public static GameObject Prefab;
        private static GameObject _holder;
        private static SpawnSystemList _spawnList;

        private static readonly HashSet<string> RemoveTypes = new HashSet<string>
        {
            "Player", "PlayerController", "Talker", "Skills", "Pickup"
        };

        public static void Register(ZNetScene scene)
        {
            if (scene.m_namedPrefabs.ContainsKey(PrefabHash)) return;

            if (Prefab == null)
            {
                GameObject player = scene.m_prefabs.FirstOrDefault(p => p != null && p.name == "Player");
                if (player == null)
                {
                    Plugin.Log.LogError("Player prefab not found - villagers disabled");
                    return;
                }
                Prefab = Build(player, scene);
            }
            if (Prefab == null) return;

            scene.m_prefabs.Add(Prefab);
            scene.m_namedPrefabs[PrefabHash] = Prefab;
            Plugin.Log.LogInfo("Registered villager prefab");
        }

        private static GameObject Build(GameObject player, ZNetScene scene)
        {
            try
            {
                if (_holder == null)
                {
                    _holder = new GameObject("VillageWorkers_Prefabs");
                    _holder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_holder);
                }

                GameObject go = UnityEngine.Object.Instantiate(player, _holder.transform);
                go.name = PrefabName;

                Player p = go.GetComponent<Player>();
                Humanoid h = go.AddComponent<Humanoid>();
                CopySerializedFields(typeof(Character), p, h);
                CopySerializedFields(typeof(Humanoid), p, h);

                h.m_name = "Villager";
                h.m_faction = Character.Faction.Players;
                h.m_group = "";
                h.m_health = Plugin.WorkerHealth.Value;
                h.m_boss = false;
                h.m_disableWhileSleeping = true;
                h.m_killedForAchievements = Utils.AchievementInclusion.Excluded;
                h.m_defaultItems = new GameObject[0];
                h.m_randomWeapon = new GameObject[0];
                h.m_randomArmor = new GameObject[0];
                h.m_randomShield = new GameObject[0];
                h.m_randomSets = new Humanoid.ItemSet[0];
                h.m_randomItems = new Humanoid.RandomItem[0];

                foreach (Component c in go.GetComponents<Component>())
                {
                    if (c == null || c == h) continue;
                    if (RemoveTypes.Contains(c.GetType().Name)) UnityEngine.Object.DestroyImmediate(c);
                }

                ZNetView nview = go.GetComponent<ZNetView>();
                nview.m_persistent = true;
                nview.m_distant = false;
                nview.m_type = ZDO.ObjectType.Default;

                VisEquipment vis = go.GetComponent<VisEquipment>();
                if (vis != null) vis.m_isPlayer = true;

                // AI first, so it is the first Interactable on the object.
                WorkerAI ai = go.AddComponent<WorkerAI>();
                ai.m_viewRange = 30f;
                ai.m_viewAngle = 120f;
                ai.m_hearRange = 15f;
                ai.m_pathAgentType = Pathfinding.AgentType.Humanoid;
                ai.m_randomMoveRange = 6f;
                ai.m_randomMoveInterval = 8f;
                ai.m_avoidFire = true;
                ai.m_afraidOfFire = false;
                ai.m_avoidWater = true;
                ai.m_aggravatable = false;
                ai.m_smoothMovement = true;
                ai.m_moveMinAngle = 25f;

                Container container = go.AddComponent<Container>();
                container.m_name = "Villager";
                container.m_width = 8;
                container.m_height = 4;
                container.m_privacy = Container.PrivacySetting.Public;
                container.m_checkGuardStone = false;
                container.m_autoDestroyEmpty = false;
                GameObject chest = scene.m_prefabs.FirstOrDefault(x => x != null && x.name == "piece_chest_wood");
                if (chest != null) container.m_bkg = chest.GetComponent<Container>()?.m_bkg;

                Plugin.Debug("Villager components: " + string.Join(", ", go.GetComponents<Component>().Select(c => c.GetType().Name)));
                return go;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to build villager prefab: " + e);
                return null;
            }
        }

        private static void CopySerializedFields(Type type, object from, object to)
        {
            foreach (FieldInfo f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsInitOnly || f.IsLiteral || f.IsNotSerialized) continue;
                bool serialized = f.IsPublic || f.GetCustomAttribute<SerializeField>() != null;
                if (!serialized) continue;
                try { f.SetValue(to, f.GetValue(from)); }
                catch { /* ignore */ }
            }
        }

        // ------------------------------------------------------------- spawning

        public static void AddSpawner(SpawnSystem system)
        {
            if (!Plugin.SpawnEnabled.Value || Prefab == null) return;
            if (_spawnList == null)
            {
                _spawnList = _holder.AddComponent<SpawnSystemList>();
                Heightmap.Biome biomes = Heightmap.Biome.None;
                foreach (string b in Plugin.SpawnBiomes.Value.Split(','))
                {
                    if (Enum.TryParse(b.Trim(), true, out Heightmap.Biome biome)) biomes |= biome;
                }
                if (biomes == Heightmap.Biome.None) biomes = Heightmap.Biome.Meadows;
                _spawnList.m_spawners.Add(new SpawnSystem.SpawnData
                {
                    m_name = PrefabName,
                    m_enabled = true,
                    m_prefab = Prefab,
                    m_biome = biomes,
                    m_biomeArea = Heightmap.BiomeArea.Everything,
                    m_maxSpawned = 1,
                    m_spawnInterval = Plugin.SpawnInterval.Value,
                    m_spawnChance = Plugin.SpawnChance.Value,
                    m_spawnDistance = 80f,
                    m_spawnRadiusMin = 30f,
                    m_spawnRadiusMax = 60f,
                    m_groupSizeMin = 1,
                    m_groupSizeMax = 1,
                    m_spawnAtDay = true,
                    m_spawnAtNight = false,
                    m_minAltitude = 1f,
                    m_maxTilt = 25f,
                    m_huntPlayer = false,
                    m_groundOffset = 0.5f,
                    m_maxLevel = 1,
                    m_minLevel = 1,
                });
            }
            if (!system.m_spawnLists.Contains(_spawnList)) system.m_spawnLists.Add(_spawnList);
        }

        public static WorkerAI SpawnNear(Vector3 pos)
        {
            if (Prefab == null) return null;
            Vector3 p = pos + UnityEngine.Random.insideUnitSphere.Horizontal().normalized * 4f;
            if (ZoneSystem.instance.GetSolidHeight(p, out float h)) p.y = h + 0.2f;
            GameObject go = UnityEngine.Object.Instantiate(Prefab, p, Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0));
            return go.GetComponent<WorkerAI>();
        }

        // ------------------------------------------------------------- knowledge about the world's prefabs

        private static Dictionary<string, Piece> _cropToPlant;
        private static List<GameObject> _smelterPrefabs;

        /// <summary>Maps a grown crop (pickable prefab name) to the sapling piece that grows it.</summary>
        public static Dictionary<string, Piece> CropToPlant
        {
            get
            {
                if (_cropToPlant == null && ZNetScene.instance != null)
                {
                    _cropToPlant = new Dictionary<string, Piece>();
                    foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                    {
                        if (prefab == null) continue;
                        Plant plant = prefab.GetComponent<Plant>();
                        Piece piece = prefab.GetComponent<Piece>();
                        if (plant == null || piece == null || plant.m_grownPrefabs == null) continue;
                        foreach (GameObject grown in plant.m_grownPrefabs)
                        {
                            if (grown != null && grown.GetComponent<Pickable>() != null && !_cropToPlant.ContainsKey(grown.name))
                                _cropToPlant[grown.name] = piece;
                        }
                    }
                }
                return _cropToPlant ?? new Dictionary<string, Piece>();
            }
        }

        public static bool IsCrop(string pickablePrefab) => CropToPlant.ContainsKey(pickablePrefab);

        public static List<GameObject> SmelterPrefabs
        {
            get
            {
                if (_smelterPrefabs == null && ZNetScene.instance != null)
                    _smelterPrefabs = ZNetScene.instance.m_prefabs.Where(p => p != null && p.GetComponent<Smelter>() != null && p.GetComponent<Piece>() != null).ToList();
                return _smelterPrefabs ?? new List<GameObject>();
            }
        }

        public static void ResetCaches()
        {
            _cropToPlant = null;
            _smelterPrefabs = null;
        }
    }

    [HarmonyPatch]
    internal static class PrefabPatches
    {
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake)), HarmonyPostfix]
        private static void ZNetSceneAwake(ZNetScene __instance)
        {
            WorkerPrefab.ResetCaches();
            WorkerPrefab.Register(__instance);
        }

        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.Awake)), HarmonyPostfix]
        private static void SpawnSystemAwake(SpawnSystem __instance) => WorkerPrefab.AddSpawner(__instance);

        // The spawn limits should only count wanderers - otherwise hired villagers stop new ones from
        // ever appearing anywhere near the village.
        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.GetNrOfZDOInstances)), HarmonyPostfix]
        private static void CountOnlyWild(GameObject prefab, List<ZDO> ZDOs, bool eventCreaturesOnly, ref int __result)
        {
            if (__result == 0 || eventCreaturesOnly || prefab == null || prefab.name != WorkerPrefab.PrefabName) return;
            int hash = prefab.name.GetStableHashCode();
            __result = ZDOs.Count(z => z.GetPrefab() == hash && z.GetInt(WorkerAI.K_State) == (int)WorkerState.Wild);
        }

        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.HaveInstanceInRange)), HarmonyPostfix]
        private static void RangeOnlyWild(GameObject prefab, Vector3 centerPoint, float minDistance, ref bool __result)
        {
            if (!__result || prefab == null || prefab.name != WorkerPrefab.PrefabName) return;
            __result = WorkerAI.All.Any(w => w != null && w.State == WorkerState.Wild &&
                                             Utils.DistanceXZ(w.transform.position, centerPoint) < minDistance);
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal)), HarmonyPostfix]
        private static void InitTerminal()
        {
            new Terminal.ConsoleCommand("vw_spawn", "[Village Workers] Spawns a wandering villager next to you", args =>
            {
                if (Player.m_localPlayer == null) return;
                WorkerAI w = WorkerPrefab.SpawnNear(Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 3f);
                args.Context?.AddString(w != null ? "Spawned a wandering villager." : "Villager prefab missing.");
            });
            new Terminal.ConsoleCommand("vw_list", "[Village Workers] Lists loaded villagers", args =>
            {
                foreach (WorkerAI w in WorkerAI.All)
                    args.Context?.AddString($"{w.GetName()} - {w.State} - {w.GetStatus()}");
            });
            new Terminal.ConsoleCommand("vw_window", "[Village Workers] Opens the village window", args =>
            {
                UI.VillageWindow.Open(null);
            });
        }
    }

    public static class VecExt
    {
        public static Vector3 Horizontal(this Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
