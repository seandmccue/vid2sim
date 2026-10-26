#if META_INTERACTION_SDK
using System;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

namespace Vid2Sim.Generated
{
    public class ScrewAction : MonoBehaviour, IStepAction
    {
        [SerializeField] float requiredTurnDegrees = 360f;
        [SerializeField] float maxTiltDegrees = 12f;
        [SerializeField] float maxPositionDrift = 0.03f;

        Part _part;
        Step _step;
        Action<StepEvent> _emit;

        Transform _target;
        HandGrabInteractable _handGrab;
        GrabInteractable _grab;

        bool _bound;
        bool _completed;
        bool _isSelected;

        float _totalTwistDeg;
        float _cwDeg;
        Quaternion _prevRotation;
        Vector3 _initialAxisWorld;
        Vector3 _initialPosition;

        static readonly Vector3 ScrewAxisLocal = Vector3.up;

        public void Bind(Part part, Step step, Action<StepEvent> emit)
        {
            if (_bound || part == null || step == null || emit == null)
                return;

            _part = part;
            _step = step;
            _emit = emit;

            _target = _part.transform;

            _handGrab = _part.GetComponentInChildren<HandGrabInteractable>(true);
            _grab = _part.GetComponentInChildren<GrabInteractable>(true);

            if (_handGrab != null)
                _target = _handGrab.transform;
            else if (_grab != null)
                _target = _grab.transform;

            if (_target == null)
                _target = _part.transform;

            _initialAxisWorld = _target.TransformDirection(ScrewAxisLocal).normalized;
            _initialPosition = _target.position;
            _prevRotation = _target.rotation;
            _totalTwistDeg = 0f;
            _cwDeg = 0f;
            _isSelected = false;
            _completed = false;
            _bound = true;

            if (_handGrab != null)
                _handGrab.WhenStateChanged += OnStateChanged;
            if (_grab != null)
                _grab.WhenStateChanged += OnStateChanged;
        }

        void OnStateChanged(InteractableStateChangeArgs args)
        {
            _isSelected = args.NewState == InteractableState.Select;
            if (_isSelected && _target != null)
            {
                _prevRotation = _target.rotation;
            }
        }

        void Update()
        {
            if (!_bound || _completed || _target == null || _part == null || _step == null)
                return;

            Vector3 curAxisWorld = _target.TransformDirection(ScrewAxisLocal).normalized;
            float tilt = Vector3.Angle(curAxisWorld, _initialAxisWorld);

            Quaternion delta = _target.rotation * Quaternion.Inverse(_prevRotation);
            float turn = 0f;
            float angle;
            Vector3 axis;
            delta.ToAngleAxis(out angle, out axis);
            if (angle > 180f)
                angle -= 360f;
            if (Mathf.Abs(angle) > 0.001f && axis.sqrMagnitude > 0.001f)
            {
                turn = angle * Vector3.Dot(axis.normalized, curAxisWorld);
            }

            if (tilt <= maxTiltDegrees)
            {
                _totalTwistDeg += Mathf.Abs(turn);
                if (turn < 0f)
                    _cwDeg += -turn;
            }

            _prevRotation = _target.rotation;

            float drift = Vector3.Distance(_target.position, _initialPosition);

            if (_totalTwistDeg >= requiredTurnDegrees && tilt <= maxTiltDegrees && drift <= maxPositionDrift)
            {
                Complete();
            }
        }

        void Complete()
        {
            if (_completed)
                return;
            _completed = true;
            var handler = _emit;
            if (handler != null && _part != null && _step != null)
            {
                handler(new StepEvent(_part.id, _step.action));
            }
        }

        void OnDestroy()
        {
            if (_handGrab != null)
                _handGrab.WhenStateChanged -= OnStateChanged;
            if (_grab != null)
                _grab.WhenStateChanged -= OnStateChanged;
            _emit = null;
            _bound = false;
        }
    }
}
#endif
