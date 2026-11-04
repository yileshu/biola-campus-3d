using System;
using System.Collections.Generic;
using BiolaCampus.Geo;
using UnityEngine;

namespace BiolaCampus.Osm
{
    // ---------------------------------------------------------------------------------------------
    // Serialized form of Assets/Resources/OSM/biola_campus.json (produced by Tools/osm/fetch_osm.py
    // from the Overpass API). Coordinates are flat [lat, lon, lat, lon, ...] arrays.
    // Data © OpenStreetMap contributors, available under the Open Database License (ODbL 1.0).
    // ---------------------------------------------------------------------------------------------

    [Serializable]
    public class OsmMeta
    {
        public string source;
        public string attribution;
        public string license;
        public string fetchedUtc;
        public bool placeholder;
        public string placeholderReason;
        public string campusName;
        public string campusOsmId;
        public double centerLat;
        public double centerLon;
        public double centerHeight;
    }

    [Serializable] public class OsmRing { public double[] pts; }

    [Serializable]
    public class OsmBuilding
    {
        public string id;
        public string name;
        public string kind;
        public float levels;
        public float height;
        public double[] pts;
    }

    [Serializable] public class OsmPath { public string id; public string kind; public string name; public double[] pts; }
    [Serializable] public class OsmArea { public string id; public string kind; public string name; public double[] pts; }
    [Serializable] public class OsmPoi { public string id; public string name; public string kind; public double lat; public double lon; }

    [Serializable]
    public class OsmCampusFile
    {
        public OsmMeta meta;
        public OsmRing[] boundary;
        public OsmBuilding[] buildings;
        public OsmPath[] paths;
        public OsmArea[] areas;
        public OsmPoi[] pois;
    }

    // ------------------------------- Projected (Unity-space) data ---------------------------------

    public class CampusBuilding
    {
        public string Id;
        public string Name;
        public string Kind;
        public Vector2[] Outline;     // east, north (m)
        public Rect Bounds;
        public Vector2 Centroid;
        public float HeightMeters;    // estimated
        public bool HasName => !string.IsNullOrEmpty(Name);
        public float CachedGroundY = float.NaN;
    }

    public class CampusLine
    {
        public string Kind;
        public string Name;
        public Vector2[] Points;
    }

    public class CampusArea
    {
        public string Kind;
        public string Name;
        public Vector2[] Outline;
    }

    public class CampusPoi
    {
        public string Name;
        public string Kind;
        public Vector2 Position;
    }

    public class CampusMap
    {
        public const string ResourcePath = "OSM/biola_campus";
        public const string Attribution = "© OpenStreetMap contributors";

        // Used only when the OSM cache is missing entirely. NOT from OSM — see README ("OSM data").
        public const double FallbackLat = 33.9058;
        public const double FallbackLon = -118.0148;
        public const double FallbackHeight = 20.0;

        public OsmMeta Meta;
        public EnuFrame Frame;
        public readonly List<Vector2[]> BoundaryRings = new List<Vector2[]>();
        public readonly List<CampusBuilding> Buildings = new List<CampusBuilding>();
        public readonly List<CampusLine> Paths = new List<CampusLine>();
        public readonly List<CampusArea> Areas = new List<CampusArea>();
        public readonly List<CampusPoi> Pois = new List<CampusPoi>();
        public Rect Bounds;

        public bool IsPlaceholder => Meta == null || Meta.placeholder;
        public bool HasBoundary => BoundaryRings.Count > 0;

        public static CampusMap Load()
        {
            OsmCampusFile file = null;
            var ta = Resources.Load<TextAsset>(ResourcePath);
            if (ta != null)
            {
                try { file = JsonUtility.FromJson<OsmCampusFile>(ta.text); }
                catch (Exception e) { Debug.LogError("[Biola] Could not parse OSM cache: " + e.Message); }
            }
            if (file == null || file.meta == null)
            {
                Debug.LogWarning("[Biola] OSM cache missing; using built-in fallback origin. Run Tools/osm/fetch_osm.py.");
                file = new OsmCampusFile
                {
                    meta = new OsmMeta
                    {
                        placeholder = true,
                        placeholderReason = "OSM cache missing",
                        campusName = "Biola University",
                        centerLat = FallbackLat, centerLon = FallbackLon, centerHeight = FallbackHeight,
                        attribution = Attribution,
                    }
                };
            }
            return FromFile(file);
        }

