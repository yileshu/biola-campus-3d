using BiolaCampus.Core;
using BiolaCampus.Osm;
using BiolaCampus.World;
using CesiumForUnity;
using UnityEditor;
using UnityEngine;

namespace BiolaCampus.EditorTools
{
    /// <summary>
    /// Optional: stream the Google tiles into the Scene view while editing. The preview objects use
    /// HideFlags.DontSave so the tileset URL (which contains the API key) is never written to a scene.
    /// </summary>
    public static class EditorTilesPreview
    {
        const string RootName = "[Preview] Biola Georeference (not saved)";

        [MenuItem("Biola/Preview Google 3D Tiles in Scene View", priority = 60)]
        static void Preview()
        {
            Remove();
            if (!SecretsLoader.HasGoogleKey)
            {
                EditorUtility.DisplayDialog("Biola", "No API key in Assets/Config/secrets.json (copy secrets.example.json).", "OK");
                return;
            }
            var map = CampusMap.Load();
            var root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            var geo = root.AddComponent<CesiumGeoreference>();
            geo.SetOriginLongitudeLatitudeHeight(map.Frame.OriginLon, map.Frame.OriginLat, map.Frame.OriginHeight);
            var tsGo = new GameObject("Google Photorealistic 3D Tiles") { hideFlags = HideFlags.DontSave };
            tsGo.SetActive(false);
            tsGo.transform.SetParent(root.transform, false);
            var ts = tsGo.AddComponent<Cesium3DTileset>();
            ts.tilesetSource = CesiumDataSource.FromUrl;
            ts.url = CampusWorld.GoogleTilesRootUrl + "?key=" + SecretsLoader.GoogleMapsApiKey;
            ts.showCreditsOnScreen = true;
            ts.createPhysicsMeshes = false;
            tsGo.SetActive(true);

            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.LookAt(Vector3.zero, Quaternion.Euler(35, 0, 0), 600f);
        }

        [MenuItem("Biola/Remove Scene View Tiles Preview", priority = 61)]
        static void Remove()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);
        }
    }
}
