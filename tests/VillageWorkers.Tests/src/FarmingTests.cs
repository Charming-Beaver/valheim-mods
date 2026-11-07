using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VillageWorkers.Jobs;

namespace VillageWorkers.Tests
{
    /// <summary>Farmers harvest, replant and plant fields, alone and together, so that every crop grows.</summary>
    public static class FarmingTests
    {
        private static string CropItem(Farm farm) => farm.CropPrefab.GetComponent<Pickable>().m_itemPrefab.name;

        private static int Planted(WorkerAI w) => w.Stats.L.Count(e => e.M.StartsWith("planted"));

        /// <summary>The farmer picks every ripe crop, plants the seed back where it grew and stores the harvest.</summary>
        [Test]
        public static IEnumerator HarvestsAndReplants()
        {
            Farm farm = null;
            yield return Farm.Create(Vector3.zero, 6f, 6f, 0f, f => farm = f);
            List<Pickable> crops = farm.PlantCrops(2);
            List<Vector3> spots = crops.Select(c => c.transform.position).ToList();
            Container chest = TestKit.SpawnChest(farm.Area.ToWorld(-6f, -1f));
            // Seeds for replanting only: with more, the farmer would plant the rest of the field too.
            TestKit.Put(chest.GetInventory(), farm.Seed, spots.Count);
            WorkerAI w = null;
            yield return TestKit.Villager(farm.Area.ToWorld(-5f, 1f), null, chest, x => w = x);
            yield return TestKit.Indexed(crops);
            TestKit.Farming(w, farm.Area);

            yield return TestKit.WaitUntil(() => TestKit.Crops(farm).Count == 0 && TestKit.Saplings(farm).Count == spots.Count,
                "the farmer to harvest and replant every crop", 150f, () => TestKit.Describe(w, chest));
            List<Plant> saplings = TestKit.Saplings(farm);
            foreach (Vector3 s in spots)
                Assert.True(saplings.Any(p => Utils.DistanceXZ(p.transform.position, s) < 0.3f), $"A crop was replanted where one grew, at {s}");
            Assert.Equal(0, TestKit.Count(chest.GetInventory(), farm.Seed) + TestKit.Count(w.Inv, farm.Seed), "Seeds left");
            yield return TestKit.WaitUntil(() => TestKit.Count(chest.GetInventory(), CropItem(farm)) >= spots.Count,
                "the harvest to be stored in the chest", 120f, () => TestKit.Describe(w, chest));
            TestKit.AssertAllGrow(saplings, "Replanted");
        }

        /// <summary>
        /// With seeds in the chest and a box set, the farmer plants the whole field (a turned box), as close together as
        /// lets every crop grow, also next to crops that have grown, and nothing outside it.
        /// </summary>
        [Test]
        public static IEnumerator PlantsTheFieldWithRoomToGrow()
        {
            Farm farm = null;
            yield return Farm.Create(Vector3.zero, 4f, 5f, 22.5f, f => farm = f, margin: 3f);
            Container chest = TestKit.SpawnChest(farm.Area.ToWorld(-6f, 0f));
            TestKit.Put(chest.GetInventory(), farm.Seed, 100);
            WorkerAI w = null;
            yield return TestKit.Villager(farm.Area.ToWorld(-5f, 2f), null, chest, x => w = x);
            TestKit.Farming(w, farm.Area);

            yield return TestKit.WaitUntilSettled(() => TestKit.Saplings(farm).Count, "planting", 25f, 300f);
            List<Plant> saplings = TestKit.Saplings(farm);
            float step = PlantSpace.Spacing(farm.Plant, farm.Plant);
            int room = Mathf.FloorToInt(farm.Area.W / step) * Mathf.FloorToInt(farm.Area.L / step);
            TestPlugin.Log.LogInfo($"Planted {saplings.Count} crops {step:0.00} m apart, the field has room for {room}");
            Assert.True(saplings.Count >= room, $"The farmer planted {saplings.Count} crops, the field has room for {room} ({w.GetStatus()}); "
                                                + TestKit.ExplainEmptySpots(farm, saplings));
            foreach (Plant p in saplings)
            {
                Assert.True(farm.Area.Contains(p.transform.position), $"The crop at {p.transform.position} is inside the box");
                Assert.True(PlantSpace.Cultivated(p.transform.position), $"The crop at {p.transform.position} is on cultivated ground");
            }
            Assert.Equal(saplings.Count, 100 - TestKit.Count(chest.GetInventory(), farm.Seed) - TestKit.Count(w.Inv, farm.Seed), "Seeds used");
            yield return TestKit.ScreenshotOf(farm.Area.Center, "planted-field");
            TestKit.AssertAllGrow(saplings, "Planted");
            yield return TestKit.GrowHalf(saplings, "Planted");
            yield return TestKit.ScreenshotOf(farm.Area.Center, "planted-field-half-grown");
        }

