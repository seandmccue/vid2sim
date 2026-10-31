using System.Collections;
using UnityEngine;

namespace Vid2Sim.Interaction
{
    /// <summary>
    /// Editor / WebGL fallback with no SDK: hover by camera ray, click to select (and grab), drag to move,
    /// right-drag to turn. The selected part is the target of the HUD buttons and the keys Q/E (turn),
    /// W/S (lift/lower) and X/Z (pull out/push back), so every action has an unambiguous control.
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
        public float turnStepDegrees = 50f;   // a little past the detector's 45-degree threshold, so one press always counts
        public float liftStep = 0.10f;
        public float pullStep = 0.12f;
        public float nudgeSeconds = 0.35f;

        static SimpleMouseDriver _held;
        /// <summary>The part last clicked: what the HUD buttons and the keys act on. Null after Escape or a click on nothing.</summary>
        public static SimpleMouseDriver Active { get; private set; }
        /// <summary>Set by the web HUD while the cursor is over one of its buttons, so the click is not also a part click.</summary>
        public static bool PointerOverHud;
        /// <summary>Debug readout for the web HUD: what the cursor is over and the last click.</summary>
        public static string HoverName = "", LastClick = "";
        static SimpleMouseDriver _logger;   // one driver logs mouse-downs and handles the keys, not one per part
        static SimpleMouseDriver _hovered;
        static int _hoverFrame = -1;
        void OnEnable() { if (_logger == null) _logger = this; }
        void OnDisable() { if (_logger == this) _logger = null; if (Active == this) Active = null; if (_hovered == this) _hovered = null; }
        float _depth;
        Vector3 _grabOffset;
        bool _wasHit;
        RaycastHit _lastHit;
        Coroutine _nudge;

        public static void ClearSelection() => Active = null;

        /// <summary>Hovered part this frame if any, else the selected one: the target of a key or button.</summary>
        public static SimpleMouseDriver Target => _hovered != null && _hoverFrame >= Time.frameCount - 1 ? _hovered : Active;

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || part == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit h = default;
            bool rayHit = !PointerOverHud && Physics.Raycast(ray, out h, 5f) && h.collider.gameObject == gameObject;
            // a press whose move arrives in the same frame (fast flick, automation) still targets the part
            // the cursor was over last frame
            bool hit = rayHit || (_wasHit && Input.GetMouseButtonDown(0));
            if (hit && !rayHit) { h = _lastHit; }
            _wasHit = rayHit; if (rayHit) _lastHit = h;
            part.NotifyHover(hit || _held == this);
            if (hit) { HoverName = part.Part.id; _hovered = this; _hoverFrame = Time.frameCount; }

            if (Input.GetMouseButtonDown(0) && this == _logger)
            {
                bool overAny = Physics.Raycast(ray, out var dh, 5f);
                Debug.Log($"[Mouse] down at {Input.mousePosition}, over {(overAny ? dh.collider.gameObject.name : "nothing")}");
                if (!overAny && !PointerOverHud) Active = null;   // click on empty space deselects
            }
            if (Input.GetMouseButtonDown(0) && hit)
            {
                LastClick = $"{part.Part.id} @ {Input.mousePosition}";
                Debug.Log($"[Mouse] click {part.Part.id} press={isPress}");
                if (isPress) { part.NotifySelect(); part.NotifyUnselect(); return; }
                Active = this;
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
                    part.TurnBy(Input.GetAxis("Mouse X") * 6f);
                if (Input.GetMouseButtonUp(0)) { _held = null; part.NotifyUnselect(); }
            }
            else if (hit && Input.GetMouseButton(1))
            {
                part.NotifySelect();
                part.TurnBy(Input.GetAxis("Mouse X") * 6f);
                if (Input.GetMouseButtonUp(1)) part.NotifyUnselect();
            }

            if (this == _logger) Keys();
        }

        // Q/E turn, W/S lift/lower, X/Z pull out/push back, Escape deselects; on the hovered part, else the selected one
        void Keys()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Active = null;
            var t = Target;
            if (t == null || t.isPress) return;
            if (Input.GetKeyDown(KeyCode.Q)) t.Turn(-t.turnStepDegrees);
            if (Input.GetKeyDown(KeyCode.E)) t.Turn(t.turnStepDegrees);
            if (Input.GetKeyDown(KeyCode.W)) t.Lift(1);
            if (Input.GetKeyDown(KeyCode.S)) t.Lift(-1);
            if (Input.GetKeyDown(KeyCode.X)) t.Pull(1);
            if (Input.GetKeyDown(KeyCode.Z)) t.Pull(-1);
        }

        /// <summary>Turn in one go, clockwise for positive degrees (one call meets the detector's 45-degree threshold).</summary>
        public void Turn(float degreesClockwise)
        {
            if (part == null) return;
            part.NotifySelect();
            part.TurnBy(degreesClockwise);
            part.NotifyUnselect();
        }

        /// <summary>Raise (+1) or lower (-1) by liftStep over nudgeSeconds, held the whole way so a lift step counts.</summary>
        public void Lift(int direction) => Nudge(Vector3.up * liftStep * Mathf.Sign(direction));

        /// <summary>Pull toward the trainee (+1) or push back (-1) by pullStep, held the whole way so a pull step counts.</summary>
        public void Pull(int direction)
        {
            Nudge(part.Outward * pullStep * Mathf.Sign(direction));
        }

        public void Nudge(Vector3 worldDelta)
        {
            if (part == null) return;
            if (_nudge != null) StopCoroutine(_nudge);
            _nudge = StartCoroutine(NudgeRoutine(worldDelta));
        }

        IEnumerator NudgeRoutine(Vector3 delta)
        {
            part.NotifySelect();
            var from = transform.position; var to = from + delta;
            float t = 0f;
            while (t < nudgeSeconds)
            {
                t += Time.unscaledDeltaTime;
                transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / nudgeSeconds)));
                yield return null;
            }
            transform.position = to;
            yield return null;   // one frame at the end pose while still held, for the detector
            part.NotifyUnselect();
            _nudge = null;
        }
    }
}
