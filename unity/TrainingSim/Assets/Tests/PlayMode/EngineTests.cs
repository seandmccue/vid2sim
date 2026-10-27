// Headless PlayMode tests for the data-driven engine. They drive PartBehaviour directly (the same calls the
// Interaction SDK backend makes) so step completion, wrong-part detection, safety skips and scoring are
// verified without a headset. Run:
//   Unity -batchmode -nographics -projectPath unity/TrainingSim -runTests -testPlatform PlayMode -testResults /tmp/playmode.xml
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vid2Sim.Interaction;

namespace Vid2Sim.Tests
{
    public class EngineTests
    {
        GameObject _root;
        Camera _cam;

        [SetUp]
        public void SetUp()
        {
            InteractionBackends.Override = new SimpleInteractionBackend();
            _cam = new GameObject("MainCamera", typeof(Camera)).GetComponent<Camera>();
            _cam.tag = "MainCamera";
            _cam.transform.position = new Vector3(0f, 1.2f, 0f);
            _root = new GameObject("Vid2Sim");
        }

        [TearDown]
        public void TearDown()
        {
            InteractionBackends.Override = null;
            Time.timeScale = 1f;
            Object.Destroy(_root);
            Object.Destroy(_cam.gameObject);
        }

        IEnumerator Load(string procedureId, System.Action<SpecLoader, PartFactory, StepSequencer, Scorer> ready)
        {
            var loader = _root.AddComponent<SpecLoader>();
            loader.procedureId = procedureId;
            var factory = _root.AddComponent<PartFactory>();
            var seq = _root.AddComponent<StepSequencer>();
            var scorer = _root.AddComponent<Scorer>();
            yield return loader.Load();
            Assert.IsNull(loader.Error, loader.Error);
            yield return factory.Spawn(loader.Procedure, loader, _cam.transform);
            scorer.Attach(seq);
            ready(loader, factory, seq, scorer);
        }

        [UnityTest]
        public IEnumerator Loader_validates_committed_specs()
        {
            foreach (var id in new[] { "compressor-filter-swap", "nazava-water-filter-install", "swap-graphics-card-msi-trident-3" })
            {
                var loader = _root.AddComponent<SpecLoader>();
                loader.procedureId = id;
                yield return loader.Load();
                Assert.IsNull(loader.Error, $"{id}: {loader.Error}");
                Assert.AreEqual("1.0", loader.Procedure.schema_version);
                Assert.Greater(loader.Procedure.steps.Length, 0);
                Assert.AreEqual(0, SpecValidator.Validate(loader.Procedure).Count);
                Object.Destroy(loader);
            }
        }

        [Test]
        public void Validator_rejects_bad_specs()
        {
            var spec = new ProcedureSpec
            {
                schema_version = "1.0",
                procedure = new ProcedureInfo { id = "x", title = "x", fps = 30, width = 10, height = 10 },
                parts = new[] { new Part { id = "a", name = "a", prompt_phrase = "a", interaction = "grab", box = new[] { 0, 0, 1, 1 }, size_hint_m = 0.1f } },
                steps = new[] { new Step { order = 1, part_id = "ghost", action = "unscrew", start_frame = 5, end_frame = 1 } },
            };
            var errors = SpecValidator.Validate(spec);
            Assert.That(errors, Has.Some.Contains("unknown"));
            Assert.That(errors, Has.Some.Contains("vocabulary"));
            Assert.That(errors, Has.Some.Contains("end_frame"));
        }

        [UnityTest]
        public IEnumerator Factory_spawns_one_part_per_spec_part_on_the_bench()
        {
            PartFactory factory = null; SpecLoader loader = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { loader = l; factory = f; });
            Assert.AreEqual(loader.Procedure.parts.Length, factory.Spawned.Count);
            foreach (var p in factory.Spawned.Values)
            {
                Assert.IsNotNull(p.GetComponent<Collider>(), p.name);
                var d = Vector3.Distance(p.transform.position, _cam.transform.position);
                Assert.Less(d, 1.2f, $"{p.name} is {d:0.00} m away; must be within reach");
                var c = p.GetComponent<Collider>().bounds.size;
                Assert.GreaterOrEqual(Mathf.Min(c.x, c.y, c.z), 0.039f, $"{p.name} collider smaller than 4 cm");
            }
            // the part used by a 'place' step has a snap zone
            Assert.IsNotNull(factory.Spawned["new_filter"].SnapZone);
        }

        [UnityTest]
        public IEnumerator Sequencer_runs_the_compressor_procedure_to_a_pass()
        {
            PartFactory factory = null; StepSequencer seq = null; Scorer scorer = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { factory = f; seq = s; scorer = sc; });
            bool? passed = null;
            var started = new List<int>();
            seq.StepStarted += (st, i) => started.Add(st.order);
            seq.Finished += p => passed = p;
            seq.Begin(factory.Spawned.Count > 0 ? ReadSpec(seq) : null, factory);

