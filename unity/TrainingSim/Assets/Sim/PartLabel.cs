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
            tmp.color = new Color(0.85f, 0.88f, 0.95f, 0.95f);
            tmp.enableWordWrapping = false;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color(0f, 0f, 0f, 0.9f);
            go.transform.localScale = Vector3.one * 0.06f;
            label.Text = tmp;
            label.Follow(part.transform);
            return label;
        }

        Transform _target;
        public void Follow(Transform t) { _target = t; }

        public void SetCurrent(bool on)
        {
            if (!Text) return;
            Text.color = on ? new Color(1f, 0.85f, 0.2f) : new Color(0.85f, 0.88f, 0.95f, 0.95f);
            Text.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            transform.localScale = Vector3.one * (on ? 0.08f : 0.06f);
        }

        void LateUpdate()
        {
            if (_target) transform.position = _target.position + Vector3.up * _lift;
            if (_cam == null && Camera.main) _cam = Camera.main.transform;
            if (_cam) transform.rotation = Quaternion.LookRotation(transform.position - _cam.position, Vector3.up);
        }
    }
}
