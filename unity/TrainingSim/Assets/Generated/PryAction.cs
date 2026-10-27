#if META_INTERACTION_SDK
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

namespace Vid2Sim.Generated
{
    public class PryAction : MonoBehaviour, Vid2Sim.IStepAction
    {
        [SerializeField] private float pullDistanceM = 0.10f;
        [SerializeField] private float liftHeightM = 0.05f;
        [SerializeField] private float tiltAngleDeg = 18f;

        private Vid2Sim.Part _part;
        private Vid2Sim.Step _step;
        private System.Action<Vid2Sim.StepEvent> _emit;
        private HandGrabInteractable _grabInteractable;
        private bool _done;
        private bool _isGrabbed;
        private Vector3 _startWorldPos;
        private Quaternion _startWorldRot;
        private Vector3 _startUpDir;

        public void Bind(Vid2Sim.Part part, Vid2Sim.Step step, System.Action<Vid2Sim.StepEvent> emit)
        {
            _part = part;
            _step = step;
            _emit = emit;
            _done = false;
            _isGrabbed = false;
            _startWorldPos = transform.position;
            _startWorldRot = transform.rotation;
            _startUpDir = _startWorldRot * Vector3.up;
            Subscribe();
        }

        private void Subscribe()
        {
            if (_grabInteractable == null)
            {
                _grabInteractable = GetComponent<HandGrabInteractable>();
            }
            if (_grabInteractable != null)
            {
                _grabInteractable.WhenStateChanged -= OnInteractableStateChanged;
                _grabInteractable.WhenStateChanged += OnInteractableStateChanged;
            }
        }

        private void OnInteractableStateChanged(InteractableStateChangeArgs args)
        {
            _isGrabbed = args.NewState == InteractableState.Select;
        }

        private void Update()
        {
            if (_done || _emit == null || !_isGrabbed)
            {
                return;
            }

            Vector3 disp = transform.position - _startWorldPos;
            float dist = disp.magnitude;
            float lift = Vector3.Dot(disp, _startUpDir);
            float angle = Quaternion.Angle(_startWorldRot, transform.rotation);

            float halfPull = pullDistanceM * 0.5f;
            float halfLift = liftHeightM * 0.5f;
            float halfTilt = tiltAngleDeg * 0.5f;

            bool pulledWithPry = dist >= pullDistanceM && (lift >= halfLift || angle >= halfTilt);
            bool liftedClear = lift >= liftHeightM;
            bool tiltedOff = angle >= tiltAngleDeg && dist >= halfPull;

            if (pulledWithPry || liftedClear || tiltedOff)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (_done)
            {
                return;
            }
            _done = true;
            string partId = _part != null && !string.IsNullOrEmpty(_part.id) ? _part.id : "vent_panel";
            string action = _step != null && !string.IsNullOrEmpty(_step.action) ? _step.action : "pry";
            var emit = _emit;
            _emit = null;
            emit?.Invoke(new Vid2Sim.StepEvent(partId, action));
        }

        private void OnDestroy()
        {
            if (_grabInteractable != null)
            {
                _grabInteractable.WhenStateChanged -= OnInteractableStateChanged;
            }
        }
    }
}
#endif
