using System;
using System.IO;
using System.Linq;
using BiolaCampus.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
                if (!File.Exists(ScenePath) || !File.Exists(MaterialsDir + "/UrpLitTemplate.mat") || !File.Exists(UrpDir + "/URP_Medium.asset"))
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
            EnsureUrp();
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
            if (UrpActive)
            {
                MakeLitTemplate("UrpLitTemplate", UrpLit, false);
                MakeLitTemplate("UrpLitInstancedTemplate", UrpLit, true);
            }
            else
            {
                MakeLitTemplate("StdLitTemplate", "Standard", false);
                MakeLitTemplate("StdLitInstancedTemplate", "Standard", true);
            }
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
            if (!create && m.shader != null && m.shader.name != shaderName && Shader.Find(shaderName) != null) m.shader = Shader.Find(shaderName);
            if (create)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) { Debug.LogWarning("[Biola] Shader not found: " + shaderName); return null; }
                m = new Material(shader) { name = name };
            }
            // Real texture assets (built-in textures do not serialize, and URP's material validation would then
            // drop the keywords and the build would strip those variants). Runtime materials replace them.
            m.SetTexture("_BumpMap", TemplateTexture("TemplateNormal", new Color(0.5f, 0.5f, 1f, 1f), true));
            m.SetTexture("_EmissionMap", TemplateTexture("TemplateBlack", Color.black, false));
            m.SetTexture("_MetallicGlossMap", null);
            m.SetColor("_EmissionColor", new Color(0.004f, 0.004f, 0.004f));
            m.EnableKeyword("_NORMALMAP");
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.DisableKeyword("_METALLICGLOSSMAP");
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = instanced;
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        const string UrpLit = "Universal Render Pipeline/Lit";
        static bool UrpActive => GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset;

        static Texture2D TemplateTexture(string name, Color c, bool normalMap)
        {
            string path = $"{MaterialsDir}/{name}.png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
                var px = new Color[16];
                for (int i = 0; i < 16; i++) px[i] = c;
                t.SetPixels(px); t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp != null && normalMap && imp.textureType != TextureImporterType.NormalMap)
            {
                imp.textureType = TextureImporterType.NormalMap;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Re-imports Cesium's Shader Graphs so they are generated for the active pipeline (URP). A Library
        /// imported before URP was installed only has the built-in target, which renders magenta.
        /// </summary>
        public static void ReimportCesiumShaders()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { "Packages/com.cesium.unity" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".shadergraph") || path.EndsWith(".shadersubgraph"))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    n++;
                }
            }
            Debug.Log($"[Biola] Re-imported {n} Cesium shader graphs for the active render pipeline.");
        }

        static Material MakeMaterial(string name, string shaderName, Color color)
        {
            if (shaderName == "Standard" && UrpActive) shaderName = UrpLit;
            string path = $"{MaterialsDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                var want = Shader.Find(shaderName);
                if (want != null && existing.shader != want) { existing.shader = want; existing.color = color; EditorUtility.SetDirty(existing); }
                return existing;
            }
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogWarning("[Biola] Shader not found: " + shaderName); return null; }
            var m = new Material(shader) { name = name };
            if (m.HasProperty("_Color")) m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------------- URP

        public const string UrpDir = "Assets/Resources/BiolaURP";

        /// <summary>
        /// Creates Low / Medium / High URP assets sharing one Universal Renderer (with an SSAO feature)
        /// and makes Medium the default pipeline. RenderingSetup switches between them at runtime.
        /// </summary>
        public static void EnsureUrp()
        {
            Directory.CreateDirectory(UrpDir);
            string rdPath = UrpDir + "/BiolaRenderer.asset";
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
            if (rd == null)
            {
                rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                AssetDatabase.CreateAsset(rd, rdPath);
            }
            if (rd.postProcessData == null)
                rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");

            if (!rd.rendererFeatures.Exists(f => f is ScreenSpaceAmbientOcclusion))
            {
                var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "SSAO";
                AssetDatabase.AddObjectToAsset(ssao, rd);
                rd.rendererFeatures.Add(ssao);
            }
            // Keep URP's feature map (local file ids) in sync so the features survive reimport.
            var so = new SerializedObject(rd);
            var map = so.FindProperty("m_RendererFeatureMap");
            if (map != null)
            {
                map.arraySize = rd.rendererFeatures.Count;
                for (int i = 0; i < rd.rendererFeatures.Count; i++)
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(rd.rendererFeatures[i], out string _, out long id))
                        map.GetArrayElementAtIndex(i).longValue = id;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(rd);

            UniversalRenderPipelineAsset medium = null;
            foreach (var (name, shadows, cascades, dist, res, soft, lights, scale) in new[]
                     {
                         ("Low", false, 1, 0f, 1024, false, 2, 0.85f),
                         ("Medium", true, 2, 160f, 2048, true, 4, 1f),
                         ("High", true, 4, 320f, 4096, true, 8, 1f),
                     })
            {
                string path = $"{UrpDir}/URP_{name}.asset";
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (a == null)
                {
                    a = UniversalRenderPipelineAsset.Create(rd);
                    AssetDatabase.CreateAsset(a, path);
                }
                a.supportsHDR = true;
                a.msaaSampleCount = 1;
                a.renderScale = scale;
                a.shadowDistance = dist;
                a.shadowCascadeCount = cascades;
                a.mainLightShadowmapResolution = res;
                a.maxAdditionalLightsCount = lights;
                a.supportsCameraDepthTexture = true;
                var aso = new SerializedObject(a);
                void Set(string prop, System.Action<SerializedProperty> apply) { var pr = aso.FindProperty(prop); if (pr != null) apply(pr); else Debug.LogWarning("[Biola] URP asset has no " + prop); }
                Set("m_MainLightShadowsSupported", p => p.boolValue = shadows);
                Set("m_SoftShadowsSupported", p => p.boolValue = soft);
                Set("m_AdditionalLightsRenderingMode", p => p.intValue = 1);   // per pixel (street lamps)
                Set("m_AdditionalLightShadowsSupported", p => p.boolValue = false);
                aso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(a);
                if (name == "Medium") medium = a;
            }

            if (medium != null && GraphicsSettings.defaultRenderPipeline != medium)
            {
                GraphicsSettings.defaultRenderPipeline = medium;
                Debug.Log("[Biola] URP enabled (default asset: URP_Medium).");
            }
            // URP needs its global settings asset (default volume profile, shader resources) for builds. The
            // editor normally creates it when URP is first enabled interactively; do it explicitly for CI.
            try
            {
                var gsType = typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
                var ensure = gsType?.GetMethod("Ensure", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var gs = ensure?.Invoke(null, new object[] { true });
                Debug.Log("[Biola] URP global settings: " + (gs != null ? AssetDatabase.GetAssetPath((UnityEngine.Object)gs) : "not found"));
            }
            catch (System.Exception e) { Debug.LogWarning("[Biola] Could not ensure URP global settings: " + e.Message); }
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- player settings

        /// <summary>
        /// Web settings for Cesium for Unity: native C/C++ multithreading on, a raised memory ceiling
        /// (tiles are decoded into WASM memory), Brotli with decompression fallback for small downloads
        /// on any static host, WebGL 2 by default and optional WebGPU.
        /// </summary>
        public static void ApplyCommonPlayerSettings()
        {
            PlayerSettings.companyName = "Biola Campus 3D (unofficial)";
            PlayerSettings.productName = "Biola Campus 3D";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
        }

        public static void ApplyWebPlayerSettings(bool webGpu)
        {
            ApplyCommonPlayerSettings();

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
