using System.IO;
using UnityEditor;
using UnityEngine;

namespace Vid2Sim.Editor
{
    /// <summary>
    /// Assets a player build needs that the editor silently supplies: TextMeshPro essentials (TMP Settings,
    /// default font) and material assets so the Standard shader variants are not stripped from builds.
    /// Both build methods call Ensure() first; the WebGL run showed what happens without it (five TMP
    /// exceptions and a null shader on the first spawned part).
    /// </summary>
    public static class ProjectAssets
    {
        public const string PartMaterialPath = "Assets/Sim/Resources/Vid2SimPart.mat";
        public const string ZoneMaterialPath = "Assets/Sim/Resources/Vid2SimZone.mat";
        const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        [MenuItem("Vid2Sim/Ensure Project Assets")]
        public static void Ensure()
        {
            if (!File.Exists(TmpSettingsPath))
            {
                // the essentials ship inside com.unity.ugui as a .unitypackage; importing it is version-stable
                var pkg = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
                if (!File.Exists(pkg)) throw new FileNotFoundException("TMP essentials package not found", pkg);
                AssetDatabase.ImportPackage(pkg, false);
                AssetDatabase.Refresh();
                Debug.Log("[ProjectAssets] imported TextMeshPro essentials from " + pkg);
            }
            if (AssetDatabase.LoadAssetAtPath<Material>(PartMaterialPath) == null)
            {
                var m = new Material(Shader.Find("Standard")) { color = Color.white };
                AssetDatabase.CreateAsset(m, PartMaterialPath);
                Debug.Log("[ProjectAssets] created " + PartMaterialPath);
            }
            if (AssetDatabase.LoadAssetAtPath<Material>(ZoneMaterialPath) == null)
            {
                var z = new Material(Shader.Find("Standard")) { color = new Color(0.2f, 0.8f, 0.4f, 0.35f) };
                // Fade rendering mode, set here so the blended variant and keyword ship in the build
                z.SetFloat("_Mode", 2f);
                z.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                z.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                z.SetInt("_ZWrite", 0);
                z.DisableKeyword("_ALPHATEST_ON");
                z.EnableKeyword("_ALPHABLEND_ON");
                z.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                z.renderQueue = 3000;
                AssetDatabase.CreateAsset(z, ZoneMaterialPath);
                Debug.Log("[ProjectAssets] created " + ZoneMaterialPath);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
