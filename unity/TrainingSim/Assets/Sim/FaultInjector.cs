using System.Collections.Generic;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Makes inspect steps a judgment: before an attempt, every part that an inspect step looks at is set
    /// good or faulty at random (one state per part), so "inspect the lift chain" means deciding whether
    /// the cotter pins are there, not hovering for two seconds. The spec's check/failure text explains a
    /// wrong call. Re-planned on every restart, so a repeat is a different exam.
    /// </summary>
    public class FaultInjector : MonoBehaviour
    {
        [Range(0f, 1f)] public float faultRate = 0.5f;
        [Tooltip("0 = fresh random each plan; any other value makes the plan repeatable (tests)")]
        public int seed = 0;

        readonly Dictionary<string, bool> _plan = new();
        public IReadOnlyDictionary<string, bool> Plan => _plan;

        public void PlanFaults(ProcedureSpec spec, PartFactory factory)
        {
            _plan.Clear();
            if (spec == null || factory == null) return;
            var rng = seed != 0 ? new System.Random(seed) : new System.Random();
            foreach (var s in spec.steps)
            {
                if (s.action != Actions.Inspect || _plan.ContainsKey(s.part_id)) continue;
                _plan[s.part_id] = rng.NextDouble() < faultRate;
            }
            // never a flawless exam when there is more than one thing to inspect
            if (_plan.Count > 1 && !_plan.ContainsValue(true))
            {
                var keys = new List<string>(_plan.Keys);
                _plan[keys[rng.Next(keys.Count)]] = true;
            }
            foreach (var kv in _plan)
                if (factory.Spawned.TryGetValue(kv.Key, out var part)) part.SetFault(kv.Value);
            foreach (var part in factory.Spawned.Values)
                if (!_plan.ContainsKey(part.Part.id)) part.SetFault(false);
        }

        public bool IsFaulty(string partId) => _plan.TryGetValue(partId, out var f) && f;

        /// <summary>How many inspect steps look at a faulty part (the exam's answer key size).</summary>
        public int FaultyInspectSteps(ProcedureSpec spec)
        {
            int n = 0;
            foreach (var s in spec.steps) if (s.action == Actions.Inspect && IsFaulty(s.part_id)) n++;
            return n;
        }
    }
}