            // 1 press power_switch
            Assert.AreEqual(1, seq.Current.order);
            var sw = factory.Spawned["power_switch"]; sw.NotifySelect(); sw.NotifyUnselect();
            yield return null;
            Assert.AreEqual(2, seq.Current.order, "press should complete on select");

            // 2 turn_ccw drain_valve: rotate counter-clockwise past 45 degrees while held
            var valve = factory.Spawned["drain_valve"]; valve.NotifySelect();
            valve.transform.rotation = valve.SpawnRotation * Quaternion.AngleAxis(60f, Vector3.up);
            yield return null; yield return null;
            Assert.AreEqual(3, seq.Current.order, "turn_ccw should complete past 45 degrees");
            valve.NotifyUnselect();

            // 3 turn_ccw filter_cover
            var cover = factory.Spawned["filter_cover"]; cover.NotifySelect();
            cover.transform.rotation = cover.SpawnRotation * Quaternion.AngleAxis(60f, Vector3.up);
            yield return null; yield return null;
            Assert.AreEqual(4, seq.Current.order);
            cover.NotifyUnselect();

            // 4 pull old_filter: held and moved 10 cm
            var old = factory.Spawned["old_filter"]; old.NotifySelect();
            old.transform.position = old.SpawnPosition + new Vector3(0f, 0f, -0.15f);
            yield return null; yield return null;
            Assert.AreEqual(5, seq.Current.order, "pull should complete after 10 cm");
            old.NotifyUnselect();

            // 5 place new_filter: released inside its snap zone
            var nf = factory.Spawned["new_filter"]; nf.NotifySelect();
            nf.transform.position = nf.SnapZone.position + new Vector3(0.01f, 0f, 0f);
            nf.NotifyUnselect();
            yield return null;
            Assert.AreEqual(6, seq.Current.order, "place should complete on release in the snap zone");

            // 6 turn_cw filter_cover
            cover.ResetPose(); cover.NotifySelect();
            cover.transform.rotation = cover.SpawnRotation * Quaternion.AngleAxis(-60f, Vector3.up);
            yield return null; yield return null;
            cover.NotifyUnselect();

            Assert.AreEqual(true, passed, "procedure should pass");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, started);
            Assert.IsNotNull(scorer.Current);
            Assert.AreEqual(6, scorer.Current.steps_completed);
            Assert.AreEqual(0, scorer.Current.wrong_part_touches);
            Assert.Greater(scorer.Current.score, 90);
        }

        [UnityTest]
        public IEnumerator Wrong_part_is_counted_and_safety_skip_fails_the_attempt()
        {
            PartFactory factory = null; StepSequencer seq = null; Scorer scorer = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { factory = f; seq = s; scorer = sc; });
            bool? passed = null; Step wrongStep = null; PartBehaviour wrongPart = null; bool safetyRaised = false;
            seq.Finished += p => passed = p;
            seq.WrongPart += (st, p) => { wrongStep = st; wrongPart = p; };
            seq.SafetySkipped += st => safetyRaised = true;
            seq.Begin(ReadSpec(seq), factory);

            // touch the drain valve while step 1 expects the power switch
            factory.Spawned["drain_valve"].NotifySelect();
            factory.Spawned["drain_valve"].NotifyUnselect();
            yield return null;
            Assert.AreEqual(1, seq.Current.order, "wrong part must not advance");
            Assert.AreEqual(1, wrongStep.order);
            Assert.AreEqual("drain_valve", wrongPart.Part.id);

            // step 1 is a safety step: skipping it ends the attempt with a fail and a zero score
            seq.Skip();
            yield return null;
            Assert.IsTrue(safetyRaised);
            Assert.AreEqual(false, passed);
            Assert.IsFalse(seq.Running);
            Assert.AreEqual(1, scorer.Current.wrong_part_touches);
            Assert.IsTrue(scorer.Current.safety_skipped);
            Assert.AreEqual(0, scorer.Current.score);
        }

        [UnityTest]
        public IEnumerator Pause_blocks_completion_and_resume_allows_it()
        {
            PartFactory factory = null; StepSequencer seq = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { factory = f; seq = s; });
            seq.Begin(ReadSpec(seq), factory);
            seq.Pause(true);
            factory.Spawned["power_switch"].NotifySelect();
            yield return null;
            Assert.AreEqual(1, seq.Current.order, "paused sequencer must ignore events");
            seq.Pause(false);
            factory.Spawned["power_switch"].NotifyUnselect();
            factory.Spawned["power_switch"].NotifySelect();
            yield return null;
            Assert.AreEqual(2, seq.Current.order);
        }

        [UnityTest]
        public IEnumerator Nazava_spec_with_custom_actions_spawns_and_starts()
        {
            PartFactory factory = null; StepSequencer seq = null;
            yield return Load("nazava-water-filter-install", (l, f, s, sc) => { factory = f; seq = s; });
            Assert.AreEqual(7, factory.Spawned.Count);
            seq.Begin(ReadSpec(seq), factory);
            Assert.AreEqual(1, seq.Current.order);
            // step 1 is a plain 'place' on the base stand: release in the snap zone completes it
            var bs = factory.Spawned["base_stand"]; bs.NotifySelect();
            bs.transform.position = bs.SnapZone.position; bs.NotifyUnselect();
            yield return null;
            Assert.AreEqual(2, seq.Current.order);
        }

        static ProcedureSpec ReadSpec(StepSequencer seq)
        {
            return seq.GetComponent<SpecLoader>().Procedure;
        }
    }
}

