#if META_INTERACTION_SDK && !UNITY_WEBGL
using UnityEngine;
using System;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Grab;

namespace Vid2Sim.Generated
{
    public class DisconnectAction : MonoBehaviour, IStepAction
    {
        [SerializeField, Tooltip("World-space pull distance from start pose required to disconnect.")]
        private float pullDistanceM = 0.12f;

        [SerializeField, Tooltip("Minimum continuous grab time before pull can complete.")]
        private float minHoldTimeS = 0.15f;

        private Part _part;
        private Step _step;
        private Action<StepEvent> _emit;
        private HandGrabInteractable _interactable;

        private bool _bound;
        private bool _completed;
        private bool _isSelected;
        private Vector3 _startLocalPos;
        private float _selectStartTime;

        public void Bind(Part part, Step step, Action<StepEvent> emit)
        {
            if (_bound || _completed)
            {
                return;
            }

            _part = part;
            _step = step;
            _emit = emit;
            _bound = true;

            _startLocalPos = transform.localPosition;

            _interactable = GetComponent<HandGrabInteractable>();
            if (_interactable != null)
            {
                _interactable.WhenStateChanged += HandleStateChanged;
            }
        }

        private void HandleStateChanged(InteractableStateChangeArgs args)
        {
            if (!_bound || _completed)
            {
                return;
            }

            if (args.NewState == InteractableState.Select)
            {
                _isSelected = true;
                _selectStartTime = Time.time;
            }
            else if (args.PreviousState == InteractableState.Select)
            {
                _isSelected = false;
                CheckForCompletion();
            }
        }

        private void Update()
        {
            if (!_bound || _completed || !_isSelected)
            {
                return;
            }

            CheckForCompletion();
        }

        private void CheckForCompletion()
        {
            if (_completed || !_bound)
            {
                return;
            }

            if (Time.time - _selectStartTime < minHoldTimeS)
            {
                return;
            }

            float d = Vector3.Distance(transform.localPosition, _startLocalPos);
            if (d >= pullDistanceM)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (_completed || !_bound)
            {
                return;
            }

            _completed = true;
            _isSelected = false;

            try
            {
                if (_emit != null && _part != null && _step != null)
                {
                    _emit.Invoke(new StepEvent(_part.id, _step.action));
                }
            }
            catch (Exception)
            {
            }
        }

        private void OnDestroy()
        {
            if (_interactable != null)
            {
                _interactable.WhenStateChanged -= HandleStateChanged;
                _interactable = null;
            }
            _emit = null;
        }
    }
}
#endif
