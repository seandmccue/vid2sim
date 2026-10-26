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

        void Update()
        {
            if (IsHovered) HoverSeconds += Time.deltaTime;
        }

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
