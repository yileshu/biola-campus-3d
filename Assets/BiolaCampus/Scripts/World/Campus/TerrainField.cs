using System;
using UnityEngine;

namespace BiolaCampus.World.Campus
{
    /// <summary>
    /// Bare-earth height grid built by Tools/lidar/fetch_lidar.py from USGS 3DEP lidar (public domain).
    /// Heights are Unity Y (metres relative to the CesiumGeoreference origin, which sits on the ground
    /// at the campus centre). Grid axes are local east (x) / north (z).
    /// </summary>
    public class TerrainField
    {
        [Serializable]
        public class Meta
        {
            public string source, license, generatedUtc, verticalDatum, geoidSource;
            public double geoidUndulation, originLat, originLon, originEllipsoidHeight, originOrthometricHeight;
            public float cell, x0, z0, groundCoverage;
            public int nx, ny;
        }

        [Serializable] class FileFormat { public Meta meta; public string heights; }

        public const string ResourcePath = "Terrain/biola_terrain";

        public Meta Info { get; private set; }
        public int Nx => Info.nx;
        public int Nz => Info.ny;
        public float Cell => Info.cell;
        public float X0 => Info.x0;
        public float Z0 => Info.z0;
        float[] _h;

        public static TerrainField Load()
        {
            var ta = Resources.Load<TextAsset>(ResourcePath);
            if (ta == null) return null;
            try
            {
                var f = JsonUtility.FromJson<FileFormat>(ta.text);
                byte[] raw = Convert.FromBase64String(f.heights);
                int n = f.meta.nx * f.meta.ny;
                if (raw.Length < n * 2) throw new Exception("height grid truncated");
                var h = new float[n];
                for (int i = 0; i < n; i++) h[i] = (short)(raw[2 * i] | (raw[2 * i + 1] << 8)) * 0.01f;
                return new TerrainField { Info = f.meta, _h = h };
            }
            catch (Exception e)
            {
                Debug.LogError("[Biola] Could not read lidar terrain: " + e.Message);
                return null;
            }
        }

        /// <summary>Height at grid node (i, j), clamped to the grid.</summary>
        public float Node(int i, int j)
        {
            i = Mathf.Clamp(i, 0, Info.nx - 1);
            j = Mathf.Clamp(j, 0, Info.ny - 1);
            return _h[j * Info.nx + i];
        }

        /// <summary>Bilinear height at a local (east, north) position. Cell values are cell-centred.</summary>
        public float Height(float x, float z)
        {
            float fx = (x - Info.x0) / Info.cell - 0.5f, fz = (z - Info.z0) / Info.cell - 0.5f;
            int i = Mathf.FloorToInt(fx), j = Mathf.FloorToInt(fz);
            float tx = fx - i, tz = fz - j;
            float a = Node(i, j), b = Node(i + 1, j), c = Node(i, j + 1), d = Node(i + 1, j + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        public float Height(Vector2 p) => Height(p.x, p.y);

        public Vector3 Normal(float x, float z)
        {
            float e = Info.cell;
            float hx = Height(x + e, z) - Height(x - e, z), hz = Height(x, z + e) - Height(x, z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }
    }

    /// <summary>Per-building lidar measurements and detected trees (Assets/Resources/OSM/biola_lidar.json).</summary>
    public class LidarData
    {
        [Serializable]
        public class Building
        {
            public string id, name, roof;
            public float ground, height, eave, ridge;
            public int points;
            public bool Measured => !string.IsNullOrEmpty(roof);
        }

        [Serializable] public class Tree { public float x, z, h, r; public string kind; }
        [Serializable] class FileFormat { public Building[] buildings; public Tree[] trees; }

        public readonly System.Collections.Generic.Dictionary<string, Building> ById = new System.Collections.Generic.Dictionary<string, Building>();
        public Tree[] Trees = Array.Empty<Tree>();

        public static LidarData Load()
        {
            var ta = Resources.Load<TextAsset>("OSM/biola_lidar");
            if (ta == null) return null;
            try
            {
                var f = JsonUtility.FromJson<FileFormat>(ta.text);
                var d = new LidarData { Trees = f.trees ?? Array.Empty<Tree>() };
                if (f.buildings != null)
                    foreach (var b in f.buildings)
                        if (!string.IsNullOrEmpty(b.id) && !d.ById.ContainsKey(b.id)) d.ById[b.id] = b;
                return d;
            }
            catch (Exception e)
            {
                Debug.LogError("[Biola] Could not read lidar building data: " + e.Message);
                return null;
            }
        }
    }
}
