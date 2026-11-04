using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BiolaCampus.Geo;
using BiolaCampus.Osm;
using CesiumForUnity;
using UnityEngine;

namespace BiolaCampus.World.Campus
{
    /// <summary>
    /// Our own campus core: lidar terrain + OSM ground, procedural models for every OSM building
    /// (with extra detail on the ~20 "hero" buildings), lidar trees and street furniture.
    /// Google Photorealistic 3D Tiles are clipped away inside the campus boundary
    /// (<see cref="GoogleClipping"/>) and stay visible everywhere else.
    /// </summary>
    public class HeroCampus : MonoBehaviour
    {
        public const int HeroCount = 22;
        public TerrainField Terrain { get; private set; }
        public LidarData Lidar { get; private set; }
        public readonly List<CampusBuilding> Heroes = new List<CampusBuilding>();

        static readonly string[] NotHero = { "Parking", "Grove #", "Modulars", "Gatehouse", "Central Plant", "Central Stores", "Facilities", "Production Center", "Mail Services" };

        public static bool Available(out TerrainField terrain, out LidarData lidar)
        {
            terrain = TerrainField.Load();
            lidar = LidarData.Load();
            return terrain != null;
        }

        public void Build(CampusMap map, TerrainField terrain, LidarData lidar)
        {
            Terrain = terrain; Lidar = lidar;
            float t0 = Time.realtimeSinceStartup;
            CampusMaterials.HookNight();

            GroundBuilder.Build(transform, map, terrain);

            // Walkway points for entrance placement.
            var walk = new List<Vector2>();
            foreach (var p in map.Paths)
                if (p.Kind == "footway" || p.Kind == "pedestrian" || p.Kind == "path")
                    walk.AddRange(GroundBuilder.Resample(p.Points, 3f));

            PickHeroes(map);
            var root = new GameObject("Buildings (OSM + lidar)").transform;
            root.SetParent(transform, false);
            int built = 0;
            foreach (var b in map.Buildings)
            {
                bool hero = Heroes.Contains(b);
                LidarData.Building lb = null;
                if (lidar != null && b.Id != null) lidar.ById.TryGetValue(b.Id, out lb);
                if (lb != null && lb.Measured) b.HeightMeters = lb.roof == "pitched" ? lb.ridge : lb.height;
                Vector2? toward = null;
                if (hero && walk.Count > 0)
                {
                    float best = float.MaxValue;
                    foreach (var w in walk) { float d = (w - b.Centroid).sqrMagnitude; if (d < best) { best = d; toward = w; } }
                }
                var opts = new BuildingBuilder.Options { Hero = hero, Style = StyleFor(b), Residential = IsResidential(b), EntranceToward = toward };
                try
                {
                    if (BuildingBuilder.Build(root, b, lb, terrain, opts) != null) built++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Biola] Building '{b.Name ?? b.Id}' failed: {e.Message}");
                }
            }

            var veg = new GameObject("Trees & street furniture").AddComponent<Vegetation>();
            veg.transform.SetParent(transform, false);
            veg.Build(map, terrain, lidar);

            CampusWorld.Diag($"Hero campus built in {(Time.realtimeSinceStartup - t0):0.00}s: {built} buildings ({Heroes.Count} hero), " +
                             $"terrain {terrain.Info.source}");
        }

        void PickHeroes(CampusMap map)
        {
            Vector2 core = map.Bounds.center;
            foreach (var a in map.Areas)
                if (a.Name == "Metzger Lawn") { core = Polygon2D.Centroid(a.Outline); break; }
            var scored = new List<(CampusBuilding b, float s)>();
            foreach (var b in map.Buildings)
            {
                if (!b.HasName) continue;
                bool skip = false;
                foreach (var n in NotHero) if (b.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) skip = true;
                if (skip) continue;
                float area = Mathf.Abs(Polygon2D.SignedArea(b.Outline));
                float d = Vector2.Distance(b.Centroid, core);
                scored.Add((b, Mathf.Sqrt(area) / (1f + d / 160f)));
            }
            scored.Sort((x, y) => y.s.CompareTo(x.s));
            for (int i = 0; i < scored.Count && Heroes.Count < HeroCount; i++) Heroes.Add(scored[i].b);
            CampusWorld.Diag("Hero buildings: " + string.Join(", ", Heroes.ConvertAll(h => h.Name)));
        }

