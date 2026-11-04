using System.Collections.Generic;
using BiolaCampus.Geo;
using BiolaCampus.Osm;
using UnityEngine;
using UnityEngine.Rendering;

namespace BiolaCampus.World.Campus
{
    /// <summary>
    /// Procedural building models from OSM footprints + USGS lidar heights/roof type.
    /// Facades use a 4-bay × 3-floor procedural tile mapped so windows land on whole bays and floors.
    /// </summary>
    public static class BuildingBuilder
    {
        public const float BayWidth = 3.2f;

        enum Sub { Facade, Plain, Plinth, Trim, Roof, Doors, Metal, Beacon, Count }

        public class Options
        {
            public bool Hero;
            public int Style;
            public bool Residential;
            public Vector2? EntranceToward;   // nearest walkway point
        }

        public static GameObject Build(Transform parent, CampusBuilding b, LidarData.Building lidar, TerrainField t, Options o)
        {
            var outline = Clean(b.Outline);
            if (outline.Count < 3) return null;
            if (Polygon2D.SignedArea(outline) < 0) outline.Reverse();   // CCW (east, north)

            float area = Mathf.Abs(Polygon2D.SignedArea(outline));
            Vector2 centroid = Polygon2D.Centroid(outline);
            float groundY = lidar != null && lidar.points > 0 ? lidar.ground : t.Height(centroid);
            float minY = groundY;
            foreach (var p in outline) minY = Mathf.Min(minY, t.Height(p));
            float baseY = minY - 0.4f;

            bool pitched = lidar != null && lidar.Measured && lidar.roof == "pitched";
            float totalH = lidar != null && lidar.Measured ? (pitched ? lidar.eave : lidar.height) : b.HeightMeters;
            totalH = Mathf.Clamp(totalH, 3f, 60f);
            float wallTop = groundY + totalH;
            float parapet = pitched ? 0f : (area > 120 ? 0.9f : 0.35f);

            float floorH = o.Residential ? 3.1f : 3.9f;
            int floors = Mathf.Max(1, Mathf.RoundToInt((totalH - parapet) / floorH));
            float effFloor = Mathf.Max(2.6f, (totalH - parapet) / floors);

            var mb = new MultiMesh((int)Sub.Count);

            // ---- walls
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i], c = outline[(i + 1) % outline.Count];
                float len = Vector2.Distance(a, c);
                if (len < 0.05f) continue;
                if (len < 2.2f || floors == 0)
                {
                    WallQuad(mb, (int)Sub.Plain, a, c, groundY, wallTop, len / 4f, (wallTop - groundY) / 4f, 0f);
                    continue;
                }
                int bays = Mathf.Max(1, Mathf.RoundToInt(len / BayWidth));
                float uMax = bays / 4f;
                float vAt(float y) => (y - groundY) / (3f * effFloor);
                WallQuadUV(mb, (int)Sub.Facade, a, c, groundY, wallTop, 0f, uMax, vAt(groundY), vAt(wallTop));
            }

            // ---- plinth (slightly proud of the wall, runs into the terrain)
            var plinthRing = Offset(outline, 0.07f);
            for (int i = 0; i < plinthRing.Count; i++)
            {
                Vector2 a = plinthRing[i], c = plinthRing[(i + 1) % plinthRing.Count];
                float len = Vector2.Distance(a, c);
                WallQuad(mb, (int)Sub.Plinth, a, c, baseY, groundY + 0.45f, len / 2f, 0.5f, 0f);
            }
            // plinth top lip
            Ring(mb, (int)Sub.Plinth, outline, plinthRing, groundY + 0.45f, groundY + 0.45f, true);

            // ---- roof
            bool roofDone = false;
            if (pitched)
            {
                float ridgeTop = Mathf.Clamp(groundY + lidar.ridge, wallTop + 1.2f, wallTop + 7f);
                roofDone = PitchedRoof(mb, outline, area, wallTop, ridgeTop);
                if (!roofDone) parapet = 0.6f;
            }
            if (!roofDone) FlatRoof(mb, outline, wallTop, parapet, o.Hero && area > 500, b.Id);

