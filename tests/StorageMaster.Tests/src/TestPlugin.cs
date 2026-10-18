using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
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

        private void Awake()
        {
            Log = Logger;
            string marker = Path.Combine(Paths.ConfigPath, MarkerFile);
            if (!File.Exists(marker))
            {
                Log.LogInfo($"No {MarkerFile} in BepInEx/config, tests stay off");
                return;
            }
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

            var tests = FindTests();
            Write($"RUNNING {tests.Count} tests");
            int passed = 0, failed = 0;
            foreach (MethodInfo test in tests)
            {
                string name = $"{test.DeclaringType.Name}.{test.Name}";
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
            Finish(passed, failed);
        }

        private static IEnumerator Wrap(MethodInfo test)
        {
            yield return TestKit.Reset();
            object result = test.Invoke(null, null);
            if (result is IEnumerator e) yield return e;
        }

        private static void Finish(int passed, int failed)
        {
            Write($"DONE {passed} passed, {failed} failed");
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

    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message)
        {
        }
    }
}
