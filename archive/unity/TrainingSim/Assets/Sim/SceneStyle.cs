using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Art direction applied at runtime so both scenes share one look without editing scene files:
    /// a light studio background, soft ambient, one warm key light with soft shadows, and a pale floor
    /// under the bench. The stylised-low-poly look the parts and the bench are designed for.
    /// </summary>
    public class SceneStyle : MonoBehaviour
    {
        public Color background = new(0.90f, 0.91f, 0.94f);
        public Color ambient = new(0.62f, 0.64f, 0.70f);
        public Color keyLight = new(1f, 0.97f, 0.92f);
        public float keyIntensity = 1.15f;
        public Vector3 keyEuler = new(48f, -28f, 0f);
        public Color floor = new(0.86f, 0.87f, 0.90f);
        public bool makeFloor = true;

        public void Apply(Transform origin)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = background;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = false;

            var light = FindFirstObjectByType<Light>();
            if (light == null || light.type != LightType.Directional)
                light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = keyLight;
            light.intensity = keyIntensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.45f;
            light.shadowBias = 0.02f;
            light.transform.rotation = Quaternion.Euler(keyEuler);
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 6f);

            if (makeFloor && GameObject.Find("Floor") == null)
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Plane);
                f.name = "Floor";
                f.transform.SetParent(transform, false);
                float y = origin != null ? origin.position.y - 1.2f : 0f;   // seated eye height is ~1.2 m
                f.transform.position = new Vector3(origin ? origin.position.x : 0f, y, origin ? origin.position.z : 0f);
                f.transform.localScale = Vector3.one * 2f;    // 20 x 20 m
                Destroy(f.GetComponent<Collider>());
                var src = Resources.Load<Material>("Vid2SimPart");
                var m = src ? new Material(src) : new Material(Shader.Find("Standard"));
                m.color = floor;
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
                f.GetComponent<Renderer>().material = m;
                f.GetComponent<Renderer>().receiveShadows = true;
            }
        }
    }
}
