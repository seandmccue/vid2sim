#if META_INTERACTION_SDK && !UNITY_WEBGL
using System;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Grab;

namespace Vid2Sim.Generated
{
    [RequireComponent(typeof(HandGrabInteractable))]
    public class WriteAction : MonoBehaviour, IStepAction
    {
        [SerializeField] private float holdTimeRequired = 1.2f;
        [SerializeField] private float writeDistanceRequiredM = 0.12f;
        [SerializeField] private float jitterIgnoreM = 0.002f;

        private Part _part;
        private Step _step;
        private Action<StepEvent> _emit;
        private HandGrabInteractable _grab;

        private bool _bound;
        private bool _done;
        private bool _isSelected;
        private bool _hasLast;
        private float _holdTime;
        private float _writeDistance;
        private Vector3 _lastPos;

        public void Bind(Part part, Step step, Action<StepEvent> emit)
        {
            if (_bound || _done)
            {
                return;
            }

            _part = part;
            _step = step;
            _emit = emit;
            _bound = true;

            _grab = GetComponent<HandGrabInteractable>();
            if (_grab == null)
            {
                Debug.LogWarning("WriteAction: HandGrabInteractable missing on " + gameObject.name, this);
                return;
            }

            _grab.WhenStateChanged += HandleStateChanged;
            _isSelected = _grab.State == InteractableState.Select;
            _hasLast = false;
            _holdTime = 0f;
            _writeDistance = 0f;
            enabled = true;
        }

        private void HandleStateChanged(InteractableStateChangeArgs args)
        {
            bool selected = args.NewState == InteractableState.Select;
            if (selected && !_isSelected)
            {
                _hasLast = false;
            }
            _isSelected = selected;
            if (!selected)
            {
                _hasLast = false;
            }
        }

        private void Update()
        {
            if (!_bound || _done)
            {
                return;
            }

            if (!_isSelected)
            {
                return;
            }

            _holdTime += Time.deltaTime;

            if (!_hasLast)
            {
                _lastPos = transform.position;
                _hasLast = true;
            }
            else
            {
                float d = Vector3.Distance(transform.position, _lastPos);
                _lastPos = transform.position;
                if (d >= jitterIgnoreM)
                {
                    _writeDistance += d;
                }
            }

            if (_holdTime >= holdTimeRequired && _writeDistance >= writeDistanceRequiredM)
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
            var emit = _emit;
            _emit = null;
            enabled = false;
            if (emit != null && _part != null && _step != null)
            {
                emit(new StepEvent(_part.id, _step.action));
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
