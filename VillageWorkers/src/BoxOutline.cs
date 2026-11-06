using System.Collections.Generic;
using UnityEngine;

namespace VillageWorkers
{
    /// <summary>The outline of a box area on the ground, drawn with the segments of the ward's ring.</summary>
    public class BoxOutline : MonoBehaviour
    {
        // About one segment per meter, like the ward's ring.
        private const float SegmentLength = 1f;

        public Vector3 Center;
        public float Width = 2f, Length = 2f, Yaw;

        private GameObject _segment;
        private LayerMask _mask;
        private readonly List<GameObject> _segments = new List<GameObject>();

        public static BoxOutline Create()
        {
            GameObject ward = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("guard_stone") : null;
            CircleProjector ring = ward != null ? ward.GetComponent<PrivateArea>()?.m_areaMarker : null;
            if (ring == null || ring.m_prefab == null) return null;
            BoxOutline outline = new GameObject("VillageWorkers_BoxOutline").AddComponent<BoxOutline>();
            outline._segment = ring.m_prefab;
            outline._mask = ring.m_mask;
            return outline;
        }

        public int Segments => _segments.Count;

        private void LateUpdate()
        {
            Quaternion rot = Quaternion.Euler(0f, Yaw, 0f);
            float w = Width * 0.5f, l = Length * 0.5f;
            Vector3[] corners =
            {
                Center + rot * new Vector3(-w, 0f, -l), Center + rot * new Vector3(w, 0f, -l),
                Center + rot * new Vector3(w, 0f, l), Center + rot * new Vector3(-w, 0f, l),
            };
            int[] perSide = new int[4];
            int total = 0;
            for (int s = 0; s < 4; s++)
            {
                perSide[s] = Mathf.Clamp(Mathf.RoundToInt(Vector3.Distance(corners[s], corners[(s + 1) % 4]) / SegmentLength), 1, 200);
                total += perSide[s];
            }
            while (_segments.Count < total) _segments.Add(Instantiate(_segment, transform));
            while (_segments.Count > total)
            {
                Destroy(_segments[_segments.Count - 1]);
                _segments.RemoveAt(_segments.Count - 1);
            }

            int i = 0;
            for (int s = 0; s < 4; s++)
            {
                Vector3 from = corners[s], to = corners[(s + 1) % 4];
                Quaternion along = Quaternion.LookRotation(to - from, Vector3.up);
                for (int k = 0; k < perSide[s]; k++)
                {
                    Vector3 p = Vector3.Lerp(from, to, (k + 0.5f) / perSide[s]);
                    if (Physics.Raycast(p + Vector3.up * 500f, Vector3.down, out RaycastHit hit, 1000f, _mask.value)) p.y = hit.point.y;
                    _segments[i].transform.SetPositionAndRotation(p, along);
                    i++;
                }
            }
        }
    }
}
