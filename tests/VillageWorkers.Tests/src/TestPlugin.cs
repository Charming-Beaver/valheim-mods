using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;

namespace VillageWorkers.Tests
{
    /// <summary>How the game the tests run in is set up (run-tests.ps1 -Hosting and -Server).</summary>
    public enum Setup
    {
        /// <summary>A world of our own, like singleplayer.</summary>
        Singleplayer,
        /// <summary>A world of our own with "Start server" ticked, so other players could join.</summary>
        Host,
        /// <summary>Joined to a dedicated server on this machine, which runs the mod and this plugin too.</summary>
        Server,
    }

    /// <summary>
    /// Runs the Village Workers tests inside the game. tests/run-tests.ps1 installs this plugin and creates the marker
    /// file; without the marker the plugin does nothing, so a leftover copy can't hijack a normal game.
    /// In the game, it loads (or creates) a test character and world, or joins the test server, runs every [Test],
    /// writes the results file and quits. On the dedicated server (-Server) it only tells the script when the server is
    /// ready and carries out the tests' requests (see <see cref="ServerSide"/>).
    /// </summary>
    [BepInPlugin("charmingbeaver.valheim.villageworkers.tests", "Village Workers Tests", "1.0.0")]
    [BepInDependency(Plugin.Guid)]
    public class TestPlugin : BaseUnityPlugin
    {
        public const string MarkerFile = "VillageWorkers.Tests.run";
        public const string ResultsFile = "VillageWorkers.Tests.results.txt";
        public const string ScreenshotDir = "VillageWorkers.Tests.screenshots";

        internal static ManualLogSource Log;
        internal static TestPlugin Instance;
        private static string _resultsPath;

        public static Setup Setup { get; private set; }

        /// <summary>The marker file's settings (key=value lines), e.g. host, port, password and server-dir for -Server.</summary>
        public static Dictionary<string, string> Options { get; private set; } = new Dictionary<string, string>();

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            string marker = Path.Combine(Paths.ConfigPath, MarkerFile);
            if (!File.Exists(marker))
            {
                Log.LogInfo($"No {MarkerFile} in BepInEx/config, tests stay off");
                return;
            }
            string text = File.ReadAllText(marker).Trim();
            // Removed straight away: if the game crashes, the next normal start is a normal game again.
            File.Delete(marker);
            // "play" alone is run-tests.ps1 -Play; anything else is key=value lines.
            if (text == "play") Options["mode"] = "play";
            foreach (string line in text.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) Options[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            string mode = Opt("mode", "test");
            Setup = (Setup)Enum.Parse(typeof(Setup), Opt("setup", nameof(Setup.Singleplayer)), true);

            if (mode == "server")
            {
                StartCoroutine(ServerSide.Run());
                return;
            }
            _resultsPath = Path.Combine(Paths.BepInExRootPath, ResultsFile);
            File.WriteAllText(_resultsPath, "");
            TestKit.ScreenshotPath = Path.Combine(Paths.BepInExRootPath, ScreenshotDir);
            Directory.CreateDirectory(TestKit.ScreenshotPath);
            StartCoroutine(Run(mode == "play"));
        }

        public static string Opt(string key, string fallback = null) => Options.TryGetValue(key, out string v) && v.Length > 0 ? v : fallback;

        private static void Write(string line)
        {
            Log.LogInfo(line);
            File.AppendAllText(_resultsPath, line + Environment.NewLine);
        }

