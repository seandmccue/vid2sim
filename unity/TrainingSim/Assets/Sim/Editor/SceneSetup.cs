using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if XR_MANAGEMENT
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine.XR.Management;
#endif

namespace Vid2Sim.Editor
{
    /// <summary>
    /// Creates Assets/Scenes/Sim.unity: a Bootstrap object plus, when the Meta XR SDK is installed, the Building
    /// Blocks the sim needs (Camera Rig, Hand Tracking, Interactions Rig, Hand Interactions, Grab Interactor,
    /// Hand Poke Interactor) installed through the SDK's own block installer, so the result is identical to adding
    /// them from Meta → Tools → Building Blocks. Bootstrap.rigOrigin is set to the OVRCameraRig root.
    /// Without the SDK it falls back to a plain camera at seated height (mouse backend).
    ///
    /// Headless (no -quit: the method exits the editor itself once the async installs finish):
    ///   Unity -batchmode -nographics -projectPath unity/TrainingSim -executeMethod Vid2Sim.Editor.SceneSetup.CreateSimSceneHeadless
    /// </summary>
    public static class SceneSetup
    {
        public const string WebScenePath = "Assets/Scenes/Web.unity";

        /// <summary>Browser output scene: plain camera with WebCameraRig, light, Bootstrap. No Meta rig.</summary>
        [MenuItem("Vid2Sim/Create Web Scene")]
        public static void CreateWebScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            if (cam)
            {
                cam.nearClipPlane = 0.05f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.09f, 0.1f, 0.12f);
                cam.gameObject.AddComponent<WebCameraRig>();
            }
            var boot = new GameObject("Vid2Sim");
            boot.AddComponent<Bootstrap>();
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, WebScenePath);
            Debug.Log("[SceneSetup] saved " + WebScenePath);
        }

        const string ScenePath = "Assets/Scenes/Sim.unity";

        // Block ids are GUIDs that are stable across SDK versions (BlockDataIds.cs in com.meta.xr.sdk.core and
        // com.meta.xr.sdk.interaction.ovr; the two interactor ids come from Editor/Blocks/Interactors/*.asset).
        // Order matters only for readability: InstallWithDependencies pulls prerequisites in on its own.
        static readonly (string id, string name)[] Blocks =
        {
            ("e47682b9-c270-40b1-b16d-90b627a5ce1b", "Camera Rig"),
            ("8b26b298-7bf4-490e-b245-a039c0184303", "Hand Tracking"),
            ("81f55626-5fad-45e9-a1df-184f330da7ba", "Interactions Rig"),
            ("0393ca30-f2a9-4865-a40f-f9a68d01c3a9", "Hand Interactions"),
            ("4187448e-9c2a-4021-ad92-fb65840f2721", "Grab Interactor"),
            ("f61a01e5-61c7-4589-bd2d-107bccae8f02", "Hand Poke Interactor"),
        };

        [MenuItem("Vid2Sim/Create Sim Scene")]
        public static void CreateSimScene() { _ = CreateAsync(exitWhenDone: false); }

        public static void CreateSimSceneHeadless() { _ = CreateAsync(exitWhenDone: true); }

        /// <summary>
        /// Everything the Quest build needs that lives in project settings, scripted and idempotent:
        /// OpenXR loader on for Android + Standalone (XR Plug-in Management), hand tracking = Controllers And Hands
        /// (OVRProjectConfig), Meta → Tools → Project Setup Tool "Fix All" for Android, then the AndroidManifest.
        /// Headless: Unity -batchmode -nographics -projectPath unity/TrainingSim -executeMethod Vid2Sim.Editor.SceneSetup.ApplyMetaProjectSetupHeadless
        /// (no -quit; the method exits the editor itself). Unity may abort with code 134 while tearing down after
        /// the exit call; the "[SceneSetup] Meta project setup applied" log line is the success signal.
        /// </summary>
        [MenuItem("Vid2Sim/Apply Meta Project Setup (Android)")]
        public static void ApplyMetaProjectSetup() { _ = ProjectSetupAsync(exitWhenDone: false); }

        public static void ApplyMetaProjectSetupHeadless() { _ = ProjectSetupAsync(exitWhenDone: true); }

        static async Task CreateAsync(bool exitWhenDone)
        {
            int code = 0;
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var light = new GameObject("Directional Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var boot = new GameObject("Vid2Sim").AddComponent<Bootstrap>();

                Transform rig = null;
                if (HasMetaSdk)
                {
                    rig = await InstallBuildingBlocks();
                    if (rig == null) code = 1;
                }
                if (rig != null) boot.rigOrigin = rig;
                else
                {
                    var cam = new GameObject("Main Camera").AddComponent<Camera>();
                    cam.tag = "MainCamera";
                    cam.transform.position = new Vector3(0f, 1.2f, 0f);
                    cam.nearClipPlane = 0.05f;
                }

                Directory.CreateDirectory("Assets/Scenes");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[SceneSetup] saved {ScenePath} " + (rig != null
                    ? "with Meta XR Building Blocks (rigOrigin = " + rig.name + ")"
                    : HasMetaSdk ? "WITHOUT the Meta rig: block install failed, see errors above" : "with a plain camera (no Meta SDK)"));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            if (exitWhenDone) EditorApplication.Exit(code);
        }

        static bool HasMetaSdk =>
