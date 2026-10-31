using System.Collections;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>Something that holds the procedure until the trainee is ready (the web start screen).</summary>
    public interface IStartGate { bool Started { get; } }

    /// <summary>
    /// Cold start to first step in under 15 s, pause/resume, restart without relaunch, seated recenter,
    /// and the dev hot-reload: when the dashboard pushes a new spec.json to persistentDataPath, reload it.
    /// </summary>
    [RequireComponent(typeof(SpecLoader), typeof(PartFactory), typeof(StepSequencer))]
    [RequireComponent(typeof(Scorer), typeof(CoachUI), typeof(Speech))]
    public class SessionFlow : MonoBehaviour
    {
        [Tooltip("Head / rig origin. Defaults to Camera.main's parent, else this transform.")]
        public Transform origin;
        public bool hotReload = true;
        public float hotReloadPollSeconds = 2f;
        public KeyCode restartKey = KeyCode.R, pauseKey = KeyCode.P, skipKey = KeyCode.N, reloadKey = KeyCode.L, helpKey = KeyCode.H;

        SpecLoader _loader; PartFactory _factory; StepSequencer _seq; Scorer _scorer; CoachUI _ui; Speech _speech; StepGuide _guide; FaultInjector _faults; ExamMode _exam;
        System.DateTime _lastSpecMtime;
        float _bootStart;

        void Awake()
        {
            _loader = GetComponent<SpecLoader>(); _factory = GetComponent<PartFactory>(); _seq = GetComponent<StepSequencer>();
            _scorer = GetComponent<Scorer>(); _ui = GetComponent<CoachUI>(); _speech = GetComponent<Speech>();
            _guide = GetComponent<StepGuide>() ?? gameObject.AddComponent<StepGuide>();
            _faults = GetComponent<FaultInjector>() ?? gameObject.AddComponent<FaultInjector>();
            _scorer.UseFaults(_faults);
            _exam = GetComponent<ExamMode>() ?? gameObject.AddComponent<ExamMode>();
            _exam.Changed += _ => { if (_seq.Spec != null && !_guide.FullDemoRunning) Restart(); };   // switching modes is a fresh attempt
            if (origin == null) origin = Camera.main ? (Camera.main.transform.parent ?? Camera.main.transform) : transform;
        }

        IEnumerator Start()
        {
            _bootStart = Time.realtimeSinceStartup;
#if UNITY_WEBGL
            _ui.showWorldPanel = false;   // the screen HUD carries the text in the browser
#endif
            (GetComponent<SceneStyle>() ?? gameObject.AddComponent<SceneStyle>()).Apply(origin);
            _ui.Build(origin, _speech);
            _scorer.Attach(_seq);
            _ui.Attach(_seq, _scorer);
            _guide.Attach(_seq, _factory, _ui);
            yield return LoadAndRun();
            if (hotReload) StartCoroutine(HotReloadLoop());
        }

        IEnumerator LoadAndRun()
        {
            _seq.Stop();
            _ui.SetTitle("Loading…");
            yield return _loader.Load();
            if (_loader.Error != null)
            {
                _ui.SetTitle("Spec error");
                _ui.SetStatus(_loader.Error);
                Debug.LogError("[SessionFlow] " + _loader.Error);
                yield break;
            }
            _lastSpecMtime = _loader.HotSpecMtime();
            _ui.SetTitle(_loader.Procedure.procedure.title);
            yield return _factory.Spawn(_loader.Procedure, _loader, origin);
            _factory.Verdicts?.Attach(_seq);
            _exam.AttachBench(_factory, _factory.Bench);
            var rig = FindFirstObjectByType<WebCameraRig>();
            if (rig) rig.Frame(_factory.StageBounds);
            var gate = GetComponent<IStartGate>() ?? (IStartGate)FindFirstObjectByType<WebCameraRig>();
            var hud = FindFirstObjectByType<WebHud>();
            if (hud) hud.Attach(_seq, _guide, _scorer, _factory, _loader.Procedure.procedure.title, _loader.Procedure);
            if (gate != null)
            {
                while (!gate.Started) yield return null;              // the start screen's click
                if (!_exam.Enabled) yield return _guide.PlayFullDemo(_loader.Procedure);  // show the whole thing once, then play
            }
            _faults.PlanFaults(_loader.Procedure, _factory);
            _seq.exam = _scorer.exam = _exam.Enabled;
            _seq.Begin(_loader.Procedure, _factory);
            Debug.Log($"[SessionFlow] cold start to first step: {Time.realtimeSinceStartup - _bootStart:0.0}s");
        }

        public void Restart()
        {
            _seq.Stop();
            _factory.ResetPoses();
            _faults.PlanFaults(_loader.Procedure, _factory);   // a different exam every time
            _seq.exam = _scorer.exam = _exam.Enabled;
            _scorer.Begin(_loader.Procedure);
            _seq.Begin(_loader.Procedure, _factory);
        }

        public void TogglePause() => _seq.Pause(!_seq.Paused);
        public void Reload() { StopAllCoroutines(); StartCoroutine(ReloadRoutine()); }

        IEnumerator ReloadRoutine()
        {
            yield return LoadAndRun();
            if (hotReload) StartCoroutine(HotReloadLoop());
        }

        public void Recenter()
        {
            // seated: re-place bench and panel in front of wherever the head is now
            Reload();
        }

        IEnumerator HotReloadLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(hotReloadPollSeconds);
                if (_loader.UsesRemoteSpec)
                {
                    bool changed = false;
                    yield return _loader.RemoteSpecChanged(c => changed = c);
                    if (changed)
                    {
                        Debug.Log("[SessionFlow] remote spec changed; hot reloading");
                        yield return LoadAndRun();
                    }
                    continue;
                }
                var m = _loader.HotSpecMtime();
                if (m > _lastSpecMtime)
                {
                    Debug.Log("[SessionFlow] spec.json changed on device; hot reloading");
                    _lastSpecMtime = m;
                    yield return LoadAndRun();
                }
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(restartKey)) Restart();
            if (Input.GetKeyDown(pauseKey)) TogglePause();
            if (Input.GetKeyDown(skipKey)) _seq.Skip();
            if (Input.GetKeyDown(reloadKey)) Reload();
            if (Input.GetKeyDown(helpKey) && !_guide.FullDemoRunning && !_exam.Enabled) _guide.Demonstrate(_seq.Current);
#if META_CORE_SDK && !UNITY_WEBGL
            // Quest: A = restart, B = pause, Y = skip (debug), X = recenter. Hands-only users never need these.
            if (OVRInput.GetDown(OVRInput.Button.One)) Restart();
            if (OVRInput.GetDown(OVRInput.Button.Two)) TogglePause();
            if (OVRInput.GetDown(OVRInput.Button.Four)) _seq.Skip();
            if (OVRInput.GetDown(OVRInput.Button.Three)) Recenter();
#endif
        }
    }
}