        /// <summary>
        /// Two farmers with boxes that overlap (one turned, so their rows don't line up) plant from the same chest:
        /// they never plant on the same spot or too close to each other's crops.
        /// </summary>
        [Test]
        public static IEnumerator OverlappingAreasShareTheField()
        {
            // What the farmers decide goes to LogOutput.log, for when this fails.
            Plugin.DebugLogging.Value = true;
            Farm a = null, b = null;
            yield return Farm.Create(new Vector3(-1.5f, 0f, 0f), 5f, 5f, 0f, f => a = f, margin: 2f);
            yield return Farm.Create(new Vector3(1.5f, 0f, 0.5f), 5f, 5f, 22.5f, f => b = f, margin: 2f);
            Container chest = TestKit.SpawnChest(a.Area.ToWorld(-6f, 0f));
            TestKit.Put(chest.GetInventory(), a.Seed, 200);
            WorkerAI wa = null, wb = null;
            yield return TestKit.Villager(a.Area.ToWorld(-5f, 2f), null, chest, x => wa = x);
            yield return TestKit.Villager(a.Area.ToWorld(-5f, -2f), null, chest, x => wb = x);
            TestKit.Farming(wa, a.Area);
            TestKit.Farming(wb, b.Area);

            // A farmer who finds nothing free for a moment (the other one has the last spots) takes a break of 45 s before
            // looking again, plus up to 20 s until its tasks are checked.
            yield return TestKit.WaitUntilSettled(() => TestKit.Saplings(a).Count, "planting", 75f, 500f);
            List<Plant> saplings = TestKit.Saplings(a);
            float step = PlantSpace.Spacing(a.Plant, a.Plant);
            int oneField = Mathf.FloorToInt(a.Area.W / step) * Mathf.FloorToInt(a.Area.L / step);
            TestPlugin.Log.LogInfo($"Planted {saplings.Count} crops ({wa.GetName()} {Planted(wa)}, {wb.GetName()} {Planted(wb)}); one field has room for {oneField}");
            Assert.True(Planted(wa) > 0 && Planted(wb) > 0, $"Both farmers planted ({Planted(wa)} and {Planted(wb)})");
            Assert.True(saplings.Count > oneField, $"Together they planted more than one field holds: {saplings.Count}, one holds {oneField}. "
                                                  + $"{TestKit.Describe(wa, chest)}. {TestKit.Describe(wb)}. First field: {TestKit.ExplainEmptySpots(a, saplings)}. "
                                                  + $"Second field: {TestKit.ExplainEmptySpots(b, saplings)}");
            foreach (Plant p in saplings)
                Assert.True(a.Area.Contains(p.transform.position) || b.Area.Contains(p.transform.position), $"The crop at {p.transform.position} is in one of the boxes");
            yield return TestKit.ScreenshotOf(Vector3.Lerp(a.Area.Center, b.Area.Center, 0.5f), "overlapping-fields");
            TestKit.AssertAllGrow(saplings, "Planted by two farmers");
            yield return TestKit.GrowHalf(saplings, "Planted by two farmers");
        }

