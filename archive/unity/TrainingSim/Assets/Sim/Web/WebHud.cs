using System.Collections.Generic;
using UnityEngine;
using Vid2Sim.Interaction;

namespace Vid2Sim
{
    /// <summary>
    /// Screen-space HUD for the browser output: big step text, a plain how-to line, a progress bar, the step
    /// list with checkmarks, and toasts for done / wrong part / stall. Replaces the small world-space panel on
    /// the web (the Quest keeps the panel in front of the bench).
    /// </summary>
    public class WebHud : MonoBehaviour
    {
        StepSequencer _seq; StepGuide _guide; Scorer _scorer; PartFactory _factory; ProcedureSpec _spec;
        string _title = "", _stepText = "", _how = "", _toast = "", _caption = "";
        Color _toastColor = Color.white;
        float _toastUntil;
        int _index = -1, _count;
        readonly HashSet<int> _done = new();
        bool _finished, _passed;
        GUIStyle _h1, _h2, _body, _small, _pill, _toastStyle, _btn, _btnHot;
        Rect _barRect;

        public void Attach(StepSequencer seq, StepGuide guide, Scorer scorer, PartFactory factory, string title, ProcedureSpec spec)
        {
            _seq = seq; _guide = guide; _scorer = scorer; _factory = factory; _title = title; _spec = spec;
            _count = spec?.steps.Length ?? 0;
            seq.StepStarted += (s, i) =>
            {
                _index = i; _count = _spec.steps.Length;
                _stepText = (ExamMode.On ? "Exam, step " : "Step ") + $"{s.order} of {_count}: {s.instruction}";
                _how = ExamMode.On ? (s.action == Actions.Inspect ? "Look it over, then make the call. Every call is final; you'll see how you did at the end." : "No hints in an exam. Find it and do it.")
                                   : StepGuide.HowTo(s, factory.Backend);
                if (s.safety) _how += "  (safety step: can't be skipped)";
            };
            seq.StepCompleted += (s, secs) =>
            {
                _done.Add(s.order);
                if (ExamMode.On) { Toast(s.action == Actions.Inspect ? "Call recorded." : $"Step {s.order} done.", new Color(0.75f, 0.8f, 0.9f), 1.5f); return; }
                string what = s.action == Actions.Inspect
                    ? (factory.Spawned.TryGetValue(s.part_id, out var ip) && ip.Faulty ? $"Fault caught: {s.failure}" : $"Right, it's fine: {s.check}")
                    : $"Step {s.order} done in {secs:0.0}s";
                Toast(what, new Color(0.3f, 0.9f, 0.5f), s.action == Actions.Inspect ? 4f : 2.5f);
            };
            seq.Misjudged += (s, p, saidFault) =>
                Toast($"Not quite. Look for: {s.check}. " + (p.Faulty ? $"This one is faulty: {s.failure}" : "This one is fine."), new Color(1f, 0.55f, 0.4f), 6f);
            seq.WrongPart += (s, p) => Toast($"That's the {p.Part.name}. You need the {NameOf(s.part_id)}.", new Color(1f, 0.55f, 0.4f));
            seq.Stalled += (s, p) => Toast(ExamMode.On ? $"Still on step {s.order}: {s.instruction}" : $"Stuck? Watch the yellow ghost. {s.coach}", new Color(1f, 0.85f, 0.3f), 5f);
            seq.Finished += passed => { _finished = true; _passed = passed; };
            guide.CaptionChanged += c => _caption = c;
        }

        string NameOf(string partId) => _factory != null && _factory.Spawned.TryGetValue(partId, out var p) ? p.Part.name : partId;

        void Toast(string text, Color c, float seconds = 2.5f) { _toast = text; _toastColor = c; _toastUntil = Time.unscaledTime + seconds; }

