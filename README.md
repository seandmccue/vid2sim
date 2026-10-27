# vid2sim — Video to XR Training Sim

Film a real procedure once on a phone. Get a playable, hands-first Quest training module back the same hour.

**Spark understands, SAM segments, Muse Code builds, Muse Image paints.**

Meta Global AI Developer Hackathon, Oct 26 – Nov 4, 2026. Startup track, theme *Push the Frontier*.
Everything in this repo was built during the hackathon, from an empty repo on Oct 26. `prior/` holds any
pre-existing Frame Sixty code (currently none).

## One-command run

```bash
python3 -m venv .venv && .venv/bin/pip install -r backend/requirements.txt
cp .env.example .env          # paste MODEL_API_KEY from https://dev.meta.ai/platform
.venv/bin/python backend/run.py data/procedures/p1/p1.mp4
```

Out: `spec.json`, `transcript.json`, `review.json`, `bundle/` (crops, masks, textures), `metrics.json`, and the
Unity payload under `unity/TrainingSim/Assets/StreamingAssets/procedures/<id>/`. Add `--mock` to run the whole
chain with no key and no cost (real SAM protocol text, canned model answers).

Dashboard: `.venv/bin/uvicorn backend.app:app --port 8000` then `cd dashboard && npm install && npm run dev`.

## How it works

1. **Capture.** A trainer films the procedure once, narrating. 30 s to 3 min, MP4, faststart.
2. **Understand.** Narration goes through **Muse Voice Transcribe** (Spark 1.3's audio path is flagged degraded,
   so it never hears audio). **Muse Spark 1.3** reads the frames plus the frame-stamped transcript and returns the
   procedure as structured JSON against `backend/schema/procedure.schema.json`. A second pass flags any step the
   narration does not support.
3. **Segment.** **SAM 3.1**, one request per part on a frame-accurate trim of that part's step range. Concept
   prompt first; if it returns nothing or mean confidence < 0.5, Spark returns a box on the keyframe and SAM
   re-runs with `propagation_direction: forward`. `source` on each part records which path found it.
4. **Build.** A data-driven Unity engine reads the spec. **Muse Code** generates a `MonoBehaviour` only for verbs
   outside the engine's vocabulary, gated by a headless Unity compile. **Muse Image** turns keyframe crops into
   part textures.
5. **Review.** The trainer sees the steps and the masks in the dashboard, renames a part or reorders a step,
   and hits Rebuild. The spec hot-loads into a running dev build on the headset over adb.
6. **Train.** Hands-first on Quest. Step order, time per step, wrong-part touches and safety skips are scored;
   the coach panel speaks the instruction and one coaching line.

## Repo layout

```text
backend/
  run.py                  one command: video -> spec -> StreamingAssets
  app.py                  FastAPI: upload, run, status, spec get/put, build
  schema/procedure.schema.json   the contract (frozen 1.0)
  pipeline/
    transcribe.py         Voice Transcribe -> transcript.json
    extract.py            Spark 1.3 -> draft spec (two passes)
    segment.py            SAM 3.1 per part, trim, box fallback, meta-sam-parser
    bundle.py             keyframes, crops, size hints, mask PNGs, spec.json
    generate.py           Muse Code (compile-gated) + Muse Image textures
    package.py            StreamingAssets payload
    validate.py / metrics.py / common.py
  mock/                   canned responses for VID2SIM_MOCK=1
  tests/                  pytest, 11 tests, full mock run on a synthetic clip
dashboard/                React + Vite
unity/TrainingSim/        Unity 6 engine (see unity/README.md)
data/procedures/<id>/     p.mp4 (not committed), transcript.json, spec.json, bundle/, ground_truth.json
metrics/runs.csv          run_id, procedure, stage, model, wall_s, cost_usd, frames, tokens
```

## Spec contract

`procedure.schema.json` is read by the pipeline (writer), the dashboard (editor) and the engine (reader),
validated on every write and load. Rules the engine depends on:

- `steps[].action` is a closed vocabulary: `press grab pull lift place turn_cw turn_ccw inspect`. Anything else
  goes in `custom_action` and is a Muse Code generation candidate.