        /// <summary>Two farmers with the same field and its ripe crops: each crop is picked once and replanted once.</summary>
        [Test]
        public static IEnumerator TwoFarmersHarvestTheSameCrops()
        {
            // What the farmers decide goes to LogOutput.log, for when this fails.
            Plugin.DebugLogging.Value = true;
            Farm farm = null;
            yield return Farm.Create(Vector3.zero, 6f, 6f, 0f, f => farm = f);
            List<Pickable> crops = farm.PlantCrops(3);
            List<Vector3> spots = crops.Select(c => c.transform.position).ToList();
            Container chest = TestKit.SpawnChest(farm.Area.ToWorld(-6f, 0f));
            // Exactly enough to replant, so a second sapling on a spot would show as a crop not replanted elsewhere.
            TestKit.Put(chest.GetInventory(), farm.Seed, spots.Count);
            WorkerAI wa = null, wb = null;
            yield return TestKit.Villager(farm.Area.ToWorld(-5f, 2f), null, chest, x => wa = x);
            yield return TestKit.Villager(farm.Area.ToWorld(-5f, -2f), null, chest, x => wb = x);
            yield return TestKit.Indexed(crops);
            TestKit.Farming(wa, farm.Area);
            TestKit.Farming(wb, farm.Area);

            yield return TestKit.WaitUntil(() => TestKit.Crops(farm).Count == 0, "both farmers to harvest every crop", 150f);
            yield return TestKit.WaitUntilSettled(() => TestKit.Saplings(farm).Count, "replanting", 45f, 180f);
            List<Plant> saplings = TestKit.Saplings(farm);
            foreach (Vector3 s in spots)
            {
                int here = saplings.Count(p => Utils.DistanceXZ(p.transform.position, s) < 0.3f);
                Assert.True(here == 1, $"Crops replanted at {s}: {here}. {TestKit.Describe(wa, chest)}. {TestKit.Describe(wb)}");
            }
            Assert.Equal(spots.Count, saplings.Count, "Crops replanted");
            int harvested = TestKit.Count(chest.GetInventory(), CropItem(farm)) + TestKit.Count(wa.Inv, CropItem(farm)) + TestKit.Count(wb.Inv, CropItem(farm));
            Assert.True(harvested >= spots.Count, $"Every crop was harvested once: {harvested} {CropItem(farm)}");
            TestKit.AssertAllGrow(saplings, "Replanted by two farmers");
        }

        /// <summary>
        /// Planting a carrot grows a seed carrot (for Carrot Seeds). The farmer doesn't plant the harvest like that, unless
        /// the task's options choose that crop.
        /// </summary>
        [Test]
        public static IEnumerator PlantsTheHarvestOnlyWhenAskedTo()
        {
            Farm farm = null;
            yield return Farm.Create(Vector3.zero, 3f, 3f, 0f, f => farm = f);
            Container chest = TestKit.SpawnChest(farm.Area.ToWorld(-5f, 0f));
            TestKit.Put(chest.GetInventory(), "Carrot", 20);
            WorkerAI w = null;
            yield return TestKit.Villager(farm.Area.ToWorld(-4f, 2f), null, chest, x => w = x);
            TestKit.Farming(w, farm.Area);
            int Plants() => Object.FindObjectsByType<Plant>(FindObjectsSortMode.None).Count(p => farm.Area.Contains(p.transform.position));

            yield return new WaitForSeconds(15f);
            Assert.Equal(0, Plants(), "Crops planted with carrots");
            Assert.Equal(20, TestKit.Count(chest.GetInventory(), "Carrot") + TestKit.Count(w.Inv, "Carrot"), "Carrots");

            // Options: only seed carrots, the crop that gives Carrot Seeds.
            WorkerConfig cfg = w.Config.Clone();
            cfg.Tasks[0].Filter = new List<string> { Farm.SeedName };
            w.NView.GetZDO().Set(WorkerAI.K_Cfg, cfg.ToJson());
            yield return TestKit.WaitUntil(() => Plants() >= 4, "seed carrots to be planted", 90f, () => TestKit.Describe(w, chest));
        }

        /// <summary>
        /// The farmer picks every ripe crop before replanting, then stores the harvest and takes the seeds in one trip:
        /// not a walk to the chest for every crop.
        /// </summary>
        [Test]
        public static IEnumerator HarvestsEverythingBeforeReplanting()
        {
            Farm farm = null;
            yield return Farm.Create(Vector3.zero, 6f, 6f, 0f, f => farm = f);
            List<Pickable> crops = farm.PlantCrops(3);
            // Far enough that a trip to it shows.
            Container chest = TestKit.SpawnChest(farm.Area.ToWorld(-10f, 0f));
            TestKit.Put(chest.GetInventory(), farm.Seed, crops.Count);
            WorkerAI w = null;
            yield return TestKit.Villager(farm.Area.ToWorld(-2f, -4f), null, chest, x => w = x);
            yield return TestKit.Indexed(crops);
            TestKit.Farming(w, farm.Area);

            int visits = 0, ripeWhenReplanting = -1;
            bool atChest = false;
            float end = Time.realtimeSinceStartup + 180f;
            while (TestKit.Saplings(farm).Count < crops.Count)
            {
                Assert.True(Time.realtimeSinceStartup < end, $"Timed out waiting for the field to be replanted ({TestKit.Describe(w, chest)})");
                float d = Utils.DistanceXZ(w.transform.position, chest.transform.position);
                if (!atChest && d < 2.5f) visits++;
                atChest = d < 2.5f || (atChest && d < 4f);
                if (ripeWhenReplanting < 0 && TestKit.Saplings(farm).Count > 0) ripeWhenReplanting = TestKit.Crops(farm).Count;
                yield return null;
            }
            Assert.Equal(0, ripeWhenReplanting, "Ripe crops left when the first one was replanted");
            Assert.True(visits <= 1, $"The farmer went to the chest {visits} times to replant {crops.Count} crops");
            yield return TestKit.WaitUntil(() => TestKit.Count(chest.GetInventory(), CropItem(farm)) >= crops.Count,
                "the harvest to be stored in the chest", 30f, () => TestKit.Describe(w, chest));
            TestKit.AssertAllGrow(TestKit.Saplings(farm), "Replanted");
        }

