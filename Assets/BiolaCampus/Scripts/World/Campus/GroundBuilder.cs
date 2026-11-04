using System.Collections.Generic;
using BiolaCampus.Geo;
using BiolaCampus.Osm;
using UnityEngine;
using UnityEngine.Rendering;
using Surface = BiolaCampus.World.Campus.ProceduralTextures.Surface;

namespace BiolaCampus.World.Campus
{
    /// <summary>
    /// Campus ground: lidar terrain mesh (clipped to the OSM campus boundary, with colliders) plus
    /// OSM areas and paths draped on top. Google tiles are clipped away over the same area
    /// (see <see cref="GoogleClipping"/>), so this is what you walk on.
    /// </summary>
    public static class GroundBuilder
    {
        // Height offsets above the terrain (kept generous to avoid z-fighting at drone distance).
        const float AreaLift = 0.06f, RoadLift = 0.12f, WalkLift = 0.18f;
        const int ChunkCells = 32;

        public static float GrassTile = 6f;

        public static void Build(Transform parent, CampusMap map, TerrainField terrain)
        {
            var root = new GameObject("Ground (lidar + OSM)").transform;
            root.SetParent(parent, false);
            BuildTerrain(root, map, terrain);
            BuildAreas(root, map, terrain);
            BuildPaths(root, map, terrain);
        }

        // ------------------------------------------------------------------ terrain

