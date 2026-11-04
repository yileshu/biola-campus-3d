using System.Collections.Generic;
using BiolaCampus.Core;
using BiolaCampus.Osm;
using UnityEngine;
using UnityEngine.UI;

namespace BiolaCampus.UI
{
    /// <summary>
    /// North-up campus minimap rasterised at runtime from the cached OSM data (buildings, paths,
    /// green areas, parking, campus boundary) with a rotating player marker. M toggles a large view
    /// with building names.
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        const int TexSize = 1024;
        const float SmallSize = 240f, LargeSize = 760f;

        CampusMap _map;
        Transform _player;
        Rect _world;              // world rect (east/north metres) covered by the texture
        RectTransform _frame, _mapRect, _marker;
        RawImage _image;
        readonly List<Text> _names = new List<Text>();
        bool _large;
        Text _footer;

        public void Build(Canvas canvas, CampusMap map)
        {
            _map = map;
            Rect b = map.Bounds;
            float size = Mathf.Max(b.width, b.height) * 1.12f + 40f;
            _world = new Rect(b.center.x - size / 2, b.center.y - size / 2, size, size);

            _frame = UIFactory.Image("Minimap", canvas.transform, new Color(0, 0, 0, 0.55f)).rectTransform;
            _frame.gameObject.AddComponent<RectMask2D>();
            var mapImg = UIFactory.Rect("Map", _frame);
            _mapRect = mapImg;
            _image = mapImg.gameObject.AddComponent<RawImage>();
            _image.texture = Rasterize();
            _image.raycastTarget = false;
            _mapRect.Stretch(4);

            _marker = UIFactory.Image("Player", _mapRect, UIFactory.AccentColor).rectTransform;
            var markerImg = _marker.GetComponent<Image>();
            markerImg.sprite = UIFactory.ArrowSprite();
            _marker.sizeDelta = new Vector2(18, 18);
            _marker.anchorMin = _marker.anchorMax = Vector2.zero;
            var outline = _marker.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.white; outline.effectDistance = new Vector2(1.5f, 1.5f);

            var north = UIFactory.Label("N", _frame, "N", 18, TextAnchor.UpperCenter);
            north.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(30, 24));
            north.Shadow();

