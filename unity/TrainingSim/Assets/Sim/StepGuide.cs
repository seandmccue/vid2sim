using System.Collections;
using UnityEngine;
using Vid2Sim.Interaction;

namespace Vid2Sim
{
    /// <summary>
    /// Shows the trainee how to do each step: when a step starts, the expected part pulses and a translucent
    /// ghost of it performs the motion (press, grab, pull, lift, place, turn, inspect) twice, with a one-line
    /// "how" in the coach panel phrased for the input in use (mouse or hands). Replays on stall or on demand.
    /// </summary>
    public class StepGuide : MonoBehaviour
    {
        public float demoSeconds = 1.4f;
        public int demoLoops = 2;
        public bool demoOnStepStart = true;

        PartFactory _factory;
        GameObject _ghost;
        Coroutine _running;
        public bool IsDemoRunning => _running != null;
        /// <summary>Caption for the HUD while the full run-through demo plays; empty otherwise.</summary>
        public event System.Action<string> CaptionChanged;
        public bool FullDemoRunning { get; private set; }

        public void Attach(StepSequencer seq, PartFactory factory, CoachUI ui)
        {
            _factory = factory;
            seq.StepStarted += (s, i) =>
            {
                ui?.SetHow(HowTo(s, factory.Backend));
                LightLabel(s.part_id);
                if (demoOnStepStart) Demonstrate(s);
            };
            seq.Finished += _ => LightLabel(null);
            seq.Stalled += (s, p) => Demonstrate(s);
            seq.Finished += _ => StopDemo();
        }

        /// <summary>One sentence the trainee can act on, in the vocabulary of the input in use.</summary>
        public static string HowTo(Step s, IInteractionBackend backend)
        {
            bool mouse = backend == null || backend.Name.StartsWith("simple");
            switch (s.action)
            {
                case Actions.Press:   return mouse ? "Click it." : "Poke it with a finger.";
                case Actions.Grab:    return mouse ? "Click and hold it." : "Grab it.";
                case Actions.Pull:    return mouse ? "Drag it out, away from where it sits." : "Grab it and pull it out.";
                case Actions.Lift:    return mouse ? "Drag it upward." : "Grab it and lift it.";
                case Actions.Place:   return mouse ? "Drag it onto the green zone and let go." : "Grab it and set it in the green zone.";
                case Actions.TurnCw:  return mouse ? "Hover it and press E (clockwise)." : "Grab it and turn it clockwise.";
                case Actions.TurnCcw: return mouse ? "Hover it and press Q (counter-clockwise)." : "Grab it and turn it counter-clockwise.";
                case Actions.Inspect: return mouse ? "Hold the cursor on it for two seconds." : "Look at it closely for two seconds.";
            }
            return "";
        }

        void LightLabel(string partId)
        {
            if (_factory == null) return;
            foreach (var kv in _factory.Spawned)
                kv.Value.GetComponentInParent<Transform>();   // no-op; labels are siblings, found by name below
            foreach (var label in Object.FindObjectsByType<PartLabel>(FindObjectsSortMode.None))
                label.SetCurrent(partId != null && label.name == $"Label:{partId}");
        }

        /// <summary>
        /// Run-through of the whole procedure before the trainee starts: each step's part lights and its ghost
        /// performs the motion once, with a caption. The sequencer is not started until this ends.
        /// </summary>
        public IEnumerator PlayFullDemo(ProcedureSpec spec, float secondsPerStep = 1.6f)
        {
            if (spec == null || _factory == null) yield break;
            FullDemoRunning = true;
            int n = spec.steps.Length;
            for (int i = 0; i < n; i++)
            {
                var s = spec.steps[i];
                CaptionChanged?.Invoke($"How it's done, {i + 1} of {n}: {s.instruction}");
                LightLabel(s.part_id);
                if (_factory.Spawned.TryGetValue(s.part_id, out var part))
                {
                    StopDemo();
                    part.Highlight(secondsPerStep);
                    var saved = (demoSeconds, demoLoops);
                    demoSeconds = secondsPerStep * 0.8f; demoLoops = 1;
                    _running = StartCoroutine(DemoRoutine(part, s));
                    yield return new WaitForSecondsRealtime(secondsPerStep);
                    demoSeconds = saved.demoSeconds; demoLoops = saved.demoLoops;
                }
                else yield return new WaitForSecondsRealtime(0.4f);
            }
            StopDemo();
            CaptionChanged?.Invoke("Your turn. Do the steps in order.");
            yield return new WaitForSecondsRealtime(1.2f);
            CaptionChanged?.Invoke("");
            LightLabel(null);
            FullDemoRunning = false;
        }

