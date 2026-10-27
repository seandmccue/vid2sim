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
        public void NotifyHover(bool on)
        {
            if (on == IsHovered) return;
            IsHovered = on;
            if (on) { HoverSeconds = 0f; HoverStarted?.Invoke(this); } else HoverEnded?.Invoke(this);
        }

        Renderer _renderer;
        Color _baseColor;
        float _highlightUntil;

        void Update()
        {
            if (IsHovered) HoverSeconds += Time.deltaTime;
            if (_highlightUntil > 0f)
            {
                if (_renderer == null) _renderer = GetComponent<Renderer>();
                if (_renderer != null)
                {
                    if (Time.unscaledTime < _highlightUntil)
                    {
                        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
                        _renderer.material.color = Color.Lerp(_baseColor, new Color(1f, 0.85f, 0.2f), t);
                    }
                    else { _renderer.material.color = _baseColor; _highlightUntil = 0f; }
                }
            }
        }

        /// <summary>Pulse the part yellow for a few seconds so a stalled trainee sees what the coach means.</summary>
        public void Highlight(float seconds = 4f)
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_renderer != null && _highlightUntil <= 0f) _baseColor = _renderer.material.color;
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
            // rotation about the part's own up axis, degrees, + = clockwise as seen from above
            var delta = Quaternion.Inverse(SpawnRotation) * transform.rotation;
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            return angle * Mathf.Sign(Vector3.Dot(axis, Vector3.up)) * -1f;
        }
    }
}
