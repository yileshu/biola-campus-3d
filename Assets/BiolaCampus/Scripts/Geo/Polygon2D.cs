using System.Collections.Generic;
using UnityEngine;

namespace BiolaCampus.Geo
{
    /// <summary>Small 2D polygon toolkit (points are (east, north) metres).</summary>
    public static class Polygon2D
    {
        public static bool Contains(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if (((a.y > p.y) != (b.y > p.y)) &&
                    (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y + 1e-12f) + a.x))
                    inside = !inside;
            }
            return inside;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + t * ab);
        }

        public static float DistanceToOutline(IReadOnlyList<Vector2> poly, Vector2 p, bool closed = true)
        {
            float best = float.MaxValue;
            int n = poly.Count;
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
                best = Mathf.Min(best, DistanceToSegment(p, poly[i], poly[(i + 1) % n]));
            return best;
        }

        /// <summary>0 when inside, else distance to the outline.</summary>
        public static float Distance(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            return Contains(poly, p) ? 0f : DistanceToOutline(poly, p);
        }

        public static float SignedArea(IReadOnlyList<Vector2> poly)
        {
            double a = 0;
            for (int i = 0, n = poly.Count; i < n; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % n];
                a += (double)p.x * q.y - (double)q.x * p.y;
            }
            return (float)(a * 0.5);
        }

        public static Vector2 Centroid(IReadOnlyList<Vector2> poly)
        {
            double cx = 0, cy = 0, a = 0;
            for (int i = 0, n = poly.Count; i < n; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % n];
                double cross = (double)p.x * q.y - (double)q.x * p.y;
                a += cross; cx += (p.x + q.x) * cross; cy += (p.y + q.y) * cross;
            }
            if (System.Math.Abs(a) < 1e-6)
            {
                Vector2 s = Vector2.zero;
                foreach (var v in poly) s += v;
                return poly.Count > 0 ? s / poly.Count : Vector2.zero;
            }
            a *= 0.5;
            return new Vector2((float)(cx / (6 * a)), (float)(cy / (6 * a)));
        }

        public static Rect Bounds(IReadOnlyList<Vector2> pts)
        {
            if (pts.Count == 0) return new Rect();
            Vector2 min = pts[0], max = pts[0];
            foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Ear-clipping triangulation of a simple polygon. Returns indices into <paramref name="poly"/>.</summary>
        public static List<int> Triangulate(IReadOnlyList<Vector2> poly)
        {
            var result = new List<int>();
            int n = poly.Count;
            if (n < 3) return result;
            var idx = new List<int>(n);
            bool ccw = SignedArea(poly) > 0;
            for (int i = 0; i < n; i++) idx.Add(ccw ? i : n - 1 - i);

            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
                    if (Cross(b - a, c - b) <= 1e-9f) continue; // reflex or degenerate
                    bool contains = false;
                    for (int k = 0; k < idx.Count && !contains; k++)
                    {
                        int ik = idx[k];
                        if (ik == i0 || ik == i1 || ik == i2) continue;
                        contains = PointInTriangle(poly[ik], a, b, c);
                    }
                    if (contains) continue;
                    result.Add(i0); result.Add(i1); result.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // Self-intersecting / degenerate input: fall back to a fan for what is left.
                    for (int i = 1; i < idx.Count - 1; i++) { result.Add(idx[0]); result.Add(idx[i]); result.Add(idx[i + 1]); }
                    return result;
                }
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
            return result;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }
    }
}
