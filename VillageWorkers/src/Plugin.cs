using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

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
        public static Synced<bool> SpawnEnabled;
        public static Synced<float> SpawnChance;
        public static Synced<float> SpawnInterval;
        public static Synced<string> SpawnBiomes;

        // Behaviour
        public static Synced<float> WorkStart;
        public static Synced<float> WorkEnd;
        public static Synced<float> StartSearchRadius;
        public static Synced<float> MaxSearchRadius;
        public static Synced<float> StationRadius;
        public static Synced<float> WorkerHealth;
        public static Synced<float> WorkSpeed;
        public static Synced<float> CraftSeconds;
        public static Synced<bool> ToolDurability;
        public static Synced<string> HuntTargets;
        public static Synced<int> StatsDaysKept;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            VillageKey = Config.Bind("General", "VillageWindowKey", new KeyboardShortcut(KeyCode.F7),
                "Opens the village management window.");
            DebugLogging = Config.Bind("General", "DebugLogging", false, "Write verbose worker AI logs.");

            SpawnEnabled = Sync(Config.Bind("Spawning", "Enabled", true, "Wandering villagers spawn in the world."));
            SpawnChance = Sync(Config.Bind("Spawning", "SpawnChance", 8f,
                "Chance (percent) per spawn check that a wanderer appears near a player."));
            SpawnInterval = Sync(Config.Bind("Spawning", "SpawnInterval", 300f, "Seconds between spawn checks per zone."));
            SpawnBiomes = Sync(Config.Bind("Spawning", "Biomes", "Meadows,BlackForest,Plains",
                "Comma separated biomes in which wanderers can be met."));

            WorkStart = Sync(Config.Bind("Behaviour", "WorkStart", 0.25f,
                "Day fraction (0 = midnight, 0.25 = sunrise, 0.5 = noon) at which workers get up."));
            WorkEnd = Sync(Config.Bind("Behaviour", "WorkEnd", 0.75f, "Day fraction at which workers go to bed."));
            StartSearchRadius = Sync(Config.Bind("Behaviour", "StartSearchRadius", 20f,
                "Radius (m) a worker without an assigned area searches first."));
            MaxSearchRadius = Sync(Config.Bind("Behaviour", "MaxSearchRadius", 140f,
                "Maximum radius (m) a worker without an assigned area will expand its search to."));
            StationRadius = Sync(Config.Bind("Behaviour", "StationRadius", 60f,
                "Radius (m) around the bed in which station workers (smelt/cook/brew/craft/fuel) look for stations if no area is set."));
            WorkerHealth = Sync(Config.Bind("Behaviour", "WorkerHealth", 150f, "Base health of a worker."));
            WorkSpeed = Sync(Config.Bind("Behaviour", "WorkSpeed", 1f, "Multiplier for how fast workers chop, mine and craft."));
            CraftSeconds = Sync(Config.Bind("Behaviour", "CraftSeconds", 4f, "Seconds a worker needs to craft one recipe."));
            ToolDurability = Sync(Config.Bind("Behaviour", "ToolDurability", true, "Workers wear out their tools."));
            HuntTargets = Sync(Config.Bind("Behaviour", "HuntTargets", "Deer,Boar,Neck,Hare,Lox,Chicken,Hen,Asksvin",
                "Creature prefab names hunters may chase."));
            StatsDaysKept = Sync(Config.Bind("Behaviour", "StatsDaysKept", 30,
                "Days of detailed per-day statistics kept for every worker; older days are merged into lifetime totals."));

            ServerSync.Changed += WorkerPrefab.ApplySettings;
            ServerSync.WatchConfigFile(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded" + (Headless ? " on a dedicated server" : ""));
        }

        private static Synced<T> Sync<T>(ConfigEntry<T> entry) => new Synced<T>(entry);

        /// <summary>A dedicated server: no screen, no local player, no windows.</summary>
        public static bool Headless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        private void Update()
        {
            try
            {
                ServerSync.Update(Config);
                WorldIndex.Tick();
                if (Headless) return;
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
            ServerSync.StopWatching();
            _harmony?.UnpatchSelf();
        }

        public static void Debug(string msg)
        {
            if (DebugLogging != null && DebugLogging.Value) Log.LogInfo(msg);
        }
    }
}
