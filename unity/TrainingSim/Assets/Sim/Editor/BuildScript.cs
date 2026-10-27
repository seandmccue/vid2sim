using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Vid2Sim.Editor
{
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Sim.unity";

        /// <summary>
        /// Browser output: same engine, mouse backend, spec and textures fetched from the dashboard URL.
        ///   Unity -batchmode -nographics -quit -projectPath unity/TrainingSim -buildTarget WebGL -executeMethod Vid2Sim.Editor.BuildScript.BuildWebGL
        /// Output Builds/web/, served by backend/app.py at /sim/. Compression off so any static server works.
        /// </summary>
        [MenuItem("Vid2Sim/Build WebGL")]
        public static void BuildWebGL()
        {
            ProjectAssets.Ensure();
            if (!File.Exists(SceneSetup.WebScenePath)) SceneSetup.CreateWebScene();
            Directory.CreateDirectory("Builds");
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.runInBackground = true;
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { SceneSetup.WebScenePath },
                locationPathName = "Builds/web",
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[BuildScript] WebGL {report.summary.result} in {report.summary.totalTime.TotalSeconds:0}s -> {opts.locationPathName}");
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("Vid2Sim/Build Android APK")]
        public static void BuildAndroid()
        {
            ProjectAssets.Ensure();
            if (!File.Exists(ScenePath)) SceneSetup.CreateSimScene();
            Directory.CreateDirectory("Builds");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            if (string.IsNullOrEmpty(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)) ||
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android).StartsWith("com.DefaultCompany"))
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.framesixty.vid2sim");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/vid2sim.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[BuildScript] {report.summary.result} in {report.summary.totalTime.TotalSeconds:0}s -> {opts.locationPathName}");
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
