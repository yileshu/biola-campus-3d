using System;
using System.IO;
using UnityEngine;

namespace BiolaCampus.Core
{
    /// <summary>
    /// Loads the Google Map Tiles API key.
    ///  * Editor: reads Assets/Config/secrets.json directly (git-ignored).
    ///  * Player builds: SecretsBuildProcessor copies it to Assets/Resources/Generated/BiolaSecrets.json
    ///    (also git-ignored) just before the build and deletes it afterwards.
    /// The key is never logged. Note that any key used by a WebGL client is visible to that client, so
    /// restrict it to the Map Tiles API and to your website's referrer (see README).
    /// </summary>
    public static class SecretsLoader
    {
        public const string EditorPath = "Config/secrets.json";        // relative to Application.dataPath
        public const string ResourcePath = "Generated/BiolaSecrets";   // Resources path (no extension)

        [Serializable]
        class SecretsFile { public string googleMapsApiKey; }

        static string _cached;
        static bool _loaded;

        public static string GoogleMapsApiKey
        {
            get
            {
                if (!_loaded) { _cached = Load(); _loaded = true; }
                return _cached;
            }
        }

        public static bool HasGoogleKey => !string.IsNullOrEmpty(GoogleMapsApiKey);

        static string Load()
        {
            string json = null;
#if UNITY_EDITOR
            try
            {
                string path = Path.Combine(Application.dataPath, EditorPath);
                if (File.Exists(path)) json = File.ReadAllText(path);
            }
            catch (Exception e) { Debug.LogWarning("[Biola] Could not read secrets.json: " + e.GetType().Name); }
#endif
            if (json == null)
            {
                var ta = Resources.Load<TextAsset>(ResourcePath);
                if (ta != null) json = ta.text;
            }
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var key = JsonUtility.FromJson<SecretsFile>(json)?.googleMapsApiKey;
                if (string.IsNullOrWhiteSpace(key) || key.StartsWith("YOUR_", StringComparison.Ordinal)) return null;
                return key.Trim();
            }
            catch { return null; }
        }

        /// <summary>Replace the key in any text before it is shown or logged.</summary>
        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var k = GoogleMapsApiKey;
            if (!string.IsNullOrEmpty(k)) text = text.Replace(k, "***");
            return System.Text.RegularExpressions.Regex.Replace(text, "key=[A-Za-z0-9_\\-]{20,}", "key=***");
        }
    }
}
