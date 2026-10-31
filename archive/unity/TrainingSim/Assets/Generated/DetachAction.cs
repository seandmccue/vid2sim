#if META_INTERACTION_SDK && !UNITY_WEBGL
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Grab;

namespace Vid2Sim.Generated
{
    public class DetachAction : MonoBehaviour, IStepAction
    {
        [SerializeField] private float pullDistanceM = 0.06f;
        [SerializeField] private float minHoldTimeS = 0.05f;

        private Part _part;
        private Step _step;
        private System.Action<StepEvent> _emit;
        private HandGrabInteractable _grab;

        private Vector3 _startLocalPos;
        private Vector3 _startWorldPos;
        private bool _wasGrabbed;
        private bool _isGrabbed;
        private bool _done;
        private float _beyondTime;

        public void Bind(Part part, Step step, System.Action<StepEvent> emit)
        {
            if (_grab != null)
            {
                _grab.WhenStateChanged -= OnStateChanged;
                _grab = null;
            }

            _part = part;
            _step = step;
            _emit = emit;
            _done = false;
            _wasGrabbed = false;
            _isGrabbed = false;
            _beyondTime = 0f;

            _startLocalPos = transform.localPosition;
            _startWorldPos = transform.position;

            _grab = GetComponent<HandGrabInteractable>();
            if (_grab != null)
            {
                _grab.WhenStateChanged += OnStateChanged;
                if (_grab.State == InteractableState.Select)
                {
                    _isGrabbed = true;
                    _wasGrabbed = true;
                }
            }
        }

        private void OnStateChanged(InteractableStateChangeArgs args)
        {
            if (_done || _grab == null)
            {
                return;
            }

            bool grabbed = _grab.State == InteractableState.Select;
            _isGrabbed = grabbed;
            if (grabbed)
            {
                _wasGrabbed = true;
            }
        }

        private void Update()
        {
            if (_done || _grab == null)
            {
                return;
            }

            bool grabbed = _grab.State == InteractableState.Select;
            if (grabbed)
            {
                _isGrabbed = true;
                _wasGrabbed = true;
            }
            else
            {
                _isGrabbed = false;
            }

            if (!_wasGrabbed)
            {
                return;
            }

            float localDist = Vector3.Distance(transform.localPosition, _startLocalPos);
            float worldDist = Vector3.Distance(transform.position, _startWorldPos);
            float dist = localDist > worldDist ? localDist : worldDist;

            if (dist >= pullDistanceM)
            {
                _beyondTime += Time.deltaTime;
                if (_beyondTime >= minHoldTimeS)
                {
                    Complete();
                }
            }
            else
            {
                _beyondTime = 0f;
            }
        }

        private void Complete()
        {
            if (_done)
            {
                return;
            }
            _done = true;
            var emit = _emit;
            if (emit == null)
            {
                return;
            }
            string pid = _part != null && !string.IsNullOrEmpty(_part.id) ? _part.id : "accelerator_rod";
            string act = _step != null && !string.IsNullOrEmpty(_step.action) ? _step.action : "detach";
            emit(new StepEvent(pid, act));
        }

        private void OnDestroy()
        {
            if (_grab != null)
            {
                _grab.WhenStateChanged -= OnStateChanged;
                _grab = null;
            }
            _emit = null;
        }
    }
}
#endif