#if META_CORE_SDK
            true;
#else
            false;
#endif

        /// <summary>Installs each block via BlockData.InstallWithDependencies (internal → reflection). Returns the rig root.</summary>
        static async Task<Transform> InstallBuildingBlocks()
        {
            var blockDataType = FindType("Meta.XR.BuildingBlocks.Editor.BlockData");
            if (blockDataType == null)
            {
                Debug.LogError("[SceneSetup] Meta.XR.BuildingBlocks.Editor.BlockData not found; is com.meta.xr.sdk.core installed?");
                return null;
            }
            var idProp = blockDataType.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
            var byId = new Dictionary<string, UnityEngine.Object>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + blockDataType.Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), blockDataType);
                if (asset == null) continue;
                if (idProp?.GetValue(asset) is string id && id.Length > 0) byId[id] = asset;
            }

            foreach (var (id, name) in Blocks)
            {
                if (IsBlockInScene(id)) { Debug.Log($"[SceneSetup] {name}: already in scene"); continue; }
                if (!byId.TryGetValue(id, out var data))
                {
                    Debug.LogError($"[SceneSetup] Building Block '{name}' ({id}) not found in the installed SDK packages");
                    return null;
                }
                var install = data.GetType().GetMethod("InstallWithDependencies",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(GameObject) }, null);
                if (install == null)
                {
                    Debug.LogError($"[SceneSetup] {data.GetType().Name}.InstallWithDependencies(GameObject) not found; SDK API changed. Add the blocks by hand (unity/README.md).");
                    return null;
                }
                try
                {
                    await (Task)install.Invoke(data, new object[] { null });
                    Debug.Log($"[SceneSetup] installed Building Block: {name}");
                }
                catch (Exception e)
                {
                    var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                    if (inner.GetType().Name == "InstallationCancelledException")
                    {
                        Debug.Log($"[SceneSetup] {name}: {inner.Message}");
                        continue;
                    }
                    Debug.LogError($"[SceneSetup] failed to install {name}: {inner}");
                    return null;
                }
            }

            var rigType = FindType("OVRCameraRig");
            var rig = rigType != null ? UnityEngine.Object.FindFirstObjectByType(rigType) as Component : null;
            if (rig == null) Debug.LogError("[SceneSetup] no OVRCameraRig in the scene after installing the Camera Rig block");
            return rig != null ? rig.transform : null;
        }

        static bool IsBlockInScene(string blockId)
        {
            var bbType = FindType("Meta.XR.BuildingBlocks.BuildingBlock");
            if (bbType == null) return false;
            var idProp = bbType.GetProperty("BlockId", BindingFlags.Instance | BindingFlags.Public);
            foreach (var bb in UnityEngine.Object.FindObjectsByType(bbType, FindObjectsSortMode.None))
                if (idProp?.GetValue(bb) as string == blockId) return true;
            return false;
        }

        static async Task ProjectSetupAsync(bool exitWhenDone)
        {
            int code = 0;
            try
            {
                EnableOpenXRLoader(BuildTargetGroup.Android);
                EnableOpenXRLoader(BuildTargetGroup.Standalone);
                SetHandTrackingSupport("ControllersAndHands");

                var setup = FindType("OVRProjectSetup");
                var fixAll = setup?.GetMethod("FixAllAsync", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(BuildTargetGroup) }, null);
                if (fixAll == null)
                {
                    Debug.LogError("[SceneSetup] OVRProjectSetup.FixAllAsync not found; run Meta → Tools → Project Setup Tool by hand.");
                    code = 1;
                }
                else
                {
                    await (Task)fixAll.Invoke(null, new object[] { BuildTargetGroup.Android });
                    // regenerate Assets/Plugins/Android/AndroidManifest.xml so the hand-tracking permission lands
                    var manifest = FindType("OVRManifestPreprocessor")?.GetMethod("GenerateOrUpdateAndroidManifest", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(bool) }, null);
                    if (manifest != null) manifest.Invoke(null, new object[] { true });
                    else Debug.LogWarning("[SceneSetup] OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest not found; run Meta → Tools → Update AndroidManifest.xml by hand");
                    AssetDatabase.SaveAssets();
                    Debug.Log("[SceneSetup] Meta project setup applied for Android");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            if (exitWhenDone) EditorApplication.Exit(code);
        }

        /// <summary>XR Plug-in Management: create the settings asset if missing and turn the OpenXR loader on.</summary>
        static void EnableOpenXRLoader(BuildTargetGroup group)
        {
#if XR_MANAGEMENT
            const string loader = "UnityEngine.XR.OpenXR.OpenXRLoader";
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget per) || per == null)
            {
                Directory.CreateDirectory("Assets/XR");
                per = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(per, "Assets/XR/XRGeneralSettings.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, per, true);
            }
            if (!per.HasManagerSettingsForBuildTarget(group)) per.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = per.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.IsLoaderAssigned(loader, group) &&
                !XRPackageMetadataStore.AssignLoader(general.Manager, loader, group))
                Debug.LogError($"[SceneSetup] could not assign the OpenXR loader for {group}; is com.unity.xr.openxr installed?");
            else
                Debug.Log($"[SceneSetup] OpenXR loader active for {group}");
            EditorUtility.SetDirty(per);
            EditorUtility.SetDirty(general);
            AssetDatabase.SaveAssets();
#else
            Debug.LogWarning("[SceneSetup] com.unity.xr.management missing; enable the OpenXR loader in Project Settings → XR Plug-in Management by hand");
#endif
        }

        /// <summary>OVRProjectConfig.handTrackingSupport (drives the manifest's hand-tracking permission). Reflection: it lives in Oculus.VR.Editor.</summary>
        static void SetHandTrackingSupport(string value)
        {
            var cfgType = FindType("OVRProjectConfig");
            var cfg = cfgType?.GetProperty("CachedProjectConfig", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            var field = cfgType?.GetField("handTrackingSupport", BindingFlags.Instance | BindingFlags.Public);
            var commit = cfgType?.GetMethod("CommitProjectConfig", BindingFlags.Static | BindingFlags.Public);
            if (cfg == null || field == null || commit == null)
            {
                Debug.LogWarning("[SceneSetup] OVRProjectConfig not found; set Hand Tracking Support = Controllers And Hands in the OVRCameraRig inspector by hand");
                return;
            }
            field.SetValue(cfg, Enum.Parse(field.FieldType, value));
            commit.Invoke(null, new[] { cfg });
            Debug.Log($"[SceneSetup] OVRProjectConfig.handTrackingSupport = {value}");
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