        /// <summary>
        /// Planting takes a cultivator, as for a player: without one the farmer leaves the field alone. Given one in a
        /// chest, they take it, use it and wear it.
        /// </summary>
        [Test]
        public static IEnumerator PlantingTakesACultivator()
        {
            Farm farm = null;
            // Room around the crops for new ones.
            yield return Farm.Create(Vector3.zero, 6f, 6f, 0f, f => farm = f);
            List<Pickable> crops = farm.PlantCrops(2);
            Container chest = TestKit.SpawnChest(farm.Area.ToWorld(-5f, 0f));
            TestKit.Put(chest.GetInventory(), farm.Seed, 20);
            WorkerAI w = null;
            yield return TestKit.Villager(farm.Area.ToWorld(-4f, 2f), null, chest, x => w = x);
            yield return TestKit.Indexed(crops);
            TestKit.FarmingWithoutCultivator(w, farm.Area);

            yield return new WaitForSeconds(15f);
            Assert.Equal(crops.Count, TestKit.Crops(farm).Count, $"Crops left without a cultivator ({w.GetStatus()})");
            Assert.Equal(0, TestKit.Saplings(farm).Count, "Crops planted without a cultivator");

            TestKit.Put(chest.GetInventory(), "Cultivator", 1);
            float full = ObjectDB.instance.GetItemPrefab("Cultivator").GetComponent<ItemDrop>().m_itemData.GetMaxDurability();
            yield return TestKit.WaitUntil(() => TestKit.Crops(farm).Count == 0 && TestKit.Saplings(farm).Count > crops.Count,
                "the farmer to take the cultivator, harvest and plant", 150f,
                () => TestKit.Describe(w, chest) + "; " + TestKit.ExplainEmptySpots(farm, TestKit.Saplings(farm)));
            ItemDrop.ItemData cultivator = w.Inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab?.name == "Cultivator");
            Assert.True(cultivator != null, "The farmer carries the cultivator");
            Assert.True(cultivator.m_durability < full, $"The cultivator wore down from planting ({cultivator.m_durability:0.0} of {full:0.0})");
        }

        /// <summary>When two villagers claim the same spot at the same moment, exactly one of them gives way.</summary>
        [Test]
        public static IEnumerator SameMomentClaimsLetExactlyOneGo()
        {
            WorkerAI a = null, b = null;
            yield return TestKit.Villager(TestKit.Ahead(3f), null, null, x => a = x);
            yield return TestKit.Villager(TestKit.Ahead(4f), null, null, x => b = x);
            // Let them start (which clears any claim an earlier owner left).
            yield return new WaitForSeconds(1f);
            Vector3 spot = TestKit.Ahead(10f);
            a.SetClaims(new List<Vector3> { spot });
            b.SetClaims(new List<Vector3> { TestKit.Ahead(30f), spot + Vector3.right * 0.1f });
            yield return null;
            Assert.True(a.ClaimedByOther(spot, 0.3f, false) && b.ClaimedByOther(spot, 0.3f, false), "Each sees the other's claim");
            bool aGivesWay = a.ClaimedByOther(spot, 0.3f, true), bGivesWay = b.ClaimedByOther(spot, 0.3f, true);
            Assert.True(aGivesWay != bGivesWay, $"Exactly one gives way (the first: {aGivesWay}, the second: {bGivesWay})");
            a.ReleaseClaims();
            yield return null;
            Assert.True(!b.ClaimedByOther(spot, 0.3f, false), "A released claim is free again");
            Assert.True(!a.ClaimedByOther(TestKit.Ahead(20f), 0.3f, false), "Claims only cover their spot");
        }
    }
}