namespace Vid2Sim.Tests
{
    /// <summary>Hot reload and result persistence: the parts of the loop the dashboard and the scorer depend on.</summary>
    public class PersistenceTests
    {
        string _hotDir;

        [SetUp]
        public void SetUp()
        {
            InteractionBackends.Override = new SimpleInteractionBackend();
            _hotDir = SpecLoader.PersistentDir("compressor-filter-swap");
        }

        [TearDown]
        public void TearDown()
        {
            InteractionBackends.Override = null;
            if (System.IO.Directory.Exists(_hotDir)) System.IO.Directory.Delete(_hotDir, true);
        }

        [UnityTest]
        public IEnumerator Loader_prefers_the_hot_pushed_spec_over_streaming_assets()
        {
            // the dashboard's Rebuild pushes spec.json here over adb; the engine must load it first
            var shipped = System.IO.File.ReadAllText(System.IO.Path.Combine(SpecLoader.StreamingDir("compressor-filter-swap"), "spec.json"));
            var hot = shipped.Replace("\"title\": \"Replace the intake filter\"", "\"title\": \"HOT RELOADED\"");
            Assert.AreNotEqual(shipped, hot, "fixture title must be present");
            System.IO.Directory.CreateDirectory(_hotDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(_hotDir, "spec.json"), hot);

            var go = new GameObject("loader");
            var loader = go.AddComponent<SpecLoader>();
            loader.procedureId = "compressor-filter-swap";
            yield return loader.Load();
            Assert.IsNull(loader.Error, loader.Error);
            Assert.AreEqual("HOT RELOADED", loader.Procedure.procedure.title);
            Assert.That(loader.LoadedFrom, Does.Contain(Application.persistentDataPath));
            Assert.Greater(loader.HotSpecMtime(), System.DateTime.MinValue);
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Loader_rejects_an_invalid_hot_spec_without_crashing()
        {
            System.IO.Directory.CreateDirectory(_hotDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(_hotDir, "spec.json"), "{\"schema_version\":\"1.0\",\"procedure\":{\"id\":\"x\"},\"parts\":[],\"steps\":[]}");
            var go = new GameObject("loader");
            var loader = go.AddComponent<SpecLoader>();
            loader.procedureId = "compressor-filter-swap";
            yield return loader.Load();
            Assert.IsNotNull(loader.Error);
            Assert.That(loader.Error, Does.Contain("validation"));
            Assert.IsNull(loader.Procedure);
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Scorer_writes_a_result_file_when_the_attempt_ends()
        {
            var cam = new GameObject("MainCamera", typeof(Camera)); cam.tag = "MainCamera";
            var root = new GameObject("Vid2Sim");
            var loader = root.AddComponent<SpecLoader>(); loader.procedureId = "compressor-filter-swap";
            var factory = root.AddComponent<PartFactory>();
            var seq = root.AddComponent<StepSequencer>();
            var scorer = root.AddComponent<Scorer>();
            yield return loader.Load();
            yield return factory.Spawn(loader.Procedure, loader, cam.transform);
            scorer.Attach(seq);
            Scorer.Result saved = null;
            scorer.Saved += r => saved = r;
            seq.Begin(loader.Procedure, factory);
            seq.Skip();   // step 1 is a safety step: attempt ends, result written
            yield return null;
            Assert.IsNotNull(saved);
            Assert.IsFalse(saved.passed);
            var dir = System.IO.Path.Combine(Application.persistentDataPath, "results");
            Assert.IsTrue(System.IO.Directory.Exists(dir));
            var files = System.IO.Directory.GetFiles(dir, "compressor-filter-swap_*.json");
            Assert.Greater(files.Length, 0);
            foreach (var f in files) System.IO.File.Delete(f);
            Object.Destroy(root); Object.Destroy(cam);
        }
    }
}
