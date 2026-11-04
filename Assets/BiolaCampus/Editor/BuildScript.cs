using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BiolaCampus.EditorTools
{
    /// <summary>
    /// Command-line / CI entry point (GameCI: buildMethod: BiolaCampus.EditorTools.BuildScript.BuildWebGL).
    /// Arguments:
    ///   -customBuildPath &lt;dir&gt;   output folder (GameCI passes this)
    ///   -webgpu true|false        add WebGPU (with WebGL 2 fallback). Default false.
    ///   -development              development build
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("Biola/Build Web Player (WebGL 2)", priority = 40)]
        public static void BuildWebGLMenu() => Build("Builds/WebGL", webGpu: false, development: false, exitOnFinish: false);

        [MenuItem("Biola/Build Web Player (WebGPU, experimental)", priority = 41)]
        public static void BuildWebGPUMenu() => Build("Builds/WebGPU", webGpu: true, development: false, exitOnFinish: false);

        public static void BuildWebGL()
        {
            string output = Arg("-customBuildPath") ?? "build/WebGL/WebGL";
            bool webGpu = string.Equals(Arg("-webgpu") ?? Environment.GetEnvironmentVariable("BIOLA_WEBGPU"), "true", StringComparison.OrdinalIgnoreCase);
            bool dev = Environment.GetCommandLineArgs().Contains("-development");
            Build(output, webGpu, dev, exitOnFinish: Application.isBatchMode);
        }

        [MenuItem("Biola/Build Windows Player (High)", priority = 42)]
        public static void BuildWindowsMenu() => BuildStandalone("Builds/Windows/BiolaCampus3D.exe", false);

        /// <summary>CI entry point for the Windows desktop build (GameCI targetPlatform StandaloneWindows64).</summary>
        public static void BuildWindows()
        {
            string output = Arg("-customBuildPath") ?? "build/StandaloneWindows64/BiolaCampus3D.exe";
            if (!output.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) output = Path.Combine(output, "BiolaCampus3D.exe");
            BuildStandalone(output, Application.isBatchMode);
        }

        static void BuildStandalone(string output, bool exitOnFinish)
        {
            int exitCode = 1;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
                BiolaProjectSetup.EnsureUrp();
                BiolaProjectSetup.EnsureMaterials();
                BiolaProjectSetup.EnsureMainScene(openScene: false);
                BiolaProjectSetup.ApplyCommonPlayerSettings();
                // Mono: IL2CPP for Windows cannot be cross-compiled on the Linux CI image.
                PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
                PlayerSettings.defaultScreenWidth = 1920;
                PlayerSettings.defaultScreenHeight = 1080;
                PlayerSettings.resizableWindow = true;
                AssetDatabase.SaveAssets();
                SecretsBuildProcessor.Stage();

                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { BiolaProjectSetup.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                };
                Debug.Log($"[Biola] Building Windows player → {output} (default preset High)");
                var summary = BuildPipeline.BuildPlayer(options).summary;
                Debug.Log($"[Biola] Build {summary.result}: {summary.totalSize / (1024f * 1024f):0.0} MB, {summary.totalErrors} errors, {summary.totalTime}");
                exitCode = summary.result == BuildResult.Succeeded ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[Biola] Build failed: " + e);
            }
            finally
            {
                SecretsBuildProcessor.Cleanup();
            }
            if (exitOnFinish) EditorApplication.Exit(exitCode);
        }

        static void Build(string output, bool webGpu, bool development, bool exitOnFinish)
        {
            int exitCode = 1;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

                BiolaProjectSetup.EnsureUrp();
                BiolaProjectSetup.EnsureMaterials();
                BiolaProjectSetup.EnsureMainScene(openScene: false);
                BiolaProjectSetup.ApplyWebPlayerSettings(webGpu);
                AssetDatabase.SaveAssets();
                SecretsBuildProcessor.Stage();

                Directory.CreateDirectory(output);
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { BiolaProjectSetup.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = development ? BuildOptions.Development : BuildOptions.None,
                };
                Debug.Log($"[Biola] Building Web player → {output} (WebGPU: {webGpu}, development: {development})");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                Debug.Log($"[Biola] Build {summary.result}: {summary.totalSize / (1024f * 1024f):0.0} MB, {summary.totalErrors} errors, {summary.totalTime}");
                exitCode = summary.result == BuildResult.Succeeded ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[Biola] Build failed: " + e);
                exitCode = 1;
            }
            finally
            {
                SecretsBuildProcessor.Cleanup();
            }

            if (exitOnFinish) EditorApplication.Exit(exitCode);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
