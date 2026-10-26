using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>Step order, seconds per step, wrong-part touches, safety skips. Writes a result JSON at the end.</summary>
    public class Scorer : MonoBehaviour
    {
        [Serializable] public class StepResult { public int order; public string part_id; public string action; public float seconds; public int wrong_part_touches; }
        [Serializable] public class Result
        {
            public string procedure_id; public string started_at; public float total_seconds; public bool passed;
            public int steps_total; public int steps_completed; public int wrong_part_touches; public bool safety_skipped;
            public int score; public List<StepResult> steps = new();
        }

        public Result Current { get; private set; }
        public event Action<Result> Saved;

        StepResult _live;
        float _t0;

        public void Attach(StepSequencer seq)
        {
            seq.StepStarted += (s, i) =>
            {
                if (Current == null) Begin(seq.Spec);
                _live = new StepResult { order = s.order, part_id = s.part_id, action = s.action };
            };
            seq.StepCompleted += (s, secs) =>
            {
                if (_live == null) return;
                _live.seconds = secs; Current.steps.Add(_live); Current.steps_completed++; _live = null;
            };
            seq.WrongPart += (s, p) => { Current.wrong_part_touches++; if (_live != null) _live.wrong_part_touches++; };
            seq.SafetySkipped += s => Current.safety_skipped = true;
            seq.Finished += passed => Finish(passed);
        }

        public void Begin(ProcedureSpec spec)
        {
            Current = new Result { procedure_id = spec.procedure.id, started_at = DateTime.UtcNow.ToString("o"), steps_total = spec.steps.Length };
            _t0 = Time.unscaledTime;
        }

        void Finish(bool passed)
        {
            if (Current == null) return;
            Current.total_seconds = Time.unscaledTime - _t0;
            Current.passed = passed && !Current.safety_skipped;
            // 100 base, -10 per wrong touch, -0.5/s over a 20 s/step budget; safety skip is a hard 0
            float budget = 20f * Current.steps_total;
            float score = 100f - 10f * Current.wrong_part_touches - Mathf.Max(0f, Current.total_seconds - budget) * 0.5f;
            Current.score = Current.safety_skipped ? 0 : Mathf.Clamp(Mathf.RoundToInt(score), 0, 100);
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "results");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{Current.procedure_id}_{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(path, JsonUtility.ToJson(Current, true));
                Debug.Log($"[Scorer] wrote {path}");
            }
            catch (Exception e) { Debug.LogWarning("[Scorer] could not write result: " + e.Message); }
            Saved?.Invoke(Current);
        }
    }
}
