using UnityEngine;

namespace Vid2Sim.Interaction
{
    /// <summary>
    /// Editor / XR Simulator fallback with no SDK: hover by camera ray, click to grab and drag, right-drag to
    /// rotate, click a press part to poke it. Lets the whole step flow be tested in the Unity editor.
    /// </summary>
    public class SimpleInteractionBackend : IInteractionBackend
    {
        public string Name => "simple (mouse)";

        public void Attach(PartBehaviour part, GameObject go, string interaction, bool needsRotate)
        {
            var d = go.AddComponent<SimpleMouseDriver>();
            d.part = part;
            d.isPress = interaction == "press";
        }
    }

    public class SimpleMouseDriver : MonoBehaviour
    {
        public PartBehaviour part;
        public bool isPress;
        static SimpleMouseDriver _held;
        /// <summary>Debug readout for the web HUD: what the cursor is over and the last click.</summary>
        public static string HoverName = "", LastClick = "";
        static SimpleMouseDriver _logger;   // one driver logs mouse-downs, not one per part
        void OnEnable() { if (_logger == null) _logger = this; }
        void OnDisable() { if (_logger == this) _logger = null; }
        float _depth;
        Vector3 _grabOffset;
        bool _hoverThisFrame;
        bool _wasHit;
        RaycastHit _lastHit;

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || part == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            bool rayHit = Physics.Raycast(ray, out var h, 5f) && h.collider.gameObject == gameObject;
            // a press whose move arrives in the same frame (fast flick, automation) still targets the part
            // the cursor was over last frame
            bool hit = rayHit || (_wasHit && Input.GetMouseButtonDown(0));
            if (hit && !rayHit) { h = _lastHit; }
            _wasHit = rayHit; if (rayHit) _lastHit = h;
            _hoverThisFrame = hit || _held == this;
            part.NotifyHover(_hoverThisFrame);
            if (hit) HoverName = part.Part.id;

            if (Input.GetMouseButtonDown(0) && this == _logger) Debug.Log($"[Mouse] down at {Input.mousePosition}, over {(Physics.Raycast(ray, out var dh, 5f) ? dh.collider.gameObject.name : "nothing")}");
            if (Input.GetMouseButtonDown(0) && hit)
            {
                LastClick = $"{part.Part.id} @ {Input.mousePosition}";
                Debug.Log($"[Mouse] click {part.Part.id} press={isPress}");
                if (isPress) { part.NotifySelect(); part.NotifyUnselect(); return; }
                _held = this;
                _depth = h.distance;
                _grabOffset = transform.position - h.point;
                part.NotifySelect();
            }
            if (_held == this)
            {
                if (Input.GetMouseButton(0))
                {
                    transform.position = ray.GetPoint(_depth) + _grabOffset;
                    // snap assist: a mouse drag can't reach the zone's depth, so snap when the cursor is over it
                    if (part.SnapZone != null)
                    {
                        var zs = cam.WorldToScreenPoint(part.SnapZone.position);
                        if (zs.z > 0f && Vector2.Distance(zs, Input.mousePosition) < 45f * (Screen.height / 600f))
                            transform.position = part.SnapZone.position;
                    }
                }
                if (Input.GetMouseButton(1))
                    transform.Rotate(Vector3.up, -Input.GetAxis("Mouse X") * 6f, Space.World);
                if (Input.GetMouseButtonUp(0)) { _held = null; part.NotifyUnselect(); }
            }
            else if (hit && Input.GetMouseButton(1))
            {
                part.NotifySelect();
                transform.Rotate(Vector3.up, -Input.GetAxis("Mouse X") * 6f, Space.World);
                if (Input.GetMouseButtonUp(1)) part.NotifyUnselect();
            }
            else if (hit && !isPress && (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E)))
            {
                // keyboard turn for trackpads: Q = counter-clockwise, E = clockwise, a quarter turn per press
                // (one press meets the detector's 45-degree threshold; several presses in one frame count once)
                part.NotifySelect();
                transform.Rotate(Vector3.up, Input.GetKeyDown(KeyCode.Q) ? 45f : -45f, Space.World);
                part.NotifyUnselect();
            }
        }
    }
}
