using System;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Holds the current step, arms a detector for the expected part + action, advances on the matching
    /// StepEvent, raises WrongPart for touches elsewhere. Safety steps cannot be skipped: Skip() on one ends
    /// the attempt with a fail, which is what industrial clients ask for.
    /// </summary>
    public class StepSequencer : MonoBehaviour
    {
        public ProcedureSpec Spec { get; private set; }
        public int Index { get; private set; } = -1;
        public Step Current => Spec != null && Index >= 0 && Index < Spec.steps.Length ? Spec.steps[Index] : null;
        public bool Running { get; private set; }
        public bool Paused { get; private set; }

        public event Action<Step, int> StepStarted;
        public event Action<Step, float> StepCompleted;    // step, seconds it took
        public event Action<Step, PartBehaviour> WrongPart;
        public event Action<Step> SafetySkipped;
        public event Action<bool> Finished;                // passed?
        /// <summary>Raised once per step when nothing has happened for stallSeconds: the coach repeats the
        /// instruction and the expected part is highlighted (the pitch's "coaching when the trainee stalls").</summary>
        public event Action<Step, PartBehaviour> Stalled;
        /// <summary>Inspect step: the trainee's Pass/Fail call was wrong (part, they said "fault"). The step stays open.</summary>
        public event Action<Step, PartBehaviour, bool> Misjudged;
        public float stallSeconds = 20f;
        bool _stallRaised;

        PartFactory _factory;
        ActionDetector _detector;
        float _stepStart;

        public void Begin(ProcedureSpec spec, PartFactory factory)
        {
            Spec = spec; _factory = factory;
            _detector = _detector ? _detector : gameObject.AddComponent<ActionDetector>();
            Index = -1; Running = true; Paused = false;
            Next();
        }

        public void Pause(bool on)
        {
            if (!Running) return;
            Paused = on;
            _detector.enabled = !on;
            Time.timeScale = on ? 0f : 1f;
        }

        public void Stop()
        {
            Running = false;
            _detector?.Disarm();
            Time.timeScale = 1f;
        }

        /// <summary>Trainer/debug skip. Skipping a safety step fails the attempt.</summary>
        public void Skip()
        {
            var s = Current;
            if (!Running || s == null) return;
            if (s.safety)
            {
                SafetySkipped?.Invoke(s);
                Stop();
                Finished?.Invoke(false);
                return;
            }
            Next();
        }

        void Next()
        {
            _detector.Disarm();
            Index++;
            if (Index >= Spec.steps.Length)
            {
                Running = false;
                Finished?.Invoke(true);
                return;
            }
            _stepStart = Time.unscaledTime;
            _stallRaised = false;
            var step = Current;
            _detector.Arm(step, _factory, OnEvent, p => { _stepStart = Time.unscaledTime; WrongPart?.Invoke(step, p); },
                          (p, saidFault) => { _stepStart = Time.unscaledTime; Misjudged?.Invoke(step, p, saidFault); });
            StepStarted?.Invoke(step, Index);
        }

        void Update()
        {
            if (!Running || Paused || _stallRaised || Current == null) return;
            if (Time.unscaledTime - _stepStart < stallSeconds) return;
            _stallRaised = true;
            _factory.Spawned.TryGetValue(Current.part_id, out var expected);
            Stalled?.Invoke(Current, expected);
        }

        /// <summary>Tests and debug menus: pretend the stall timer elapsed.</summary>
        public void ForceStallCheck() { _stepStart = Time.unscaledTime - stallSeconds - 1f; Update(); }

        void OnEvent(StepEvent e)
        {
            var s = Current;
            if (!Running || Paused || s == null) return;
            if (e.PartId != s.part_id || e.Action != s.action) return;
            StepCompleted?.Invoke(s, Time.unscaledTime - _stepStart);
            Next();
        }
    }
}