            foreach (var bld in map.Buildings)
            {
                if (!bld.HasName) continue;
                var t = UIFactory.Label("Name", _mapRect, bld.Name, 13, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.95f));
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = WorldToUV(bld.Centroid);
                t.rectTransform.sizeDelta = new Vector2(160, 18);
                t.Shadow();
                t.gameObject.SetActive(false);
                _names.Add(t);
            }

            _footer = UIFactory.Label("Attribution", _frame, "Map data " + CampusMap.Attribution, 12, TextAnchor.LowerRight, new Color(1, 1, 1, 0.8f));
            _footer.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-6, 3), new Vector2(-8, 16));
            _footer.Shadow();

            if (map.IsPlaceholder)
            {
                var warn = UIFactory.Label("Placeholder", _frame, "OSM cache not downloaded yet", 13, TextAnchor.UpperCenter, new Color(1f, 0.85f, 0.4f));
                warn.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(0, 18));
                warn.Shadow();
            }
            SetLarge(false);
        }

        public void SetPlayer(Transform player) => _player = player;

        public void SetVisible(bool v) { if (_frame != null) _frame.gameObject.SetActive(v); }

        void SetLarge(bool large)
        {
            _large = large;
            if (large) _frame.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(LargeSize, LargeSize));
            else _frame.Place(Vector2.one, Vector2.one, Vector2.one, new Vector2(-18, -18), new Vector2(SmallSize, SmallSize));
            foreach (var n in _names) n.gameObject.SetActive(large);
            _marker.sizeDelta = large ? new Vector2(26, 26) : new Vector2(18, 18);
        }

        void Update()
        {
            if (_frame == null || !_frame.gameObject.activeInHierarchy) return;
            if (InputShim.Pressed(KeyCode.M) || InputShim.Pressed(KeyCode.Tab)) SetLarge(!_large);
            if (_player == null) { _marker.gameObject.SetActive(false); return; }
            _marker.gameObject.SetActive(true);
            Vector2 uv = WorldToUV(CampusMap.XZ(_player.position));
            _marker.anchorMin = _marker.anchorMax = uv;
            _marker.anchoredPosition = Vector2.zero;
            _marker.localRotation = Quaternion.Euler(0, 0, -_player.eulerAngles.y);
        }

        Vector2 WorldToUV(Vector2 p) => new Vector2((p.x - _world.xMin) / _world.width, (p.y - _world.yMin) / _world.height);

        // ------------------------------------------------------------------ rasteriser

        Color32[] _px;

        Texture2D Rasterize()
        {
            _px = new Color32[TexSize * TexSize];
            var bg = new Color32(38, 44, 40, 255);
            for (int i = 0; i < _px.Length; i++) _px[i] = bg;

            // Campus interior tint
            foreach (var ring in _map.BoundaryRings) FillPolygon(ring, new Color32(58, 74, 56, 255));

            foreach (var a in _map.Areas)
            {
                Color32 c;
                switch (a.Kind)
                {
                    case "parking": c = new Color32(78, 80, 86, 255); break;
                    case "pitch": case "stadium": case "track": c = new Color32(70, 128, 70, 255); break;
                    case "grass": case "park": case "garden": case "meadow": case "recreation_ground": c = new Color32(72, 112, 64, 255); break;
                    case "water": case "swimming_pool": case "fountain": c = new Color32(70, 120, 170, 255); break;
                    default: c = new Color32(80, 92, 74, 255); break;
                }
                FillPolygon(a.Outline, c);
            }

            foreach (var p in _map.Paths)
            {
                bool road = p.Kind == "service" || p.Kind == "residential" || p.Kind == "tertiary" || p.Kind == "secondary" || p.Kind == "primary" || p.Kind == "unclassified";
                var c = road ? new Color32(214, 214, 206, 255) : new Color32(196, 176, 140, 255);
                float w = road ? 2.2f : 1.2f;
                for (int i = 0; i < p.Points.Length - 1; i++) DrawLine(p.Points[i], p.Points[i + 1], c, w);
            }

            foreach (var bld in _map.Buildings)
            {
                var fill = bld.HasName ? new Color32(206, 200, 186, 255) : new Color32(160, 160, 160, 255);
                FillPolygon(bld.Outline, fill);
                var edge = new Color32(110, 104, 96, 255);
                for (int i = 0; i < bld.Outline.Length; i++) DrawLine(bld.Outline[i], bld.Outline[(i + 1) % bld.Outline.Length], edge, 0.8f);
            }

            foreach (var ring in _map.BoundaryRings)
                for (int i = 0; i < ring.Length; i++) DrawLine(ring[i], ring[(i + 1) % ring.Length], new Color32(255, 200, 60, 255), 2.4f);

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "Minimap (OSM)" };
            tex.SetPixels32(_px);
            tex.Apply(true, true);
            _px = null;
            return tex;
        }

        Vector2 ToPx(Vector2 w) => new Vector2((w.x - _world.xMin) / _world.width * TexSize, (w.y - _world.yMin) / _world.height * TexSize);

        void FillPolygon(Vector2[] poly, Color32 c)
        {
            if (poly.Length < 3) return;
            var pts = new Vector2[poly.Length];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < poly.Length; i++) { pts[i] = ToPx(poly[i]); minY = Mathf.Min(minY, pts[i].y); maxY = Mathf.Max(maxY, pts[i].y); }
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minY), 0, TexSize - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, TexSize - 1);
            var xs = new List<float>();
            for (int y = y0; y <= y1; y++)
            {
                float sy = y + 0.5f;
                xs.Clear();
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                {
                    Vector2 a = pts[i], b = pts[j];
                    if ((a.y > sy) != (b.y > sy)) xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int x0 = Mathf.Clamp(Mathf.CeilToInt(xs[k] - 0.5f), 0, TexSize - 1);
                    int x1 = Mathf.Clamp(Mathf.FloorToInt(xs[k + 1] - 0.5f), 0, TexSize - 1);
                    for (int x = x0; x <= x1; x++) _px[y * TexSize + x] = c;
                }
            }
        }

        void DrawLine(Vector2 wa, Vector2 wb, Color32 c, float widthPx)
        {
            Vector2 a = ToPx(wa), b = ToPx(wb);
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len * 2f));
            int r = Mathf.Max(0, Mathf.CeilToInt(widthPx * 0.5f - 0.5f));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= TexSize || y >= TexSize) continue;
                        _px[y * TexSize + x] = c;
                    }
            }
        }
    }
}
