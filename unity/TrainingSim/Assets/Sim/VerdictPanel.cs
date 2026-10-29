using System;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Two bench buttons for judgment steps, LOOKS OK (green) and FAULT (red), at the front of the bench.
    /// Pokeable with hands, clickable with the mouse (both through the interaction backend as press parts);
    /// the web HUD's buttons and the keys 1 / 2 call Give() too. The verdict goes to the current step's part.
    /// </summary>
    public class VerdictPanel : MonoBehaviour
    {
        public static readonly Color OkColor = new(0.36f, 0.72f, 0.48f), FaultColor = new(0.90f, 0.32f, 0.28f);
        PartFactory _factory;
        Func<Step> _current;
        PartBehaviour _ok, _fault;

        public static VerdictPanel Create(PartFactory factory, Transform bench, ProcedureSpec spec, float reachWidth)
        {
            bool any = false;
            foreach (var s in spec.steps) if (s.action == Actions.Inspect) { any = true; break; }
            if (!any) return null;
            var go = new GameObject("VerdictPanel");
            go.transform.SetParent(bench, false);
            var panel = go.AddComponent<VerdictPanel>();
            panel._factory = factory;
            panel._ok = BenchControls.Button(factory, bench, "__verdict_ok", "LOOKS OK", OkColor, new Vector3(-reachWidth / 2f - 0.02f, 0f, -0.15f));
            panel._fault = BenchControls.Button(factory, bench, "__verdict_fault", "FAULT", FaultColor, new Vector3(reachWidth / 2f + 0.02f, 0f, -0.15f));
            panel._ok.Selected += _ => panel.Give(false);
            panel._fault.Selected += _ => panel.Give(true);
            return panel;
        }

        /// <summary>Hook the sequencer so a verdict reaches the part the current step inspects.</summary>
        public void Attach(StepSequencer seq) { _current = () => seq.Current; }

        /// <summary>True while the current step wants a verdict (drives HUD and button pulsing).</summary>
        public bool Wanted => _current?.Invoke()?.action == Actions.Inspect;

        public void Give(bool faultFound)
        {
            var s = _current?.Invoke();
            if (s == null || s.action != Actions.Inspect) return;
            if (_factory.Spawned.TryGetValue(s.part_id, out var part)) part.GiveVerdict(faultFound);
        }

        void Update()
        {
            if (_ok == null || _fault == null) return;
            float pulse = Wanted ? 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 4f) : 1f;
            _ok.transform.localScale = BenchControls.Size * pulse;
            _fault.transform.localScale = BenchControls.Size * pulse;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Give(false);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Give(true);
        }
    }
}