- `parts[].interaction` picks the Interaction SDK component: `press` → PokeInteractable, `turn` → HandGrab +
  OneGrabRotateTransformer, else HandGrabInteractable.
- `parts[].box` is in source-video pixels, inclusive x2/y2, exactly as SAM reports it. Never scaled.
- `parts[].source` is `concept`, `box` or `manual`. That's a metric.
- `steps[].safety: true` cannot be skipped; skipping ends the attempt with a fail.

## Metrics

Filled in from `metrics/runs.csv` and each procedure's `metrics.json` as runs happen.

| Procedure | Upload → playable | API cost | Steps correct vs ground truth | Parts: text / box / manual |
| --- | --- | --- | --- | --- |
| Nazava water-filter install, 79 s, 848×480, narrated, CC BY test clip | 9.6 min single pass (14.6 min incl. a code-gen rerun) | $1.21 | 6/7, 1 extra | 3 / 2 / 2 |
| MSI Trident GPU swap, 260 s, 1080p, no narration, CC BY test clip | 24.1 min incl. two crash-and-resume passes | $2.39 | 5/7, 9 finer-grained extras; verify correctly flagged 1 invented step | 3 / 7 / 2 |
| synthetic colour-bar clip, 6 s (chain check only) | 2.8 min | $0.32 | n/a | 0 / 0 / 5 |
| compressor-filter-swap (Sean's own footage) | _tbd_ | _tbd_ | _tbd_ | _tbd_ |

Both test clips are attributed in `data/procedures/*/ATTRIBUTION.md` and are never part of the demo. Numbers are
aggregated over every run of that clip (resumed stages cost $0 and 0 s); the per-stage rows are in `metrics/runs.csv`.
Rendered card: `docs/metrics-card.png`.

Notes on the Nazava run (`data/procedures/nazava/metrics.json`):

- Voice Transcribe returned 7 frame-stamped turns that match the clip's own subtitle track almost word for word.
- Spark 1.3 extracted 8 steps in the right order with sensible coach lines and flagged 3 as safety steps; the verify
  pass flagged one step's wording against the narration. Ground truth here was written from the clip's subtitles by
  the pipeline author, not a trainer, so treat the 6/7 as a sanity check rather than a claim.
- SAM 3.1 found the upper container (464 frames, mean c 0.83), ceramic filter (102 frames) and lower container
  (94 frames) from the text prompt alone. The base stand and tube went through the box fallback; the tube box is
  wrong (Spark boxed the container in the presenter's hands), which the dashboard overlay shows at a glance. The
  rubber seal and the lid were found by neither path and are marked `manual` for the trainer to draw.

Notes on the GPU-swap run (`data/procedures/trident-gpu-swap/metrics.json`), the Nov 2 "different machine,
zero code changes" test:

- No narration at all (the transcript is the word "Nice."). Spark 1.3 read the burned-in captions and extracted
  15 steps and 12 parts in order; our 7-step ground truth is coarser, so 9 extracted steps count as extras.
- The verify pass, given the video as evidence when the transcript is thin, cited the caption behind each of 14
  steps and flagged the 15th ("place the replacement card") as never shown: the trim ends before the install.
  That is a hallucinated step caught before it reached a trainee.
- SAM found 10 of 12 parts, 7 of them only through the box fallback (screws, cables, covers at 0.84 to 0.99
  confidence). The CPU cooler and the new card were found by neither path.
- It did not run with zero code changes. It found that meta-sam-parser validates every mask on push in pure
  Python (8 CPU minutes for one 1080p part), that a 1080p PNG keyframe stalls the box fallback, and a NameError
  in the texture stage the mock tests had not reached. All three fixes are in the pipeline, none in the spec.
- Spark's reasoning shares its output budget: at 6000 tokens the first Nazava extraction returned nothing, and at
  8000 the three generated C# scripts came back empty. Both now default to 16000 with one retry at double.

Prices used: Spark $1.25/M in, $4.25/M out; SAM $0.20 per 1k frames, 50-frame minimum per request.
Transcribe and Image rows are labelled estimates until Billing confirms the unit price.

## Risks we designed around

| Risk | Mitigation |
| --- | --- |
| Spark merges or misreads steps | Structured output against a fixed schema; second pass verifies each step against the transcript; dashboard review catches the rest |
| SAM's text prompt misses an industrial part | Box fallback from Spark on the keyframe, or the trainer draws one in the dashboard. Zero matches is a branch, never a crash |
| Generated Unity code doesn't compile | Engine is data-driven; Muse Code only writes the long tail, and every script passes a headless compile before it is referenced |
| Parts have no 3D geometry | Representative primitives sized from SAM's box, textured from the keyframe crop. Photogrammetry is the roadmap, not the demo |
| Spark 1.3 audio is degraded | Narration goes through Voice Transcribe; Spark gets frames plus text |
| Hand tracking flaky on small parts | Every collider ≥ 4 cm, generous snap zones, poke and grab only |

## Engine tests (no headset needed)

Seven PlayMode tests drive the engine the way the Interaction SDK does: the six-step compressor procedure runs
to a pass with scoring, a wrong-part touch is counted, skipping a safety step fails the attempt, pause blocks
events, both committed specs load and validate. They pass headless on Unity 6000.3.21f1 with Meta XR SDK 207.0.1.

```bash
Unity -batchmode -nographics -projectPath unity/TrainingSim -runTests -testPlatform PlayMode -testResults /tmp/playmode.xml
```

## Two outputs, one spec

The same engine ships twice from the same `spec.json`:

| Output | Build | Input | Launch |
| --- | --- | --- | --- |
| Quest, hands-first | `BuildScript.BuildAndroid` → `Builds/vid2sim.apk` | Meta Interaction SDK: poke, hand grab, rotate | sideload; dashboard **Rebuild → Quest** hot-pushes a new spec |
| Browser, mouse-first | `BuildScript.BuildWebGL` → `Builds/web/`, served at `/sim/` | click = press, drag = grab / pull / lift / place, right-drag = turn, hover = inspect | dashboard **Play in browser** → `/api/procedures/<id>/play`; a saved edit hot-reloads in the open page |

Sequencer, scorer, coach panel and spoken lines are identical; the web build uses the browser's own speech synthesis.
Only the interaction backend and the camera differ, selected at compile time (`InteractionBackends.Create`).

```bash
Unity -batchmode -nographics -quit -projectPath unity/TrainingSim -buildTarget WebGL -executeMethod Vid2Sim.Editor.BuildScript.BuildWebGL
```

## Quest build

```bash
Unity -batchmode -nographics -quit -projectPath unity/TrainingSim -buildTarget Android -executeMethod Vid2Sim.Editor.BuildScript.BuildAndroid
adb install -r unity/TrainingSim/Builds/vid2sim.apk
```

Builds headless on 6000.3.21f1 with Meta XR SDK 207.0.1 (IL2CPP, ARM64, OpenXR, hand tracking): 11 min cold,
about 2 min warm, 65 MB. The dashboard's Rebuild → Quest hot-pushes a new spec into the installed build without
rebuilding.

## Docs

- [docs/SUBMISSION.md](docs/SUBMISSION.md): the write-up, numbers to fill from measured runs
- [docs/VIDEO.md](docs/VIDEO.md): 3-minute shot list timed against the rubric, X post
- [docs/FILMING.md](docs/FILMING.md): how to shoot and narrate so footage is pipeline-ready
- [docs/metrics-card.png](docs/metrics-card.png): rendered by `backend/metrics_card.py` from `metrics.json`

## Development

Feature branches, one PR per component, merged to `main`. CI runs the backend tests and the dashboard build.

```bash
.venv/bin/python -m pytest backend/tests -q
cd dashboard && npm run build
Unity -batchmode -nographics -quit -projectPath unity/TrainingSim -executeMethod Vid2Sim.Editor.CompileGate.Run
```

Raw video is never committed. Footage links: _tbd_.
