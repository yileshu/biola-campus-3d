using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BiolaCampus.World.Campus
{
    /// <summary>
    /// PBR materials for our own campus models. Works with URP (Universal Render Pipeline/Lit) and the
    /// built-in pipeline (Standard). Every material enables the same keyword set (normal map,
    /// metallic/smoothness map, emission) so a single shader variant — compiled into the build by the
    /// template materials that Biola ▸ Setup Project creates under Resources/BiolaMaterials — covers all.
    /// </summary>
    public static class CampusMaterials
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static readonly List<Material> Emissive = new List<Material>();
        static readonly Dictionary<Material, Color> NightEmission = new Dictionary<Material, Color>();

        public static bool IsUrp => GraphicsSettings.currentRenderPipeline != null;

        static Material Template(bool instanced)
        {
            string name = (IsUrp ? "UrpLit" : "StdLit") + (instanced ? "Instanced" : "") + "Template";
            var t = Resources.Load<Material>("BiolaMaterials/" + name);
            if (t != null) return t;
            var sh = Shader.Find(IsUrp ? "Universal Render Pipeline/Lit" : "Standard");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            if (instanced) m.enableInstancing = true;
            return m;
        }

        /// <summary>
        /// Lit material from a texture set. <paramref name="tiling"/> is texture repeats per UV unit.
        /// <paramref name="nightEmission"/> is the emission colour applied in night mode (black = none).
        /// </summary>
        public static Material Lit(string key, ProceduralTextures.Set set, Color tint, Vector2 tiling,
            float smoothnessScale = 1f, Color? nightEmission = null, bool instanced = false, bool doubleSided = false)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var m = new Material(Template(instanced)) { name = key };
            if (instanced) m.enableInstancing = true;
            bool urp = m.HasProperty("_BaseMap");

            if (urp)
            {
                m.SetTexture("_BaseMap", set.Albedo);
                m.SetTextureScale("_BaseMap", tiling);
                m.SetColor("_BaseColor", tint);
                m.SetTexture("_MetallicGlossMap", set.Mask);
                m.SetFloat("_Smoothness", Mathf.Clamp01(smoothnessScale));
                m.SetFloat("_Metallic", 1f);
                m.SetFloat("_WorkflowMode", 1f);
                if (doubleSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            }
            else
            {
                m.SetTexture("_MainTex", set.Albedo);
                m.SetTextureScale("_MainTex", tiling);
                m.SetColor("_Color", tint);
                m.SetTexture("_MetallicGlossMap", set.Mask);
                m.SetFloat("_GlossMapScale", Mathf.Clamp01(smoothnessScale));
            }
            m.SetTexture("_BumpMap", set.Normal);
            m.SetFloat("_BumpScale", 1f);
            m.SetTexture("_EmissionMap", set.Emission);
            m.SetColor("_EmissionColor", Color.black);
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");   // URP
            m.EnableKeyword("_METALLICGLOSSMAP");       // Standard
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            if (nightEmission.HasValue && nightEmission.Value.maxColorComponent > 0)
            {
                Emissive.Add(m);
                NightEmission[m] = nightEmission.Value;
                if (SunController.Instance != null && SunController.Instance.IsNight) m.SetColor("_EmissionColor", nightEmission.Value);
            }
            Cache[key] = m;
            return m;
        }

        /// <summary>Switch window/lamp emission on (night) or off (day).</summary>
        public static void SetNight(bool night)
        {
            foreach (var m in Emissive)
                if (m != null) m.SetColor("_EmissionColor", night ? NightEmission[m] : Color.black);
        }

        static bool _subscribed;
        public static void HookNight()
        {
            if (_subscribed) return;
            _subscribed = true;
            SunController.NightChanged += SetNight;
        }
    }
}
