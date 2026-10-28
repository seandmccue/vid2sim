using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// A cyan arrow on the current step's part showing the motion it needs: up for lift, toward the trainee
    /// for pull, down onto it for press, an arc for a turn (in the part's own turn plane), a line to the
    /// green zone for place, a ring for grab/inspect. Follows the part and pulses until the step completes.
    /// </summary>
    public class MotionHint : MonoBehaviour
    {
        public Color color = new(0.12f, 0.50f, 1f, 0.95f);
        public float width = 0.006f;

        LineRenderer _line;
        PartBehaviour _part;
        Step _step;
        public bool Visible => _part != null;
        public string CurrentAction => _step?.action ?? "";

        public void Show(PartBehaviour part, Step step)
        {
            _part = part; _step = step;
            if (_line == null)
            {
                var go = new GameObject("MotionHint");
                go.transform.SetParent(transform, false);
                _line = go.AddComponent<LineRenderer>();
                _line.useWorldSpace = true;
                _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _line.numCornerVertices = 2; _line.numCapVertices = 2;
                var src = Resources.Load<Material>("Vid2SimZone");
                _line.material = src ? new Material(src) : new Material(Shader.Find("Standard"));
                _line.material.color = color;
            }
            _line.enabled = part != null && step != null;
        }

        public void Hide() { _part = null; _step = null; if (_line) _line.enabled = false; }

        void Update()
        {
            if (_part == null || _step == null || _line == null) return;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f);
            _line.startWidth = _line.endWidth = width * (0.8f + 0.4f * pulse);
            var c = color; c.a = 0.55f + 0.45f * pulse; _line.material.color = c;

            var col = _part.GetComponent<Collider>();
            var b = col != null ? col.bounds : new Bounds(_part.transform.position, Vector3.one * 0.05f);
            var centre = b.center; float r = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
            var toward = _part.transform.parent ? -_part.transform.parent.forward : Vector3.back;
            float slide = 0.01f * Mathf.Sin(Time.unscaledTime * 3f);
            switch (_step.action)
            {
                case Actions.Lift:    Arrow(centre + Vector3.up * (b.extents.y + 0.01f + slide), Vector3.up, 0.12f); break;
                case Actions.Pull:    Arrow(centre + toward * (r + 0.01f + slide), toward, 0.14f); break;
                case Actions.Press:   Arrow(centre + Vector3.up * (b.extents.y + 0.12f - slide), Vector3.down, 0.09f); break;
                case Actions.Place:
                    if (_part.SnapZone) Arrow(centre, (_part.SnapZone.position - centre).normalized, Vector3.Distance(centre, _part.SnapZone.position));
                    else Ring(centre, r * 1.3f, Vector3.up);
                    break;
                case Actions.TurnCw:  Arc(centre, r * 1.35f, _part.TurnAxisWorld, clockwise: true); break;
                case Actions.TurnCcw: Arc(centre, r * 1.35f, _part.TurnAxisWorld, clockwise: false); break;
                default:              Ring(centre, r * 1.3f + slide, _part.IsCard ? _part.TurnAxisWorld : Vector3.up); break;
            }
        }

        void Arrow(Vector3 from, Vector3 dir, float length)
        {
            var tip = from + dir * length;
            var side = Vector3.Cross(dir, Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            float head = Mathf.Min(0.03f, length * 0.35f);
            var pts = new[] { from, tip, tip - dir * head + side * head * 0.6f, tip, tip - dir * head - side * head * 0.6f };
            _line.positionCount = pts.Length; _line.SetPositions(pts);
        }

        void Ring(Vector3 centre, float radius, Vector3 axis)
        {
            const int n = 40;
            var pts = new Vector3[n + 1];
            var u = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(axis, u).normalized;
            for (int i = 0; i <= n; i++) { float a = i / (float)n * Mathf.PI * 2f; pts[i] = centre + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius; }
            _line.positionCount = pts.Length; _line.SetPositions(pts);
        }

        // 270-degree arc around the axis (which points at the viewer) with an arrowhead; clockwise as the viewer sees it
        void Arc(Vector3 centre, float radius, Vector3 axis, bool clockwise)
        {
            const int n = 36;
            axis = axis.normalized;
            var u = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(axis, u).normalized;
            float sweep = clockwise ? -270f : 270f;   // + about an axis pointing at the viewer is counter-clockwise for them
            float spin = Time.unscaledTime * 60f * (clockwise ? -1f : 1f);
            var pts = new Vector3[n + 5];
            for (int i = 0; i <= n; i++)
            {
                float a = (spin + sweep * i / n) * Mathf.Deg2Rad;
                pts[i] = centre + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
            }
            var tip = pts[n]; var tangent = (pts[n] - pts[n - 1]).normalized; var side = Vector3.Cross(tangent, axis);
            float head = Mathf.Min(0.03f, radius * 0.5f);
            pts[n + 1] = tip - tangent * head + side * head * 0.6f; pts[n + 2] = tip; pts[n + 3] = tip - tangent * head - side * head * 0.6f; pts[n + 4] = tip;
            _line.positionCount = pts.Length; _line.SetPositions(pts);
        }
    }
}