        static void BuildTerrain(Transform root, CampusMap map, TerrainField t)
        {
            int nx = t.Nx, nz = t.Nz;
            float cell = t.Cell;
            // A quad between cell centres (i,j)..(i+1,j+1) is kept if any corner is on campus, so the
            // terrain slightly overlaps the boundary (the Google clip polygon is 3 m larger).
            var keep = new bool[(nx - 1) * (nz - 1)];
            var inside = new bool[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    inside[j * nx + i] = map.IsInsideCampus(new Vector2(t.X0 + (i + 0.5f) * cell, t.Z0 + (j + 0.5f) * cell));
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                    keep[j * (nx - 1) + i] = inside[j * nx + i] || inside[j * nx + i + 1] || inside[(j + 1) * nx + i] || inside[(j + 1) * nx + i + 1];

            var grass = CampusMaterials.Lit("Terrain Grass", ProceduralTextures.Ground(Surface.Grass), Color.white, Vector2.one, 0.12f);
            int chunks = 0;
            for (int cj = 0; cj < nz - 1; cj += ChunkCells)
                for (int ci = 0; ci < nx - 1; ci += ChunkCells)
                {
                    var verts = new List<Vector3>();
                    var uvs = new List<Vector2>();
                    var tris = new List<int>();
                    var index = new Dictionary<int, int>();
                    int Vert(int i, int j)
                    {
                        int key = j * nx + i;
                        if (index.TryGetValue(key, out int v)) return v;
                        float x = t.X0 + (i + 0.5f) * cell, z = t.Z0 + (j + 0.5f) * cell;
                        v = verts.Count;
                        verts.Add(new Vector3(x, t.Node(i, j), z));
                        uvs.Add(new Vector2(x / GrassTile, z / GrassTile));
                        index[key] = v;
                        return v;
                    }
                    for (int j = cj; j < Mathf.Min(cj + ChunkCells, nz - 1); j++)
                        for (int i = ci; i < Mathf.Min(ci + ChunkCells, nx - 1); i++)
                        {
                            if (!keep[j * (nx - 1) + i]) continue;
                            int a = Vert(i, j), b = Vert(i + 1, j), c = Vert(i, j + 1), d = Vert(i + 1, j + 1);
                            tris.Add(a); tris.Add(c); tris.Add(b);
                            tris.Add(b); tris.Add(c); tris.Add(d);
                            // Skirts on outer edges hide the cut when seen from outside / above.
                            if (j == 0 || !keep[(j - 1) * (nx - 1) + i]) Skirt(verts, uvs, tris, verts[a], verts[b]);
                            if (j == nz - 2 || !keep[(j + 1) * (nx - 1) + i]) Skirt(verts, uvs, tris, verts[d], verts[c]);
                            if (i == 0 || !keep[j * (nx - 1) + i - 1]) Skirt(verts, uvs, tris, verts[c], verts[a]);
                            if (i == nx - 2 || !keep[j * (nx - 1) + i + 1]) Skirt(verts, uvs, tris, verts[b], verts[d]);
                        }
                    if (tris.Count == 0) continue;
                    var mesh = new Mesh { name = $"Terrain {ci},{cj}", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                    mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
                    mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                    var go = new GameObject(mesh.name);
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = grass;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    chunks++;
                }
            CampusWorld.Diag($"Lidar terrain: {chunks} chunks, {nx}x{nz} @ {cell} m");
        }

        static void Skirt(List<Vector3> v, List<Vector2> uv, List<int> tris, Vector3 a, Vector3 b)
        {
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(a + Vector3.down * 4f); v.Add(b + Vector3.down * 4f);
            uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            tris.Add(s + 1); tris.Add(s + 3); tris.Add(s + 2);
        }

        // ------------------------------------------------------------------ areas

        static Surface? SurfaceFor(string kind)
        {
            switch (kind)
            {
                case "parking": return Surface.Asphalt;
                case "pitch": case "stadium": case "track": return Surface.Turf;
                case "grass": case "park": case "garden": case "recreation_ground": case "meadow": return Surface.LushGrass;
                case "forest": case "orchard": case "wood": case "scrub": return Surface.Mulch;
                case "construction": case "brownfield": return Surface.Granite;
                case "swimming_pool": case "fountain": case "water": return Surface.Water;
                case "bicycle_parking": case "plaza": return Surface.Concrete;
                default: return null; // e.g. landuse=residential: keep the base terrain
            }
        }

        static void BuildAreas(Transform root, CampusMap map, TerrainField t)
        {
            var bySurface = new Dictionary<Surface, MeshAccumulator>();
            foreach (var a in map.Areas)
            {
                var s = SurfaceFor(a.Kind);
                if (s == null) continue;
                if (!map.IsInsideCampus(Polygon2D.Centroid(a.Outline))) continue;
                if (a.Name != null && a.Name.ToLowerInvariant().Contains("sand")) s = Surface.Sand;
                if (!bySurface.TryGetValue(s.Value, out var acc)) bySurface[s.Value] = acc = new MeshAccumulator();
                float lift = s == Surface.Water ? AreaLift + 0.02f : AreaLift + (s == Surface.Asphalt ? 0.01f : 0f);
                Drape(acc, a.Outline, t, lift, s == Surface.Asphalt ? 8f : 6f);
            }
            foreach (var kv in bySurface)
            {
                var mat = CampusMaterials.Lit("Area " + kv.Key, ProceduralTextures.Ground(kv.Key), Color.white, Vector2.one,
                    kv.Key == Surface.Water ? 0.85f : 0.15f);
                kv.Value.Emit(root, "Areas " + kv.Key, mat, false);
            }
        }

        /// <summary>Triangulates a polygon, subdivides to ≤ 3 m edges and drapes it on the terrain.</summary>
        static void Drape(MeshAccumulator acc, Vector2[] outline, TerrainField t, float lift, float uvTile)
        {
            var tris = Polygon2D.Triangulate(outline);
            for (int k = 0; k + 2 < tris.Count; k += 3)
                Subdivide(acc, outline[tris[k]], outline[tris[k + 1]], outline[tris[k + 2]], t, lift, uvTile, 0); // CCW from above
        }

        static void Subdivide(MeshAccumulator acc, Vector2 a, Vector2 b, Vector2 c, TerrainField t, float lift, float uvTile, int depth)
        {
            float maxEdge = Mathf.Max((a - b).magnitude, (b - c).magnitude, (c - a).magnitude);
            if (maxEdge > 3f && depth < 7)
            {
                Vector2 ab = (a + b) * 0.5f, bc = (b + c) * 0.5f, ca = (c + a) * 0.5f;
                Subdivide(acc, a, ab, ca, t, lift, uvTile, depth + 1);
                Subdivide(acc, ab, b, bc, t, lift, uvTile, depth + 1);
                Subdivide(acc, ca, bc, c, t, lift, uvTile, depth + 1);
                Subdivide(acc, ab, bc, ca, t, lift, uvTile, depth + 1);
                return;
            }
            acc.Tri(P(a, t, lift), P(b, t, lift), P(c, t, lift), a / uvTile, b / uvTile, c / uvTile);
        }

        static Vector3 P(Vector2 p, TerrainField t, float lift) => new Vector3(p.x, t.Height(p) + lift, p.y);

        // ------------------------------------------------------------------ paths

        static void BuildPaths(Transform root, CampusMap map, TerrainField t)
        {
            var asphalt = new MeshAccumulator();
            var pavers = new MeshAccumulator();
            var granite = new MeshAccumulator();
            var concrete = new MeshAccumulator();
            foreach (var p in map.Paths)
            {
                float w; MeshAccumulator acc; float lift; float tile;
                switch (p.Kind)
                {
                    case "footway": w = 3f; acc = pavers; lift = WalkLift; tile = 2.4f; break;
                    case "pedestrian": w = 6f; acc = pavers; lift = WalkLift; tile = 2.4f; break;
                    case "path": case "track": w = 2.2f; acc = granite; lift = WalkLift - 0.02f; tile = 3f; break;
                    case "steps": w = 2.5f; acc = concrete; lift = WalkLift; tile = 2f; break;
                    case "service": w = 6f; acc = asphalt; lift = RoadLift; tile = 6f; break;
                    case "residential": case "unclassified": case "tertiary": case "living_street": w = 8f; acc = asphalt; lift = RoadLift; tile = 6f; break;
                    default: continue;
                }
                foreach (var run in InsideRuns(map, Resample(p.Points, 1.5f)))
                    Ribbon(acc, run, w, t, lift, tile);
            }
            asphalt.Emit(root, "Roads", CampusMaterials.Lit("Asphalt", ProceduralTextures.Ground(Surface.Asphalt), Color.white, Vector2.one), false);
            pavers.Emit(root, "Walkways", CampusMaterials.Lit("Pavers", ProceduralTextures.Ground(Surface.Pavers), Color.white, Vector2.one), false);
            granite.Emit(root, "Paths", CampusMaterials.Lit("Granite", ProceduralTextures.Ground(Surface.Granite), Color.white, Vector2.one), false);
            concrete.Emit(root, "Steps", CampusMaterials.Lit("Concrete", ProceduralTextures.Ground(Surface.Concrete), Color.white, Vector2.one), false);
        }

        public static List<Vector2> Resample(Vector2[] pts, float step)
        {
            var o = new List<Vector2>();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / step));
                for (int k = 0; k < n; k++) o.Add(Vector2.Lerp(a, b, k / (float)n));
            }
            if (pts.Length > 0) o.Add(pts[pts.Length - 1]);
            return o;
        }

