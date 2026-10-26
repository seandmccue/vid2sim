#if META_INTERACTION_SDK
using System;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

namespace Vid2Sim.Generated
{
    public class PourAction : MonoBehaviour, Vid2Sim.IStepAction
    {
        [SerializeField] private float liftHeightThreshold = 0.05f;
        [SerializeField] private float tiltAngleThreshold = 35f;
        [SerializeField] private float holdTimeThreshold = 0.8f;

        private string _partId;
        private string _action;
        private Action<StepEvent> _emit;
        private bool _emitted;

        private HandGrabInteractable _grab;
        private bool _isGrabbed;
        private Vector3 _startPos;
        private Vector3 _startUp;
        private float _holdTimer;

        public void Bind(Part part, Step step, Action<StepEvent> emit)
        {
            _partId = part.id;
            _action = step.action;
            _emit = emit;
            _emitted = false;
            _startPos = transform.position;
            _startUp = transform.up;
            _holdTimer = 0f;
            EnsureGrabInteractable();
        }

        private void EnsureGrabInteractable()
        {
            _grab = GetComponent<HandGrabInteractable>();
            if (_grab == null)
            {
                _grab = gameObject.AddComponent<HandGrabInteractable>();
            }
            _grab.WhenStateChanged -= HandleStateChanged;
            _grab.WhenStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged(InteractableStateChangeArgs args)
        {
            if (args.NewState == InteractableState.Select)
            {
                _isGrabbed = true;
                _startPos = transform.position;
                _startUp = transform.up;
                _holdTimer = 0f;
            }
            else
            {
                _isGrabbed = false;
            }
        }

        private void Update()
        {
            if (_emitted || _emit == null)
            {
                return;
            }

            float lift = transform.position.y - _startPos.y;
            float tilt = Vector3.Angle(transform.up, _startUp);

            bool liftOk = lift >= liftHeightThreshold;
            bool tiltOk = tilt >= tiltAngleThreshold;

            if (liftOk || tiltOk)
            {
                _holdTimer += Time.deltaTime;
                if (_holdTimer >= holdTimeThreshold)
                {
                    Complete();
                }
            }
            else
            {
                _holdTimer = 0f;
            }
        }

        private void Complete()
        {
            if (_emitted)
            {
                return;
            }
            _emitted = true;
            var cb = _emit;
            _emit = null;
            if (cb != null)
            {
                cb(new StepEvent(_partId, _action));
            }
        }

        private void OnDestroy()
        {
            if (_grab != null)
            {
                _grab.WhenStateChanged -= HandleStateChanged;
                _grab = null;
            }
            _emit = null;
        }
    }
}
#endif
