using System;
using System.Collections;
using System.Linq;
using StorageMaster.UI;
using UnityEngine;
using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    /// <summary>Opening the Storage window in a base with many chests mustn't freeze the game (#16).</summary>
    public static class PerformanceTests
    {
        private const int Chests = 60;

        // Generous, since machines differ: a frozen frame was several hundred milliseconds before #16.
        private const float LongestFrameMs = 100f;

        [Test]
        private static IEnumerator OpeningWithManyChestsDoesntFreeze()
        {
            Plugin.StorageMaxChests.Value = Chests;
            // Rings of 12 chests, 3 to 12 meters away.
            for (int i = 0; i < Chests; i++) SpawnChest(3f + i / 12 * 2.2f, i % 12 * 30f + i / 12 * 15f);
            yield return Frames(5);
            // For comparison: the longest of a few ordinary frames, without the window.
            float idle = 0f;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                idle = Mathf.Max(idle, Time.unscaledDeltaTime * 1000f);
            }

            float first = 0f, again = 0f, firstFill = 0f, againFill = 0f;
            yield return LongestFrameWhileOpening((ms, fill) => (first, firstFill) = (ms, fill));
            StorageWindow.Close();
            yield return Frames(3);
            yield return LongestFrameWhileOpening((ms, fill) => (again, againFill) = (ms, fill));

            Debug.Log($"[StorageMaster.Tests] Opening the Storage window with {Chests} chests: longest frame {first:0} ms the first time, {again:0} ms the next time " +
                      $"(without the window: {idle:0} ms). All chests open after {firstFill:0.0} s, and {againFill:0.0} s the next time.");
            Assert.True(first < LongestFrameMs, $"Longest frame opening the first time: {first:0} ms (at most {LongestFrameMs:0})");
            Assert.True(again < LongestFrameMs, $"Longest frame opening again: {again:0} ms (at most {LongestFrameMs:0})");
        }

        /// <summary>
        /// Opens the window and reports the longest frame until every chest is open (and a few frames more), and how
        /// many seconds it took until every chest was open.
        /// </summary>
        private static IEnumerator LongestFrameWhileOpening(Action<float, float> report)
        {
            float start = Time.realtimeSinceStartup;
            StorageWindow.Open();
            float longest = 0f, filled = 0f;
            int after = 0;
            while (after < 5)
            {
                // A frame's time shows up in the next frame, so this includes the frame Open() ran in.
                yield return null;
                longest = Mathf.Max(longest, Time.unscaledDeltaTime * 1000f);
                if (StorageWindow._cards.Count == Chests && StorageWindow._cards.All(card => card.Claimed))
                {
                    if (after++ == 0) filled = Time.realtimeSinceStartup - start;
                }
                if (Time.realtimeSinceStartup > start + 30f) throw new AssertionException("Timed out waiting for every chest in the window to open");
            }
            report(longest, filled);
        }
    }
}