            // ---- hero details
            if (o.Hero && o.EntranceToward.HasValue) Entrance(mb, outline, groundY, o.EntranceToward.Value);
            if (b.HasName && b.Name == "Library") Beacon(mb, centroid, wallTop);

            // ---- materials & object
            var mats = new Material[(int)Sub.Count];
            int st = o.Style;
            var style = ProceduralTextures.Styles[st];
            var nightWin = style.Name == "Garage" ? Color.black : new Color(1f, 0.80f, 0.55f) * 1.6f;
            mats[(int)Sub.Facade] = CampusMaterials.Lit("Facade " + style.Name, ProceduralTextures.Facade(st), Color.white, Vector2.one, 1f, nightWin);
            mats[(int)Sub.Plain] = CampusMaterials.Lit("Wall " + style.Name, ProceduralTextures.PlainWall(st), Color.white, Vector2.one);
            mats[(int)Sub.Plinth] = CampusMaterials.Lit("Plinth", ProceduralTextures.Ground(ProceduralTextures.Surface.Concrete), new Color(0.85f, 0.84f, 0.80f), Vector2.one);
            mats[(int)Sub.Trim] = CampusMaterials.Lit("Trim " + style.Name, ProceduralTextures.Solid("trim" + style.Name, style.Trim, 0.25f, 0, 0.05f), Color.white, Vector2.one);
            mats[(int)Sub.Roof] = pitched && roofDone
                ? CampusMaterials.Lit("Clay Roof", ProceduralTextures.RoofTile(), Color.white, Vector2.one)
                : CampusMaterials.Lit("Flat Roof", ProceduralTextures.FlatRoof(), Color.white, Vector2.one);
            mats[(int)Sub.Doors] = CampusMaterials.Lit("Doors", ProceduralTextures.Doors(), Color.white, Vector2.one, 1f, new Color(1f, 0.85f, 0.6f) * 1.4f);
            mats[(int)Sub.Metal] = CampusMaterials.Lit("Rooftop Metal", ProceduralTextures.Solid("hvac", new Color(0.62f, 0.64f, 0.66f), 0.45f, 0.6f, 0.08f), Color.white, Vector2.one);
            mats[(int)Sub.Beacon] = CampusMaterials.Lit("Library Beacon", ProceduralTextures.Solid("beacon", new Color(0.85f, 0.92f, 1f), 0.9f, 0f, 0.02f, Color.white), Color.white, Vector2.one, 1f, new Color(0.8f, 0.9f, 1f) * 6f);

