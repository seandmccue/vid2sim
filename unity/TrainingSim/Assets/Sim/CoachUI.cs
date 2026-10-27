using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Vid2Sim
{
    /// <summary>
    /// World-space panel above the bench: step text, progress, pass/fail, one coaching line. Built in code so
    /// the scene stays empty; Android TTS reads the instruction on each step and the coach line on a mistake.
    /// </summary>
    public class CoachUI : MonoBehaviour
    {
        public Vector3 offsetFromBench = new(0f, 0.45f, 0.15f);
        public Vector2 panelSize = new(0.6f, 0.26f);
        public bool showWorldPanel = true;

        TextMeshProUGUI _title, _step, _progress, _coach, _status, _how;
        Image _bg;
        Speech _speech;
        float _coachUntil;

        public void Build(Transform origin, Speech speech)
        {
            _speech = speech;
            if (!showWorldPanel) return;   // the web HUD carries the text instead
            var canvasGo = new GameObject("CoachCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelSize.x * 1000f, panelSize.y * 1000f);
            canvasGo.transform.localScale = Vector3.one * 0.001f;
            var fwd = origin.forward; fwd.y = 0f; fwd.Normalize();
            canvasGo.transform.position = origin.position + fwd * 0.85f + Vector3.up * (0.75f + offsetFromBench.y);
            canvasGo.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            _bg = new GameObject("BG", typeof(Image)).GetComponent<Image>();
            _bg.transform.SetParent(canvasGo.transform, false);
            _bg.color = new Color(0.08f, 0.09f, 0.11f, 0.88f);
            Stretch(_bg.rectTransform);

            _title = Text("Title", canvasGo.transform, 26, FontStyles.Bold, new Vector2(0.03f, 0.80f), new Vector2(0.80f, 0.97f));
            _progress = Text("Progress", canvasGo.transform, 22, FontStyles.Normal, new Vector2(0.80f, 0.80f), new Vector2(0.97f, 0.97f), TextAlignmentOptions.TopRight);
            _step = Text("Step", canvasGo.transform, 34, FontStyles.Bold, new Vector2(0.03f, 0.42f), new Vector2(0.97f, 0.78f));
            _how = Text("How", canvasGo.transform, 24, FontStyles.Normal, new Vector2(0.03f, 0.30f), new Vector2(0.97f, 0.42f));
            _how.color = new Color(0.55f, 0.85f, 1f);
            _coach = Text("Coach", canvasGo.transform, 24, FontStyles.Italic, new Vector2(0.03f, 0.18f), new Vector2(0.97f, 0.30f));
            _coach.color = new Color(1f, 0.85f, 0.4f);
            _status = Text("Status", canvasGo.transform, 20, FontStyles.Normal, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.17f));
            _status.color = new Color(0.7f, 0.75f, 0.8f);
        }

        public void Attach(StepSequencer seq, Scorer scorer)
        {
            seq.StepStarted += (s, i) =>
            {
                if (_step == null) { _speech?.Say(s.instruction); return; }
                _step.text = $"{s.order}. {s.instruction}";
                _progress.text = $"{i + 1} / {seq.Spec.steps.Length}";
                _status.text = s.safety ? "SAFETY STEP: cannot be skipped. " + s.check : s.check;
                _coach.text = "";
                _speech?.Say(s.instruction);
            };
            seq.StepCompleted += (s, secs) => { if (_status) _status.text = $"Done in {secs:0.0}s."; };
            seq.WrongPart += (s, p) =>
            {
                // the coach calls it out once per step
                if (Time.unscaledTime < _coachUntil) return;
                _coachUntil = Time.unscaledTime + 4f;
                if (_coach) _coach.text = $"Not the {p.Part.name}. {s.coach}";
                _speech?.Say(s.coach);
            };
            seq.Stalled += (s, part) =>
            {
                var name = part != null ? part.Part.name : s.part_id;
                if (_coach) _coach.text = $"Stuck? Find the {name}. {s.coach}";
                part?.Highlight();
                _speech?.Say($"{s.instruction} Look for the {name}.");
            };
            seq.SafetySkipped += s =>
            {
                if (_coach) _coach.text = s.failure;
                _speech?.Say("Safety step skipped. " + s.failure);
            };
            seq.Finished += passed =>
            {
                var r = scorer.Current;
                if (_how) _how.text = "";
                if (_step) _step.text = passed ? "Procedure complete." : "Attempt failed.";
                if (_coach) _coach.text = r != null ? $"Score {r.score}. {r.total_seconds:0}s, {r.wrong_part_touches} wrong-part touches." : "";
                if (_status) _status.text = "Say or press Restart to try again.";
                _speech?.Say(passed ? "Procedure complete. Well done." : "Attempt failed.");
            };
        }

        public void SetTitle(string title) { if (_title) _title.text = title; }
        void Safe(System.Action a) { if (_step != null) a(); }
        public void SetHow(string how) { if (_how) _how.text = how; }
        public void SetStatus(string s) { if (_status) _status.text = s; }

        static TextMeshProUGUI Text(string name, Transform parent, float size, FontStyles style, Vector2 min, Vector2 max,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var t = new GameObject(name, typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            t.transform.SetParent(parent, false);
            t.fontSize = size; t.fontStyle = style; t.alignment = align; t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.Normal;
            var rt = t.rectTransform; rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return t;
        }

        static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
    }
}
