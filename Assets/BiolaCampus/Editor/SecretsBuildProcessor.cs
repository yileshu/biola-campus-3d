using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BiolaCampus.EditorTools
{
    /// <summary>
    /// Copies the Google key from Assets/Config/secrets.json (or the GOOGLE_MAPS_API_KEY environment
    /// variable) into Assets/Resources/Generated/BiolaSecrets.json for the duration of a player build,
    /// then deletes it. Both paths are git-ignored. The key itself is never logged.
    /// </summary>
    public class SecretsBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string SourcePath = "Assets/Config/secrets.json";
        public const string GeneratedDir = "Assets/Resources/Generated";
        public const string GeneratedPath = GeneratedDir + "/BiolaSecrets.json";

        public int callbackOrder => -1000;

        [Serializable] class SecretsFile { public string googleMapsApiKey; }

        public void OnPreprocessBuild(BuildReport report) => Stage();
        public void OnPostprocessBuild(BuildReport report) => Cleanup();

        public static void Stage()
        {
            string key = null;
            if (File.Exists(SourcePath))
            {
                try { key = JsonUtility.FromJson<SecretsFile>(File.ReadAllText(SourcePath))?.googleMapsApiKey; } catch { }
            }
            if (string.IsNullOrWhiteSpace(key) || key.StartsWith("YOUR_", StringComparison.Ordinal))
                key = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY");

            Directory.CreateDirectory(GeneratedDir);
            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogWarning("[Biola] No Google Map Tiles API key found — the build will run in offline OSM mode. " +
                                 "Create Assets/Config/secrets.json (see secrets.example.json) or set GOOGLE_MAPS_API_KEY.");
                if (File.Exists(GeneratedPath)) File.Delete(GeneratedPath);
            }
            else
            {
                File.WriteAllText(GeneratedPath, JsonUtility.ToJson(new SecretsFile { googleMapsApiKey = key.Trim() }));
                Debug.Log("[Biola] Google Map Tiles API key staged for the build (value not shown).");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public static void Cleanup()
        {
            try
            {
                if (File.Exists(GeneratedPath)) File.Delete(GeneratedPath);
                if (File.Exists(GeneratedPath + ".meta")) File.Delete(GeneratedPath + ".meta");
                AssetDatabase.Refresh();
            }
            catch (Exception e) { Debug.LogWarning("[Biola] Could not remove staged secrets: " + e.GetType().Name); }
        }
    }
}
