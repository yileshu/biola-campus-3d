using System.Collections.Generic;
using BiolaCampus.Osm;
using UnityEngine;
using UnityEngine.Rendering;

namespace BiolaCampus.World.Campus
{
    /// <summary>
    /// GPU-instanced trees (positions/heights/crowns detected in USGS lidar) and street furniture
    /// (lamps, benches, trash cans, bike racks placed procedurally along OSM walkways). Low-poly
    /// meshes generated in code. Trunks/posts get simple colliders; lamps light up at night.
    /// </summary>
    public class Vegetation : MonoBehaviour
    {
        class Batch
        {
            public Mesh Mesh;
            public Material Material;
            public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
            public ShadowCastingMode Shadows = ShadowCastingMode.On;
        }

        readonly List<Batch> _batches = new List<Batch>();
        readonly List<Vector3> _lampHeads = new List<Vector3>();
        readonly List<Light> _lampLights = new List<Light>();
        Camera _cam;
        float _lightTimer;
        bool _night;
        public int TreeCount { get; private set; }
        public int PropCount { get; private set; }

        const int MaxLampLights = 10;

        public void Build(CampusMap map, TerrainField t, LidarData lidar)
        {
            var colliders = new GameObject("Tree & prop colliders").transform;
            colliders.SetParent(transform, false);

            // ---------------- meshes & materials
            var bark = CampusMaterials.Lit("Bark", ProceduralTextures.Solid("bark", new Color(0.36f, 0.27f, 0.20f), 0.1f, 0, 0.25f), Color.white, Vector2.one, 0.18f, null, true);
            var palmBark = CampusMaterials.Lit("Palm Bark", ProceduralTextures.Solid("palmbark", new Color(0.52f, 0.44f, 0.34f), 0.1f, 0, 0.3f), Color.white, Vector2.one, 0.18f, null, true);
            var oakLeaf = CampusMaterials.Lit("Oak Leaves", ProceduralTextures.Solid("oakleaf", new Color(0.20f, 0.33f, 0.13f), 0.2f, 0, 0.3f), Color.white, Vector2.one, 0.18f, null, true);
            var oakLeaf2 = CampusMaterials.Lit("Oak Leaves 2", ProceduralTextures.Solid("oakleaf2", new Color(0.29f, 0.38f, 0.16f), 0.2f, 0, 0.3f), Color.white, Vector2.one, 0.18f, null, true);
            var palmLeaf = CampusMaterials.Lit("Palm Fronds", ProceduralTextures.Solid("palmleaf", new Color(0.33f, 0.45f, 0.18f), 0.25f, 0, 0.25f), Color.white, Vector2.one, 0.18f, null, true, true);
            var jacaranda = CampusMaterials.Lit("Jacaranda Blossom", ProceduralTextures.Solid("jacaranda", new Color(0.58f, 0.50f, 0.74f), 0.2f, 0, 0.25f), Color.white, Vector2.one, 0.18f, null, true);
            var metal = CampusMaterials.Lit("Dark Metal", ProceduralTextures.Solid("darkmetal", new Color(0.12f, 0.13f, 0.14f), 0.5f, 0.8f, 0.05f), Color.white, Vector2.one, 0.45f, null, true, metallic: 0.7f);
            var wood = CampusMaterials.Lit("Bench Wood", ProceduralTextures.Solid("benchwood", new Color(0.55f, 0.38f, 0.22f), 0.3f, 0, 0.2f), Color.white, Vector2.one, 0.18f, null, true);
            var lampGlass = CampusMaterials.Lit("Lamp Glass", ProceduralTextures.Solid("lampglass", new Color(0.95f, 0.92f, 0.85f), 0.8f, 0, 0.02f, Color.white), Color.white, Vector2.one, 0.7f, new Color(1f, 0.82f, 0.55f) * 8f, true);
            var steel = CampusMaterials.Lit("Steel", ProceduralTextures.Solid("steel", new Color(0.55f, 0.57f, 0.58f), 0.6f, 0.9f, 0.05f), Color.white, Vector2.one, 0.55f, null, true, metallic: 0.8f);

            var trunk = LowPoly.Cylinder(8, 0.5f, 0.32f, 1f);             // unit height, base radius 0.5
            var palmTrunk = LowPoly.Cylinder(7, 0.5f, 0.4f, 1f);
            var canopy = LowPoly.Blob(1, 17, 0.28f);                        // unit radius
            var canopy2 = LowPoly.Blob(2, 31, 0.32f);
            var fronds = LowPoly.PalmCrown(11);
            var pole = LowPoly.Cylinder(6, 0.5f, 0.4f, 1f);
            var lantern = LowPoly.Lantern();
            var benchSeat = LowPoly.Box(new Vector3(1.8f, 0.08f, 0.45f), new Vector3(0, 0.45f, 0));
            var benchBack = LowPoly.Box(new Vector3(1.8f, 0.4f, 0.06f), new Vector3(0, 0.72f, -0.2f));
            var benchLegs = LowPoly.Merge(LowPoly.Box(new Vector3(0.06f, 0.45f, 0.45f), new Vector3(-0.8f, 0.22f, 0)), LowPoly.Box(new Vector3(0.06f, 0.45f, 0.45f), new Vector3(0.8f, 0.22f, 0)));
            var trashCan = LowPoly.Cylinder(10, 0.28f, 0.28f, 0.95f);
            var bikeRack = LowPoly.BikeRack();

            Batch B(Mesh m, Material mat, ShadowCastingMode sh = ShadowCastingMode.On)
            {
                var b = new Batch { Mesh = m, Material = mat, Shadows = sh };
                _batches.Add(b);
                return b;
            }
            var bTrunk = B(trunk, bark); var bPalmTrunk = B(palmTrunk, palmBark);
            var bOak = B(canopy, oakLeaf); var bOak2 = B(canopy2, oakLeaf2); var bJac = B(canopy2, jacaranda);
            var bFronds = B(fronds, palmLeaf);
            var bPole = B(pole, metal); var bLantern = B(lantern, lampGlass, ShadowCastingMode.Off);
            var bSeat = B(benchSeat, wood); var bBack = B(benchBack, wood); var bLegs = B(benchLegs, metal);
            var bTrash = B(trashCan, metal); var bRack = B(bikeRack, steel);

            var occupied = new List<Vector3>(); // x, z, radius
            bool Free(Vector2 p, float r)
            {
                foreach (var o in occupied) if ((new Vector2(o.x, o.y) - p).sqrMagnitude < (o.z + r) * (o.z + r)) return false;
                return !map.IsInsideAnyBuilding(p) && map.IsInsideCampus(p);
            }

            // ---------------- trees from lidar
            if (lidar != null)
                foreach (var tr in lidar.Trees)
                {
                    var p = new Vector2(tr.x, tr.z);
                    if (!map.IsInsideCampus(p) || map.IsInsideAnyBuilding(p)) continue;
                    float y = t.Height(p) - 0.1f;
                    float h = Mathf.Clamp(tr.h, 3f, 30f), r = Mathf.Clamp(tr.r, 1.2f, 9f);
                    float yaw = Hash(tr.x, tr.z) * 360f;
                    var rot = Quaternion.Euler(0, yaw, 0);
                    if (tr.kind == "palm")
                    {
                        float tw = Mathf.Lerp(0.55f, 0.8f, Hash(tr.z, tr.x));
                        bPalmTrunk.Matrices.Add(Matrix4x4.TRS(new Vector3(p.x, y, p.y), rot, new Vector3(tw, h - 1.2f, tw)));
                        float crown = Mathf.Clamp(r * 1.1f, 2.6f, 4.2f);
                        bFronds.Matrices.Add(Matrix4x4.TRS(new Vector3(p.x, y + h - 1.4f, p.y), rot, new Vector3(crown, crown * 0.8f, crown)));
                        AddCollider(colliders, p, y, 0.3f, h);
                        occupied.Add(new Vector3(p.x, p.y, 1.2f));
                    }
                    else
                    {
                        // Canopy half-height between 0.55 r and 0.95 r (no tall "eggs" for narrow trees),
                        // sitting on top of the trunk with its crown at the lidar height.
                        float halfY = Mathf.Clamp((h - 2f) * 0.35f, r * 0.55f, r * 0.95f);
                        float cy = y + h - halfY;
                        float trunkH = Mathf.Max(1.5f, cy - y);
                        float tw = Mathf.Clamp(r * 0.11f + 0.15f, 0.25f, 0.9f);
                        bTrunk.Matrices.Add(Matrix4x4.TRS(new Vector3(p.x, y, p.y), rot, new Vector3(tw, trunkH, tw)));
                        var scale = new Vector3(r, halfY, r);
                        var batch = tr.kind == "jacaranda" ? bJac : (Hash(tr.x + 3, tr.z) < 0.5f ? bOak : bOak2);
                        batch.Matrices.Add(Matrix4x4.TRS(new Vector3(p.x, cy, p.y), rot, scale));
                        AddCollider(colliders, p, y, Mathf.Max(0.25f, tw * 0.5f), trunkH);
                        occupied.Add(new Vector3(p.x, p.y, Mathf.Max(1f, tw)));
                    }
                    TreeCount++;
                }

            // ---------------- street furniture along walkways
            int lampN = 0, benchN = 0, trashN = 0, rackN = 0;
            foreach (var path in map.Paths)
            {
                bool walk = path.Kind == "footway" || path.Kind == "pedestrian" || path.Kind == "path";
                bool road = path.Kind == "service" || path.Kind == "residential";
                if (!walk && !road) continue;
                float halfW = path.Kind == "pedestrian" || road ? (road ? 4f : 3f) : 1.5f;
                foreach (var run in GroundBuilder.InsideRuns(map, GroundBuilder.Resample(path.Points, 1f)))
                {
                    float dist = 0, nextLamp = 6f, nextBench = 14f;
                    int side = 1;
                    for (int i = 1; i < run.Count; i++)
                    {
                        dist += Vector2.Distance(run[i], run[i - 1]);
                        Vector2 dir = (run[i] - run[i - 1]).normalized, nrm = new Vector2(-dir.y, dir.x);
                        if (dist >= nextLamp)
                        {
                            nextLamp += road ? 32f : 26f;
                            Vector2 lp = run[i] + nrm * side * (halfW + 0.8f);
                            side = -side;
                            if (Free(lp, 0.6f))
                            {
                                float y = t.Height(lp);
                                bPole.Matrices.Add(Matrix4x4.TRS(new Vector3(lp.x, y, lp.y), Quaternion.identity, new Vector3(0.18f, 4.2f, 0.18f)));
                                bLantern.Matrices.Add(Matrix4x4.TRS(new Vector3(lp.x, y + 4.2f, lp.y), Quaternion.identity, Vector3.one));
                                _lampHeads.Add(new Vector3(lp.x, y + 4.0f, lp.y));
                                AddCollider(colliders, lp, y, 0.12f, 4.2f);
                                occupied.Add(new Vector3(lp.x, lp.y, 0.6f));
                                lampN++;
                            }
                        }
                        if (walk && dist >= nextBench)
                        {
                            nextBench += 38f;
                            Vector2 bp = run[i] - nrm * side * (halfW + 0.9f);
                            if (Free(bp, 1.2f))
                            {
                                float y = t.Height(bp);
                                // Face the walkway.
                                Vector2 face = (run[i] - bp).normalized;
                                var rot = Quaternion.LookRotation(new Vector3(face.x, 0, face.y));
                                var m = Matrix4x4.TRS(new Vector3(bp.x, y, bp.y), rot, Vector3.one);
                                bSeat.Matrices.Add(m); bBack.Matrices.Add(m); bLegs.Matrices.Add(m);
                                var box = new GameObject("Bench").AddComponent<BoxCollider>();
                                box.transform.SetParent(colliders, false);
                                box.transform.SetPositionAndRotation(new Vector3(bp.x, y + 0.45f, bp.y), rot);
                                box.size = new Vector3(1.8f, 0.9f, 0.5f);
                                occupied.Add(new Vector3(bp.x, bp.y, 1.1f));
                                benchN++;
                                if (benchN % 2 == 0)
                                {
                                    Vector2 tp = bp + new Vector2(face.y, -face.x) * 1.5f;
                                    if (Free(tp, 0.4f))
                                    {
                                        bTrash.Matrices.Add(Matrix4x4.TRS(new Vector3(tp.x, t.Height(tp), tp.y), Quaternion.identity, Vector3.one));
                                        AddCollider(colliders, tp, t.Height(tp), 0.3f, 0.95f);
                                        occupied.Add(new Vector3(tp.x, tp.y, 0.4f));
                                        trashN++;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Bike racks next to the larger named buildings, on the side facing the nearest walkway.
            foreach (var b in map.Buildings)
            {
                if (!b.HasName || b.Bounds.width * b.Bounds.height < 600) continue;
                Vector2 c = b.Centroid;
                Vector2 best = c; float bestD = float.MaxValue;
                foreach (var path in map.Paths)
                    if (path.Kind == "footway" || path.Kind == "pedestrian")
                        foreach (var v in path.Points) { float d = (v - c).sqrMagnitude; if (d < bestD) { bestD = d; best = v; } }
                if (bestD == float.MaxValue) continue;
                Vector2 dir = (best - c).normalized;
                // walk outward from the centroid until outside the footprint, then step 3 m further
                Vector2 p = c;
                for (int k = 0; k < 200 && map.IsInsideAnyBuilding(p); k++) p += dir * 1f;
                p += dir * 3f + new Vector2(-dir.y, dir.x) * 4f;
                for (int k = 0; k < 2; k++)
                {
                    Vector2 q = p + new Vector2(-dir.y, dir.x) * (k * 1.2f);
                    if (!Free(q, 0.6f)) continue;
                    var rot = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
                    bRack.Matrices.Add(Matrix4x4.TRS(new Vector3(q.x, t.Height(q), q.y), rot, Vector3.one));
                    occupied.Add(new Vector3(q.x, q.y, 0.6f));
                    rackN++;
                }
            }
            PropCount = lampN + benchN + trashN + rackN;
            CampusWorld.Diag($"Vegetation: {TreeCount} lidar trees; props: {lampN} lamps, {benchN} benches, {trashN} trash cans, {rackN} bike racks");

            // Night lighting pool (nearest lamps get real point lights).
            for (int i = 0; i < MaxLampLights; i++)
            {
                var l = new GameObject("Lamp Light").AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.type = LightType.Point;
                l.range = 16f;
                l.intensity = 3.2f;
                l.color = new Color(1f, 0.80f, 0.55f);
                l.shadows = LightShadows.None;
                l.enabled = false;
                _lampLights.Add(l);
            }
            SunController.NightChanged += OnNight;
            if (SunController.Instance != null) OnNight(SunController.Instance.IsNight);
        }

        void OnDestroy() => SunController.NightChanged -= OnNight;

        void OnNight(bool night)
        {
            _night = night;
            if (!night) foreach (var l in _lampLights) l.enabled = false;
            _lightTimer = 0f;
        }

        static void AddCollider(Transform parent, Vector2 p, float y, float radius, float height)
        {
            var go = new GameObject("c");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(p.x, y + height * 0.5f, p.y);
            var cap = go.AddComponent<CapsuleCollider>();
            cap.radius = radius;
            cap.height = Mathf.Max(height, radius * 2);
        }

        static float Hash(float a, float b)
        {
            unchecked
            {
                int h = (int)(a * 73.13f) * 73856093 ^ (int)(b * 37.71f) * 19349663;
                return ((h ^ (h >> 13)) & 0xFFFF) / 65535f;
            }
        }

        void Update()
        {
            if (_cam == null) _cam = Camera.main;
            foreach (var b in _batches)
            {
                if (b.Matrices.Count == 0) continue;
                var rp = new RenderParams(b.Material) { shadowCastingMode = b.Shadows, receiveShadows = true, worldBounds = new Bounds(Vector3.zero, Vector3.one * 5000f) };
                for (int i = 0; i < b.Matrices.Count; i += 1023)
                    Graphics.RenderMeshInstanced(rp, b.Mesh, 0, b.Matrices, Mathf.Min(1023, b.Matrices.Count - i), i);
            }

            if (_night && _cam != null)
            {
                _lightTimer -= Time.unscaledDeltaTime;
                if (_lightTimer <= 0f)
                {
                    _lightTimer = 0.4f;
                    Vector3 c = _cam.transform.position;
                    _lampHeads.Sort((x, y) => (x - c).sqrMagnitude.CompareTo((y - c).sqrMagnitude));
                    for (int i = 0; i < _lampLights.Count; i++)
                    {
                        bool on = i < _lampHeads.Count && (_lampHeads[i] - c).sqrMagnitude < 160f * 160f;
                        _lampLights[i].enabled = on;
                        if (on) _lampLights[i].transform.position = _lampHeads[i];
                    }
                }
            }
        }
    }

    /// <summary>Tiny procedural low-poly meshes.</summary>
    public static class LowPoly
    {
        public static Mesh Cylinder(int sides, float r0, float r1, float h)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * r0); v.Add(d * r1 + Vector3.up * h);
                uv.Add(new Vector2(i / (float)sides, 0)); uv.Add(new Vector2(i / (float)sides, 1));
            }
            for (int i = 0; i < sides; i++)
            {
                int s = i * 2;
                t.Add(s); t.Add(s + 1); t.Add(s + 3);
                t.Add(s); t.Add(s + 3); t.Add(s + 2);
            }
            // top cap
            int c = v.Count; v.Add(Vector3.up * h); uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++) { t.Add(c); t.Add(i * 2 + 3); t.Add(i * 2 + 1); }
            return Finish("Cylinder", v, uv, t);
        }

        /// <summary>Lumpy canopy: icosphere-ish blob (unit radius) with deterministic displacement.</summary>
        public static Mesh Blob(int subdiv, int seed, float lumpiness)
        {
            float g = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            var faces = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                        3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int s = 0; s < subdiv; s++)
            {
                var nf = new List<int>();
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int m)) return m;
                    verts.Add((verts[a] + verts[b]) * 0.5f);
                    mid[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = nf;
            }
            var rnd = new System.Random(seed);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var disp = new float[verts.Count];
            for (int i = 0; i < verts.Count; i++) disp[i] = 1f + ((float)rnd.NextDouble() - 0.5f) * 2f * lumpiness;
            // flat-shaded faces (unique vertices) for a crisp low-poly look
            for (int i = 0; i < faces.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int vi = faces[i + k];   // icosahedron faces already have Cross(v1-v0, v2-v0) pointing outward
                    var p = verts[vi].normalized * disp[vi];
                    p.y *= p.y < 0 ? 0.55f : 1f;  // flatter underside
                    v.Add(p); uv.Add(new Vector2(p.x * 0.5f + 0.5f, p.z * 0.5f + 0.5f));
                    t.Add(v.Count - 1);
                }
            }
            return Finish("Canopy", v, uv, t);
        }

