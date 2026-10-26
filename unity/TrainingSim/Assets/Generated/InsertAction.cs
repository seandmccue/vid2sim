#if META_INTERACTION_SDK
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

namespace Vid2Sim.Generated
{
    public class InsertAction : MonoBehaviour, Vid2Sim.IStepAction
    {
        [SerializeField] float placeDistanceThreshold = 0.06f;
        [SerializeField] float settleTolerance = 0.01f;
        [SerializeField] float settleAngleTolerance = 7f;
        [SerializeField] float settleTimeRequired = 1.0f;

        Vid2Sim.Part _part;
        Vid2Sim.Step _step;
        System.Action<Vid2Sim.StepEvent> _emit;

        Transform _tracked;
        HandGrabInteractable _grab;

        bool _bound;
        bool _emitted;
        bool _isGrabbed;
        bool _hasGrabbedOnce;
        bool _awaitingSettle;

        Vector3 _initialPos;
        Quaternion _initialRot;
        Vector3 _grabStartPos;
        Quaternion _grabStartRot;
        Vector3 _settleAnchorPos;
        Quaternion _settleAnchorRot;
        float _settleTimer;
        float _maxDisplacement;

        public void Bind(Vid2Sim.Part part, Vid2Sim.Step step, System.Action<Vid2Sim.StepEvent> emit)
        {
            _part = part;
            _step = step;
            _emit = emit;
            _emitted = false;
            _isGrabbed = false;
            _hasGrabbedOnce = false;
            _awaitingSettle = false;
            _settleTimer = 0f;
            _maxDisplacement = 0f;

            GameObject partGo = gameObject;
            if (part is Component pc && pc != null)
            {
                partGo = pc.gameObject;
            }

            _tracked = partGo != null ? partGo.transform : transform;
            _initialPos = _tracked.position;
            _initialRot = _tracked.rotation;
            _settleAnchorPos = _initialPos;
            _settleAnchorRot = _initialRot;
            _grabStartPos = _initialPos;
            _grabStartRot = _initialRot;

            HandGrabInteractable found = null;
            if (partGo != null)
            {
                found = partGo.GetComponent<HandGrabInteractable>();
                if (found == null)
                {
                    found = partGo.GetComponentInChildren<HandGrabInteractable>();
                }
            }
            if (found == null)
            {
                found = GetComponent<HandGrabInteractable>();
            }

            if (_grab != null && _grab != found)
            {
                try { _grab.WhenStateChanged -= OnStateChanged; } catch { }
            }

            _grab = found;
            if (_grab != null)
            {
                try { _grab.WhenStateChanged -= OnStateChanged; } catch { }
                _grab.WhenStateChanged += OnStateChanged;
            }

            _bound = true;
        }

        void OnStateChanged(InteractableStateChangeArgs args)
        {
            if (!_bound || _emitted) return;
            if (_tracked == null) return;

            if (args.NewState == InteractableState.Select)
            {
                _isGrabbed = true;
                _hasGrabbedOnce = true;
                _awaitingSettle = false;
                _settleTimer = 0f;
                _grabStartPos = _tracked.position;
                _grabStartRot = _tracked.rotation;
                _maxDisplacement = 0f;
            }
            else if (args.PreviousState == InteractableState.Select)
            {
                _isGrabbed = false;
                float d = Vector3.Distance(_grabStartPos, _tracked.position);
                if (d > _maxDisplacement) _maxDisplacement = d;
                if (_hasGrabbedOnce && _maxDisplacement >= placeDistanceThreshold)
                {
                    _awaitingSettle = true;
                    _settleTimer = 0f;
                    _settleAnchorPos = _tracked.position;
                    _settleAnchorRot = _tracked.rotation;
                }
                else
                {
                    _awaitingSettle = false;
                }
            }
        }

        void Update()
        {
            if (!_bound || _emitted || _tracked == null) return;
            if (_emit == null) return;

            if (_isGrabbed)
            {
                float d = Vector3.Distance(_grabStartPos, _tracked.position);
                if (d > _maxDisplacement) _maxDisplacement = d;
                return;
            }

            if (_grab != null)
            {
                if (!_hasGrabbedOnce || !_awaitingSettle) return;
            }
            else
            {
                float dInit = Vector3.Distance(_initialPos, _tracked.position);
                if (dInit < placeDistanceThreshold)
                {
                    _settleAnchorPos = _tracked.position;
                    _settleAnchorRot = _tracked.rotation;
                    _settleTimer = 0f;
                    return;
                }
                if (!_hasGrabbedOnce)
                {
                    _hasGrabbedOnce = true;
                    _settleAnchorPos = _tracked.position;
                    _settleAnchorRot = _tracked.rotation;
                    _settleTimer = 0f;
                    return;
                }
            }

            float posMove = Vector3.Distance(_tracked.position, _settleAnchorPos);
            float angMove = Quaternion.Angle(_tracked.rotation, _settleAnchorRot);
            if (posMove > settleTolerance || angMove > settleAngleTolerance)
            {
                _settleAnchorPos = _tracked.position;
                _settleAnchorRot = _tracked.rotation;
                _settleTimer = 0f;
                return;
            }

            _settleTimer += Time.deltaTime;
            if (_settleTimer >= settleTimeRequired)
            {
                Complete();
            }
        }

        void Complete()
        {
            if (_emitted) return;
            _emitted = true;
            _awaitingSettle = false;
            var cb = _emit;
            if (cb != null)
            {
                cb.Invoke(new Vid2Sim.StepEvent(_part.id, _step.action));
            }
        }

        void OnDestroy()
        {
            if (_grab != null)
            {
                try { _grab.WhenStateChanged -= OnStateChanged; } catch { }
                _grab = null;
            }
            _emit = null;
        }
    }
}
#endif
