using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Browser output camera: seated height, looking at the bench. Arrow keys / WASD orbit, scroll zooms,
    /// and an on-screen hint explains the mouse mapping the SimpleInteractionBackend uses
    /// (click = press, drag = grab/pull/lift/place, right-drag = turn, hover = inspect).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class WebCameraRig : MonoBehaviour
    {
        public Vector3 pivot = new(0f, 0.75f, 0.7f);   // bench centre: PartFactory places parts 0.7 m ahead at 0.75 m
        public float distance = 0.9f;
        public float yaw = 0f, pitch = 28f;
        public float orbitSpeed = 60f, zoomSpeed = 0.4f;
        public bool showHint = true;

        void Start() { Apply(); }

        void Update()
        {
            float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
            {
                yaw += h * orbitSpeed * Time.unscaledDeltaTime;
                pitch = Mathf.Clamp(pitch - v * orbitSpeed * Time.unscaledDeltaTime, 5f, 80f);
                Apply();
            }
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                distance = Mathf.Clamp(distance - scroll * zoomSpeed * 0.25f, 0.4f, 2.5f);
                Apply();
            }
        }

        void Apply()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = pivot - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }

        void OnGUI()
        {
            if (!showHint) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(0.9f, 0.93f, 1f) } };
            GUI.Box(new Rect(12, 12, 560, 26), GUIContent.none);
            GUI.Label(new Rect(20, 16, 560, 22), "Click = press · Drag = grab / pull / lift / place · Right-drag = turn · Hover = inspect · Arrows orbit · Scroll zooms · R restart · P pause", style);
        }
    }
}
