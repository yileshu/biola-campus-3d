using System.Collections.Generic;
using BiolaCampus.Geo;
using BiolaCampus.Osm;
using UnityEngine;

namespace BiolaCampus.World
{
    /// <summary>
    /// Fallback world used when Google 3D Tiles cannot be streamed (no key, key rejected, offline):
    /// a flat ground plane with OSM building footprints extruded to their tagged/estimated heights.
    /// Built entirely from the cached OSM data (© OpenStreetMap contributors).
    /// </summary>
    public class OfflineCampus : MonoBehaviour
    {
        public void Build(CampusMap map)
        {
            Rect b = map.Bounds;
            float size = Mathf.Max(b.width, b.height) + 600f;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(transform, false);
            ground.transform.localPosition = new Vector3(b.center.x, -0.5f, b.center.y);
            ground.transform.localScale = new Vector3(size, 1f, size);
            ground.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Lit("Ground", new Color(0.42f, 0.55f, 0.33f));

            var flatMat = MaterialLibrary.Lit("Paving", new Color(0.72f, 0.70f, 0.66f));
            foreach (var area in map.Areas)
            {
                Color c = area.Kind == "parking" ? new Color(0.35f, 0.35f, 0.37f)
                        : area.Kind == "pitch" || area.Kind == "grass" || area.Kind == "park" ? new Color(0.36f, 0.6f, 0.3f)
                        : new Color(0.6f, 0.6f, 0.55f);
                var mat = MaterialLibrary.Lit("Area_" + area.Kind, c);
                CreateFlat("Area " + area.Kind, area.Outline, 0.02f, mat);
            }
            foreach (var path in map.Paths)
                CreatePath(path, path.Kind == "footway" || path.Kind == "path" || path.Kind == "pedestrian" ? 2.5f : 6f, flatMat);

            var named = MaterialLibrary.Lit("BuildingNamed", new Color(0.86f, 0.82f, 0.74f));
            var plain = MaterialLibrary.Lit("Building", new Color(0.78f, 0.78f, 0.78f));
            foreach (var bld in map.Buildings)
                CreateBuilding(bld, bld.HasName ? named : plain);
        }

        void CreateFlat(string name, Vector2[] outline, float y, Material mat)
        {
            var tris = Polygon2D.Triangulate(outline);
            if (tris.Count == 0) return;
            var verts = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++) verts[i] = new Vector3(outline[i].x, y, outline[i].y);
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = FlipForTopFace(tris).ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AddMeshObject(name, mesh, mat, false);
        }

        void CreatePath(CampusLine line, float width, Material mat)
        {
            var pts = line.Points;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 a = pts[i], c = pts[i + 1];
                Vector2 dir = (c - a).normalized;
                Vector2 n = new Vector2(-dir.y, dir.x) * (width * 0.5f);
                int s = verts.Count;
                verts.Add(new Vector3(a.x - n.x, 0.04f, a.y - n.y));
                verts.Add(new Vector3(a.x + n.x, 0.04f, a.y + n.y));
                verts.Add(new Vector3(c.x + n.x, 0.04f, c.y + n.y));
                verts.Add(new Vector3(c.x - n.x, 0.04f, c.y - n.y));
                tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                tris.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 }); // double sided
            }
            if (verts.Count == 0) return;
            var mesh = new Mesh { name = "Path" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            AddMeshObject("Path " + line.Kind, mesh, mat, false);
        }

        void CreateBuilding(CampusBuilding b, Material mat)
        {
            var outline = b.Outline;
            int n = outline.Length;
            float h = Mathf.Max(3f, b.HeightMeters);
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Roof
            var roofTris = Polygon2D.Triangulate(outline);
            for (int i = 0; i < n; i++) verts.Add(new Vector3(outline[i].x, h, outline[i].y));
            tris.AddRange(FlipForTopFace(roofTris));

            // Walls (each quad gets its own vertices for flat shading; emit both windings so footprint
            // orientation does not matter).
            for (int i = 0; i < n; i++)
            {
                Vector2 a = outline[i], c = outline[(i + 1) % n];
                int s = verts.Count;
                verts.Add(new Vector3(a.x, 0, a.y));
                verts.Add(new Vector3(c.x, 0, c.y));
                verts.Add(new Vector3(c.x, h, c.y));
                verts.Add(new Vector3(a.x, h, a.y));
                bool ccw = Polygon2D.SignedArea(outline) > 0;
                if (ccw) tris.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                else tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }

            var mesh = new Mesh { name = b.Name ?? b.Id ?? "Building" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AddMeshObject(mesh.name, mesh, mat, true);
        }

        /// <summary>Polygon2D returns CCW triangles in (east, north); Unity front faces seen from +Y need the reverse.</summary>
        static List<int> FlipForTopFace(List<int> tris)
        {
            var r = new List<int>(tris.Count);
            for (int i = 0; i + 2 < tris.Count; i += 3) { r.Add(tris[i]); r.Add(tris[i + 2]); r.Add(tris[i + 1]); }
            return r;
        }

        void AddMeshObject(string name, Mesh mesh, Material mat, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
    }
}
