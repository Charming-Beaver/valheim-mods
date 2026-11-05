using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace VillageWorkers
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "charmingbeaver.valheim.villageworkers";
        public const string Name = "Village Workers";
        public const string Version = "1.0.0";

        public static Plugin Instance;
        public static ManualLogSource Log;

        // General
        public static ConfigEntry<KeyboardShortcut> VillageKey;
        public static ConfigEntry<bool> DebugLogging;

        // Spawning
        public static ConfigEntry<bool> SpawnEnabled;
        public static ConfigEntry<float> SpawnChance;
        public static ConfigEntry<float> SpawnInterval;
        public static ConfigEntry<string> SpawnBiomes;

        // Behaviour
        public static ConfigEntry<float> WorkStart;
        public static ConfigEntry<float> WorkEnd;
        public static ConfigEntry<float> StartSearchRadius;
        public static ConfigEntry<float> MaxSearchRadius;
        public static ConfigEntry<float> StationRadius;
        public static ConfigEntry<float> WorkerHealth;
        public static ConfigEntry<float> WorkSpeed;
        public static ConfigEntry<float> CraftSeconds;
        public static ConfigEntry<bool> ToolDurability;
        public static ConfigEntry<string> HuntTargets;
        public static ConfigEntry<int> StatsDaysKept;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            VillageKey = Config.Bind("General", "VillageWindowKey", new KeyboardShortcut(KeyCode.F7),
                "Opens the village management window.");
            DebugLogging = Config.Bind("General", "DebugLogging", false, "Write verbose worker AI logs.");

            SpawnEnabled = Config.Bind("Spawning", "Enabled", true, "Wandering villagers spawn in the world.");
            SpawnChance = Config.Bind("Spawning", "SpawnChance", 8f,
                "Chance (percent) per spawn check that a wanderer appears near a player.");
            SpawnInterval = Config.Bind("Spawning", "SpawnInterval", 300f, "Seconds between spawn checks per zone.");
            SpawnBiomes = Config.Bind("Spawning", "Biomes", "Meadows,BlackForest,Plains",
                "Comma separated biomes in which wanderers can be met.");

            WorkStart = Config.Bind("Behaviour", "WorkStart", 0.25f,
                "Day fraction (0 = midnight, 0.25 = sunrise, 0.5 = noon) at which workers get up.");
            WorkEnd = Config.Bind("Behaviour", "WorkEnd", 0.75f, "Day fraction at which workers go to bed.");
            StartSearchRadius = Config.Bind("Behaviour", "StartSearchRadius", 20f,
                "Radius (m) a worker without an assigned area searches first.");
            MaxSearchRadius = Config.Bind("Behaviour", "MaxSearchRadius", 140f,
                "Maximum radius (m) a worker without an assigned area will expand its search to.");
            StationRadius = Config.Bind("Behaviour", "StationRadius", 60f,
                "Radius (m) around the bed in which station workers (smelt/cook/brew/craft/fuel) look for stations if no area is set.");
            WorkerHealth = Config.Bind("Behaviour", "WorkerHealth", 150f, "Base health of a worker.");
            WorkSpeed = Config.Bind("Behaviour", "WorkSpeed", 1f, "Multiplier for how fast workers chop, mine and craft.");
            CraftSeconds = Config.Bind("Behaviour", "CraftSeconds", 4f, "Seconds a worker needs to craft one recipe.");
            ToolDurability = Config.Bind("Behaviour", "ToolDurability", true, "Workers wear out their tools.");
            HuntTargets = Config.Bind("Behaviour", "HuntTargets", "Deer,Boar,Neck,Hare,Lox,Chicken,Hen,Asksvin",
                "Creature prefab names hunters may chase.");
            StatsDaysKept = Config.Bind("Behaviour", "StatsDaysKept", 30,
                "Days of detailed per-day statistics kept for every worker; older days are merged into lifetime totals.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            try
            {
                WorldIndex.Tick();
                UI.VillageWindow.GlobalUpdate();
                WorldPicker.GlobalUpdate();
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        public static void Debug(string msg)
        {
            if (DebugLogging != null && DebugLogging.Value) Log.LogInfo(msg);
        }
    }
}
