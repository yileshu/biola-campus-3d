using System;
using System.Collections.Generic;
using BiolaCampus.Osm;
using BiolaCampus.World;
using UnityEngine;

namespace BiolaCampus.Core
{
    /// <summary>
    /// Automated camera tour used by CI (Tools/capture/capture.mjs). Enabled with the URL query
    /// <c>?shot=all</c> (or <c>?shot=quad</c> for a single spot). For each fixed spot it places the
    /// camera, waits until the streamed tiles have settled, then logs
    /// <c>BIOLA_SHOT_READY &lt;name&gt;</c> and holds still so Playwright can take a screenshot.
    /// Optional query parameters: <c>quality=low|medium|high</c>, <c>night=1</c>.
    /// </summary>
    public class ScreenshotTour : MonoBehaviour
    {
        public struct Spot
        {
            public string Name;
            public Vector3 Position;   // y is height above ground when GroundRelative
            public Vector3 LookAt;
            public bool GroundRelative;
        }

        const float MinWait = 8f, MaxWait = 150f, SettleTime = 3f, HoldTime = 5f;

        public static string RequestedShot => Query("shot");

        public static string Query(string key)
        {
            string url = Application.absoluteURL ?? "";
            int q = url.IndexOf('?');
            if (q >= 0)
                foreach (var part in url.Substring(q + 1).Split('&'))
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
                        return Uri.UnescapeDataString(kv[1]);
                }
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-biola-" + key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        Camera _camera;
        CampusWorld _world;
        readonly List<Spot> _spots = new List<Spot>();
        int _index = -1;
        float _t, _settled;
        bool _ready;
        float _groundY;
        bool _groundFound;

        public void Begin(Camera cam, CampusWorld world, CampusMap map, string which)
        {
            _camera = cam;
            _world = world;
            foreach (var s in BuildSpots(map))
                if (which == "all" || string.Equals(which, s.Name, StringComparison.OrdinalIgnoreCase)) _spots.Add(s);
            CampusWorld.Diag($"Screenshot tour: {_spots.Count} spot(s): " + string.Join(", ", _spots.ConvertAll(s => s.Name)));
            Next();
        }