        static bool IsResidential(CampusBuilding b)
        {
            if (b.Name == null) return false;
            foreach (var n in new[] { "Apartments", "Hope", "Horton", "Alpha", "Sigma", "Stewart", "Hart", "Emerson", "Thompson", "Blackstone", "Hall" })
                if (b.Name.Contains(n) && !b.Name.Contains("Crowell") && !b.Name.Contains("Myers") && !b.Name.Contains("Bardwell") && !b.Name.Contains("Rood") && !b.Name.Contains("Feinberg"))
                    return true;
            return false;
        }

        /// <summary>
        /// Facade style per building. Approximations: exterior materials could not be confirmed from
        /// photos in this environment, so styles follow building type (see README "Hero campus").
        /// </summary>
        static int StyleFor(CampusBuilding b)
        {
            string n = b.Name ?? "";
            bool Has(params string[] keys) { foreach (var k in keys) if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }
            if (Has("Parking")) return 5;                                         // Garage
            if (Has("Chapel", "Prayer")) return 4;                                // Mission
            if (Has("Library", "Science", "Cinema", "School of Business", "Student Services", "Union")) return 3;  // Modern
            if (Has("Myers", "Bardwell", "Rood", "Crowell Hall", "Feinberg")) return 2;                 // Brick
            if (IsResidential(b)) return 1;                                       // Sand stucco
            int h = (b.Id ?? n).GetHashCode() & 0xFF;
            return h % 3 == 0 ? 1 : 0;                                            // mostly cream stucco
        }
    }

    /// <summary>Hides Google tiles inside the campus boundary using Cesium's polygon clipping.</summary>
    public static class GoogleClipping
    {
        public const float BufferMeters = 3f;

        public static CesiumPolygonRasterOverlay Apply(Cesium3DTileset tileset, CesiumGeoreference georeference, CampusMap map)
        {
            if (!map.HasBoundary) return null;
            Vector2[] largest = map.BoundaryRings[0];
            foreach (var r in map.BoundaryRings) if (Mathf.Abs(Polygon2D.SignedArea(r)) > Mathf.Abs(Polygon2D.SignedArea(largest))) largest = r;
            var ring = new List<Vector2>(largest);
            if (Polygon2D.SignedArea(ring) < 0) ring.Reverse();
            ring = BuildingBuilder.Offset(ring, BufferMeters);

            var sb = new StringBuilder("{\"type\":\"Polygon\",\"coordinates\":[[");
            for (int i = 0; i <= ring.Count; i++)
            {
                map.Frame.ToGeodetic(ring[i % ring.Count], out double lat, out double lon);
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(lon.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(lat.ToString("R", CultureInfo.InvariantCulture)).Append(']');
            }
            sb.Append("]]}");

            var go = new GameObject("Google clip polygon (campus boundary + 3 m)");
            go.transform.SetParent(georeference.transform, false);
            var polygon = go.AddComponent<CesiumCartographicPolygon>();
            var doc = CesiumGeoJsonDocument.Parse(sb.ToString());
            if (doc == null)
            {
                CampusWorld.Diag("Clipping: GeoJSON polygon could not be parsed — Google tiles stay visible on campus");
                return null;
            }
            polygon.document = doc;

            // The tileset is already active: each property setter below calls Refresh() (remove + re-add),
            // so the overlay ends up registered once with the final key and polygon.
            var overlay = tileset.gameObject.AddComponent<CesiumPolygonRasterOverlay>();
            overlay.materialKey = "Clipping";       // matches the clipping slot of Cesium's default tileset material
            overlay.showCreditsOnScreen = false;
            overlay.invertSelection = false;        // hide what is INSIDE the polygon
            overlay.excludeSelectedTiles = true;    // skip tiles entirely inside (saves downloads)
            overlay.polygons = new List<CesiumCartographicPolygon> { polygon };
            CampusWorld.Diag($"Clipping: Google tiles hidden inside the campus boundary ({ring.Count} vertices, +{BufferMeters} m)");
            return overlay;
        }
    }
}
