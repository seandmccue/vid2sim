#if META_INTERACTION_SDK && !UNITY_WEBGL
using System;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

namespace Vid2Sim.Generated
{
    [RequireComponent(typeof(HandGrabInteractable))]
    public class SlideAction : MonoBehaviour, Vid2Sim.IStepAction
    {
        [SerializeField] private float slideDistanceM = 0.15f;
        [SerializeField] private float minHoldTimeS = 0.1f;

        private Vid2Sim.Part _part;
        private Vid2Sim.Step _step;
        private Action<Vid2Sim.StepEvent> _emit;
        private HandGrabInteractable _handGrab;

        private bool _bound;
        private bool _done;
        private bool _isSelected;
        private bool _hasGrabbed;
        private float _grabStartTime;
        private Vector3 _bindWorldPos;
        private Vector3 _grabStartPos;

        public void Bind(Vid2Sim.Part part, Vid2Sim.Step step, Action<Vid2Sim.StepEvent> emit)
        {
            _part = part;
            _step = step;
            _emit = emit;
            _bindWorldPos = transform.position;
            _grabStartPos = _bindWorldPos;
            _hasGrabbed = false;
            _isSelected = false;
            _done = false;

            if (_handGrab == null)
            {
                _handGrab = GetComponent<HandGrabInteractable>();
            }

            if (_handGrab != null)
            {
                _handGrab.WhenStateChanged -= HandleStateChanged;
                _handGrab.WhenStateChanged += HandleStateChanged;
            }

            _bound = true;
        }

        private void HandleStateChanged(InteractableStateChangeArgs args)
        {
            if (_done || !_bound)
            {
                return;
            }

            bool selected = args.NewState == InteractableState.Select;
            if (selected && !_isSelected)
            {
                _hasGrabbed = true;
                _grabStartTime = Time.time;
                _grabStartPos = transform.position;
            }
            _isSelected = selected;
        }

        private void Update()
        {
            if (!_bound || _done || _emit == null)
            {
                return;
            }

            if (!_hasGrabbed)
            {
                return;
            }

            if (Time.time - _grabStartTime < minHoldTimeS)
            {
                return;
            }

            float distFromOrigin = Vector3.Distance(transform.position, _bindWorldPos);
            float distFromGrab = Vector3.Distance(transform.position, _grabStartPos);
            float d = distFromOrigin > distFromGrab ? distFromOrigin : distFromGrab;

            if (d >= slideDistanceM)
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

            string pid = _part != null && !string.IsNullOrEmpty(_part.id) ? _part.id : "side_cover";
            string act = _step != null && !string.IsNullOrEmpty(_step.action) ? _step.action : "slide";

            var emit = _emit;
            if (emit != null)
            {
                emit.Invoke(new Vid2Sim.StepEvent(pid, act));
            }
        }

        private void OnDestroy()
        {
            if (_handGrab != null)
            {
                _handGrab.WhenStateChanged -= HandleStateChanged;
                _handGrab = null;
            }
            _emit = null;
            _bound = false;
        }
    }
}
#endif
