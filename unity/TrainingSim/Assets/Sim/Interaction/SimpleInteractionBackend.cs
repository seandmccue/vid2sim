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
        float _depth;
        Vector3 _grabOffset;
        bool _hoverThisFrame;

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || part == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            bool hit = Physics.Raycast(ray, out var h, 5f) && h.collider.gameObject == gameObject;
            _hoverThisFrame = hit || _held == this;
            part.NotifyHover(_hoverThisFrame);

            if (hit && Input.GetMouseButtonDown(0))
            {
                if (isPress) { part.NotifySelect(); part.NotifyUnselect(); return; }
                _held = this;
                _depth = h.distance;
                _grabOffset = transform.position - h.point;
                part.NotifySelect();
            }
            if (_held == this)
            {
                if (Input.GetMouseButton(0))
                    transform.position = ray.GetPoint(_depth) + _grabOffset;
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
        }
    }
}
