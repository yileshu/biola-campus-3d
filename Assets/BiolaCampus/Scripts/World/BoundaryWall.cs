using BiolaCampus.Osm;
using UnityEngine;

namespace BiolaCampus.World
{
    /// <summary>
    /// Invisible, very tall box colliders along every edge of the OSM campus boundary so the
    /// walker (and the drone) stay on campus. Lives on the Ignore Raycast layer so look-at
    /// raycasts and ground probes pass through it, while the CharacterController still collides.
    /// </summary>
    public class BoundaryWall : MonoBehaviour
    {
        public const float WallHeight = 2400f;
        public const float WallThickness = 2f;
        public int SegmentCount { get; private set; }

        public void Build(CampusMap map)
        {
            int layer = 2; // "Ignore Raycast"
            foreach (var ring in map.BoundaryRings)
            {
                for (int i = 0; i < ring.Length; i++)
                {
                    Vector2 a = ring[i], b = ring[(i + 1) % ring.Length];
                    Vector2 d = b - a;
                    float len = d.magnitude;
                    if (len < 0.05f) continue;
                    var seg = new GameObject("Wall " + SegmentCount);
                    seg.layer = layer;
                    seg.transform.SetParent(transform, false);
                    Vector2 mid = (a + b) * 0.5f;
                    seg.transform.localPosition = new Vector3(mid.x, 0f, mid.y);
                    float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; // rotate +Z onto the edge direction
                    seg.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    var box = seg.AddComponent<BoxCollider>();
                    // Overlap the ends slightly so corners have no gaps.
                    box.size = new Vector3(WallThickness, WallHeight, len + WallThickness);
                    SegmentCount++;
                }
            }
        }
    }
}
