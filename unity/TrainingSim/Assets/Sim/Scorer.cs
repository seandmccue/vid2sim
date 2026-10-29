using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>Step order, seconds per step, wrong-part touches, safety skips. Writes a result JSON at the end.</summary>
    public class Scorer : MonoBehaviour
    {
        [Serializable] public class StepResult { public int order; public string part_id; public string action; public float seconds; public int wrong_part_touches; public string verdict; }
        [Serializable] public class Result
        {
            public string procedure_id; public string mode; public string started_at; public float total_seconds; public bool passed;
            public int steps_total; public int steps_completed; public int wrong_part_touches; public bool safety_skipped;
            public int misjudgements; public int faults_total; public int faults_caught; public int faults_missed; public int false_alarms;
            public int score; public List<StepResult> steps = new();
            /// <summary>What the trainee got wrong on inspect steps, revealed at the end: "Missed: Lift chain. chain jumps..."</summary>
            public List<string> reveal = new();
            public bool IsExam => mode == "exam";
        }

        [Tooltip("Exam mode: score = faults caught, false alarms cost, pass = every fault caught and no false alarm")]
        public bool exam = false;
        ProcedureSpec _spec;

        public Result Current { get; private set; }
        public event Action<Result> Saved;

        StepResult _live;
        float _t0;
        FaultInjector _faults;
        public void UseFaults(FaultInjector faults) { _faults = faults; }

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
            seq.Misjudged += (s, p, said) => Current.misjudgements++;
            seq.Verdict += (s, p, said) =>
            {
                // the call that closed the step: always right in guided mode, whatever they said in an exam
                string v = p.Faulty ? (said ? "caught" : "missed") : (said ? "false_alarm" : "ok");
                if (_live != null) _live.verdict = v;
                var part = _spec?.FindPart(s.part_id); string name = part != null ? part.name : s.part_id;
                switch (v)
                {
                    case "caught": Current.faults_caught++; break;
                    case "missed": Current.faults_missed++; Current.reveal.Add($"Missed: {name} was faulty. {s.failure}"); break;
                    case "false_alarm": Current.false_alarms++; Current.reveal.Add($"False alarm: {name} was fine."); break;
                }
            };
            seq.WrongPart += (s, p) => { Current.wrong_part_touches++; if (_live != null) _live.wrong_part_touches++; };
            seq.SafetySkipped += s => Current.safety_skipped = true;
            seq.Finished += passed => Finish(passed);
        }

        /// <summary>One line for the end of an attempt, in either mode.</summary>
        public static string Summary(Result r)
        {
            if (r == null) return "";
            if (r.IsExam && r.faults_total > 0)
                return $"Faults caught {r.faults_caught} of {r.faults_total}, {r.false_alarms} false alarm{(r.false_alarms == 1 ? "" : "s")}, {r.total_seconds:0}s. Score {r.score}.";
            return $"Score {r.score}. {r.total_seconds:0}s, {r.wrong_part_touches} wrong-part touches" + (r.faults_total > 0 ? $", faults caught {r.faults_caught}/{r.faults_total}, {r.misjudgements} wrong calls." : ".");
        }

        public void Begin(ProcedureSpec spec)
        {
            _spec = spec;
            Current = new Result { procedure_id = spec.procedure.id, mode = exam ? "exam" : "guided", started_at = DateTime.UtcNow.ToString("o"),
                                   steps_total = spec.steps.Length, faults_total = _faults != null ? _faults.FaultyInspectSteps(spec) : 0 };
            _t0 = Time.unscaledTime;
        }

        void Finish(bool passed)
        {
            if (Current == null) return;
            Current.total_seconds = Time.unscaledTime - _t0;
            Current.passed = passed && !Current.safety_skipped;
            float score;
            if (Current.IsExam && Current.faults_total > 0)
            {
                // the exam is about catching faults: 100 x caught / total, -15 per false alarm, -10 per wrong touch;
                // pass = every fault caught, no false alarm
                score = 100f * Current.faults_caught / Current.faults_total - 15f * Current.false_alarms - 10f * Current.wrong_part_touches;
                Current.passed = Current.passed && Current.faults_missed == 0 && Current.false_alarms == 0;
            }
            else
            {
                // 100 base, -10 per wrong touch or wrong call, -0.5/s over a 20 s/step budget
                float budget = 20f * Current.steps_total;
                score = 100f - 10f * Current.wrong_part_touches - 10f * Current.misjudgements - Mathf.Max(0f, Current.total_seconds - budget) * 0.5f;
            }
            Current.score = Current.safety_skipped ? 0 : Mathf.Clamp(Mathf.RoundToInt(score), 0, 100);   // safety skip is a hard 0
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
