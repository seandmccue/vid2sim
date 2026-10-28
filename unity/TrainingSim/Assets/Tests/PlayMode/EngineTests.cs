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
            foreach (var id in new[] { "compressor-filter-swap", "nazava-water-filter-install", "swap-graphics-card-msi-trident-3", "release-left-side-engine-connections-m151", "fit-deep-sea-diving-helmet", "forklift-pre-shift-inspection" })
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
        public void Loader_parses_the_web_launch_query()
        {
            var q = SpecLoader.ParseQuery("https://host/sim/index.html?spec=%2Fapi%2Fprocedures%2Fnazava%2Fspec&textures=/api/procedures/nazava/files/bundle/textures&procedure=nazava");
            Assert.AreEqual("/api/procedures/nazava/spec", q["spec"]);
            Assert.AreEqual("/api/procedures/nazava/files/bundle/textures", q["textures"]);
            Assert.AreEqual("nazava", q["procedure"]);
            Assert.AreEqual(0, SpecLoader.ParseQuery("https://host/sim/index.html").Count);
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

        [Test]
        public void Player_build_assets_are_present()
        {
            // Without these a player build throws on the first part (stripped shader) and on every coach label (no TMP settings).
            Assert.IsNotNull(Resources.Load<Material>("Vid2SimPart"), "Assets/Sim/Resources/Vid2SimPart.mat missing: run Vid2Sim → Ensure Project Assets");
            Assert.IsNotNull(Resources.Load<Material>("Vid2SimZone"), "Assets/Sim/Resources/Vid2SimZone.mat missing");
            Assert.IsNotNull(Resources.Load<TMPro.TMP_Settings>("TMP Settings"), "TextMeshPro essentials not imported");
        }

        [UnityTest]
        public IEnumerator CoachUI_builds_its_panel()
        {
            var root = new GameObject("ui");
            var ui = root.AddComponent<CoachUI>();
            var speech = root.AddComponent<Speech>();
            ui.Build(_cam.transform, speech);
            yield return null;
            Assert.AreEqual(6, root.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Length);   // title, progress, step, how, coach, status
            ui.SetTitle("t"); ui.SetStatus("s");
            Object.Destroy(root);
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
        public IEnumerator Motion_before_a_step_arms_does_not_complete_it()
        {
            PartFactory factory = null; StepSequencer seq = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { factory = f; seq = s; });
            // rotate the cover (step 3's part) before the procedure even starts
            var cover = factory.Spawned["filter_cover"];
            cover.transform.rotation = cover.SpawnRotation * Quaternion.AngleAxis(90f, Vector3.up);
            seq.Begin(ReadSpec(seq), factory);
            var sw = factory.Spawned["power_switch"]; sw.NotifySelect(); sw.NotifyUnselect(); yield return null;
            var valve = factory.Spawned["drain_valve"]; valve.NotifySelect();
            valve.transform.rotation = valve.SpawnRotation * Quaternion.AngleAxis(60f, Vector3.up);
            yield return null; yield return null; valve.NotifyUnselect();
            Assert.AreEqual(3, seq.Current.order);
            yield return null; yield return null;
            Assert.AreEqual(3, seq.Current.order, "pre-armed rotation must not complete step 3");
            cover.NotifySelect();
            cover.transform.rotation = cover.transform.rotation * Quaternion.AngleAxis(60f, Vector3.up);
            yield return null; yield return null;
            Assert.AreEqual(4, seq.Current.order, "rotation after arming completes it");
        }

        [UnityTest]
        public IEnumerator Stall_coaches_once_and_highlights_the_expected_part()
        {
            PartFactory factory = null; StepSequencer seq = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { factory = f; seq = s; });
            int stalls = 0; PartBehaviour hinted = null;
            seq.Stalled += (st, p) => { stalls++; hinted = p; };
            seq.Begin(ReadSpec(seq), factory);
            yield return null;
            Assert.AreEqual(0, stalls, "no stall right after a step starts");
            seq.ForceStallCheck();
            Assert.AreEqual(1, stalls);
            Assert.AreEqual("power_switch", hinted.Part.id);
            hinted.Highlight();
            Assert.IsTrue(hinted.IsHighlighted);
            seq.ForceStallCheck();
            Assert.AreEqual(1, stalls, "a stall is raised once per step");
            // completing the step resets the timer for the next one
            var sw = factory.Spawned["power_switch"]; sw.NotifySelect(); sw.NotifyUnselect(); yield return null;
            Assert.AreEqual(2, seq.Current.order);
            seq.ForceStallCheck();
            Assert.AreEqual(2, stalls);
            Assert.AreEqual("drain_valve", hinted.Part.id);
        }

        [UnityTest]
        public IEnumerator Guide_demonstrates_each_step_with_a_ghost_and_a_how_line()
        {
            PartFactory factory = null; StepSequencer seq = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { factory = f; seq = s; });
            var guide = _root.AddComponent<StepGuide>();
            guide.demoSeconds = 0.05f; guide.demoLoops = 1;
            var ui = _root.AddComponent<CoachUI>();
            ui.Build(_cam.transform, _root.AddComponent<Speech>());
            guide.Attach(seq, factory, ui);
            seq.Begin(ReadSpec(seq), factory);
            yield return null;
            Assert.IsTrue(guide.IsDemoRunning, "a demo starts with the step");
            Assert.IsNotNull(GameObject.Find("Ghost:power_switch"), "a ghost of the expected part exists");
            Assert.IsNull(GameObject.Find("Ghost:power_switch")?.GetComponent<Collider>(), "the ghost is not interactable");
            Assert.IsTrue(factory.Spawned["power_switch"].IsHighlighted);
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.IsFalse(guide.IsDemoRunning, "the demo ends on its own");
            Assert.IsNull(GameObject.Find("Ghost:power_switch"));
            Assert.AreEqual("Click it.", StepGuide.HowTo(seq.Current, new SimpleInteractionBackend()));
            Assert.AreEqual("Click it, then press Turn left (Q), counter-clockwise.", StepGuide.HowTo(ReadSpec(seq).steps[1], new SimpleInteractionBackend()));
            Assert.IsTrue(guide.GetComponent<MotionHint>().Visible, "an arrow shows the motion on the current part");
            Assert.AreEqual(Actions.Press, guide.GetComponent<MotionHint>().CurrentAction);
            Assert.AreEqual("Drag it onto the green zone and let go.", StepGuide.HowTo(ReadSpec(seq).steps[4], new SimpleInteractionBackend()));
        }

        [UnityTest]
        public IEnumerator Parts_have_labels_and_the_full_demo_walks_every_step()
        {
            PartFactory factory = null; StepSequencer seq = null; SpecLoader loader = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { loader = l; factory = f; seq = s; });
            Assert.AreEqual(factory.Spawned.Count, Object.FindObjectsByType<PartLabel>(FindObjectsSortMode.None).Length, "one label per part");
            var guide = _root.AddComponent<StepGuide>();
            guide.Attach(seq, factory, null);
            var captions = new List<string>();
            guide.CaptionChanged += c => captions.Add(c);
            yield return guide.PlayFullDemo(loader.Procedure, secondsPerStep: 0.08f);
            Assert.IsFalse(guide.FullDemoRunning);
            Assert.AreEqual(loader.Procedure.steps.Length + 2, captions.Count, "one caption per step, then 'your turn', then clear");
            Assert.That(captions[0], Does.Contain("1 of 6"));
            Assert.AreEqual("", captions[captions.Count - 1]);
            Assert.IsNull(GameObject.Find("Ghost:filter_cover"), "no ghost left behind");
            Assert.AreEqual(-1, seq.Index, "the sequencer has not started during the demo");
        }

        [UnityTest]
        public IEnumerator Photo_cards_spawn_from_cutouts_and_turn_about_their_normal()
        {
            PartFactory factory = null; SpecLoader loader = null;
            yield return Load("nazava-water-filter-install", (l, f, s, sc) => { loader = l; factory = f; });
            int cards = 0;
            foreach (var p in factory.Spawned.Values) if (p.IsCard) cards++;
            Assert.AreEqual(loader.Procedure.parts.Length, cards, "every Nazava part has a cutout, so every part is a photo card");
            var lid = factory.Spawned["filter_lid"];
            var c = lid.GetComponent<Collider>().bounds.size;
            Assert.GreaterOrEqual(Mathf.Min(c.x, c.y, c.z), 0.039f, "card collider at least 4 cm deep for hands");
            Assert.Less(Mathf.Max(c.x, c.y), 0.23f, "cards are capped so they fit the bench");
            Assert.IsNotNull(lid.GetComponent<Renderer>().material.mainTexture, "the card shows the cutout");
            Assert.AreEqual(12, lid.GetComponent<MeshFilter>().sharedMesh.triangles.Length, "double-sided quad");
            // the card faces the seat and turns about its own normal: clockwise for the viewer is positive yaw
            var toSeat = (_cam.transform.position - lid.transform.position).normalized;
            Assert.Greater(Vector3.Dot(lid.TurnAxisWorld, toSeat), 0.7f, "turn axis points at the trainee");
            lid.TurnBy(50f);
            Assert.AreEqual(50f, lid.SignedYawFromSpawn(), 0.5f);
            lid.TurnBy(-110f);
            Assert.AreEqual(-60f, lid.SignedYawFromSpawn(), 0.5f);
            // a primitive still turns about up
            PartFactory f2 = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { f2 = f; });
            var valve = f2.Spawned["drain_valve"];
            Assert.IsFalse(valve.IsCard);
            valve.transform.rotation = valve.SpawnRotation * Quaternion.AngleAxis(60f, Vector3.up);
            Assert.AreEqual(-60f, valve.SignedYawFromSpawn(), 0.5f, "+60 about up is counter-clockwise from above");
        }

        [UnityTest]
        public IEnumerator Mouse_driver_buttons_complete_turn_pull_and_place_steps()
        {
            PartFactory factory = null; StepSequencer seq = null; Scorer scorer = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { factory = f; seq = s; scorer = sc; });
            SimpleMouseDriver D(string id) { var d = factory.Spawned[id].GetComponent<SimpleMouseDriver>(); d.nudgeSeconds = 0.05f; return d; }

            // lift / lower move the part by the step while it is held (before any step arms, so nothing completes)
            var nf = D("new_filter"); float y0 = nf.transform.position.y;
            nf.Lift(1);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(y0 + nf.liftStep, nf.transform.position.y, 0.002f);
            Assert.IsFalse(factory.Spawned["new_filter"].IsSelected, "released after the nudge");
            nf.Lift(-1);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(y0, nf.transform.position.y, 0.002f);

            bool? passed = null; seq.Finished += p => passed = p;
            seq.Begin(ReadSpec(seq), factory);
            var sw = factory.Spawned["power_switch"]; sw.NotifySelect(); sw.NotifyUnselect();
            yield return null;
            Assert.AreEqual(2, seq.Current.order);
            var dv = D("drain_valve"); dv.Turn(-dv.turnStepDegrees);                       // the "Turn left" button / Q
            yield return null; yield return null;
            Assert.AreEqual(3, seq.Current.order, "one quarter turn counter-clockwise completes turn_ccw");
            var fc = D("filter_cover"); fc.Turn(-fc.turnStepDegrees);
            yield return null; yield return null;
            Assert.AreEqual(4, seq.Current.order);
            D("old_filter").Pull(1);                           // the "Pull out" button / X
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(5, seq.Current.order, "pulling out by the step completes pull");
            var place = factory.Spawned["new_filter"]; place.NotifySelect();
            place.transform.position = place.SnapZone.position;
            place.NotifyUnselect();
            yield return null;
            Assert.AreEqual(6, seq.Current.order);
            fc.Turn(fc.turnStepDegrees);                       // the "Turn right" button / E
            yield return null; yield return null;
            Assert.AreEqual(true, passed);
            Assert.AreEqual(0, scorer.Current.wrong_part_touches);
        }

        /// <summary>Stand-in for a Muse Code host model: a 2 m body with anchors on its right flank and roof.</summary>
        [HostShape("test-host-rig")]
        public class TestHost : IHostShape
        {
            public void Build(GameObject root)
            {
                ShapeKit.Box(root, new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 1.0f, 2.0f), ShapeKit.Yellow);
                ShapeKit.Anchor(root, "side_lamp", new Vector3(0.6f, 0.8f, 0f), Vector3.right, 0.2f);
                ShapeKit.Anchor(root, "roof_cap", new Vector3(0f, 1.0f, 0.5f), Vector3.up, 0.3f);
            }
        }

        static ProcedureSpec HostRigSpec() => new ProcedureSpec
        {
            schema_version = "1.0",
            procedure = new ProcedureInfo { id = "test-host-rig", title = "Host rig", fps = 30, width = 100, height = 100, duration_s = 1 },
            parts = new[]
            {
                new Part { id = "side_lamp", name = "Side lamp", prompt_phrase = "lamp", interaction = "grab", box = new[] { 0, 0, 9, 9 }, size_hint_m = 0.4f, source = "concept" },
                new Part { id = "roof_cap", name = "Roof cap", prompt_phrase = "cap", interaction = "turn", box = new[] { 0, 0, 9, 9 }, size_hint_m = 0.1f, source = "concept" },
                new Part { id = "manual", name = "Manual", prompt_phrase = "manual", interaction = "inspect", box = new[] { 0, 0, 9, 9 }, size_hint_m = 0.3f, source = "concept" },
            },
            steps = new[]
            {
                new Step { order = 1, part_id = "side_lamp", action = "pull", instruction = "Pull the lamp", check = "", failure = "", coach = "" },
                new Step { order = 2, part_id = "roof_cap", action = "turn_cw", instruction = "Turn the cap", check = "", failure = "", coach = "" },
                new Step { order = 3, part_id = "manual", action = "inspect", instruction = "Check the manual", check = "present", failure = "", coach = "" },
            },
        };

        [UnityTest]
        public IEnumerator Host_model_places_parts_at_their_anchors_and_loose_items_in_front()
        {
            var spec = HostRigSpec();
            var loader = _root.AddComponent<SpecLoader>();
            loader.procedureId = spec.procedure.id;
            var factory = _root.AddComponent<PartFactory>();
            yield return factory.Spawn(spec, loader, _cam.transform);

            Assert.IsNotNull(factory.Host, "a [HostShape] class for the procedure builds the machine body");
            Assert.AreEqual(factory.hostSize / 2.0f, factory.HostScale, 1e-4f, "scaled so the 2 m body spans hostSize on the bench");
            var bench = factory.Host.parent;

            var lamp = factory.Spawned["side_lamp"];
            var anchorWorld = factory.Host.TransformPoint(new Vector3(0.6f, 0.8f, 0f));
            Assert.Less(Vector3.Distance(lamp.transform.position, anchorWorld), 1e-3f, "the lamp sits at its anchor on the right flank");
            Assert.Greater(Vector3.Dot(lamp.Outward, bench.right), 0.99f, "its outward direction is the flank normal");
            Assert.Greater(Vector3.Dot(-lamp.transform.forward, bench.right), 0.99f, "its front faces out of the flank");
            var lb = lamp.GetComponent<Collider>().bounds;
            Assert.LessOrEqual(Mathf.Max(lb.size.x, lb.size.y, lb.size.z), 0.2f * factory.HostScale + 0.03f, "sized from the anchor's real size, not the video hint");

            // pull moves along Outward (off the flank), not toward the seat
            var d = lamp.GetComponent<SimpleMouseDriver>(); d.nudgeSeconds = 0.05f;
            var before = lamp.transform.position;
            d.Pull(1);
            yield return new WaitForSeconds(0.2f);
            var moved = lamp.transform.position - before;
            Assert.Greater(Vector3.Dot(moved.normalized, bench.right), 0.99f);
            Assert.Greater(moved.magnitude, 0.05f);

            // a turn on a roof part still measures cleanly in the part's own frame
            var cap = factory.Spawned["roof_cap"];
            Assert.Greater(Vector3.Dot(cap.Outward, Vector3.up), 0.99f);
            cap.TurnBy(50f);
            Assert.AreEqual(50f, cap.SignedYawFromSpawn(), 0.5f);

            // no anchor: a loose item in the front row, ahead of the machine
            var manual = factory.Spawned["manual"];
            var local = bench.InverseTransformPoint(manual.transform.position);
            Assert.AreEqual(0f, local.x, 1e-3f);
            Assert.AreEqual(0f, local.z, 1e-3f);
            Assert.Less(local.z, bench.InverseTransformPoint(factory.Host.position).z - 0.3f);

            Assert.Greater(factory.StageBounds.size.z, 0.9f, "the stage bounds cover the body for camera framing");
        }

        /// <summary>Stand-in for a Muse Code shape: a body and a knob, the way generated classes are written.</summary>
        [PartShape("compressor-filter-swap", "drain_valve")]
        public class TestValveShape : IPartShape
        {
            public void Build(GameObject root, float size, Color accent)
            {
                ShapeKit.Cylinder(root, new Vector3(0f, size * 0.15f, 0f), size, size * 0.3f, ShapeKit.DarkSteel);
                ShapeKit.Box(root, new Vector3(0f, size * 0.45f, 0f), new Vector3(size * 0.9f, size * 0.3f, size * 0.2f), accent);
            }
        }

        [UnityTest]
        public IEnumerator Generated_shape_replaces_the_primitive_and_still_turns_and_ghosts()
        {
            PartFactory factory = null; StepSequencer seq = null;
            yield return Load("compressor-filter-swap", (l, f, s, sc) => { factory = f; seq = s; });
            var valve = factory.Spawned["drain_valve"];
            Assert.IsFalse(valve.IsCard);
            Assert.IsNull(valve.GetComponent<Renderer>(), "a shaped part is a root with child primitives");
            Assert.AreEqual(2, valve.GetComponentsInChildren<Renderer>().Length, "the test shape built two primitives");
            var b = valve.GetComponent<BoxCollider>().bounds.size;
            Assert.Greater(Mathf.Min(b.x, b.y, b.z), 0.01f, "collider wraps what was built");
            Assert.LessOrEqual(Mathf.Max(b.x, b.y, b.z), 0.22f * 1.1f, "model kept inside the size budget");
            Assert.AreEqual(valve.transform.position.y - b.y / 2f, factory.Spawned["power_switch"].transform.position.y - factory.Spawned["power_switch"].GetComponent<Collider>().bounds.size.y / 2f, 0.01f, "sits on the bench like its neighbours");
            // highlight tints every child; the ghost is a translucent copy with the same number of renderers
            valve.Highlight(1f);
            yield return null;
            foreach (var r in valve.GetComponentsInChildren<Renderer>()) Assert.AreNotEqual(ShapeKit.DarkSteel, r.material.color, "tinted");
            var guide = _root.AddComponent<StepGuide>();
            guide.demoSeconds = 0.05f; guide.demoLoops = 1;
            guide.Attach(seq, factory, null);
            guide.Demonstrate(ReadSpec(seq).steps[1]);
            yield return null;
            var ghost = GameObject.Find("Ghost:drain_valve");
            Assert.IsNotNull(ghost);
            Assert.AreEqual(2, ghost.GetComponentsInChildren<Renderer>().Length);
            Assert.AreEqual(0, ghost.GetComponentsInChildren<Collider>().Length, "ghost is not interactable");
            // and the turn step still completes through the driver
            seq.Begin(ReadSpec(seq), factory);
            var sw = factory.Spawned["power_switch"]; sw.NotifySelect(); sw.NotifyUnselect();
            yield return null;
            var d = valve.GetComponent<SimpleMouseDriver>(); d.Turn(-d.turnStepDegrees);
            yield return null; yield return null;
            Assert.AreEqual(3, seq.Current.order);
        }

        [UnityTest]
        public IEnumerator Inspect_steps_are_judgments_with_injected_faults()
        {
            PartFactory factory = null; StepSequencer seq = null; Scorer scorer = null; SpecLoader loader = null;
            yield return Load("forklift-pre-shift-inspection", (l, f, s, sc) => { loader = l; factory = f; seq = s; scorer = sc; });
            var faults = _root.AddComponent<FaultInjector>();
            faults.seed = 7; faults.faultRate = 0.5f;
            scorer.UseFaults(faults);
            faults.PlanFaults(loader.Procedure, factory);
            Assert.IsNotNull(factory.Verdicts, "a procedure with inspect steps gets the two bench buttons");
            Assert.IsTrue(factory.Controls.ContainsKey("__verdict_ok") && factory.Controls.ContainsKey("__verdict_fault"));
            Assert.IsFalse(factory.Spawned.ContainsKey("__verdict_ok"), "controls are not parts");
            int faulty = 0; foreach (var kv in faults.Plan) if (kv.Value) faulty++;
            Assert.Greater(faulty, 0, "at least one inspected part is faulty");
            Assert.Less(faulty, faults.Plan.Count, "and not all of them, with this seed");
            foreach (var kv in faults.Plan) Assert.AreEqual(kv.Value, factory.Spawned[kv.Key].Faulty);

            factory.Verdicts.Attach(seq);
            var misjudged = new List<string>();
            seq.Misjudged += (s, p, said) => misjudged.Add(p.Part.id);
            seq.Begin(ReadSpec(seq), factory);
            Assert.AreEqual(Actions.Inspect, seq.Current.action, "the forklift procedure opens with an inspection");
            var first = factory.Spawned[seq.Current.part_id];
            // the wrong call is coached and counted; the step stays open
            factory.Verdicts.Give(!first.Faulty);
            yield return null;
            Assert.AreEqual(1, seq.Current.order);
            CollectionAssert.AreEqual(new[] { first.Part.id }, misjudged);
            Assert.AreEqual(1, scorer.Current.misjudgements);
            // the right call completes it; the bench button does the same as the HUD
            factory.Controls[first.Faulty ? "__verdict_fault" : "__verdict_ok"].NotifySelect();
            factory.Controls[first.Faulty ? "__verdict_fault" : "__verdict_ok"].NotifyUnselect();
            yield return null;
            Assert.AreEqual(2, seq.Current.order, "verdict matching the injected state completes the inspect step");
            Assert.AreEqual(0, scorer.Current.wrong_part_touches, "pressing a verdict button is never a wrong-part touch");
            // hovering alone no longer completes an inspection
            seq.Stop(); seq.Begin(ReadSpec(seq), factory);
            first.NotifyHover(true);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(1, seq.Current.order);
            first.NotifyHover(false);
            // a re-plan with another seed changes the exam
            faults.seed = 8; faults.PlanFaults(loader.Procedure, factory);
            bool differs = false;
            foreach (var kv in faults.Plan) if (factory.Spawned[kv.Key].Faulty != kv.Value) Assert.Fail("plan and parts disagree");
            Assert.AreEqual(faults.FaultyInspectSteps(loader.Procedure) > 0, true);
            differs = true; Assert.IsTrue(differs);
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
        public IEnumerator Loader_fetches_a_spec_from_a_url()
        {
            // the web output gets its spec from the dashboard URL; a file:// URL goes through the same request path
            var shipped = System.IO.Path.Combine(SpecLoader.StreamingDir("compressor-filter-swap"), "spec.json");
            var tmp = System.IO.Path.Combine(Application.temporaryCachePath, "url-spec.json");
            System.IO.File.WriteAllText(tmp, System.IO.File.ReadAllText(shipped).Replace("\"title\": \"Replace the intake filter\"", "\"title\": \"FROM URL\""));
            var go = new GameObject("loader");
            var loader = go.AddComponent<SpecLoader>();
            loader.specUrl = "file://" + tmp;
            yield return loader.Load();
            Assert.IsNull(loader.Error, loader.Error);
            Assert.IsTrue(loader.UsesRemoteSpec);
            Assert.AreEqual("FROM URL", loader.Procedure.procedure.title);
            Assert.AreEqual("compressor-filter-swap", loader.procedureId, "procedure id comes from the fetched spec");
            bool changed = true;
            yield return loader.RemoteSpecChanged(c => changed = c);
            Assert.IsFalse(changed, "unchanged remote spec must not trigger a reload");
            System.IO.File.WriteAllText(tmp, System.IO.File.ReadAllText(tmp).Replace("FROM URL", "EDITED"));
            yield return loader.RemoteSpecChanged(c => changed = c);
            Assert.IsTrue(changed, "an edited remote spec triggers a reload");
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
