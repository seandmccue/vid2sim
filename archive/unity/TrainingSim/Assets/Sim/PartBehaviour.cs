using System;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// One spawned part. The interaction backend (Meta Interaction SDK or the editor fallback) feeds
    /// Select/Unselect/Hover into this; ActionDetector turns those plus motion into StepEvents.
    /// </summary>
    public class PartBehaviour : MonoBehaviour
    {
        public Part Part { get; private set; }
        public Vector3 SpawnPosition { get; private set; }
        public Quaternion SpawnRotation { get; private set; }
        public Transform SnapZone { get; set; }   // only for parts used by a 'place' step
        /// <summary>
        /// Axis a turn is measured about, in the part's spawn frame, pointing at whoever watches it turn:
        /// up for a knob or cap lying on the bench, the card normal (toward the trainee) for a photo card.
        /// </summary>
        public Vector3 TurnAxisLocal = Vector3.up;
        public Vector3 TurnAxisWorld => SpawnRotation * TurnAxisLocal;
        /// <summary>True when the part is a flat photo card cut from the video rather than a primitive.</summary>
        public bool IsCard { get; set; }
        Vector3 _outward;
        /// <summary>
        /// World direction a pull moves the part and the demo ghost / hint arrows point: off the bench toward the
        /// trainee, or off the machine's surface for a part placed on a host model.
        /// </summary>
        public Vector3 Outward
        {
            get => _outward.sqrMagnitude > 1e-6f ? _outward : (transform.parent ? -transform.parent.forward : Vector3.back);
            set => _outward = value;
        }

        /// <summary>
        /// Judgment steps: an inspected part is spawned good or faulty, and the trainee has to say which.
        /// With a generated faulty model (Model:faulty child) the defect is the model itself; otherwise a
        /// generic red wear patch marks it.
        /// </summary>
        public bool Faulty { get; private set; }
        public bool HasFaultModel => transform.Find("Model:faulty") != null;
        public event Action<PartBehaviour, bool> VerdictGiven;   // (part, trainee said "fault")
        GameObject _marker;

        public void SetFault(bool faulty)
        {
            Faulty = faulty;
            var good = transform.Find("Model"); var bad = transform.Find("Model:faulty");
            if (bad != null)
            {
                if (good != null) good.gameObject.SetActive(!faulty);
                bad.gameObject.SetActive(faulty);
                _renderers = null;   // the visible set changed
                return;
            }
            if (faulty && _marker == null)
            {
                var col = GetComponent<Collider>();
                var b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one * 0.05f);
                _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _marker.name = "FaultMarker";
                var mc = _marker.GetComponent<Collider>(); if (mc) Destroy(mc);
                _marker.transform.SetParent(transform, true);
                float r = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
                _marker.transform.position = b.center + (IsCard ? TurnAxisWorld : Vector3.up) * (IsCard ? 0.004f : b.extents.y) + Vector3.right * r * 0.3f;
                _marker.transform.localRotation = Quaternion.identity;
                _marker.transform.localScale = Vector3.one * Mathf.Clamp(r * 0.35f, 0.012f, 0.04f);
                _marker.transform.localScale = new Vector3(_marker.transform.localScale.x, _marker.transform.localScale.y * 0.35f, _marker.transform.localScale.z);
                var m = Resources.Load<Material>("Vid2SimPart");
                var mat = m ? new Material(m) : new Material(Shader.Find("Standard"));
                mat.color = new Color(0.75f, 0.18f, 0.12f);
                _marker.GetComponent<Renderer>().material = mat;
                _renderers = null;
            }
            if (_marker != null) _marker.SetActive(faulty);
        }

        /// <summary>The trainee's call on an inspected part: true = "I found a fault".</summary>
        public void GiveVerdict(bool faultFound) => VerdictGiven?.Invoke(this, faultFound);

        public bool IsSelected { get; private set; }
        public bool IsHovered { get; private set; }
        public float HoverSeconds { get; private set; }

        public event Action<PartBehaviour> Selected;
        public event Action<PartBehaviour> Unselected;
        public event Action<PartBehaviour> HoverStarted;
        public event Action<PartBehaviour> HoverEnded;

        public void Init(Part part)
        {
            Part = part;
            SpawnPosition = transform.position;
            SpawnRotation = transform.rotation;
        }

        public void NotifySelect()   { if (IsSelected) return; IsSelected = true; Selected?.Invoke(this); }
        public void NotifyUnselect() { if (!IsSelected) return; IsSelected = false; Unselected?.Invoke(this); }
        Vector3 _baseScale;
        public void NotifyHover(bool on)
        {
            if (on == IsHovered) return;
            IsHovered = on;
            if (_baseScale == Vector3.zero) _baseScale = transform.localScale;
            transform.localScale = on ? _baseScale * 1.08f : _baseScale;   // visible hover feedback on any backend
            if (on) { HoverSeconds = 0f; HoverStarted?.Invoke(this); } else HoverEnded?.Invoke(this);
        }

        Renderer[] _renderers;
        Color[] _baseColors;
        float _highlightUntil;

        void CacheRenderers()
        {
            if (_renderers != null) return;
            _renderers = GetComponentsInChildren<Renderer>();
            _baseColors = new Color[_renderers.Length];
        }

        void Update()
        {
            if (IsHovered) HoverSeconds += Time.deltaTime;
            if (_highlightUntil > 0f)
            {
                CacheRenderers();
                bool on = Time.unscaledTime < _highlightUntil;
                float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] == null) continue;
                    _renderers[i].material.color = on ? Color.Lerp(_baseColors[i], new Color(1f, 0.85f, 0.2f), t) : _baseColors[i];
                }
                if (!on) _highlightUntil = 0f;
            }
        }

        /// <summary>Pulse the part yellow for a few seconds so a stalled trainee sees what the coach means.</summary>
        public void Highlight(float seconds = 4f)
        {
            CacheRenderers();
            if (_highlightUntil <= 0f)
                for (int i = 0; i < _renderers.Length; i++) if (_renderers[i] != null) _baseColors[i] = _renderers[i].material.color;
            _highlightUntil = Time.unscaledTime + seconds;
        }

        public bool IsHighlighted => _highlightUntil > 0f;

        public void ResetPose()
        {
            transform.SetPositionAndRotation(SpawnPosition, SpawnRotation);
            var rb = GetComponent<Rigidbody>();
            if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        public float SignedYawFromSpawn()
        {
            // rotation about the part's turn axis, degrees, + = clockwise as seen by the viewer the axis points at
            var delta = Quaternion.Inverse(SpawnRotation) * transform.rotation;
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            return angle * Mathf.Sign(Vector3.Dot(axis, TurnAxisLocal)) * -1f;
        }

        /// <summary>Turn about the part's own axis; + = clockwise for the viewer (what "turn_cw" means).</summary>
        public void TurnBy(float degreesClockwise) => transform.Rotate(TurnAxisWorld, -degreesClockwise, Space.World);
    }
}