            var go = new GameObject(b.Name ?? b.Id ?? "Building");
            go.transform.SetParent(parent, false);
            var mesh = mb.ToMesh(go.name, out var used);
            var renderMats = new List<Material>();
            foreach (int s in used) renderMats.Add(mats[s]);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = renderMats.ToArray();
            mr.shadowCastingMode = ShadowCastingMode.On;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            b.CachedGroundY = groundY;
            return go;
        }

        // ------------------------------------------------------------------ geometry helpers

        static List<Vector2> Clean(Vector2[] pts)
        {
            var o = new List<Vector2>();
            foreach (var p in pts)
                if (o.Count == 0 || (p - o[o.Count - 1]).sqrMagnitude > 0.09f) o.Add(p);
            if (o.Count > 2 && (o[0] - o[o.Count - 1]).sqrMagnitude < 0.09f) o.RemoveAt(o.Count - 1);
            // drop nearly collinear vertices
            for (int k = 0; k < 3 && o.Count > 3; k++)
                for (int i = o.Count - 1; i >= 0 && o.Count > 3; i--)
                {
                    Vector2 a = o[(i + o.Count - 1) % o.Count], b = o[i], c = o[(i + 1) % o.Count];
                    Vector2 d1 = (b - a).normalized, d2 = (c - b).normalized;
                    if (Vector2.Dot(d1, d2) > 0.9985f) o.RemoveAt(i);
                }
            return o;
        }

        /// <summary>Miter offset of a CCW ring; positive = outward.</summary>
        public static List<Vector2> Offset(List<Vector2> ring, float d)
        {
            var o = new List<Vector2>(ring.Count);
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = ring[(i + n - 1) % n], p = ring[i], next = ring[(i + 1) % n];
                Vector2 e1 = (p - prev).normalized, e2 = (next - p).normalized;
                Vector2 n1 = new Vector2(e1.y, -e1.x), n2 = new Vector2(e2.y, -e2.x);  // outward for CCW
                Vector2 m = (n1 + n2);
                float len = m.magnitude;
                if (len < 1e-4f) { o.Add(p + n1 * d); continue; }
                m /= len;
                float cos = Mathf.Max(0.35f, Vector2.Dot(m, n1));
                o.Add(p + m * (d / cos));
            }
            return o;
        }

        static bool ValidInset(List<Vector2> outer, List<Vector2> inner)
        {
            if (Polygon2D.SignedArea(inner) <= 0) return false;
            foreach (var p in inner) if (!Polygon2D.Contains(outer, p)) return false;
            return true;
        }

        static void WallQuad(MultiMesh mb, int sub, Vector2 a, Vector2 c, float y0, float y1, float uScale, float vScale, float u0)
            => WallQuadUV(mb, sub, a, c, y0, y1, u0, u0 + uScale, 0f, vScale);

        /// <summary>Outward-facing wall quad for edge a→c of a CCW ring.</summary>
        static void WallQuadUV(MultiMesh mb, int sub, Vector2 a, Vector2 c, float y0, float y1, float u0, float u1, float v0, float v1)
        {
            var A0 = new Vector3(a.x, y0, a.y); var C0 = new Vector3(c.x, y0, c.y);
            var A1 = new Vector3(a.x, y1, a.y); var C1 = new Vector3(c.x, y1, c.y);
            mb.Quad(sub, A0, A1, C1, C0, new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
        }

        /// <summary>Horizontal ring between two rings (outer CCW / inner CCW) at height y, facing up.</summary>
        static void Ring(MultiMesh mb, int sub, List<Vector2> inner, List<Vector2> outer, float yInner, float yOuter, bool up)
        {
            int n = Mathf.Min(inner.Count, outer.Count);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var a = new Vector3(outer[i].x, yOuter, outer[i].y); var b = new Vector3(outer[j].x, yOuter, outer[j].y);
                var c = new Vector3(inner[j].x, yInner, inner[j].y); var d = new Vector3(inner[i].x, yInner, inner[i].y);
                // CCW outer→ (a,b) then inner back (c,d): seen from above this is clockwise → front faces up.
                if (up) mb.Quad(sub, a, d, c, b, new Vector2(0, 0), new Vector2(0, 0.3f), new Vector2(1, 0.3f), new Vector2(1, 0));
                else mb.Quad(sub, a, b, c, d, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 0.3f), new Vector2(0, 0.3f));
            }
        }

        static void TopFace(MultiMesh mb, int sub, List<Vector2> ring, float y, float uvTile)
        {
            var tris = Polygon2D.Triangulate(ring);
            for (int k = 0; k + 2 < tris.Count; k += 3)
            {
                Vector2 p0 = ring[tris[k]], p1 = ring[tris[k + 1]], p2 = ring[tris[k + 2]];
                // CCW from above → reverse for Unity front face up.
                mb.Tri(sub, new Vector3(p0.x, y, p0.y), new Vector3(p2.x, y, p2.y), new Vector3(p1.x, y, p1.y), p0 / uvTile, p2 / uvTile, p1 / uvTile);
            }
        }

        static void FlatRoof(MultiMesh mb, List<Vector2> outline, float wallTop, float parapet, bool units, string id)
        {
            var inner = Offset(outline, -0.3f);
            if (parapet > 0.01f && ValidInset(outline, inner))
            {
                float roofY = wallTop - parapet;
                TopFace(mb, (int)Sub.Roof, inner, roofY, 6f);
                // inner parapet faces (facing inward = reversed edge order)
                for (int i = 0; i < inner.Count; i++)
                {
                    Vector2 a = inner[(i + 1) % inner.Count], c = inner[i];
                    WallQuadUV(mb, (int)Sub.Plain, a, c, roofY, wallTop, 0, Vector2.Distance(a, c) / 4f, 0, parapet / 4f);
                }
                Ring(mb, (int)Sub.Trim, inner, Offset(outline, 0.05f), wallTop + 0.06f, wallTop + 0.06f, true);
                // coping lip
                var lip = Offset(outline, 0.05f);
                for (int i = 0; i < lip.Count; i++)
                    WallQuadUV(mb, (int)Sub.Trim, lip[i], lip[(i + 1) % lip.Count], wallTop - 0.12f, wallTop + 0.06f, 0, 1, 0, 0.1f);
                if (units) RooftopUnits(mb, inner, roofY, id);
            }
            else
            {
                TopFace(mb, (int)Sub.Roof, outline, wallTop, 6f);
            }
        }

        static void RooftopUnits(MultiMesh mb, List<Vector2> inner, float roofY, string id)
        {
            int seed = (id ?? "x").GetHashCode();
            var rnd = new System.Random(seed);
            Rect r = Polygon2D.Bounds(inner);
            int placed = 0;
            for (int tries = 0; tries < 30 && placed < 3; tries++)
            {
                var p = new Vector2(Mathf.Lerp(r.xMin, r.xMax, (float)rnd.NextDouble()), Mathf.Lerp(r.yMin, r.yMax, (float)rnd.NextDouble()));
                float sx = 2f + (float)rnd.NextDouble() * 2.5f, sz = 1.6f + (float)rnd.NextDouble() * 2f, sy = 1.2f + (float)rnd.NextDouble() * 0.8f;
                bool ok = Polygon2D.Contains(inner, p) && Polygon2D.DistanceToOutline(inner, p) > Mathf.Max(sx, sz) * 0.75f + 0.5f;
                if (!ok) continue;
                Box(mb, (int)Sub.Metal, new Vector3(p.x, roofY + sy * 0.5f, p.y), new Vector3(sx, sy, sz), 0f);
                placed++;
            }
        }

        /// <summary>Hip or gable roof on the footprint's minimum-area bounding rectangle (with 0.5 m overhang).</summary>
        static bool PitchedRoof(MultiMesh mb, List<Vector2> outline, float area, float eaveY, float ridgeY)
        {
            MinAreaRect(outline, out Vector2 center, out Vector2 axisU, out float halfU, out float halfV);
            if (area / (4 * halfU * halfV) < 0.72f) return false;   // not rectangular enough
            Vector2 axisV = new Vector2(-axisU.y, axisU.x);
            const float over = 0.55f;
            halfU += over; halfV += over;
            float eave = eaveY - 0.25f;   // overhang drops slightly below the wall top
            Vector3 W(float u, float v, float y) { var p = center + axisU * u + axisV * v; return new Vector3(p.x, y, p.y); }
            Vector3 c00 = W(-halfU, -halfV, eave), c10 = W(halfU, -halfV, eave), c11 = W(halfU, halfV, eave), c01 = W(-halfU, halfV, eave);
            bool gable = halfU / halfV > 1.45f;
            float ridgeHalf = gable ? halfU : Mathf.Max(0f, halfU - halfV);
            Vector3 r0 = W(-ridgeHalf, 0, ridgeY), r1 = W(ridgeHalf, 0, ridgeY);
            float slopeLen = Mathf.Sqrt(halfV * halfV + (ridgeY - eave) * (ridgeY - eave));
            float uvS = 2f;
            int roof = (int)Sub.Roof, soffit = (int)Sub.Trim;

            // Long slopes (south side v<0 and north side v>0)
            RoofQuad(mb, roof, soffit, c00, c10, r1, r0, 2 * halfU / uvS, slopeLen / uvS);
            RoofQuad(mb, roof, soffit, c11, c01, r0, r1, 2 * halfU / uvS, slopeLen / uvS);
            if (gable)
            {
                // Gable end walls (plain wall material) from the eave to the ridge.
                RoofTri(mb, (int)Sub.Plain, (int)Sub.Plain, c01, c00, r0, halfV / 2f, (ridgeY - eave) / 4f);
                RoofTri(mb, (int)Sub.Plain, (int)Sub.Plain, c10, c11, r1, halfV / 2f, (ridgeY - eave) / 4f);
            }
            else
            {
                RoofTri(mb, roof, soffit, c01, c00, r0, 2 * halfV / uvS, slopeLen / uvS);
                RoofTri(mb, roof, soffit, c10, c11, r1, 2 * halfV / uvS, slopeLen / uvS);
            }
            return true;
        }

        static void RoofQuad(MultiMesh mb, int top, int bottom, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uMax, float vMax)
        {
            // a,b = eave (left→right seen from outside), c,d = ridge (right, left)
            mb.Quad(top, a, d, c, b, new Vector2(0, 0), new Vector2(0, vMax), new Vector2(uMax, vMax), new Vector2(uMax, 0));
            mb.Quad(bottom, a, b, c, d, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
        }

        static void RoofTri(MultiMesh mb, int top, int bottom, Vector3 a, Vector3 b, Vector3 apex, float uMax, float vMax)
        {
            mb.Tri(top, a, apex, b, new Vector2(0, 0), new Vector2(uMax * 0.5f, vMax), new Vector2(uMax, 0));
            mb.Tri(bottom, a, b, apex, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1));
        }

        static void MinAreaRect(List<Vector2> pts, out Vector2 center, out Vector2 axis, out float halfU, out float halfV)
        {
            float best = float.MaxValue;
            center = Vector2.zero; axis = Vector2.right; halfU = halfV = 1;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 e = pts[(i + 1) % pts.Count] - pts[i];
                if (e.sqrMagnitude < 0.25f) continue;
                Vector2 u = e.normalized, v = new Vector2(-u.y, u.x);
                float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                foreach (var p in pts)
                {
                    float pu = Vector2.Dot(p, u), pv = Vector2.Dot(p, v);
                    minU = Mathf.Min(minU, pu); maxU = Mathf.Max(maxU, pu); minV = Mathf.Min(minV, pv); maxV = Mathf.Max(maxV, pv);
                }
                float a = (maxU - minU) * (maxV - minV);
                if (a < best)
                {
                    best = a;
                    float hu = (maxU - minU) * 0.5f, hv = (maxV - minV) * 0.5f;
                    var c = u * ((maxU + minU) * 0.5f) + v * ((maxV + minV) * 0.5f);
                    if (hu >= hv) { axis = u; halfU = hu; halfV = hv; }
                    else { axis = v; halfU = hv; halfV = hu; }
                    center = c;
                }
            }
        }

        static void Entrance(MultiMesh mb, List<Vector2> outline, float groundY, Vector2 toward)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i], c = outline[(i + 1) % outline.Count];
                if (Vector2.Distance(a, c) < 6.5f) continue;
                float d = Vector2.Distance((a + c) * 0.5f, toward);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            Vector2 A = outline[best], C = outline[(best + 1) % outline.Count];
            Vector2 along = (C - A).normalized, outward = new Vector2(along.y, -along.x);
            Vector2 mid = (A + C) * 0.5f;
            const float doorW = 3.4f, doorH = 2.7f;
            Vector2 d0 = mid - along * (doorW * 0.5f) + outward * 0.05f, d1 = mid + along * (doorW * 0.5f) + outward * 0.05f;
            WallQuadUV(mb, (int)Sub.Doors, d0, d1, groundY, groundY + doorH, 0, 1, 0, 1);
            // door surround
            Vector2 s0 = d0 - along * 0.25f + outward * 0.02f, s1 = d1 + along * 0.25f + outward * 0.02f;
            WallQuadUV(mb, (int)Sub.Trim, s0, s0 + along * 0.25f, groundY, groundY + doorH + 0.25f, 0, 0.1f, 0, 1);
            WallQuadUV(mb, (int)Sub.Trim, s1 - along * 0.25f, s1, groundY, groundY + doorH + 0.25f, 0, 0.1f, 0, 1);
            // canopy + columns + landing
            float canopyY = groundY + 3.25f;
            Vector2 cc = mid + outward * 1.35f;
            float yaw = Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg;
            Box(mb, (int)Sub.Trim, new Vector3(cc.x, canopyY, cc.y), new Vector3(0.25f, 0.25f, 0f) + new Vector3(2.7f, 0f, 5.0f) * 1f, yaw, swapXZ: true);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 col = mid + outward * 2.45f + along * (s * 2.2f);
                Box(mb, (int)Sub.Trim, new Vector3(col.x, groundY + 1.6f, col.y), new Vector3(0.32f, 3.2f, 0.32f), yaw);
            }
            Vector2 lc = mid + outward * 1.4f;
            Box(mb, (int)Sub.Plinth, new Vector3(lc.x, groundY + 0.06f, lc.y), new Vector3(5.4f, 0.12f, 3.0f), yaw, swapXZ: true);
        }

        static void Beacon(MultiMesh mb, Vector2 c, float top)
        {
            const int seg = 24; const float r = 4.2f, h = 2.2f;
            float y0 = top + 0.2f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                // clockwise order in (east,north) so faces point outward
                Vector2 p0 = c + new Vector2(Mathf.Cos(-a0), Mathf.Sin(-a0)) * r, p1 = c + new Vector2(Mathf.Cos(-a1), Mathf.Sin(-a1)) * r;
                WallQuadUV(mb, (int)Sub.Beacon, p1, p0, y0, y0 + h, 0, 1, 0, 1);
                WallQuadUV(mb, (int)Sub.Beacon, p0, p1, y0, y0 + h, 0, 1, 0, 1);
            }
            var ring = new List<Vector2>();
            for (int i = 0; i < seg; i++) { float a = i * Mathf.PI * 2 / seg; ring.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r + 0.3f)); }
            TopFace(mb, (int)Sub.Trim, ring, y0 + h + 0.05f, 4f);
        }

        /// <summary>Axis-aligned (then yawed) box. With swapXZ the size is (depth, height, width) along the yaw.</summary>
        static void Box(MultiMesh mb, int sub, Vector3 center, Vector3 size, float yawDeg, bool swapXZ = false)
        {
            if (swapXZ) size = new Vector3(size.z, size.y, size.x);
            var q = Quaternion.Euler(0, yawDeg, 0);
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + q * new Vector3(x * h.x, y * h.y, z * h.z);
            var faces = new[]
            {
                (P(-1,-1,-1), P(-1, 1,-1), P( 1, 1,-1), P( 1,-1,-1)), // -z
                (P( 1,-1, 1), P( 1, 1, 1), P(-1, 1, 1), P(-1,-1, 1)), // +z
                (P(-1,-1, 1), P(-1, 1, 1), P(-1, 1,-1), P(-1,-1,-1)), // -x
                (P( 1,-1,-1), P( 1, 1,-1), P( 1, 1, 1), P( 1,-1, 1)), // +x
                (P(-1, 1,-1), P(-1, 1, 1), P( 1, 1, 1), P( 1, 1,-1)), // +y
                (P(-1,-1, 1), P(-1,-1,-1), P( 1,-1,-1), P( 1,-1, 1)), // -y
            };
            foreach (var f in faces)
                mb.Quad(sub, f.Item1, f.Item2, f.Item3, f.Item4, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
        }
    }

    /// <summary>Mesh builder with several submeshes. Quads/tris are emitted with the given winding
    /// (Unity front face = clockwise as seen by the viewer; normal = Cross(v1 - v0, v2 - v0)).</summary>
    public class MultiMesh
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int>[] _t;

        public MultiMesh(int subs)
        {
            _t = new List<int>[subs];
            for (int i = 0; i < subs; i++) _t[i] = new List<int>();
        }

        /// <summary>Quad a-b-c-d (clockwise when seen from the front).</summary>
        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int s = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
            _t[sub].Add(s); _t[sub].Add(s + 1); _t[sub].Add(s + 2);
            _t[sub].Add(s); _t[sub].Add(s + 2); _t[sub].Add(s + 3);
        }

        /// <summary>Triangle a-b-c (clockwise when seen from the front).</summary>
        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            int s = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc);
            _t[sub].Add(s); _t[sub].Add(s + 1); _t[sub].Add(s + 2);
        }

        public Mesh ToMesh(string name, out List<int> usedSubs)
        {
            usedSubs = new List<int>();
            for (int i = 0; i < _t.Length; i++) if (_t[i].Count > 0) usedSubs.Add(i);
            var m = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(_v);
            m.SetUVs(0, _uv);
            m.subMeshCount = usedSubs.Count;
            for (int k = 0; k < usedSubs.Count; k++) m.SetTriangles(_t[usedSubs[k]], k);
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }
    }
}
