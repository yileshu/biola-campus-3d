using System;
using BiolaCampus.Core;
using BiolaCampus.Osm;
using CesiumForUnity;
using UnityEngine;

namespace BiolaCampus.World
{
    /// <summary>
    /// Owns the CesiumGeoreference (origin = campus centre from OSM) and the Google Photorealistic
    /// 3D Tiles tileset. Tiles are streamed only — nothing is cached to disk or exported.
    /// Falls back to an OSM-extruded offline campus when there is no key or the tileset fails.
    /// </summary>
    public class CampusWorld : MonoBehaviour
    {
        public const string GoogleTilesRootUrl = "https://tile.googleapis.com/v1/3dtiles/root.json";

        public CesiumGeoreference Georeference { get; private set; }
        public Cesium3DTileset Tileset { get; private set; }
        public OfflineCampus Offline { get; private set; }
        public CampusMap Map { get; private set; }

        public bool IsOffline => Offline != null;
        public string StatusMessage { get; private set; }
        public event Action<string> WentOffline;

        public void Initialize(CampusMap map)
        {
            Map = map;

            var geoGo = new GameObject("CesiumGeoreference (Biola)");
            geoGo.transform.SetParent(transform, false);
            Georeference = geoGo.AddComponent<CesiumGeoreference>();
            Georeference.SetOriginLongitudeLatitudeHeight(map.Frame.OriginLon, map.Frame.OriginLat, map.Frame.OriginHeight);

            GameSettings.Changed += OnSettingsChanged;
            ApplyQuality(GameSettings.Quality);

            if (!SecretsLoader.HasGoogleKey)
            {
                GoOffline("No Google Map Tiles API key found (Assets/Config/secrets.json). Showing the offline OSM campus.");
                return;
            }

            Cesium3DTileset.OnCesium3DTilesetLoadFailure += HandleLoadFailure;

            var tsGo = new GameObject("Google Photorealistic 3D Tiles");
            tsGo.SetActive(false);                        // configure before the tileset starts loading
            tsGo.transform.SetParent(geoGo.transform, false);
            Tileset = tsGo.AddComponent<Cesium3DTileset>();
            Tileset.tilesetSource = CesiumDataSource.FromUrl;
            Tileset.url = GoogleTilesRootUrl + "?key=" + SecretsLoader.GoogleMapsApiKey;
            Tileset.showCreditsOnScreen = true;            // Google + data-provider attributions
            Tileset.createPhysicsMeshes = true;            // MeshColliders so the player can walk on tiles
            Tileset.maximumSimultaneousTileLoads = 20;
            Tileset.preloadAncestors = true;
            Tileset.preloadSiblings = true;
            Tileset.forbidHoles = false;
            Tileset.enableFrustumCulling = true;
            Tileset.enableFogCulling = true;
            ApplyQuality(GameSettings.Quality);
            tsGo.SetActive(true);
            StatusMessage = "Streaming Google Photorealistic 3D Tiles…";
        }

        void OnDestroy()
        {
            Cesium3DTileset.OnCesium3DTilesetLoadFailure -= HandleLoadFailure;
            GameSettings.Changed -= OnSettingsChanged;
        }

        TileQuality? _appliedQuality;

        void OnSettingsChanged()
        {
            if (_appliedQuality != GameSettings.Quality) ApplyQuality(GameSettings.Quality);
        }

        public void ApplyQuality(TileQuality q)
        {
            _appliedQuality = q;
            GameSettings.ApplyUnityQuality(q);
            if (Tileset == null) return;
            Tileset.maximumScreenSpaceError = GameSettings.ScreenSpaceError(q);
            Tileset.maximumCachedBytes = GameSettings.TileCacheBytes(q);   // in-memory cache only
        }

        /// <summary>0..100, or 100 when offline.</summary>
        public float LoadProgress
        {
            get
            {
                if (Tileset == null || !Tileset.isActiveAndEnabled) return 100f;
                try { return Tileset.ComputeLoadProgress(); } catch { return 0f; }
            }
        }

        void HandleLoadFailure(Cesium3DTilesetLoadFailureDetails details)
        {
            if (details.tileset != Tileset) return;
            string msg = SecretsLoader.Redact(details.message ?? "unknown error");
            Debug.LogWarning($"[Biola] Google 3D Tiles failed to load (HTTP {details.httpStatusCode}): {msg}");
            string hint = details.httpStatusCode == 403
                ? "The API key was rejected (check Map Tiles API is enabled and the website restriction matches this site)."
                : details.httpStatusCode == 429 ? "Daily Map Tiles quota exceeded." : msg;
            GoOffline("Google 3D Tiles unavailable — " + hint + " Showing the offline OSM campus.");
        }

        public void GoOffline(string reason)
        {
            if (Offline != null) return;
            StatusMessage = reason;
            if (Tileset != null) Tileset.gameObject.SetActive(false);
            var go = new GameObject("Offline OSM Campus");
            go.transform.SetParent(transform, false);
            Offline = go.AddComponent<OfflineCampus>();
            Offline.Build(Map);
            WentOffline?.Invoke(reason);
        }
    }
}
