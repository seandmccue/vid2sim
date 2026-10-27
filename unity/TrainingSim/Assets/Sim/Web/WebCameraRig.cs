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
        public Vector3 pivot = new(0f, 0.9f, 0.7f);    // between the bench (0.75 m) and the coach panel (1.2 m), 0.7 m ahead of the seat
        public float distance = 1.1f;
        public float yaw = 0f, pitch = 18f;
        public float orbitSpeed = 60f, zoomSpeed = 0.4f;
        public bool showHint = true;
        public bool debugReadout = false;

        void Start()
        {
            Apply();
            debugReadout = Application.absoluteURL.Contains("debug=1");   // URL-driven, never the serialized default
        }

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
            var style = new GUIStyle(GUI.skin.label) { fontSize = 28, normal = { textColor = new Color(0.9f, 0.93f, 1f) } };
            GUI.Box(new Rect(12, 12, 1500, 44), GUIContent.none);
            GUI.Label(new Rect(20, 18, 1500, 40), "Click = press · Drag = grab / pull / lift / place · Hover + Q/E = quarter turn (or right-drag) · Hover = inspect · Arrows orbit · Scroll zooms · R restart · P pause", style);
            if (debugReadout)
            {
                var cam = Camera.main;
                var ray = cam ? cam.ScreenPointToRay(Input.mousePosition) : default;
                string under = cam && Physics.Raycast(ray, out var h, 5f) ? h.collider.gameObject.name : "-";
                GUI.Label(new Rect(20, 60, 1800, 40), $"mouse {Input.mousePosition} screen {Screen.width}x{Screen.height} under: {under} hover: {Interaction.SimpleMouseDriver.HoverName} last click: {Interaction.SimpleMouseDriver.LastClick}", style);
            }
        }
    }
}
