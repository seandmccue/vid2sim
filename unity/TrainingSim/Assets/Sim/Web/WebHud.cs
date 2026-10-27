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
                _stepText = $"Step {s.order} of {_count}: {s.instruction}";
                _how = StepGuide.HowTo(s, factory.Backend);
                if (s.safety) _how += "  (safety step: can't be skipped)";
            };
            seq.StepCompleted += (s, secs) => { _done.Add(s.order); Toast($"Step {s.order} done in {secs:0.0}s", new Color(0.3f, 0.9f, 0.5f)); };
            seq.WrongPart += (s, p) => Toast($"That's the {p.Part.name}. You need the {NameOf(s.part_id)}.", new Color(1f, 0.55f, 0.4f));
            seq.Stalled += (s, p) => Toast($"Stuck? Watch the yellow ghost. {s.coach}", new Color(1f, 0.85f, 0.3f), 5f);
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
            _toastStyle = new GUIStyle(GUI.skin.box) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
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
            GUI.Box(_barRect, GUIContent.none); GUI.Box(_barRect, GUIContent.none);
            string who = sel != null ? $"Selected: {sel.part.Part.name}" : "Click a part to select it, then use these (or drag it).";
            GUI.Label(new Rect(_barRect.x + 20, _barRect.y + 6, barW - 40, 26), who, sel != null ? _body : _small);
            string need = _spec.steps[_index].action;
            float x = _barRect.x + 20, y = _barRect.y + 40;
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
            GUI.Box(new Rect(0, 0, W, 150), GUIContent.none); GUI.Box(new Rect(0, 0, W, 150), GUIContent.none);
            GUI.Label(new Rect(24, 8, W - 48, 28), _title, _small);
            if (!string.IsNullOrEmpty(_caption))
            {
                GUI.Label(new Rect(24, 36, W - 48, 48), _caption, _h1);
                GUI.Label(new Rect(24, 90, W - 48, 36), "Watch the yellow ghost. The real run starts right after.", _h2);
            }
            else if (_finished)
            {
                var r = _scorer != null ? _scorer.Current : null;
                GUI.Label(new Rect(24, 36, W - 48, 48), _passed ? "Procedure complete." : "Attempt failed.", _h1);
                GUI.Label(new Rect(24, 90, W - 48, 36), r != null ? $"Score {r.score}. {r.total_seconds:0}s, {r.wrong_part_touches} wrong-part touches. Press R to try again." : "Press R to try again.", _h2);
            }
            else
            {
                GUI.Label(new Rect(24, 36, W - 48, 48), _stepText, _h1);
                GUI.Label(new Rect(24, 90, W - 48, 36), _how, _h2);
            }
            float frac = _count > 0 ? (_finished ? 1f : _index / (float)_count) : 0f;
            GUI.Box(new Rect(24, 134, W - 48, 8), GUIContent.none);
            var old = GUI.color; GUI.color = new Color(0.3f, 0.9f, 0.5f);
            GUI.Box(new Rect(24, 134, (W - 48) * frac, 8), GUIContent.none); GUI.color = old;

            // left: step list
            var steps = _spec.steps;
            float y = 170f;
            GUI.Box(new Rect(16, y - 8, 380, 20 + steps.Length * 28), GUIContent.none);
            for (int i = 0; i < steps.Length; i++)
            {
                string mark = _done.Contains(steps[i].order) ? "✓" : (i == _index && !_finished ? "▶" : " ");
                var st = i == _index && !_finished ? _body : _small;
                GUI.Label(new Rect(28, y + i * 28, 360, 26), $"{mark} {steps[i].order}. {Shorten(steps[i].instruction, 36)}", st);
            }

            ActionBar(W, H);

            // toast
            if (Time.unscaledTime < _toastUntil)
            {
                old = GUI.color; GUI.color = _toastColor;
                GUI.Box(new Rect(W / 2f - 320, H - 220, 640, 56), _toast, _toastStyle);
                GUI.color = old;
            }
        }

        static string Shorten(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }
}
