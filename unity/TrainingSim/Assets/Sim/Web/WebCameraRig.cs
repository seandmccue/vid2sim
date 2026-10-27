using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Browser output camera: seated height, looking at the bench. Arrow keys / WASD orbit, scroll zooms,
    /// and an on-screen hint explains the mouse mapping the SimpleInteractionBackend uses
    /// (click = press, drag = grab/pull/lift/place, right-drag = turn, hover = inspect).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class WebCameraRig : MonoBehaviour, IStartGate
    {
        public bool Started => _started;
        public Vector3 pivot = new(0f, 0.8f, 0.7f);    // just above the bench, 0.7 m ahead of the seat
        public float distance = 0.85f;
        public float yaw = 0f, pitch = 30f;
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

        bool _started;
        GUIStyle _big, _body;

        void OnGUI()
        {
            if (!_started)
            {
                // Start overlay: explains the controls once and absorbs the first click (which the canvas eats for focus).
                _big ??= new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
                _body ??= new GUIStyle(GUI.skin.label) { fontSize = 26, normal = { textColor = new Color(0.9f, 0.93f, 1f) }, wordWrap = true };
                float w = Mathf.Min(1100f, Screen.width - 80f), h = 520f;
                var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
                GUI.Box(r, GUIContent.none); GUI.Box(r, GUIContent.none);
                GUILayout.BeginArea(new Rect(r.x + 40, r.y + 30, r.width - 80, r.height - 60));
                GUILayout.Label("Training sim", _big);
                GUILayout.Space(10);
                GUILayout.Label("First you'll watch the whole procedure once: each part lights up and a yellow ghost shows the motion. Then it's your turn, one step at a time, with the step and how to do it at the top of the screen.", _body);
                GUILayout.Space(12);
                GUILayout.Label("Click a part to select it. A cyan arrow on the part shows the motion the step needs.", _body);
                GUILayout.Label("Buttons at the bottom (or keys) move the selected part: Q / E turn, W / S lift / lower, X pull out. Or drag it; drop it on the green zone to place it.", _body);
                GUILayout.Label("Click = press          Hover for 2 s = inspect", _body);
                GUILayout.Label("Arrow keys orbit    Scroll zooms    H = show me again    R = restart    P = pause", _body);
                GUILayout.Space(16);
                GUILayout.Label("Click anywhere to begin.", _big);
                GUILayout.EndArea();
                if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.KeyDown) { _started = true; Event.current.Use(); }
                return;
            }
            if (!showHint) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 28, normal = { textColor = new Color(0.9f, 0.93f, 1f) } };
            GUI.Box(new Rect(12, Screen.height - 52, Screen.width - 24, 40), GUIContent.none);
            GUI.Label(new Rect(20, Screen.height - 48, Screen.width - 40, 36), "Click a part to select it · Drag to move · Q/E turn · W/S lift/lower · X pull out · Hover 2 s = inspect · H show me · R restart · Arrows orbit · Scroll zooms", style);
            if (debugReadout)
            {
                var cam = Camera.main;
                var ray = cam ? cam.ScreenPointToRay(Input.mousePosition) : default;
                string under = cam && Physics.Raycast(ray, out var h, 5f) ? h.collider.gameObject.name : "-";
                GUI.Label(new Rect(20, Screen.height - 96, 1800, 40), $"mouse {Input.mousePosition} screen {Screen.width}x{Screen.height} under: {under} hover: {Interaction.SimpleMouseDriver.HoverName} last click: {Interaction.SimpleMouseDriver.LastClick}", style);
            }
        }
    }
}