        public static IEnumerable<List<Vector2>> InsideRuns(CampusMap map, List<Vector2> pts)
        {
            var run = new List<Vector2>();
            foreach (var p in pts)
            {
                if (map.IsInsideCampus(p)) run.Add(p);
                else if (run.Count > 0) { if (run.Count > 1) yield return run; run = new List<Vector2>(); }
            }
            if (run.Count > 1) yield return run;
        }

        static void Ribbon(MeshAccumulator acc, List<Vector2> pts, float width, TerrainField t, float lift, float tile)
        {
            float dist = 0f;
            Vector3 pl = default, pr = default; float pv = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 tan = i == 0 ? pts[1] - pts[0] : i == pts.Count - 1 ? pts[i] - pts[i - 1] : pts[i + 1] - pts[i - 1];
                tan = tan.sqrMagnitude > 1e-6f ? tan.normalized : Vector2.right;
                Vector2 nrm = new Vector2(-tan.y, tan.x) * (width * 0.5f);
                if (i > 0) dist += Vector2.Distance(pts[i], pts[i - 1]);
                var l = P(pts[i] + nrm, t, lift);
                var r = P(pts[i] - nrm, t, lift);
                float v = dist / tile;
                if (i > 0)
                    acc.Quad(pl, pr, r, l, new Vector2(0, pv), new Vector2(width / tile, pv), new Vector2(width / tile, v), new Vector2(0, v));
                pl = l; pr = r; pv = v;
            }
        }
    }

    /// <summary>Collects triangles for one material and emits a single (UInt32) mesh.</summary>
    public class MeshAccumulator
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int> _t = new List<int>();
        public bool Empty => _t.Count == 0;

        /// <summary>Adds a triangle whose vertices are given counter-clockwise when seen from above (+Y).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            int s = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc);
            // Unity front faces are clockwise from the viewer: reverse.
            _t.Add(s); _t.Add(s + 2); _t.Add(s + 1);
        }

        /// <summary>Adds a quad a-b-c-d; emitted double-sided-safe for ribbons seen from above.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int s = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
            // Orientation-independent: choose the winding whose normal points up.
            // (Unity's front-face normal is Cross(v1 - v0, v2 - v0).)
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.y >= 0) { _t.Add(s); _t.Add(s + 1); _t.Add(s + 2); _t.Add(s); _t.Add(s + 2); _t.Add(s + 3); }
            else { _t.Add(s); _t.Add(s + 2); _t.Add(s + 1); _t.Add(s); _t.Add(s + 3); _t.Add(s + 2); }
        }

        public GameObject Emit(Transform parent, string name, Material mat, bool collider)
        {
            if (Empty) return null;
            var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(_v); mesh.SetUVs(0, _uv); mesh.SetTriangles(_t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }
    }
}
