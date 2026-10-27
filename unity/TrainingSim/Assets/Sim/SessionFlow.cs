using System.Collections;
using UnityEngine;

namespace Vid2Sim
{
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

        SpecLoader _loader; PartFactory _factory; StepSequencer _seq; Scorer _scorer; CoachUI _ui; Speech _speech; StepGuide _guide;
        System.DateTime _lastSpecMtime;
        float _bootStart;

        void Awake()
        {
            _loader = GetComponent<SpecLoader>(); _factory = GetComponent<PartFactory>(); _seq = GetComponent<StepSequencer>();
            _scorer = GetComponent<Scorer>(); _ui = GetComponent<CoachUI>(); _speech = GetComponent<Speech>();
            _guide = GetComponent<StepGuide>() ?? gameObject.AddComponent<StepGuide>();
            if (origin == null) origin = Camera.main ? (Camera.main.transform.parent ?? Camera.main.transform) : transform;
        }

        IEnumerator Start()
        {
            _bootStart = Time.realtimeSinceStartup;
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
            _seq.Begin(_loader.Procedure, _factory);
            Debug.Log($"[SessionFlow] cold start to first step: {Time.realtimeSinceStartup - _bootStart:0.0}s");
        }

        public void Restart()
        {
            _seq.Stop();
            _factory.ResetPoses();
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
            if (Input.GetKeyDown(helpKey)) _guide.Demonstrate(_seq.Current);
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
