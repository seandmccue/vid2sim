using System;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Turns a part's select/hover/motion into one StepEvent for the action a step expects.
    /// One detector is live per step; it also reports touches on other parts as WrongPart.
    ///   press     select on a press part
    ///   grab      select
    ///   pull      select, then moved >= pullDistance from spawn
    ///   lift      select, then raised >= liftHeight above spawn
    ///   place     released within snapRadius of the part's SnapZone
    ///   turn_cw   accumulated clockwise yaw >= turnDegrees (turn_ccw mirrored)
    ///   inspect   hovered for >= inspectSeconds
    /// </summary>
    public class ActionDetector : MonoBehaviour
    {
        public float pullDistance = 0.10f;
        public float liftHeight = 0.08f;
        public float snapRadius = 0.06f;
        public float turnDegrees = 45f;
        public float inspectSeconds = 1.5f;

        Step _step;
        PartBehaviour _part;
        Action<StepEvent> _emit;
        Action<PartBehaviour> _wrong;
        PartFactory _factory;
        bool _done;
        MonoBehaviour _custom;

        public void Arm(Step step, PartFactory factory, Action<StepEvent> emit, Action<PartBehaviour> wrongPart)
        {
            Disarm();
            _step = step; _factory = factory; _emit = emit; _wrong = wrongPart; _done = false;
            factory.Spawned.TryGetValue(step.part_id, out _part);
            foreach (var p in factory.Spawned.Values)
            {
                if (p == _part) continue;
                p.Selected += OnWrongSelect;
            }
            if (_part == null) return;
            _part.Selected += OnSelect;
            _part.Unselected += OnUnselect;

            if (step.HasCustomAction && TryBindGenerated(step)) return;
        }

        public void Disarm()
        {
            if (_factory != null)
                foreach (var p in _factory.Spawned.Values) p.Selected -= OnWrongSelect;
            if (_part != null) { _part.Selected -= OnSelect; _part.Unselected -= OnUnselect; }
            if (_custom) Destroy(_custom);
            _custom = null; _part = null; _step = null;
        }

        bool TryBindGenerated(Step step)
        {
            // Assets/Generated/<Verb>Action.cs, compile-gated by the pipeline. Falls back to the vocabulary verb if absent.
            string cls = "";
            foreach (var w in step.custom_action.Split('_', '-', ' ')) if (w.Length > 0) cls += char.ToUpperInvariant(w[0]) + w.Substring(1);
            cls = "Vid2Sim.Generated." + cls + "Action";
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(cls);
                if (t == null || !typeof(IStepAction).IsAssignableFrom(t) || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                _custom = (MonoBehaviour)_part.gameObject.AddComponent(t);
                ((IStepAction)_custom).Bind(_part.Part, step, e => Complete());
                Debug.Log($"[ActionDetector] bound generated {cls} for '{step.custom_action}'");
                return true;
            }
            return false;
        }

        void OnWrongSelect(PartBehaviour p) { if (!_done) _wrong?.Invoke(p); }

        void OnSelect(PartBehaviour p)
        {
            if (_done || _custom) return;
            if (_step.action == Actions.Press || _step.action == Actions.Grab) Complete();
        }

        void OnUnselect(PartBehaviour p)
        {
            if (_done || _custom || _step.action != Actions.Place || _part.SnapZone == null) return;
            if (Vector3.Distance(_part.transform.position, _part.SnapZone.position) <= snapRadius)
            {
                _part.transform.SetPositionAndRotation(_part.SnapZone.position, _part.SnapZone.rotation);
                Complete();
            }
        }

        void Update()
        {
            if (_done || _part == null || _step == null || _custom) return;
            switch (_step.action)
            {
                case Actions.Pull:
                    if (_part.IsSelected && Vector3.Distance(_part.transform.position, _part.SpawnPosition) >= pullDistance) Complete();
                    break;
                case Actions.Lift:
                    if (_part.IsSelected && _part.transform.position.y - _part.SpawnPosition.y >= liftHeight) Complete();
                    break;
                case Actions.TurnCw:
                    if (_part.SignedYawFromSpawn() >= turnDegrees) Complete();
                    break;
                case Actions.TurnCcw:
                    if (_part.SignedYawFromSpawn() <= -turnDegrees) Complete();
                    break;
                case Actions.Inspect:
                    if (_part.HoverSeconds >= inspectSeconds) Complete();
                    break;
            }
        }

        void Complete()
        {
            if (_done) return;
            _done = true;
            _emit?.Invoke(new StepEvent(_step.part_id, _step.action));
        }
    }
}
