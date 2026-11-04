using System.Collections.Generic;
using UnityEngine;

namespace BiolaCampus.World.Campus
{
    /// <summary>
    /// All surface textures are generated here at runtime (albedo, normal, metallic/smoothness and
    /// emission). Nothing is sampled from photographs or from the Google tiles; colours are chosen to
    /// match the look of the campus (warm stucco, clay tile, bronze window frames, SoCal landscaping).
    /// </summary>
    public static class ProceduralTextures
    {
        public struct Set
        {
            public Texture2D Albedo, Normal, Mask, Emission;
        }

        static readonly Dictionary<string, Set> Cache = new Dictionary<string, Set>();

        // ------------------------------------------------------------------ helpers

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Tileable value noise with a few octaves (period = size).</summary>
        static float Noise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float tx = x - xi, ty = y - yi;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            int x0 = ((xi % period) + period) % period, y0 = ((yi % period) + period) % period;
            int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), tx), Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), tx), ty);
        }

        static float Fbm(float u, float v, int baseFreq, int octaves, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            int f = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Noise(u * f, v * f, f, seed + o * 17);
                norm += amp; amp *= 0.5f; f *= 2;
            }
            return sum / norm;
        }

        static Texture2D NewTex(int size, bool linear, string name)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, linear)
            {
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = name
            };
            return t;
        }

        static void Finish(Texture2D t, Color32[] px)
        {
            t.SetPixels32(px);
            t.Apply(true, false);
        }

        /// <summary>Normal map from a height array via central differences (tangent space, linear).</summary>
        static Texture2D NormalFromHeight(float[] h, int size, float strength, string name)
        {
            var t = NewTex(size, true, name + "_N");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float l = h[y * size + (x + size - 1) % size], r = h[y * size + (x + 1) % size];
                    float d = h[((y + size - 1) % size) * size + x], u = h[((y + 1) % size) * size + x];
                    var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    px[y * size + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255), (byte)((n.y * 0.5f + 0.5f) * 255), (byte)((n.z * 0.5f + 0.5f) * 255), 255);
                }
            Finish(t, px);
            return t;
        }

        static Texture2D MaskFromSmoothness(float[] smooth, float metallic, int size, string name)
        {
            var t = NewTex(size, true, name + "_M");
            var px = new Color32[size * size];
            byte m = (byte)(metallic * 255);
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(m, 255, 0, (byte)(Mathf.Clamp01(smooth[i]) * 255));
            Finish(t, px);
            return t;
        }

        static Texture2D Flat(Color c, string name, bool linear)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear) { name = name, wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = c;
            t.SetPixels32(px); t.Apply(false, false);
            return t;
        }

        static Texture2D _black, _flatNormal;
        public static Texture2D Black => _black != null ? _black : (_black = Flat(Color.black, "Black", false));
        public static Texture2D FlatNormal => _flatNormal != null ? _flatNormal : (_flatNormal = Flat(new Color(0.5f, 0.5f, 1f, 1f), "FlatNormal", true));

        // ------------------------------------------------------------------ ground surfaces

        public enum Surface { Grass, LushGrass, Turf, Asphalt, Pavers, Concrete, Granite, Sand, Mulch, Water, Track }

        public static Set Ground(Surface s)
        {
            string key = "ground_" + s;
            if (Cache.TryGetValue(key, out var set)) return set;
            const int N = 256;
            var albedo = NewTex(N, false, key);
            var px = new Color32[N * N];
            var h = new float[N * N];
            var sm = new float[N * N];
            int seed = (int)s * 31 + 7;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float n1 = Fbm(u, v, 4, 4, seed), n2 = Fbm(u, v, 32, 2, seed + 3), n3 = Hash(x, y, seed);
                    Color c; float height; float smooth;
                    switch (s)
                    {
                        case Surface.Grass:
                        case Surface.LushGrass:
                        case Surface.Turf:
                        {
                            var a = s == Surface.LushGrass ? new Color(0.20f, 0.36f, 0.12f) : s == Surface.Turf ? new Color(0.17f, 0.40f, 0.14f) : new Color(0.27f, 0.38f, 0.15f);
                            var b = s == Surface.LushGrass ? new Color(0.32f, 0.48f, 0.18f) : s == Surface.Turf ? new Color(0.22f, 0.47f, 0.17f) : new Color(0.42f, 0.47f, 0.22f);
                            float blades = Mathf.Pow(n3, 3f);
                            c = Color.Lerp(a, b, n1 * 0.7f + blades * 0.5f);
                            if (s == Surface.Turf && ((int)(v * 4)) % 2 == 0) c *= 1.08f; // mowing stripes
                            if (s == Surface.Grass && n2 > 0.72f) c = Color.Lerp(c, new Color(0.55f, 0.50f, 0.32f), 0.5f); // dry patches
                            height = n3 * 0.6f + n2 * 0.4f; smooth = 0.12f;
                            break;
                        }
                        case Surface.Asphalt:
                            c = Color.Lerp(new Color(0.16f, 0.16f, 0.17f), new Color(0.27f, 0.27f, 0.28f), n1 * 0.6f + n3 * 0.4f);
                            if (n3 > 0.985f) c = new Color(0.45f, 0.45f, 0.45f);
                            height = n3 * 0.7f + n1 * 0.3f; smooth = 0.2f + n2 * 0.1f;
                            break;
                        case Surface.Pavers:
                        {
                            // Running-bond concrete pavers, 0.6 x 0.3 m per texel block (texture = 2.4 m).
                            float bu = u * 4f, bv = v * 8f;
                            int row = Mathf.FloorToInt(bv);
                            if (row % 2 == 1) bu += 0.5f;
                            float fu = bu - Mathf.Floor(bu), fv = bv - Mathf.Floor(bv);
                            bool joint = fu < 0.025f || fu > 0.975f || fv < 0.05f || fv > 0.95f;
                            float tone = Hash(Mathf.FloorToInt(bu), row, seed);
                            var baseC = Color.Lerp(new Color(0.72f, 0.64f, 0.54f), new Color(0.82f, 0.75f, 0.64f), tone);
                            c = joint ? new Color(0.42f, 0.38f, 0.33f) : Color.Lerp(baseC, baseC * 0.9f, n2);
                            height = joint ? 0f : 0.8f + n3 * 0.2f; smooth = joint ? 0.05f : 0.28f;
                            break;
                        }
                        case Surface.Concrete:
                        {
                            float fu = u * 2 - Mathf.Floor(u * 2), fv = v * 2 - Mathf.Floor(v * 2);
                            bool joint = fu < 0.008f || fv < 0.008f;
                            c = Color.Lerp(new Color(0.66f, 0.65f, 0.62f), new Color(0.78f, 0.77f, 0.74f), n1 * 0.7f + n3 * 0.3f);
                            if (joint) c *= 0.7f;
                            height = joint ? 0 : 0.5f + n3 * 0.3f; smooth = 0.25f;
                            break;
                        }
                        case Surface.Granite:
                            c = Color.Lerp(new Color(0.62f, 0.52f, 0.38f), new Color(0.78f, 0.68f, 0.52f), n1 * 0.5f + n3 * 0.5f);
                            height = n3; smooth = 0.08f;
                            break;
                        case Surface.Sand:
                            c = Color.Lerp(new Color(0.80f, 0.72f, 0.56f), new Color(0.90f, 0.84f, 0.68f), n1 * 0.6f + n3 * 0.4f);
                            height = n1 * 0.5f + n3 * 0.5f; smooth = 0.1f;
                            break;
                        case Surface.Mulch:
                            c = Color.Lerp(new Color(0.24f, 0.17f, 0.11f), new Color(0.40f, 0.29f, 0.19f), n3 * 0.7f + n1 * 0.3f);
                            height = n3; smooth = 0.05f;
                            break;
                        case Surface.Water:
                            c = Color.Lerp(new Color(0.10f, 0.38f, 0.52f), new Color(0.18f, 0.56f, 0.68f), n1);
                            height = n1 * 0.3f; smooth = 0.95f;
                            break;
                        default: // Track
                            c = Color.Lerp(new Color(0.55f, 0.20f, 0.15f), new Color(0.65f, 0.27f, 0.20f), n3 * 0.6f + n1 * 0.4f);
                            height = n3; smooth = 0.15f;
                            break;
                    }
                    px[y * N + x] = c;
                    h[y * N + x] = height;
                    sm[y * N + x] = smooth;
                }
            Finish(albedo, px);
            set = new Set
            {
                Albedo = albedo,
                Normal = NormalFromHeight(h, N, s == Surface.Pavers || s == Surface.Concrete ? 3f : 1.5f, key),
                Mask = MaskFromSmoothness(sm, 0f, N, key),
                Emission = Black,
            };
            Cache[key] = set;
            return set;
        }

        // ------------------------------------------------------------------ facades

        public struct FacadeStyle
        {
            public string Name;
            public Color Wall, WallAlt, Frame, Glass, Trim;
            public float WindowWidth, WindowHeight, SillHeight;   // fractions of a bay / floor
            public bool Mullion, Arched, Bands, NoLights;
        }

        public static readonly FacadeStyle[] Styles =
        {
            // Warm cream stucco, bronze frames — the most common campus look.
            new FacadeStyle { Name = "CreamStucco", Wall = new Color(0.86f, 0.80f, 0.68f), WallAlt = new Color(0.80f, 0.73f, 0.60f), Frame = new Color(0.25f, 0.20f, 0.15f), Glass = new Color(0.16f, 0.24f, 0.30f), Trim = new Color(0.93f, 0.90f, 0.82f), WindowWidth = 0.56f, WindowHeight = 0.52f, SillHeight = 0.26f, Mullion = true, Bands = true },
            // Sand-coloured stucco with white trim (residence halls).
            new FacadeStyle { Name = "SandStucco", Wall = new Color(0.80f, 0.70f, 0.55f), WallAlt = new Color(0.74f, 0.64f, 0.50f), Frame = new Color(0.92f, 0.91f, 0.88f), Glass = new Color(0.18f, 0.25f, 0.30f), Trim = new Color(0.95f, 0.94f, 0.90f), WindowWidth = 0.42f, WindowHeight = 0.48f, SillHeight = 0.28f, Mullion = false, Bands = false },
            // Red-brown brick with precast trim (older academic buildings).
            new FacadeStyle { Name = "Brick", Wall = new Color(0.56f, 0.30f, 0.22f), WallAlt = new Color(0.48f, 0.25f, 0.18f), Frame = new Color(0.90f, 0.88f, 0.82f), Glass = new Color(0.14f, 0.20f, 0.26f), Trim = new Color(0.84f, 0.80f, 0.72f), WindowWidth = 0.50f, WindowHeight = 0.55f, SillHeight = 0.25f, Mullion = true, Bands = true },
            // Modern: light grey panels with large glazing (science / cinema / business).
            new FacadeStyle { Name = "Modern", Wall = new Color(0.82f, 0.82f, 0.80f), WallAlt = new Color(0.72f, 0.73f, 0.72f), Frame = new Color(0.30f, 0.32f, 0.34f), Glass = new Color(0.20f, 0.32f, 0.40f), Trim = new Color(0.60f, 0.61f, 0.62f), WindowWidth = 0.80f, WindowHeight = 0.66f, SillHeight = 0.18f, Mullion = true, Bands = true },
            // Mission-style white stucco with arched windows (chapel, special buildings).
            new FacadeStyle { Name = "Mission", Wall = new Color(0.93f, 0.91f, 0.86f), WallAlt = new Color(0.88f, 0.86f, 0.80f), Frame = new Color(0.30f, 0.22f, 0.15f), Glass = new Color(0.15f, 0.20f, 0.25f), Trim = new Color(0.70f, 0.45f, 0.30f), WindowWidth = 0.40f, WindowHeight = 0.58f, SillHeight = 0.24f, Mullion = false, Arched = true, Bands = false },
            // Parking structure: board-formed concrete with long open bays.
            new FacadeStyle { Name = "Garage", Wall = new Color(0.74f, 0.73f, 0.70f), WallAlt = new Color(0.66f, 0.65f, 0.62f), Frame = new Color(0.62f, 0.61f, 0.58f), Glass = new Color(0.05f, 0.05f, 0.06f), Trim = new Color(0.80f, 0.79f, 0.76f), WindowWidth = 0.94f, WindowHeight = 0.48f, SillHeight = 0.30f, Mullion = false, Bands = true, NoLights = true },
        };

        /// <summary>
        /// Facade tile covering 4 bays × 3 floors (so the lit-window pattern does not visibly repeat):
        /// u = 0..1 over 4 bays, v = 0..1 over 3 floors.
        /// </summary>
        public static Set Facade(int styleIndex)
        {
            var st = Styles[Mathf.Clamp(styleIndex, 0, Styles.Length - 1)];
            string key = "facade_" + st.Name;
            if (Cache.TryGetValue(key, out var set)) return set;
            const int W = 512, H = 512;
            var albedo = NewTex(W, false, key);
            var emis = NewTex(W, false, key + "_E");
            var px = new Color32[W * H];
            var ep = new Color32[W * H];
            var h = new float[W * H];
            var sm = new float[W * H];
            int seed = styleIndex * 101 + 5;
            bool brick = st.Name == "Brick";

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W * 4f, v = y / (float)H * 3f;   // bay and floor coordinates
                    int bay = Mathf.FloorToInt(u), floor = Mathf.FloorToInt(v);
                    float bu = u - bay, fv = v - floor;
                    float n = Fbm(x / (float)W, y / (float)H, 8, 3, seed);
                    Color wall = Color.Lerp(st.Wall, st.WallAlt, n * 0.6f + Hash(x, y, seed) * 0.15f);
                    float height = 0.5f + n * 0.1f;
                    float smooth = 0.12f;

                    if (brick)
                    {
                        float rowF = y / (float)H * 3f * 48f; // ~8 cm courses
                        int row = Mathf.FloorToInt(rowF);
                        float colF = x / (float)W * 4f * 14f + (row % 2) * 0.5f;
                        bool mortar = rowF - row < 0.14f || colF - Mathf.Floor(colF) < 0.06f;
                        float tone = Hash(Mathf.FloorToInt(colF), row, seed);
                        wall = mortar ? new Color(0.70f, 0.66f, 0.60f) : Color.Lerp(st.Wall, st.WallAlt, tone) * (0.92f + n * 0.16f);
                        height = mortar ? 0.35f : 0.55f;
                    }

                    Color c = wall;
                    Color e = Color.black;

                    // Floor slab band.
                    if (st.Bands && fv < 0.06f) { c = st.Trim; height = 0.62f; }

                    float wx0 = 0.5f - st.WindowWidth * 0.5f, wx1 = 0.5f + st.WindowWidth * 0.5f;
                    float wy0 = st.SillHeight, wy1 = st.SillHeight + st.WindowHeight;
                    bool inWin = bu > wx0 && bu < wx1 && fv > wy0 && fv < wy1;
                    if (st.Arched && inWin)
                    {
                        float r = (wx1 - wx0) * 0.5f, cy = wy1 - r * 0.6f;
                        if (fv > cy)
                        {
                            float dx = (bu - 0.5f) / r, dy = (fv - cy) / (r * 0.6f);
                            inWin = dx * dx + dy * dy < 1f;
                        }
                    }
                    float frameW = 0.025f;
                    bool frame = !inWin && bu > wx0 - frameW && bu < wx1 + frameW && fv > wy0 - frameW * 1.5f && fv < wy1 + frameW * 1.5f;
                    bool sill = !inWin && bu > wx0 - 0.04f && bu < wx1 + 0.04f && fv > wy0 - 0.045f && fv <= wy0 - frameW * 1.5f;

                    if (sill) { c = st.Trim; height = 0.75f; }
                    else if (frame) { c = st.Frame; height = 0.42f; smooth = 0.35f; }
                    else if (inWin)
                    {
                        bool mull = st.Mullion && (Mathf.Abs(bu - 0.5f) < 0.012f || Mathf.Abs(fv - (wy0 + st.WindowHeight * 0.68f)) < 0.012f);
                        if (mull) { c = st.Frame; height = 0.3f; smooth = 0.35f; }
                        else
                        {
                            // Glass: sky reflection gradient + a hint of interior.
                            float g = (fv - wy0) / st.WindowHeight;
                            c = Color.Lerp(st.Glass * 0.75f, st.Glass * 1.6f + new Color(0.05f, 0.07f, 0.09f), g * 0.8f + n * 0.2f);
                            height = 0.2f; smooth = st.NoLights ? 0.05f : 0.92f;
                            // Night: ~45% of windows lit, warm with slight variation; blinds darken some.
                            float lit = Hash(bay, floor, seed + 99);
                            if (lit < 0.45f && !st.NoLights)
                            {
                                float warm = Hash(bay, floor, seed + 7);
                                e = Color.Lerp(new Color(1f, 0.78f, 0.48f), new Color(1f, 0.92f, 0.78f), warm) * (0.55f + 0.45f * (1 - g * 0.5f));
                            }
                        }
                    }
                    int i = y * W + x;
                    px[i] = c; ep[i] = e; h[i] = height; sm[i] = smooth;
                }
            Finish(albedo, px);
            Finish(emis, ep);
            set = new Set { Albedo = albedo, Normal = NormalFromHeight(h, W, 6f, key), Mask = MaskFromSmoothness(sm, 0f, W, key), Emission = emis };
            Cache[key] = set;
            return set;
        }

        /// <summary>Plain wall (no windows) in a facade style, 4 m tile.</summary>
        public static Set PlainWall(int styleIndex)
        {
            var st = Styles[Mathf.Clamp(styleIndex, 0, Styles.Length - 1)];
            string key = "plain_" + st.Name;
            if (Cache.TryGetValue(key, out var set)) return set;
            const int N = 256;
            var t = NewTex(N, false, key);
            var px = new Color32[N * N]; var h = new float[N * N]; var sm = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = Fbm(x / (float)N, y / (float)N, 8, 3, styleIndex + 900);
                    px[y * N + x] = Color.Lerp(st.Wall, st.WallAlt, n * 0.7f);
                    h[y * N + x] = n; sm[y * N + x] = 0.1f;
                }
            Finish(t, px);
            set = new Set { Albedo = t, Normal = NormalFromHeight(h, N, 1.5f, key), Mask = MaskFromSmoothness(sm, 0, N, key), Emission = Black };
            Cache[key] = set;
            return set;
        }

        /// <summary>Clay barrel-tile roof (tile = 2 m), terracotta.</summary>
        public static Set RoofTile()
        {
            const string key = "rooftile";
            if (Cache.TryGetValue(key, out var set)) return set;
            const int N = 256;
            var t = NewTex(N, false, key);
            var px = new Color32[N * N]; var h = new float[N * N]; var sm = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N * 9f, v = y / (float)N * 6f;
                    int col = Mathf.FloorToInt(u), row = Mathf.FloorToInt(v);
                    float fu = u - col, fv = v - row;
                    float barrel = Mathf.Sin(fu * Mathf.PI);
                    float tone = Hash(col, row, 3);
                    var c = Color.Lerp(new Color(0.62f, 0.28f, 0.17f), new Color(0.76f, 0.40f, 0.24f), tone) * (0.72f + 0.35f * barrel);
                    if (fv > 0.9f) c *= 0.75f; // overlap shadow
                    px[y * N + x] = c; h[y * N + x] = barrel * 0.9f - (fv > 0.9f ? 0.3f : 0); sm[y * N + x] = 0.3f;
                }
            Finish(t, px);
            set = new Set { Albedo = t, Normal = NormalFromHeight(h, N, 4f, key), Mask = MaskFromSmoothness(sm, 0, N, key), Emission = Black };
            Cache[key] = set;
            return set;
        }

        /// <summary>Flat roof membrane / gravel, light grey.</summary>
        public static Set FlatRoof()
        {
            const string key = "flatroof";
            if (Cache.TryGetValue(key, out var set)) return set;
            const int N = 256;
            var t = NewTex(N, false, key);
            var px = new Color32[N * N]; var h = new float[N * N]; var sm = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = Fbm(x / (float)N, y / (float)N, 6, 3, 41), g = Hash(x, y, 42);
                    px[y * N + x] = Color.Lerp(new Color(0.62f, 0.62f, 0.60f), new Color(0.76f, 0.75f, 0.72f), n * 0.5f + g * 0.5f);
                    h[y * N + x] = g; sm[y * N + x] = 0.15f;
                }
            Finish(t, px);
            set = new Set { Albedo = t, Normal = NormalFromHeight(h, N, 1.2f, key), Mask = MaskFromSmoothness(sm, 0, N, key), Emission = Black };
            Cache[key] = set;
            return set;
        }

        /// <summary>Glass entrance doors (one 2.4 m × 2.7 m pair per tile).</summary>
        public static Set Doors()
        {
            const string key = "doors";
            if (Cache.TryGetValue(key, out var set)) return set;
            const int N = 256;
            var t = NewTex(N, false, key); var e = NewTex(N, false, key + "_E");
            var px = new Color32[N * N]; var ep = new Color32[N * N]; var h = new float[N * N]; var sm = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    bool frame = u < 0.05f || u > 0.95f || v > 0.93f || Mathf.Abs(u - 0.5f) < 0.02f || v < 0.04f || Mathf.Abs(v - 0.45f) < 0.015f;
                    bool handle = Mathf.Abs(u - 0.45f) < 0.01f && v > 0.38f && v < 0.52f || Mathf.Abs(u - 0.55f) < 0.01f && v > 0.38f && v < 0.52f;
                    Color c = frame || handle ? new Color(0.22f, 0.20f, 0.18f) : Color.Lerp(new Color(0.10f, 0.15f, 0.19f), new Color(0.30f, 0.38f, 0.44f), v);
                    px[y * N + x] = c;
                    ep[y * N + x] = frame || handle ? Color.black : new Color(1f, 0.85f, 0.6f) * 0.9f;
                    h[y * N + x] = frame ? 0.6f : 0.2f; sm[y * N + x] = frame ? 0.4f : 0.9f;
                }
            Finish(t, px); Finish(e, ep);
            set = new Set { Albedo = t, Normal = NormalFromHeight(h, N, 4f, key), Mask = MaskFromSmoothness(sm, 0.3f, N, key), Emission = e };
            Cache[key] = set;
            return set;
        }

        /// <summary>Simple non-tiled solid-colour set (props, trim, bark, foliage).</summary>
        public static Set Solid(string key, Color color, float smoothness, float metallic = 0f, float noise = 0.12f, Color? emission = null)
        {
            key = "solid_" + key;
            if (Cache.TryGetValue(key, out var set)) return set;
            const int N = 64;
            var t = NewTex(N, false, key);
            var px = new Color32[N * N]; var h = new float[N * N]; var sm = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = Fbm(x / (float)N, y / (float)N, 4, 3, key.GetHashCode() & 0xFFFF);
                    px[y * N + x] = color * (1f - noise + 2f * noise * n);
                    h[y * N + x] = n; sm[y * N + x] = smoothness;
                }
            Finish(t, px);
            var e = emission.HasValue ? Flat(emission.Value, key + "_E", false) : Black;
            set = new Set { Albedo = t, Normal = NormalFromHeight(h, N, 1f, key), Mask = MaskFromSmoothness(sm, metallic, N, key), Emission = e };
            Cache[key] = set;
            return set;
        }
    }
}