        public static List<Spot> BuildSpots(CampusMap map)
        {
            var spots = new List<Spot>();
            Vector2 center = map.HasBoundary ? map.Bounds.center : Vector2.zero;

            CampusBuilding Find(params string[] names)
            {
                foreach (var n in names)
                    foreach (var b in map.Buildings)
                        if (b.HasName && b.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return b;
                return null;
            }

            // 1. Main entrance: just inside the Biola Ave. gatehouse, looking into campus.
            var gate = Find("Biola Ave. Entrance", "Entrance Gatehouse");
            Vector2 gatePos = gate != null ? gate.Centroid : center + new Vector2(0, -0.45f * map.Bounds.height);
            Vector2 inward = (center - gatePos).normalized;
            Vector2 entrance = gatePos + inward * 18f + new Vector2(-inward.y, inward.x) * 8f;
            spots.Add(new Spot { Name = "entrance", Position = new Vector3(entrance.x, 1.7f, entrance.y), LookAt = new Vector3(center.x, 8f, center.y), GroundRelative = true });

            // 2. Central quad: between the library, the student union and the administration building.
            var anchors = new List<CampusBuilding>();
            foreach (var n in new[] { "Library", "Student Union", "Metzger", "Student Services" })
            {
                var b = Find(n);
                if (b != null) anchors.Add(b);
            }
            Vector2 quad = center;
            if (anchors.Count > 0)
            {
                quad = Vector2.zero;
                foreach (var b in anchors) quad += b.Centroid;
                quad /= anchors.Count;
            }
            quad = NearestOpenPoint(map, quad);
            var lib = Find("Library");
            Vector2 quadLook = lib != null ? lib.Centroid : center;
            spots.Add(new Spot { Name = "quad", Position = new Vector3(quad.x, 1.7f, quad.y), LookAt = new Vector3(quadLook.x, 6f, quadLook.y), GroundRelative = true });

            // 3. Between two hero buildings: the closest pair of named buildings near the quad.
            CampusBuilding a = null, c = null; float best = float.MaxValue;
            var named = map.Buildings.FindAll(x => x.HasName && Vector2.Distance(x.Centroid, quad) < 180f && x.HeightMeters > 6f);
            for (int i = 0; i < named.Count; i++)
                for (int j = i + 1; j < named.Count; j++)
                {
                    float d = Vector2.Distance(named[i].Centroid, named[j].Centroid);
                    if (d > 35f && d < best) { best = d; a = named[i]; c = named[j]; }
                }
            if (a != null)
            {
                Vector2 mid = NearestOpenPoint(map, (a.Centroid + c.Centroid) * 0.5f);
                Vector2 axis = (c.Centroid - a.Centroid).normalized;
                Vector2 along = new Vector2(-axis.y, axis.x);
                spots.Add(new Spot { Name = "between", Position = new Vector3(mid.x - along.x * 20f, 1.7f, mid.y - along.y * 20f), LookAt = new Vector3(mid.x + along.x * 40f, 5f, mid.y + along.y * 40f), GroundRelative = true });
            }
            else spots.Add(new Spot { Name = "between", Position = new Vector3(center.x + 30f, 1.7f, center.y), LookAt = new Vector3(center.x - 30f, 5f, center.y), GroundRelative = true });

            // 4. Drone: high above the campus, looking down at ~65°.
            spots.Add(new Spot { Name = "drone", Position = new Vector3(center.x, 330f, center.y - 150f), LookAt = new Vector3(center.x, 0f, center.y + 10f), GroundRelative = false });
            return spots;
        }

        static Vector2 NearestOpenPoint(CampusMap map, Vector2 p)
        {
            if (!map.IsInsideAnyBuilding(p)) return p;
            for (int ring = 1; ring < 30; ring++)
                for (int k = 0; k < 16; k++)
                {
                    float ang = k * Mathf.PI / 8f;
                    var q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ring * 4f;
                    if (!map.IsInsideAnyBuilding(q)) return q;
                }
            return p;
        }

        void Next()
        {
            _index++;
            if (_index >= _spots.Count)
            {
                Debug.Log("BIOLA_SHOT_DONE");
                enabled = false;
                return;
            }
            _t = 0f; _settled = 0f; _ready = false; _groundFound = false; _groundY = 0f;
            Place();
            CampusWorld.Diag($"Tour spot '{_spots[_index].Name}'");
        }

        void Place()
        {
            var s = _spots[_index];
            Vector3 pos = s.Position, look = s.LookAt;
            if (s.GroundRelative)
            {
                pos.y += _groundY;
                look.y += _groundY;
            }
            _camera.transform.position = pos;
            _camera.transform.LookAt(look);
        }

        void Update()
        {
            if (_index < 0 || _index >= _spots.Count) return;
            var s = _spots[_index];
            _t += Time.unscaledDeltaTime;

            if (s.GroundRelative && !_groundFound)
            {
                if (Physics.Raycast(new Vector3(s.Position.x, 1500f, s.Position.z), Vector3.down, out var hit, 3000f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    _groundY = hit.point.y;
                    _groundFound = true;
                    _settled = 0f;
                }
                Place();
            }

            if (_ready)
            {
                if (_t > HoldTime) Next();
                return;
            }

            float progress = _world.LoadProgress;
            _settled = progress >= 99.9f ? _settled + Time.unscaledDeltaTime : 0f;
            bool groundOk = !s.GroundRelative || _groundFound || _t > 30f;
            if ((_t > MinWait && groundOk && _settled > SettleTime) || _t > MaxWait)
            {
                _ready = true;
                Debug.Log($"BIOLA_SHOT_READY {s.Name} progress={progress:0.0} tiles={_world.TilesCreated} ground={(_groundFound ? _groundY.ToString("0.0") : "none")} wait={_t:0}s fps={(UI.DiagnosticsOverlay.Instance != null ? UI.DiagnosticsOverlay.Instance.Fps : 0):0}");
                _t = 0f;
            }
        }
    }
}
