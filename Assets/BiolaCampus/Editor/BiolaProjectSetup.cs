using System;
using System.IO;
using System.Linq;
using BiolaCampus.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BiolaCampus.EditorTools
{
    /// <summary>
    /// One-click (and automatic, on first open) project setup:
    ///  * creates Assets/Scenes/Main.unity containing only the GameBootstrap object,
    ///  * creates the runtime materials under Assets/Resources/BiolaMaterials,
    ///  * applies the Web player settings Cesium for Unity needs.
    /// Everything else (georeference, tileset, player, UI) is built at runtime so the API key
    /// is never serialized into a scene.
    /// </summary>
    [InitializeOnLoad]
    public static class BiolaProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string MaterialsDir = "Assets/Resources/BiolaMaterials";
        public const string TemplateName = "PROJECT:Biola";

        static BiolaProjectSetup()
        {
            if (Application.isBatchMode) return; // CI calls RunAll() explicitly from BuildScript
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(ScenePath) || !File.Exists(MaterialsDir + "/StdLitTemplate.mat"))
                {
                    Debug.Log("[Biola] First open detected — running Biola ▸ Setup Project.");
                    RunAll(openScene: true);
                }
            };
        }

        [MenuItem("Biola/Setup Project (scene, materials, Web settings)", priority = 0)]
        public static void RunAllMenu() => RunAll(openScene: true);

        public static void RunAll(bool openScene)
        {
            EnsureMaterials();
            EnsureMainScene(openScene);
            ApplyWebPlayerSettings(webGpu: false);
            AssetDatabase.SaveAssets();
            Debug.Log("[Biola] Project setup complete.");
        }

        // ------------------------------------------------------------------------- scene

        public static void EnsureMainScene(bool openScene)
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cam = new GameObject("Main Camera") { tag = "MainCamera" };
                cam.AddComponent<Camera>();
                cam.AddComponent<AudioListener>();
                new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log("[Biola] Created " + ScenePath);
            }
            else if (openScene && !Application.isBatchMode && SceneManager.GetActiveScene().path != ScenePath)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(ScenePath);
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }

        // --------------------------------------------------------------------- materials

        public static void EnsureMaterials()
        {
            Directory.CreateDirectory(MaterialsDir);
            MakeMaterial("Ground", "Standard", new Color(0.42f, 0.55f, 0.33f));
            MakeMaterial("Paving", "Standard", new Color(0.72f, 0.70f, 0.66f));
            MakeMaterial("Building", "Standard", new Color(0.78f, 0.78f, 0.78f));
            MakeMaterial("BuildingNamed", "Standard", new Color(0.86f, 0.82f, 0.74f));
            foreach (var k in new[] { "parking", "pitch", "grass", "park" })
                MakeMaterial("Area_" + k, "Standard", Color.gray);
            // Lit templates for runtime-generated campus materials (CampusMaterials). They enable the exact
            // keyword set used at runtime so those shader variants are compiled into the player.
            MakeLitTemplate("StdLitTemplate", "Standard", false);
            MakeLitTemplate("StdLitInstancedTemplate", "Standard", true);
            var sky = MakeMaterial("Sky", "Skybox/Procedural", Color.white);
            if (sky != null)
            {
                if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", 0.04f);
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", 0.9f);
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.2f);
                EditorUtility.SetDirty(sky);
            }
        }

        public static Material MakeLitTemplate(string name, string shaderName, bool instanced)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) { Debug.LogWarning("[Biola] Shader not found: " + shaderName); return null; }
                m = new Material(shader) { name = name };
            }
            // Dummy textures make the keywords meaningful; runtime materials replace them.
            m.SetTexture("_BumpMap", Texture2D.normalTexture);
            m.SetTexture("_MetallicGlossMap", Texture2D.whiteTexture);
            m.SetTexture("_EmissionMap", Texture2D.blackTexture);
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.EnableKeyword("_METALLICGLOSSMAP");
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = instanced;
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        static Material MakeMaterial(string name, string shaderName, Color color)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogWarning("[Biola] Shader not found: " + shaderName); return null; }
            var m = new Material(shader) { name = name };
            if (m.HasProperty("_Color")) m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---------------------------------------------------------------- player settings

        /// <summary>
        /// Web settings for Cesium for Unity: native C/C++ multithreading on, a raised memory ceiling
        /// (tiles are decoded into WASM memory), Brotli with decompression fallback for small downloads
        /// on any static host, WebGL 2 by default and optional WebGPU.
        /// </summary>
        public static void ApplyWebPlayerSettings(bool webGpu)
        {
            PlayerSettings.companyName = "Biola Campus 3D (unofficial)";
            PlayerSettings.productName = "Biola Campus 3D";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;

            PlayerSettings.WebGL.threadsSupport = true;                 // "Native C/C++ Multithreading"
            PlayerSettings.WebGL.initialMemorySize = 512;               // MB
            PlayerSettings.WebGL.maximumMemorySize = 4096;              // MB — Cesium needs headroom for tiles
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.geometricMemoryGrowthStep = 0.2f;
            PlayerSettings.WebGL.memoryGeometricGrowthCap = 96;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;          // works on hosts without Content-Encoding headers (GitHub Pages)
            PlayerSettings.WebGL.dataCaching = true;                    // caches the *Unity build* only, never map tiles
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.showDiagnostics = false;
            if (Directory.Exists("Assets/WebGLTemplates/Biola")) PlayerSettings.WebGL.template = TemplateName;

            var target = NamedBuildTarget.WebGL;
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Low);   // Cesium uses reflection-bound interop
            PlayerSettings.SetIl2CppCodeGeneration(target, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
            PlayerSettings.stripEngineCode = true;

            SetWebGraphicsApis(webGpu);
        }

        public static void SetWebGraphicsApis(bool webGpu)
        {
            var apis = new System.Collections.Generic.List<GraphicsDeviceType>();
            if (webGpu)
            {
                if (Enum.TryParse("WebGPU", out GraphicsDeviceType wgpu)) apis.Add(wgpu);
                else Debug.LogWarning("[Biola] This Unity version has no WebGPU graphics API; using WebGL 2 only.");
            }
            apis.Add(GraphicsDeviceType.OpenGLES3); // WebGL 2 (also the fallback when WebGPU is unavailable)
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, apis.ToArray());
            Debug.Log("[Biola] Web graphics APIs: " + string.Join(", ", apis));
        }

        [MenuItem("Biola/Web Graphics ▸ WebGL 2 (default)", priority = 20)]
        static void UseWebGl() => SetWebGraphicsApis(false);

        [MenuItem("Biola/Web Graphics ▸ WebGPU + WebGL 2 fallback (experimental)", priority = 21)]
        static void UseWebGpu() => SetWebGraphicsApis(true);
    }
}
