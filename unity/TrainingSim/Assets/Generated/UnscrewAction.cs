#if META_INTERACTION_SDK && !UNITY_WEBGL
using Oculus.Interaction.HandGrab;
using System;
using UnityEngine;
using Oculus.Interaction;

namespace Vid2Sim.Generated
{
    public class UnscrewAction : MonoBehaviour, IStepAction
    {
        [SerializeField] private float requiredCcwDegrees = 720f;
        [SerializeField] private float requiredPullOutM = 0.015f;

        private Part _part;
        private Step _step;
        private Action<StepEvent> _emit;
        private HandGrabInteractable _grab;

        private bool _emitted;
        private bool _isGrabbed;
        private Vector3 _startPos;
        private Vector3 _startAxis = Vector3.up;
        private Vector3 _prevRadial = Vector3.right;
        private float _ccwAccum;

        public void Bind(Part part, Step step, System.Action<StepEvent> emit)
        {
            _part = part;
            _step = step;
            _emit = emit;
            _emitted = false;
            _ccwAccum = 0f;
            _isGrabbed = false;

            _startPos = transform.position;
            Vector3 a = transform.up;
            if (a.sqrMagnitude < 1e-8f)
            {
                a = Vector3.up;
            }
            _startAxis = a.normalized;

            _grab = GetComponent<HandGrabInteractable>();
            if (_grab != null)
            {
                _grab.WhenStateChanged -= OnStateChanged;
                _grab.WhenStateChanged += OnStateChanged;
            }

            _prevRadial = CurrentRadial();
        }

        private void OnStateChanged(InteractableStateChangeArgs args)
        {
            if (args.NewState == InteractableState.Select)
            {
                _isGrabbed = true;
                _prevRadial = CurrentRadial();
            }
            else if (args.PreviousState == InteractableState.Select)
            {
                _isGrabbed = false;
                _prevRadial = CurrentRadial();
            }
        }

        private Vector3 CurrentRadial()
        {
            Vector3 axis = _startAxis;
            if (axis.sqrMagnitude < 1e-8f)
            {
                axis = Vector3.up;
            }
            axis = axis.normalized;

            Vector3 r = transform.right;
            Vector3 radial = r - axis * Vector3.Dot(r, axis);
            if (radial.sqrMagnitude < 1e-6f)
            {
                r = transform.forward;
                radial = r - axis * Vector3.Dot(r, axis);
            }
            if (radial.sqrMagnitude < 1e-6f)
            {
                return _prevRadial.sqrMagnitude > 1e-6f ? _prevRadial.normalized : Vector3.right;
            }
            return radial.normalized;
        }

        private void Update()
        {
            if (_emitted || _grab == null || _part == null)
            {
                return;
            }

            if (!_isGrabbed)
            {
                return;
            }

            Vector3 curr = CurrentRadial();
            Vector3 prev = _prevRadial;
            if (prev.sqrMagnitude < 1e-6f)
            {
                _prevRadial = curr;
                return;
            }

            prev = prev.normalized;
            curr = curr.normalized;

            float delta = Vector3.SignedAngle(prev, curr, _startAxis);
            if (float.IsNaN(delta) || float.IsInfinity(delta))
            {
                delta = 0f;
            }
            _ccwAccum += delta;
            _prevRadial = curr;

            float axial = Vector3.Dot(transform.position - _startPos, _startAxis);

            if (_ccwAccum >= requiredCcwDegrees || axial >= requiredPullOutM)
            {
                TryEmit();
            }
        }

        private void TryEmit()
        {
            if (_emitted)
            {
                return;
            }
            _emitted = true;
            _isGrabbed = false;
            var e = _emit;
            _emit = null;
            if (e != null && _part != null && _step != null)
            {
                e(new StepEvent(_part.id, _step.action));
            }
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