        public static CampusMap FromFile(OsmCampusFile file)
        {
            var map = new CampusMap { Meta = file.meta };
            double h = file.meta.centerHeight;
            map.Frame = new EnuFrame(file.meta.centerLat, file.meta.centerLon, h);

            if (file.boundary != null)
                foreach (var r in file.boundary)
                {
                    var ring = map.Project(r.pts);
                    if (ring.Length >= 3) map.BoundaryRings.Add(ring);
                }

            if (file.buildings != null)
                foreach (var b in file.buildings)
                {
                    var outline = map.Project(b.pts);
                    if (outline.Length < 3) continue;
                    float height = b.height > 0 ? b.height : (b.levels > 0 ? b.levels * 3.6f + 1.5f : 9f);
                    map.Buildings.Add(new CampusBuilding
                    {
                        Id = b.id, Name = Clean(b.name), Kind = b.kind, Outline = outline,
                        Bounds = Polygon2D.Bounds(outline), Centroid = Polygon2D.Centroid(outline),
                        HeightMeters = height,
                    });
                }

            if (file.paths != null)
                foreach (var p in file.paths)
                {
                    var pts = map.Project(p.pts);
                    if (pts.Length >= 2) map.Paths.Add(new CampusLine { Kind = p.kind, Name = Clean(p.name), Points = pts });
                }

            if (file.areas != null)
                foreach (var a in file.areas)
                {
                    var pts = map.Project(a.pts);
                    if (pts.Length >= 3) map.Areas.Add(new CampusArea { Kind = a.kind, Name = Clean(a.name), Outline = pts });
                }

            if (file.pois != null)
                foreach (var p in file.pois)
                    if (!string.IsNullOrEmpty(p.name))
                        map.Pois.Add(new CampusPoi { Name = Clean(p.name), Kind = p.kind, Position = map.Frame.ToLocalXZ(p.lat, p.lon) });

            // Overall bounds: boundary if present, else everything, else a 900 m square.
            var all = new List<Vector2>();
            foreach (var r in map.BoundaryRings) all.AddRange(r);
            if (all.Count == 0) foreach (var b in map.Buildings) all.AddRange(b.Outline);
            map.Bounds = all.Count > 0 ? Polygon2D.Bounds(all) : new Rect(-450, -450, 900, 900);
            return map;
        }

        static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        Vector2[] Project(double[] pts)
        {
            if (pts == null) return Array.Empty<Vector2>();
            int n = pts.Length / 2;
            var list = new List<Vector2>(n);
            for (int i = 0; i < n; i++) list.Add(Frame.ToLocalXZ(pts[2 * i], pts[2 * i + 1]));
            // Drop the closing duplicate vertex of closed OSM ways.
            if (list.Count > 2 && (list[0] - list[list.Count - 1]).sqrMagnitude < 1e-4f) list.RemoveAt(list.Count - 1);
            return list.ToArray();
        }

        public static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>True when the point is inside the campus boundary (always true when no boundary is known).</summary>
        public bool IsInsideCampus(Vector2 p)
        {
            if (BoundaryRings.Count == 0) return true;
            int hits = 0;
            foreach (var ring in BoundaryRings) if (Polygon2D.Contains(ring, p)) hits++;
            return (hits & 1) == 1; // even-odd handles inner rings
        }

        public float DistanceToBoundary(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var ring in BoundaryRings) best = Mathf.Min(best, Polygon2D.DistanceToOutline(ring, p));
            return best;
        }

        /// <summary>Building whose footprint contains p (or lies within tolerance metres of it).</summary>
        public CampusBuilding BuildingAt(Vector2 p, float tolerance = 1.5f, bool namedOnly = true)
        {
            CampusBuilding best = null;
            float bestD = tolerance;
            foreach (var b in Buildings)
            {
                if (namedOnly && !b.HasName) continue;
                Rect r = b.Bounds;
                if (p.x < r.xMin - tolerance || p.x > r.xMax + tolerance || p.y < r.yMin - tolerance || p.y > r.yMax + tolerance) continue;
                float d = Polygon2D.Distance(b.Outline, p);
                if (d <= bestD) { bestD = d; best = b; if (d == 0f) break; }
            }
            return best;
        }

        public CampusBuilding NearestNamedBuilding(Vector2 p, float maxDistance, out float distance)
        {
            CampusBuilding best = null;
            distance = maxDistance;
            foreach (var b in Buildings)
            {
                if (!b.HasName) continue;
                Rect r = b.Bounds;
                if (p.x < r.xMin - maxDistance || p.x > r.xMax + maxDistance || p.y < r.yMin - maxDistance || p.y > r.yMax + maxDistance) continue;
                float d = Polygon2D.Distance(b.Outline, p);
                if (d < distance) { distance = d; best = b; }
            }
            return best;
        }

        public bool IsInsideAnyBuilding(Vector2 p)
        {
            foreach (var b in Buildings)
            {
                Rect r = b.Bounds;
                if (p.x < r.xMin || p.x > r.xMax || p.y < r.yMin || p.y > r.yMax) continue;
                if (Polygon2D.Contains(b.Outline, p)) return true;
            }
            return false;
        }

        /// <summary>A pleasant spawn point: a walkway vertex near the centre that is on campus and not inside a building.</summary>
        public Vector2 FindSpawnPoint()
        {
            Vector2 target = Vector2.zero;
            if (HasBoundary && !IsInsideCampus(target)) target = Bounds.center;

            Vector2 best = target;
            float bestD = float.MaxValue;
            foreach (var path in Paths)
            {
                bool walkway = path.Kind == "footway" || path.Kind == "pedestrian" || path.Kind == "path" || path.Kind == "living_street";
                if (!walkway) continue;
                foreach (var v in path.Points)
                {
                    float d = (v - target).sqrMagnitude;
                    if (d >= bestD) continue;
                    if (!IsInsideCampus(v) || IsInsideAnyBuilding(v)) continue;
                    bestD = d; best = v;
                }
            }
            if (bestD < float.MaxValue) return best;

            // No walkways: spiral out from the centre until we are outside every building.
            for (int ring = 0; ring < 40; ring++)
                for (int k = 0; k < 16; k++)
                {
                    float ang = k * Mathf.PI / 8f, rad = ring * 6f;
                    var c = target + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                    if (IsInsideCampus(c) && !IsInsideAnyBuilding(c)) return c;
                }
            return target;
        }
    }
}