        /// <summary>Palm crown: drooping fronds (unit radius), double-sided.</summary>
        public static Mesh PalmCrown(int fronds)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int f = 0; f < fronds; f++)
            {
                float a = f * Mathf.PI * 2 / fronds + (f % 2) * 0.2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                float rise = (f % 3) * 0.15f;
                const int seg = 5;
                for (int s = 0; s <= seg; s++)
                {
                    float u = s / (float)seg;
                    float width = Mathf.Sin(u * Mathf.PI) * 0.22f + 0.02f;
                    var center = dir * u + Vector3.up * (0.35f * Mathf.Sin(u * 2.2f) + rise - 0.9f * u * u);
                    v.Add(center - side * width); v.Add(center + side * width);
                    uv.Add(new Vector2(0, u)); uv.Add(new Vector2(1, u));
                }
                int b0 = v.Count - (seg + 1) * 2;
                for (int s = 0; s < seg; s++)
                {
                    int i0 = b0 + s * 2;
                    t.AddRange(new[] { i0, i0 + 2, i0 + 1, i0 + 1, i0 + 2, i0 + 3 });
                    t.AddRange(new[] { i0, i0 + 1, i0 + 2, i0 + 1, i0 + 3, i0 + 2 });
                }
            }
            return Finish("PalmCrown", v, uv, t);
        }

        public static Mesh Lantern()
        {
            var a = Box(new Vector3(0.42f, 0.5f, 0.42f), new Vector3(0, -0.15f, 0));
            var cap = Box(new Vector3(0.55f, 0.08f, 0.55f), new Vector3(0, 0.14f, 0));
            return Merge(a, cap);
        }

        public static Mesh BikeRack()
        {
            var parts = new List<Mesh>();
            for (int i = 0; i < 4; i++)
            {
                float x = -0.9f + i * 0.6f;
                parts.Add(Box(new Vector3(0.05f, 0.8f, 0.05f), new Vector3(x, 0.4f, -0.3f)));
                parts.Add(Box(new Vector3(0.05f, 0.8f, 0.05f), new Vector3(x, 0.4f, 0.3f)));
                parts.Add(Box(new Vector3(0.05f, 0.05f, 0.65f), new Vector3(x, 0.8f, 0f)));
            }
            return Merge(parts.ToArray());
        }

        public static Mesh Box(Vector3 size, Vector3 center)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + new Vector3(x * h.x, y * h.y, z * h.z);
            var faces = new[]
            {
                (P(-1,-1,-1), P(-1, 1,-1), P( 1, 1,-1), P( 1,-1,-1)),
                (P( 1,-1, 1), P( 1, 1, 1), P(-1, 1, 1), P(-1,-1, 1)),
                (P(-1,-1, 1), P(-1, 1, 1), P(-1, 1,-1), P(-1,-1,-1)),
                (P( 1,-1,-1), P( 1, 1,-1), P( 1, 1, 1), P( 1,-1, 1)),
                (P(-1, 1,-1), P(-1, 1, 1), P( 1, 1, 1), P( 1, 1,-1)),
                (P(-1,-1, 1), P(-1,-1,-1), P( 1,-1,-1), P( 1,-1, 1)),
            };
            foreach (var f in faces)
            {
                int s = v.Count;
                v.Add(f.Item1); v.Add(f.Item2); v.Add(f.Item3); v.Add(f.Item4);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            return Finish("Box", v, uv, t);
        }

        public static Mesh Merge(params Mesh[] meshes)
        {
            var ci = new CombineInstance[meshes.Length];
            for (int i = 0; i < meshes.Length; i++) ci[i] = new CombineInstance { mesh = meshes[i], transform = Matrix4x4.identity };
            var m = new Mesh { name = "Merged" };
            m.CombineMeshes(ci, true, true);
            m.RecalculateBounds();
            return m;
        }

        static Mesh Finish(string name, List<Vector3> v, List<Vector2> uv, List<int> t)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }
    }
}
