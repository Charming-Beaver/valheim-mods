using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillageWorkers.Tests
{
    /// <summary>Work areas: circles and turned boxes, and marking a box in the world.</summary>
    public static class AreaTests
    {
        [Test]
        public static void BoxContainsOnlyWhatIsInside()
        {
            Vector3 c = new Vector3(100f, 30f, -50f);
            AreaDef box = AreaDef.Box(c, 4f, 10f, 30f);
            Assert.True(box.IsBox, "A box is a box");
            Assert.True(Mathf.Abs(box.R - Mathf.Sqrt(4f + 25f)) < 0.01f, $"Its range is the distance to a corner, got {box.R}");
            foreach (Vector3 p in new[] { c, box.ToWorld(1.9f, 4.9f), box.ToWorld(-1.9f, -4.9f), box.ToWorld(1.9f, -4.9f) + Vector3.up * 20f })
                Assert.True(box.Contains(p), $"The box contains {box.Center - p}");
            // Within its range, but beside it.
            foreach (Vector3 p in new[] { box.ToWorld(2.1f, 0f), box.ToWorld(0f, 5.1f), box.ToWorld(3f, 3f), c + Vector3.right * 4.9f })
                Assert.True(!box.Contains(p), $"The box doesn't contain {box.Center - p}");

            AreaDef circle = new AreaDef(c, 5f);
            Assert.True(!circle.IsBox, "A circle isn't a box");
            Assert.True(circle.Contains(c + new Vector3(3f, 9f, 3.9f)), "The circle contains a point 4.9 m from its center");
            Assert.True(!circle.Contains(c + new Vector3(3f, 0f, 4.1f)), "The circle doesn't contain a point 5.1 m from its center");
        }

        [Test]
        public static void AreasAreSavedWithTheVillager()
        {
            var cfg = new WorkerConfig();
            cfg.Tasks.Add(new TaskConfig { Type = JobType.Farming, Area = AreaDef.Box(new Vector3(1f, 2f, 3f), 4f, 6f, 22.5f) });
            AreaDef loaded = WorkerConfig.FromJson(cfg.ToJson()).Tasks[0].Area;
            Assert.True(loaded.IsBox && loaded.W == 4f && loaded.L == 6f && loaded.A == 22.5f, $"The box came back as {loaded.Describe()} turned {loaded.A}");

            // Saved before boxes existed.
            AreaDef old = WorkerConfig.FromJson("{\"Tasks\":[{\"Type\":3,\"Area\":{\"X\":1,\"Y\":2,\"Z\":3,\"R\":20}}]}").Tasks[0].Area;
            Assert.True(!old.IsBox && old.R == 20f && old.Contains(new Vector3(15f, 0f, 3f)), "An area saved before boxes is a circle");
        }

        [Test]
        public static void BoxBetweenTwoCorners()
        {
            WorldPicker.BoxBetween(Vector3.zero, new Vector3(4f, 2f, -6f), 0f, out Vector3 center, out float w, out float l);
            Assert.True(w == 4f && l == 6f && (center - new Vector3(2f, 1f, -3f)).magnitude < 0.01f, $"Straight: {w} x {l} at {center}");

            // Turned: the sides follow the turn, whichever corners are marked.
            Quaternion rot = Quaternion.Euler(0f, 45f, 0f);
            Vector3 a = new Vector3(10f, 0f, 10f), b = a + rot * new Vector3(-3f, 0f, 8f);
            WorldPicker.BoxBetween(a, b, 45f, out center, out w, out l);
            Assert.True(Mathf.Abs(w - 3f) < 0.01f && Mathf.Abs(l - 8f) < 0.01f, $"Turned: {w} x {l}");
            AreaDef box = AreaDef.Box(center, w, l, 45f);
            Assert.True(box.Contains(Vector3.Lerp(a, b, 0.01f)) && box.Contains(Vector3.Lerp(a, b, 0.99f)), "The turned box reaches both corners");

            // The same point twice still makes a box you can see.
            WorldPicker.BoxBetween(a, a, 0f, out _, out w, out l);
            Assert.True(w == WorldPicker.MinBoxSize && l == WorldPicker.MinBoxSize, $"Smallest box: {w} x {l}");
        }

        /// <summary>
        /// The farmer has a long, narrow box: a ripe crop beside it (but within its range, which is all a circle would
        /// look at) stays where it is, while the one inside is harvested.
        /// </summary>
        [Test]
        public static IEnumerator FarmerStaysInsideTheBox()
        {
            Farm farm = null;
            yield return Farm.Create(Vector3.zero, 2f, 10f, 22.5f, f => farm = f, margin: 3f);
            Pickable inside = TestKit.Spawn(farm.CropPrefab.name, farm.Area.ToWorld(0f, 3f)).GetComponent<Pickable>();
            Pickable beside = TestKit.Spawn(farm.CropPrefab.name, farm.Area.ToWorld(2.5f, 0f)).GetComponent<Pickable>();
            Assert.True(Utils.DistanceXZ(beside.transform.position, farm.Area.Center) < farm.Area.R && !farm.Area.Contains(beside.transform.position),
                "The crop beside the box is within its range");
            Container chest = TestKit.SpawnChest(farm.Area.ToWorld(-4f, -2f));
            TestKit.Put(chest.GetInventory(), farm.Seed, 1);
            WorkerAI w = null;
            yield return TestKit.Villager(farm.Area.ToWorld(-3f, 0f), null, chest, x => w = x);
            yield return TestKit.Indexed(new[] { inside, beside });
            TestKit.Farming(w, farm.Area);

            yield return TestKit.WaitUntil(() => inside == null || inside.GetPicked(), "the crop inside to be harvested", 90f);
            yield return new WaitForSeconds(10f);
            Assert.True(beside != null && !beside.GetPicked(), $"The crop beside the box is still there ({w.GetStatus()})");
            Assert.True(TestKit.Saplings(farm).All(p => farm.Area.Contains(p.transform.position)), "Nothing was planted outside the box");
        }

        /// <summary>
        /// Marking a box in the world, as a player does: look at one corner, press E, look at the other, press E. (Where the
        /// player looks is set directly: turning the camera onto a spot from a test isn't precise.)
        /// </summary>
        [Test]
        public static IEnumerator PickerMarksABoxFromTwoCorners()
        {
            Farm farm = null;
            yield return Farm.Create(Vector3.zero, 6f, 6f, 0f, f => farm = f, margin: 3f);
            WorkerAI w = null;
            yield return TestKit.Villager(TestKit.Ahead(3f), null, null, x => w = x);
            TestKit.Farming(w, null);
            yield return TestKit.Frames(2);

            // Looking north, so the box isn't turned.
            Vector3 a = TestKit.Ground(Farm.Spot + new Vector3(-2f, 0f, -3f)), b = TestKit.Ground(Farm.Spot + new Vector3(2f, 0f, 3f));
            yield return TestKit.LookAt(TestKit.Me.transform.position + Vector3.forward * 10f);
            UI.VillageWindow.Open(w);
            yield return TestKit.Frames(3);
            UI.VillageWindow.ShowAreaChoice(w, 0);
            yield return TestKit.Screenshot("area-choice");
            UI.VillageWindow.ClosePopup();

            WorldPicker.Begin(w, WorldPicker.Mode.Box, 0);
            Assert.Equal(0f, WorldPicker._yaw, "The box starts square to where the player looks");
            WorldPicker.AimOverride = a;
            yield return TestKit.Frames(2);
            WorldPicker.OnInteract(null);
            Assert.True(WorldPicker._corner.HasValue, "The first corner is marked");
            WorldPicker.AimOverride = b;
            yield return TestKit.Frames(2);
            Assert.True(WorldPicker._outline != null && WorldPicker._outline.Segments >= 12,
                $"The box is outlined on the ground: {(WorldPicker._outline == null ? "no outline" : $"{WorldPicker._outline.Segments} segments, {WorldPicker._outline.Width:0.0} x {WorldPicker._outline.Length:0.0} m")}");
            // Seen from nearby, which doesn't change the box: its sides were set when the picker started.
            yield return TestKit.ScreenshotOf(Vector3.Lerp(a, b, 0.5f), "box-picker");
            WorldPicker.OnInteract(null);
            Assert.True(!WorldPicker.Active, "Marking the second corner finishes");

            yield return TestKit.WaitUntil(() => w.Config.Tasks[0].Area != null, "the area to reach the villager");
            AreaDef area = w.Config.Tasks[0].Area;
            Assert.True(area.IsBox && area.A == 0f, $"The task has a box, not turned: {area.Describe()} turned {area.A}");
            Assert.True(Mathf.Abs(area.W - 4f) < 0.01f && Mathf.Abs(area.L - 6f) < 0.01f, $"The box is about 4 x 6 m: {area.Describe()}");
            Assert.True(Utils.DistanceXZ(area.Center, Vector3.Lerp(a, b, 0.5f)) < 0.01f, "The box lies between the corners");

            // The window (reopened by the picker) describes it.
            yield return TestKit.WaitUntil(() => UI.VillageWindow.IsOpen, "the window to reopen");
            yield return TestKit.Screenshot("task-with-box");
        }
    }
}
