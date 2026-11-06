using System.Collections.Generic;
using UnityEngine;

namespace VillageWorkers.Jobs
{
    /// <summary>
    /// Where a crop has room to grow, by the game's own rules (Plant.UpdateHealth): the right biome and cultivated ground,
    /// no roof above, and nothing solid within its grow radius. Planting must also leave every crop around it that room,
    /// both as saplings and once they've grown.
    /// </summary>
    public static class PlantSpace
    {
        private static int _spaceMask, _roofMask;
        private static readonly Collider[] Hits = new Collider[128];
        private static readonly Dictionary<Plant, float> Footprints = new Dictionary<Plant, float>();

        // Further than any crop's grow radius plus its footprint.
        private const float NeighbourSearch = 4f;
        // Less than the margin Spacing adds.
        public const float Tolerance = 0.02f;

        /// <summary>The layers Plant.HaveGrowSpace looks at.</summary>
        public static int SpaceMask => _spaceMask != 0 ? _spaceMask
            : _spaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");

        private static int RoofMask => _roofMask != 0 ? _roofMask : _roofMask = LayerMask.GetMask("Default", "static_solid", "piece");

        /// <summary>How far apart two crops have to be so that both keep growing, also after one of them has grown.</summary>
        public static float Spacing(Plant a, Plant b) => Mathf.Max(a.m_growRadius + Footprint(b), b.m_growRadius + Footprint(a)) + 0.05f;

        /// <summary>How far a crop reaches out from where it was planted, as a sapling or grown (at its largest size).</summary>
        public static float Footprint(Plant plant)
        {
            if (Footprints.TryGetValue(plant, out float f)) return f;
            f = Reach(plant.gameObject, 1f);
            if (plant.m_grownPrefabs != null)
                foreach (GameObject grown in plant.m_grownPrefabs)
                    if (grown != null) f = Mathf.Max(f, Reach(grown, plant.m_maxScale));
            Footprints[plant] = f;
            return f;
        }

        /// <summary>Distance from the prefab's origin to the furthest point of its colliders that block growing, looking from above.</summary>
        private static float Reach(GameObject prefab, float scale)
        {
            float max = 0f;
            Vector3 root = prefab.transform.position;
            foreach (Collider c in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || (SpaceMask & (1 << c.gameObject.layer)) == 0) continue;
                Vector3 center, size;
                switch (c)
                {
                    case BoxCollider box:
                        center = box.center;
                        size = box.size;
                        break;
                    case SphereCollider sphere:
                        center = sphere.center;
                        size = Vector3.one * sphere.radius * 2f;
                        break;
                    case CapsuleCollider capsule:
                        center = capsule.center;
                        size = Vector3.one * capsule.radius * 2f;
                        size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                        break;
                    case MeshCollider mesh when mesh.sharedMesh != null:
                        center = mesh.sharedMesh.bounds.center;
                        size = mesh.sharedMesh.bounds.size;
                        break;
                    default:
                        center = Vector3.zero;
                        size = Vector3.one;
                        break;
                }
                // The corners of the collider's box, wherever the collider sits and however it's turned.
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = center + Vector3.Scale(size * 0.5f, new Vector3(i % 2 == 0 ? -1 : 1, i / 2 % 2 == 0 ? -1 : 1, i / 4 == 0 ? -1 : 1));
                    Vector3 offset = c.transform.TransformPoint(corner) - root;
                    offset.y = 0f;
                    max = Mathf.Max(max, offset.magnitude);
                }
            }
            return max * scale;
        }

        /// <summary>Whether the crop grows at all here: the biome, cultivated ground, heat and cold.</summary>
        public static bool Grows(Plant plant, Vector3 pos)
        {
            Heightmap hm = Heightmap.FindHeightmap(pos);
            if (hm == null) return false;
            Heightmap.Biome biome = hm.GetBiome(pos);
            if ((biome & plant.m_biome) == 0) return false;
            if (plant.m_needCultivatedGround && !hm.IsCultivated(pos)) return false;
            bool shielded = ShieldGenerator.IsInsideShield(pos);
            if (!plant.m_tolerateHeat && biome == Heightmap.Biome.AshLands && !shielded) return false;
            if (!plant.m_tolerateCold && (biome == Heightmap.Biome.DeepNorth || biome == Heightmap.Biome.Mountain) && !shielded) return false;
            return true;
        }

        public static bool Cultivated(Vector3 pos)
        {
            Heightmap hm = Heightmap.FindHeightmap(pos);
            return hm != null && hm.IsCultivated(pos);
        }

        /// <summary>
        /// Whether a crop planted at <paramref name="pos"/> (on the ground) has room to grow and leaves the crops around it
        /// theirs. <paramref name="ignore"/> is a crop just picked there, which may take a moment to disappear.
        /// </summary>
        public static bool Free(Vector3 pos, Plant plant, Component ignore = null)
        {
            if (Physics.Raycast(pos, Vector3.up, 100f, RoofMask)) return false;

            // What the new plant itself checks: nothing solid within its grow radius.
            int n = Physics.OverlapSphereNonAlloc(pos, plant.m_growRadius, Hits, SpaceMask);
            for (int i = 0; i < n; i++)
                if (!Ignored(Hits[i], ignore)) return false;

            // What the plants around it check, now and once they or the new one have grown.
            n = Physics.OverlapSphereNonAlloc(pos, NeighbourSearch, Hits, SpaceMask);
            for (int i = 0; i < n; i++)
            {
                if (Ignored(Hits[i], ignore)) continue;
                Plant other = AsPlant(Hits[i], out Vector3 at);
                // Spacing has a margin to spare, so neighbours on a farmer's grid (exactly Spacing apart, give or take
                // rounding) still fit.
                if (other != null && Utils.DistanceXZ(at, pos) < Spacing(plant, other) - Tolerance) return false;
            }
            return true;
        }

        /// <summary>Whether a crop already stands (or grows) right here, other than <paramref name="ignore"/>.</summary>
        public static bool Occupied(Vector3 pos, Component ignore = null)
        {
            int n = Physics.OverlapSphereNonAlloc(pos, 1f, Hits, SpaceMask);
            for (int i = 0; i < n; i++)
            {
                if (Ignored(Hits[i], ignore)) continue;
                if (AsPlant(Hits[i], out Vector3 at) != null && Utils.DistanceXZ(at, pos) < 0.3f) return true;
            }
            return false;
        }

        private static bool Ignored(Collider c, Component ignore) => ignore != null && c.transform.IsChildOf(ignore.transform);

        /// <summary>The plant a collider belongs to: a sapling, or a grown crop (as the sapling it grows from).</summary>
        private static Plant AsPlant(Collider c, out Vector3 at)
        {
            at = Vector3.zero;
            Plant plant = c.GetComponentInParent<Plant>();
            if (plant != null)
            {
                at = plant.transform.position;
                return plant;
            }
            Pickable crop = c.GetComponentInParent<Pickable>();
            if (crop == null || !WorkerPrefab.CropToPlant.TryGetValue(Utils.GetPrefabName(crop.gameObject), out Piece sapling)) return null;
            at = crop.transform.position;
            return sapling.GetComponent<Plant>();
        }
    }
}