        private IEnumerator Run(bool play)
        {
            Write("STARTED");
            Write($"SETUP {Setup.ToString().ToLowerInvariant()}");
            IEnumerator start = Flatten(AutoStart.LoadTestWorld(), out Func<Exception> startError);
            while (start.MoveNext()) yield return start.Current;
            if (startError() != null)
            {
                Write($"ABORT Could not load the test world: {startError()}");
                Finish(0, 1);
                yield break;
            }
            if (play)
            {
                IEnumerator setup = Flatten(SetUpPlayArea(), out Func<Exception> setupError);
                while (setup.MoveNext()) yield return setup.Current;
                Write(setupError() == null ? "READY Test world loaded, the game is yours" : $"READY Test world loaded, but setting it up failed: {setupError()}");
                yield break;
            }

            var others = Chainloader.PluginInfos.Values.Where(p => p.Metadata.GUID != Plugin.Guid && p.Instance?.GetType() != typeof(TestPlugin)).ToList();
            if (others.Count > 0) Write("WITH " + string.Join(", ", others.Select(p => $"{p.Metadata.Name} {p.Metadata.Version} ({p.Metadata.GUID})")));

            // run-tests.ps1 -Only: just the tests with that in their name.
            string only = Opt("only");
            var tests = FindTests().Where(t => only == null || $"{t.DeclaringType.Name}.{t.Name}".IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Write($"RUNNING {tests.Count} tests");
            int passed = 0, failed = 0, skipped = 0;
            foreach (MethodInfo test in tests)
            {
                string name = $"{test.DeclaringType.Name}.{test.Name}";
                string skip = SkipReason(test);
                if (skip != null)
                {
                    skipped++;
                    Write($"SKIP {name}: {skip}");
                    continue;
                }
                float started = Time.realtimeSinceStartup;
                IEnumerator body = Flatten(Wrap(test), out Func<Exception> error);
                while (body.MoveNext()) yield return body.Current;
                IEnumerator cleanup = Flatten(TestKit.Reset(), out Func<Exception> cleanupError);
                while (cleanup.MoveNext()) yield return cleanup.Current;

                Exception e = error() ?? cleanupError();
                string time = $"({Time.realtimeSinceStartup - started:0.0}s)";
                if (e == null)
                {
                    passed++;
                    Write($"PASS {name} {time}");
                }
                else
                {
                    failed++;
                    string message = e is AssertionException ? e.Message : e.ToString();
                    Write($"FAIL {name} {time}: {message.Replace("\n", "\n    ")}");
                }
            }
            if (Setup == Setup.Server) ServerSide.Send("quit");
            Finish(passed, failed, skipped);
        }

        private static string SkipReason(MethodInfo test)
        {
            var missing = test.GetCustomAttributes<RequiresModAttribute>().Where(r => !Chainloader.PluginInfos.ContainsKey(r.Guid)).ToList();
            if (missing.Count > 0) return $"needs {string.Join(", ", missing.Select(r => r.Guid))}";
            var present = test.GetCustomAttributes<SkipWithModAttribute>().Where(s => Chainloader.PluginInfos.ContainsKey(s.Guid)).ToList();
            if (present.Count > 0) return string.Join("; ", present.Select(s => $"{s.Guid} {s.Reason}"));
            var setups = test.GetCustomAttributes<RequiresSetupAttribute>().Select(r => r.Setup).ToList();
            if (setups.Count > 0 && !setups.Contains(Setup))
                return $"only {string.Join(" or ", setups.Select(s => s.ToString().ToLowerInvariant()))} (run-tests.ps1 {string.Join(" / ", setups.Select(SetupSwitch))})";
            return null;
        }

        private static string SetupSwitch(Setup s) => s == Setup.Host ? "-Hosting" : s == Setup.Server ? "-Server" : "without -Hosting or -Server";

        /// <summary>A villager farming a planted field next to the player, and one with nothing to do yet.</summary>
        private static IEnumerator SetUpPlayArea()
        {
            yield return TestKit.Reset();
            Farm farm = null;
            yield return Farm.Create(Vector3.zero, 8f, 6f, 0f, f => farm = f);
            farm.PlantCrops(3);
            Vector3 side = farm.Area.ToWorld(-farm.Area.W * 0.5f - 4f, 0f);
            Container chest = TestKit.SpawnChest(side + Vector3.forward * 2f);
            TestKit.Put(chest.GetInventory(), farm.Seed, 50);
            Bed bed = TestKit.SpawnBed(side - Vector3.forward * 2f);
            WorkerAI farmer = null;
            yield return TestKit.Villager(side, bed, chest, w => farmer = w);
            TestKit.Farming(farmer, farm.Area);
            // Another one, for trying out the work area picker.
            yield return TestKit.Villager(TestKit.Ahead(3f), null, chest, w => { });
            // Teleport next to the farm.
            TestKit.Me.transform.position = TestKit.Ground(side - farm.Area.Rotation * Vector3.right * 3f) + Vector3.up * 0.2f;
            // Keep what was set up when the test plugin goes away with the game.
            TestKit.KeepSpawned();
        }

        private static IEnumerator Wrap(MethodInfo test)
        {
            yield return TestKit.Reset();
            object result = test.Invoke(null, null);
            if (result is IEnumerator e) yield return e;
        }

        private static void Finish(int passed, int failed, int skipped = 0)
        {
            Write($"DONE {passed} passed, {failed} failed" + (skipped > 0 ? $", {skipped} skipped" : ""));
            Application.Quit();
        }

        private static List<MethodInfo> FindTests()
        {
            return typeof(TestPlugin).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.MetadataToken)
                .ToList();
        }

        /// <summary>
        /// Runs nested coroutines as one, stopping at the first exception (which C# can't catch around a yield).
        /// </summary>
        internal static IEnumerator Flatten(IEnumerator root, out Func<Exception> error)
        {
            var box = new Exception[1];
            error = () => box[0];
            return FlattenInner(root, box);
        }

        private static IEnumerator FlattenInner(IEnumerator root, Exception[] error)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                object current;
                try
                {
                    if (!top.MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    current = top.Current;
                }
                catch (TargetInvocationException e)
                {
                    error[0] = e.InnerException ?? e;
                    yield break;
                }
                catch (Exception e)
                {
                    error[0] = e;
                    yield break;
                }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
        }
    }

    /// <summary>Marks a static method (void or IEnumerator) as a test.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class TestAttribute : Attribute
    {
    }

    /// <summary>Runs the test only in these setups (run-tests.ps1 -Hosting or -Server). Tests without it run in every setup.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class RequiresSetupAttribute : Attribute
    {
        public readonly Setup Setup;

        public RequiresSetupAttribute(Setup setup)
        {
            Setup = setup;
        }
    }

    /// <summary>
    /// Runs the test only when another mod (by its BepInEx GUID) is loaded, i.e. with run-tests.ps1 -With.
    /// Tests without it run in every setup.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class RequiresModAttribute : Attribute
    {
        public readonly string Guid;

        public RequiresModAttribute(string guid)
        {
            Guid = guid;
        }
    }

    /// <summary>Skips the test when another mod is loaded that changes on purpose what the test checks.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class SkipWithModAttribute : Attribute
    {
        public readonly string Guid;
        public readonly string Reason;

        public SkipWithModAttribute(string guid, string reason)
        {
            Guid = guid;
            Reason = reason;
        }
    }

    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message)
        {
        }
    }
}
