using System;
using UnityEngine;

namespace BiolaCampus.Core
{
    public enum TileQuality { Low = 0, Medium = 1, High = 2 }

    /// <summary>Persistent user settings (PlayerPrefs → IndexedDB on the Web).</summary>
    public static class GameSettings
    {
        const string KSens = "biola.mouseSensitivity", KInvert = "biola.invertY", KQuality = "biola.quality",
                     KFov = "biola.fov", KLabels = "biola.labels";

        public static event Action Changed;

        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(KSens, 1.0f);
            set { PlayerPrefs.SetFloat(KSens, Mathf.Clamp(value, 0.1f, 5f)); Save(); }
        }

        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(KInvert, 0) == 1;
            set { PlayerPrefs.SetInt(KInvert, value ? 1 : 0); Save(); }
        }

        public static TileQuality Quality
        {
            get => (TileQuality)Mathf.Clamp(PlayerPrefs.GetInt(KQuality, (int)TileQuality.Medium), 0, 2);
            set { PlayerPrefs.SetInt(KQuality, (int)value); Save(); }
        }

        public static float FieldOfView
        {
            get => PlayerPrefs.GetFloat(KFov, 70f);
            set { PlayerPrefs.SetFloat(KFov, Mathf.Clamp(value, 50f, 100f)); Save(); }
        }

        public static bool ShowFloatingLabels
        {
            get => PlayerPrefs.GetInt(KLabels, 1) == 1;
            set { PlayerPrefs.SetInt(KLabels, value ? 1 : 0); Save(); }
        }

        static void Save()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Screen-space error (lower = sharper, more downloads) per quality level.</summary>
        public static float ScreenSpaceError(TileQuality q) => q == TileQuality.Low ? 32f : q == TileQuality.Medium ? 16f : 8f;

        public static long TileCacheBytes(TileQuality q) =>
            q == TileQuality.Low ? 256L * 1024 * 1024 : q == TileQuality.Medium ? 512L * 1024 * 1024 : 1024L * 1024 * 1024;

        /// <summary>Applies the matching Unity quality level (by name when available).</summary>
        public static void ApplyUnityQuality(TileQuality q)
        {
            string[] names = QualitySettings.names;
            if (names == null || names.Length == 0) return;
            string wanted = q == TileQuality.Low ? "Low" : q == TileQuality.Medium ? "Medium" : "High";
            int idx = Array.IndexOf(names, wanted);
            if (idx < 0) idx = Mathf.Clamp(Mathf.RoundToInt((names.Length - 1) * ((int)q / 2f)), 0, names.Length - 1);
            QualitySettings.SetQualityLevel(idx, true);
            QualitySettings.shadows = q == TileQuality.High ? ShadowQuality.HardOnly : ShadowQuality.Disable;
            QualitySettings.vSyncCount = 0;
        }
    }
}