        void Styles()
        {
            if (_h1 != null) return;
            _h1 = new GUIStyle(GUI.skin.label) { fontSize = 36, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, wordWrap = true };
            _h2 = new GUIStyle(GUI.skin.label) { fontSize = 26, normal = { textColor = new Color(0.55f, 0.85f, 1f) }, wordWrap = true };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = new Color(0.85f, 0.88f, 0.95f) } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = new Color(0.6f, 0.65f, 0.75f) } };
            _pill = new GUIStyle(GUI.skin.box) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _toastStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Color.white } };
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, hover = { textColor = Color.white } };
            _btnHot = new GUIStyle(_btn) { normal = { textColor = new Color(0.35f, 0.95f, 1f) }, hover = { textColor = new Color(0.35f, 0.95f, 1f) } };
        }

        /// <summary>
        /// Bottom action bar: which part is selected and one button per motion, acting on it. The button the
        /// current step needs is drawn in cyan, matching the arrow on the part. Keys are listed on the buttons.
        /// </summary>
        void ActionBar(float W, float H)
        {
            var sel = SimpleMouseDriver.Active;
            bool running = !_finished && _index >= 0 && string.IsNullOrEmpty(_caption);
            float bw = 150f, bh = 44f, gap = 8f; int count = 6;
            float barW = count * bw + (count - 1) * gap + 40f, barH = 96f;
            _barRect = new Rect(W / 2f - barW / 2f, H - 60f - barH, barW, barH);
            SimpleMouseDriver.PointerOverHud = running && _barRect.Contains(new Vector2(Input.mousePosition.x, H - Input.mousePosition.y));
            if (!running) return;
            HudPanel.Draw(_barRect);
            string need = _spec.steps[_index].action;
            float x = _barRect.x + 20, y = _barRect.y + 40;
            if (need != Actions.Inspect)
            {
                string who = sel != null ? $"Selected: {sel.part.Part.name}" : "Click a part to select it, then use these (or drag it).";
                GUI.Label(new Rect(_barRect.x + 20, _barRect.y + 6, barW - 40, 26), who, sel != null ? _body : _small);
            }
            if (need == Actions.Inspect)
            {
                // a judgment step: the two verdict buttons replace the motion buttons
                GUI.Label(new Rect(_barRect.x + 20, _barRect.y + 6, barW - 40, 26), ExamMode.On ? "Make the call. It's final." : $"Look it over, then make the call: {_spec.steps[_index].check}", _body);
                var v = _factory.Verdicts;
                GUI.enabled = v != null;
                float vw = 2 * bw + gap + 60f;
                x = W / 2f - vw / 2f;
                if (GUI.Button(new Rect(x, y, bw + 30f, bh), "Looks OK  1", _btnHot)) v.Give(false);
                if (GUI.Button(new Rect(x + bw + 30f + gap, y, bw + 30f, bh), "Fault found  2", _btnHot)) v.Give(true);
                GUI.enabled = true;
                return;
            }
            GUI.enabled = sel != null && !sel.isPress;
            Button(ref x, y, bw, bh, "↺ Turn left  Q", need == Actions.TurnCcw, () => sel.Turn(-sel.turnStepDegrees));
            Button(ref x, y, bw, bh, "↻ Turn right  E", need == Actions.TurnCw, () => sel.Turn(sel.turnStepDegrees));
            Button(ref x, y, bw, bh, "↑ Lift  W", need == Actions.Lift, () => sel.Lift(1));
            Button(ref x, y, bw, bh, "↓ Lower  S", false, () => sel.Lift(-1));
            Button(ref x, y, bw, bh, "⇣ Pull out  X", need == Actions.Pull, () => sel.Pull(1));
            Button(ref x, y, bw, bh, "⇡ Push back  Z", false, () => sel.Pull(-1));
            GUI.enabled = true;
        }

        void Button(ref float x, float y, float w, float h, string label, bool hot, System.Action act)
        {
            if (GUI.Button(new Rect(x, y, w, h), label, hot ? _btnHot : _btn)) act();
            x += w + 8f;
        }

        void OnGUI()
        {
            if (_seq == null || _spec == null) return;
            Styles();
            float W = Screen.width, H = Screen.height;

            // top band: title, step, how, progress
            HudPanel.Draw(new Rect(0, 0, W, 150));
            GUI.Label(new Rect(24, 8, W - 48, 28), _title, _small);
            if (!string.IsNullOrEmpty(_caption))
            {
                GUI.Label(new Rect(24, 36, W - 48, 48), _caption, _h1);
                GUI.Label(new Rect(24, 90, W - 48, 36), "Watch the yellow ghost. The real run starts right after.", _h2);
            }
            else if (_finished)
            {
                var r = _scorer != null ? _scorer.Current : null;
                bool exam = r != null && r.IsExam;
                GUI.Label(new Rect(24, 36, W - 48, 48), exam ? (_passed ? "Exam passed." : "Exam over.") : (_passed ? "Procedure complete." : "Attempt failed."), _h1);
                GUI.Label(new Rect(24, 90, W - 48, 36), r != null ? Scorer.Summary(r) + (exam ? "  R = new exam, T = back to guided." : "  R = try again, T = exam mode.") : "Press R to try again.", _h2);
            }
            else
            {
                GUI.Label(new Rect(24, 36, W - 48, 48), _stepText, _h1);
                GUI.Label(new Rect(24, 90, W - 48, 36), _how, _h2);
            }
            float frac = _count > 0 ? (_finished ? 1f : _index / (float)_count) : 0f;
            HudPanel.Draw(new Rect(24, 134, W - 48, 8), new Color(0.3f, 0.32f, 0.38f));
            HudPanel.Draw(new Rect(24, 134, (W - 48) * frac, 8), new Color(0.3f, 0.9f, 0.5f));

            // left: step list
            var steps = _spec.steps;
            float y = 170f;
            HudPanel.Draw(new Rect(16, y - 8, 380, 20 + steps.Length * 28));
            for (int i = 0; i < steps.Length; i++)
            {
                string mark = _done.Contains(steps[i].order) ? "✓" : (i == _index && !_finished ? "▶" : " ");
                var st = i == _index && !_finished ? _body : _small;
                GUI.Label(new Rect(28, y + i * 28, 360, 26), $"{mark} {steps[i].order}. {Shorten(steps[i].instruction, 36)}", st);
            }

            ActionBar(W, H);

            // end of an exam: what was missed, with the spec's failure text, so the exam still teaches
            if (_finished && _scorer != null && _scorer.Current != null && _scorer.Current.IsExam)
            {
                var rv = _scorer.Current.reveal;
                float ph = 60f + Mathf.Max(1, rv.Count) * 34f, pw = Mathf.Min(900f, W - 440f);
                var pr = new Rect(W / 2f - pw / 2f + 190f, H / 2f - ph / 2f, pw, ph);
                HudPanel.Draw(pr);
                GUI.Label(new Rect(pr.x + 20, pr.y + 12, pw - 40, 30), rv.Count == 0 ? "Every call was right." : "Where it went wrong:", _h2);
                for (int i = 0; i < rv.Count; i++)
                    GUI.Label(new Rect(pr.x + 20, pr.y + 48 + i * 34, pw - 40, 32), rv[i], _body);
            }

            // toast
            if (Time.unscaledTime < _toastUntil)
            {
                var tr = new Rect(W / 2f - 320, H - 220, 640, 56);
                HudPanel.Draw(tr);
                var old = GUI.color; GUI.color = _toastColor;
                GUI.Label(tr, _toast, _toastStyle);
                GUI.color = old;
            }
        }

        static string Shorten(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }
}
