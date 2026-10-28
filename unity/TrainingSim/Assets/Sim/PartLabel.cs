using TMPro;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>Floating name above a part, always facing the camera. The current step's part is lit.</summary>
    public class PartLabel : MonoBehaviour
    {
        public TextMeshPro Text { get; private set; }
        Transform _cam;
        float _lift;
        PartBehaviour _part;
        bool _current;
        /// <summary>Parts on a host model sit close together, so their names show only while current or hovered.</summary>
        public bool hideWhenIdle;

        public static PartLabel Create(PartBehaviour part, float liftAbovePart)
        {
            var go = new GameObject($"Label:{part.Part.id}");
            go.transform.SetParent(part.transform.parent, false);   // sibling, so it doesn't scale with the part
            var label = go.AddComponent<PartLabel>();
            label._lift = liftAbovePart;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = part.Part.name;
            tmp.fontSize = 3.4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Idle;
            tmp.enableWordWrapping = false;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color(1f, 1f, 1f, 0.85f);   // light halo: dark text stays readable over any part
            go.transform.localScale = Vector3.one * 0.06f;
            label.Text = tmp;
            label._part = part;
            label.Follow(part.transform);
            return label;
        }

        static readonly Color Idle = new(0.20f, 0.23f, 0.30f, 1f);      // slate on the light studio background
        static readonly Color Current = new(0.85f, 0.45f, 0.05f, 1f);   // deep amber for the step's part

        Transform _target;
        public void Follow(Transform t) { _target = t; }

        public void SetCurrent(bool on)
        {
            if (!Text) return;
            _current = on;
            Text.color = on ? Current : Idle;
            Text.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            transform.localScale = Vector3.one * (on ? 0.08f : 0.06f);
        }

        void LateUpdate()
        {
            if (_target) transform.position = _target.position + Vector3.up * _lift;
            if (Text) Text.enabled = !hideWhenIdle || _current || (_part && (_part.IsHovered || _part.IsSelected || _part.IsHighlighted));
            if (_cam == null && Camera.main) _cam = Camera.main.transform;
            if (_cam) transform.rotation = Quaternion.LookRotation(transform.position - _cam.position, Vector3.up);
        }
    }
}
