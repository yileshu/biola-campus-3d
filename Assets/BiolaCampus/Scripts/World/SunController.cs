using System;
using BiolaCampus.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BiolaCampus.World
{
    /// <summary>
    /// Real sun for La Mirada, CA at a chosen local time (default 15:30 Pacific, 15 April) plus a
    /// night mode (N) with a moon light, dark sky and an event that turns on street lamps and lit windows.
    /// Drives the directional light, ambient light, the procedural sky and distance fog.
    /// URL overrides: <c>time=HH:MM</c>, <c>date=YYYY-MM-DD</c>, <c>night=1</c>.
    /// </summary>
    public class SunController : MonoBehaviour
    {
        public static SunController Instance { get; private set; }
        public static event Action<bool> NightChanged;

        public Light Sun { get; private set; }
        public bool IsNight { get; private set; }
        public DateTime LocalTime { get; private set; }
        public double Elevation { get; private set; }
        public double Azimuth { get; private set; }

        Material _sky;
        static readonly DateTime DefaultLocal = new DateTime(2026, 4, 15, 15, 30, 0);

        public void Initialize()
        {
            Instance = this;
            var go = new GameObject("Sun (La Mirada)");
            go.transform.SetParent(transform, false);
            Sun = go.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.85f;
            Sun.shadowBias = 0.05f;
            Sun.shadowNormalBias = 0.4f;
            RenderSettings.sun = Sun;

            _sky = Resources.Load<Material>("BiolaMaterials/Sky");
            if (_sky != null) _sky = new Material(_sky);
            else
            {
                var sh = Shader.Find("Skybox/Procedural");
                if (sh != null) _sky = new Material(sh);
            }
            if (_sky != null) RenderSettings.skybox = _sky;

            LocalTime = DefaultLocal;
            string t = ScreenshotTour.Query("time"), d = ScreenshotTour.Query("date");
            if (!string.IsNullOrEmpty(d) && DateTime.TryParse(d, out var date)) LocalTime = date.Date + LocalTime.TimeOfDay;
            if (!string.IsNullOrEmpty(t) && TimeSpan.TryParse(t, out var tod)) LocalTime = LocalTime.Date + tod;
            IsNight = ScreenshotTour.Query("night") == "1";
            Apply();
        }

        public void ToggleNight() => SetNight(!IsNight);

        public void SetNight(bool night)
        {
            if (IsNight == night) return;
            IsNight = night;
            Apply();
        }

        public void SetLocalTime(DateTime local)
        {
            LocalTime = local;
            Apply();
        }

        void Update()
        {
            if (InputShim.Pressed(KeyCode.N)) ToggleNight();
        }

        void Apply()
        {
            SolarPosition.Compute(SolarPosition.PacificToUtc(LocalTime), SolarPosition.LaMiradaLat, SolarPosition.LaMiradaLon,
                out double el, out double az);
            Elevation = el; Azimuth = az;

            if (IsNight)
            {
                // Moon-ish key light: high in the opposite sky, cool and dim; shadows kept soft.
                Vector3 moon = SolarPosition.Direction(52, (az + 180) % 360);
                Sun.transform.rotation = Quaternion.LookRotation(-moon);
                Sun.color = new Color(0.62f, 0.72f, 1f);
                Sun.intensity = 0.16f;
                Sun.shadowStrength = 0.55f;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.07f, 0.09f, 0.16f);
                RenderSettings.ambientEquatorColor = new Color(0.05f, 0.06f, 0.09f);
                RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.04f);
                RenderSettings.fogColor = new Color(0.05f, 0.07f, 0.12f);
                SetSky(exposure: 0.06f, tint: new Color(0.25f, 0.3f, 0.5f), thickness: 0.6f, sunSize: 0.0f);
            }
            else
            {
                Vector3 dir = SolarPosition.Direction(Math.Max(el, 2.0), az);
                Sun.transform.rotation = Quaternion.LookRotation(-dir);
                float e01 = Mathf.Clamp01((float)el / 50f);
                // Warmer and dimmer near the horizon.
                Sun.color = Color.Lerp(new Color(1f, 0.72f, 0.48f), new Color(1f, 0.96f, 0.90f), Mathf.SmoothStep(0, 1, e01));
                Sun.intensity = Mathf.Lerp(0.55f, 1.35f, Mathf.SmoothStep(0, 1, e01));
                Sun.shadowStrength = 0.85f;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.55f, 0.66f, 0.82f);
                RenderSettings.ambientEquatorColor = new Color(0.58f, 0.58f, 0.55f);
                RenderSettings.ambientGroundColor = new Color(0.32f, 0.29f, 0.24f);
                RenderSettings.fogColor = Color.Lerp(new Color(0.86f, 0.74f, 0.62f), new Color(0.72f, 0.80f, 0.90f), e01);
                SetSky(exposure: 1.25f, tint: new Color(0.5f, 0.5f, 0.5f), thickness: 1.0f, sunSize: 0.035f);
            }
            RenderSettings.ambientIntensity = 1f;
            NightChanged?.Invoke(IsNight);
        }

        void SetSky(float exposure, Color tint, float thickness, float sunSize)
        {
            if (_sky == null) return;
            if (_sky.HasProperty("_Exposure")) _sky.SetFloat("_Exposure", exposure);
            if (_sky.HasProperty("_SkyTint")) _sky.SetColor("_SkyTint", tint);
            if (_sky.HasProperty("_AtmosphereThickness")) _sky.SetFloat("_AtmosphereThickness", thickness);
            if (_sky.HasProperty("_SunSize")) _sky.SetFloat("_SunSize", sunSize);
            if (_sky.HasProperty("_SunSizeConvergence")) _sky.SetFloat("_SunSizeConvergence", 6f);
        }
    }
}
