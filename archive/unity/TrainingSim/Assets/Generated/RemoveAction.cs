#if META_INTERACTION_SDK && !UNITY_WEBGL
using UnityEngine;
using System;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Grab;

namespace Vid2Sim.Generated
{
    public class RemoveAction : MonoBehaviour, Vid2Sim.IStepAction
    {
        [SerializeField] float liftDistanceM = 0.12f;
        [SerializeField] float upwardLiftM = 0.08f;

        Vid2Sim.Part _part;
        Vid2Sim.Step _step;
        System.Action<Vid2Sim.StepEvent> _emit;
        HandGrabInteractable _grab;
        Vector3 _anchorPos;
        bool _wasGrabbed;
        bool _completed;
        bool _bound;

        public void Bind(Vid2Sim.Part part, Vid2Sim.Step step, System.Action<Vid2Sim.StepEvent> emit)
        {
            if (_bound && _grab != null)
            {
                _grab.WhenStateChanged -= HandleStateChanged;
            }

            _part = part;
            _step = step;
            _emit = emit;
            _completed = false;
            _wasGrabbed = false;
            _anchorPos = transform.position;
            _bound = true;

            _grab = GetComponent<HandGrabInteractable>();
            if (_grab != null)
            {
                _grab.WhenStateChanged += HandleStateChanged;
                if (_grab.State == InteractableState.Select)
                {
                    _wasGrabbed = true;
                    _anchorPos = transform.position;
                }
            }
            else
            {
                _wasGrabbed = true;
            }
        }

        void HandleStateChanged(InteractableStateChangeArgs args)
        {
            if (_completed || !_bound)
            {
                return;
            }

            if (args.NewState == InteractableState.Select)
            {
                _wasGrabbed = true;
                _anchorPos = transform.position;
            }
        }

        void Update()
        {
            if (_completed || !_bound || !_wasGrabbed || _emit == null)
            {
                return;
            }

            Vector3 current = transform.position;
            float dist = Vector3.Distance(current, _anchorPos);
            float up = current.y - _anchorPos.y;

            if (dist >= liftDistanceM || up >= upwardLiftM)
            {
                Complete();
            }
        }

        void Complete()
        {
            if (_completed)
            {
                return;
            }
            _completed = true;
            try
            {
                if (_emit != null && _part != null && _step != null)
                {
                    _emit(new Vid2Sim.StepEvent(_part.id, _step.action));
                }
            }
            catch (Exception)
            {
            }
        }

        void OnDestroy()
        {
            if (_grab != null)
            {
                _grab.WhenStateChanged -= HandleStateChanged;
            }
            _emit = null;
        }
    }
}
#endif