        public void Demonstrate(Step s)
        {
            if (_factory == null || s == null || !_factory.Spawned.TryGetValue(s.part_id, out var part)) return;
            StopDemo();
            part.Highlight(demoSeconds * demoLoops + 1f);
            _running = StartCoroutine(DemoRoutine(part, s));
        }

        public void StopDemo()
        {
            if (_running != null) StopCoroutine(_running);
            _running = null;
            if (_ghost) Destroy(_ghost);
            _ghost = null;
        }

        IEnumerator DemoRoutine(PartBehaviour part, Step s)
        {
            _ghost = MakeGhost(part);
            var t0 = part.transform.position;
            var r0 = part.transform.rotation;
            var up = Vector3.up;
            var outward = part.transform.parent ? -part.transform.parent.forward : Vector3.back;   // off the bench, toward the trainee
            var target = part.SnapZone ? part.SnapZone.position : t0 + outward * 0.14f;
            for (int loop = 0; loop < demoLoops; loop++)
            {
                float t = 0f;
                while (t < demoSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / demoSeconds);
                    float wave = Mathf.Sin(k * Mathf.PI);          // out and back
                    var pos = t0; var rot = r0; var scale = part.transform.localScale;
                    switch (s.action)
                    {
                        case Actions.Press:   pos = t0 - up * 0.02f * wave; break;
                        case Actions.Grab:    scale = part.transform.localScale * (1f + 0.15f * wave); break;
                        case Actions.Pull:    pos = t0 + outward * 0.15f * wave; break;
                        case Actions.Lift:    pos = t0 + up * 0.12f * wave; break;
                        case Actions.Place:   pos = Vector3.Lerp(t0, target, Mathf.SmoothStep(0f, 1f, k)); break;
                        case Actions.TurnCw:  rot = r0 * Quaternion.AngleAxis(-90f * Mathf.SmoothStep(0f, 1f, k), up); break;
                        case Actions.TurnCcw: rot = r0 * Quaternion.AngleAxis(90f * Mathf.SmoothStep(0f, 1f, k), up); break;
                        case Actions.Inspect: scale = part.transform.localScale * (1f + 0.3f * wave); break;
                    }
                    if (_ghost == null) yield break;
                    _ghost.transform.SetPositionAndRotation(pos, rot);
                    _ghost.transform.localScale = scale;
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(0.3f);
            }
            if (_ghost) Destroy(_ghost);
            _ghost = null;
            _running = null;
        }

        static GameObject MakeGhost(PartBehaviour part)
        {
            var src = part.GetComponent<MeshFilter>();
            var g = new GameObject($"Ghost:{part.Part.id}");
            g.transform.SetPositionAndRotation(part.transform.position, part.transform.rotation);
            g.transform.localScale = part.transform.localScale;
            var mf = g.AddComponent<MeshFilter>();
            mf.sharedMesh = src ? src.sharedMesh : null;
            var mr = g.AddComponent<MeshRenderer>();
            var zone = Resources.Load<Material>("Vid2SimZone");
            var m = zone ? new Material(zone) : new Material(Shader.Find("Standard"));
            m.color = new Color(1f, 0.85f, 0.2f, 0.45f);
            mr.material = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;   // no collider: the ghost is never interactable
        }
    }
}
