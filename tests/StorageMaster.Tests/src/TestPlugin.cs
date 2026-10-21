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

namespace StorageMaster.Tests
{
    /// <summary>
    /// Runs the Storage Master tests inside the game. tests/run-tests.ps1 installs this plugin and creates the
    /// marker file; without the marker the plugin does nothing, so a leftover copy can't hijack a normal game.
    /// It loads (or creates) a test character and world, runs every [Test], writes the results file and quits.
    /// </summary>
    [BepInPlugin("charmingbeaver.valheim.storagemaster.tests", "Storage Master Tests", "1.0.0")]
    [BepInDependency(Plugin.Guid)]
    public class TestPlugin : BaseUnityPlugin
    {
        public const string MarkerFile = "StorageMaster.Tests.run";
        public const string ResultsFile = "StorageMaster.Tests.results.txt";
        public const string ScreenshotDir = "StorageMaster.Tests.screenshots";

        internal static ManualLogSource Log;
        private static string _resultsPath;
        private static bool _play;

        private void Awake()
        {
            Log = Logger;
            string marker = Path.Combine(Paths.ConfigPath, MarkerFile);
            if (!File.Exists(marker))
            {
                Log.LogInfo($"No {MarkerFile} in BepInEx/config, tests stay off");
                return;
            }
            // "play" (run-tests.ps1 -Play) only loads the test world, for trying things by hand.
            _play = File.ReadAllText(marker).Trim() == "play";
            // Removed straight away: if the game crashes, the next normal start is a normal game again.
            File.Delete(marker);
            _resultsPath = Path.Combine(Paths.BepInExRootPath, ResultsFile);
            File.WriteAllText(_resultsPath, "");
            TestKit.ScreenshotPath = Path.Combine(Paths.BepInExRootPath, ScreenshotDir);
            Directory.CreateDirectory(TestKit.ScreenshotPath);
            StartCoroutine(Run());
        }

        private static void Write(string line)
        {
            Log.LogInfo(line);
            File.AppendAllText(_resultsPath, line + Environment.NewLine);
        }

        private IEnumerator Run()
        {
            Write("STARTED");
            IEnumerator start = Flatten(AutoStart.LoadTestWorld(), out Func<Exception> startError);
            while (start.MoveNext()) yield return start.Current;
            if (startError() != null)
            {
                Write($"ABORT Could not load the test world: {startError()}");
                Finish(0, 1);
                yield break;
            }
            if (_play)
            {
                IEnumerator setup = Flatten(SetUpPlayArea(), out Func<Exception> setupError);
                while (setup.MoveNext()) yield return setup.Current;
                Write(setupError() == null ? "READY Test world loaded, the game is yours" : $"READY Test world loaded, but setting up chests failed: {setupError()}");
                yield break;
            }

            var others = Chainloader.PluginInfos.Values.Where(p => p.Metadata.GUID != Plugin.Guid && p.Instance?.GetType() != typeof(TestPlugin)).ToList();
            if (others.Count > 0) Write("WITH " + string.Join(", ", others.Select(p => $"{p.Metadata.Name} {p.Metadata.Version} ({p.Metadata.GUID})")));

            var tests = FindTests();
            Write($"RUNNING {tests.Count} tests");
            int passed = 0, failed = 0, skipped = 0;
            foreach (MethodInfo test in tests)
            {
                string name = $"{test.DeclaringType.Name}.{test.Name}";
                var missing = test.GetCustomAttributes<RequiresModAttribute>().Where(r => !Chainloader.PluginInfos.ContainsKey(r.Guid)).ToList();
                if (missing.Count > 0)
                {
                    skipped++;
                    Write($"SKIP {name}: needs {string.Join(", ", missing.Select(r => r.Guid))}");
                    continue;
                }
                var present = test.GetCustomAttributes<SkipWithModAttribute>().Where(s => Chainloader.PluginInfos.ContainsKey(s.Guid)).ToList();
                if (present.Count > 0)
                {
                    skipped++;
                    Write($"SKIP {name}: {string.Join("; ", present.Select(s => $"{s.Guid} {s.Reason}"))}");
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
            Finish(passed, failed, skipped);
        }

        /// <summary>A few chests with items next to the player, to try the Storage window with.</summary>
        private static IEnumerator SetUpPlayArea()
        {
            string[] items = { "Wood", "Stone", "Resin", "Flint", "Raspberry", "LeatherScraps", "DeerHide", "Coins" };
            // Always the same layout, so a session can be repeated.
            var random = new System.Random(42);
            // 20 chests all around, 3 to 14 m away (all within the Storage window's range).
            for (int c = 0; c < 20; c++)
            {
                Container chest = TestKit.SpawnChest(3f + c % 6 * 2.2f, c * 18f);
                yield return null;
                Inventory inv = chest.GetInventory();
                int width = inv.GetWidth(), slots = width * inv.GetHeight();
                if (c % 3 == 0)
                {
                    // Nearly full of one item, with a different item in the last slot.
                    string main = items[c / 3 % items.Length];
                    for (int s = 0; s < slots - 1; s++) TestKit.Put(inv, main, random.Next(10, 51), s % width, s / width);
                    string other = items[(c / 3 + 3) % items.Length];
                    TestKit.Put(inv, other, random.Next(1, 30), (slots - 1) % width, (slots - 1) / width);
                }
                else
                {
                    // A few stacks of mixed items.
                    int count = random.Next(2, 6);
                    for (int s = 0; s < count; s++) TestKit.Put(inv, items[random.Next(items.Length)], random.Next(1, 51), s % width, s / width);
                }
            }
            // The test character's inventory is only for testing.
            TestKit.Mine.RemoveAll();
            TestKit.Put(TestKit.Mine, "Wood", 20, 0, 1);
            TestKit.Put(TestKit.Mine, "Stone", 15, 1, 1);

            // With Adventure Backpacks: a backpack on your back, with items the chests hold.
            if (AdventureBackpacksCompat.Active)
            {
                ItemDrop.ItemData backpackItem = TestKit.Put(TestKit.Mine, "BackpackMeadows", 1, 7, 3);
                TestKit.Me.EquipItem(backpackItem);
                yield return null;
                Inventory backpack = AdventureBackpacksCompat.Backpack(TestKit.Me);
                if (backpack != null)
                {
                    backpack.RemoveAll();
                    string[] inBackpack = { "Resin", "Flint", "Raspberry" };
                    for (int i = 0; i < inBackpack.Length && i < backpack.GetWidth() * backpack.GetHeight(); i++)
                    {
                        TestKit.Put(backpack, inBackpack[i], 10 + 5 * i, i % backpack.GetWidth(), i / backpack.GetWidth());
                    }
                }
            }
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
        private static IEnumerator Flatten(IEnumerator root, out Func<Exception> error)
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

    /// <summary>
    /// Skips the test when another mod is loaded that changes on purpose what the test checks, e.g. ExtraSlots keeping
    /// hotbar items out of "Place stacks". The reason shows in the results.
    /// </summary>
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
