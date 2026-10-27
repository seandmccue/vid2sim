# Morning handoff (written overnight Oct 26 → 27)

Everything below is on `main` through PRs 1–16. CI is green. `pytest backend/tests` 11/11, Unity PlayMode 10/10.

## What runs today

| Lane | State |
| --- | --- |
| Pipeline | Four real clips end to end on the live API, two of them public domain (DoD engine maintenance, 112 s; DoD diving-helmet fitting, 196 s, both narrated, compile gate on), plus the CC BY Nazava filter (79 s) and 1080p GPU swap (260 s, captions only). Numbers in the README table and `docs/metrics-card.png`. `--resume` reuses finished stages. |
| Backend + dashboard | Upload → run → steps → masks on demand → edit → draw a box → save → Rebuild → Quest, verified in the browser on both clips. `POST /api/procedures/<id>/build?apk=true` built `Builds/vid2sim.apk` (71 MB, three procedures) from the dashboard's own endpoint. |
| Unity engine | Compiles against Meta XR SDK 207.0.1. 13 headless PlayMode tests pass, including the full compressor procedure to a pass with scoring. `Builds/vid2sim.apk` builds headless in ~2 min warm. **Rebuild it before sideloading**: the web build exposed two player-only failures (stripped Standard shader, missing TMP essentials) that were fixed on Oct 27; any APK from before that fix will spawn nothing. |
| Generated code | 6 Spark-written `IStepAction` scripts compile against the SDK; 1 (`UnplugAction`) failed the gate and is pended, engine falls back to `pull`. |
| Web output | Same engine built for WebGL (`Builds/web`, served at `/sim/`). Dashboard **Play in browser** opens any procedure mouse-first; saved edits hot-reload in the page. Played the compressor procedure start to finish in a browser (score 100). The first web runs found three engine bugs that also affected the Quest build: stripped shader, missing TMP essentials, and motion before a step armed counting toward it. All fixed; APK rebuilt. |
| Docs | `docs/SUBMISSION.md`, `docs/VIDEO.md`, `docs/FILMING.md` drafted. |

## What only you can do, in order

1. **Sideload and play.** This is the Oct 28 exit test and nothing else matters until it passes.
   ```bash
   adb install -r unity/TrainingSim/Builds/vid2sim.apk
   ```
   The build loads `compressor-filter-swap` first (hand-written spec). A = restart, B = pause, Y = skip, X = recenter.
   If hands don't grab: the Building Blocks were added headless by script (`unity/README.md`); open the scene in the
   editor once and check the rig has Hand Tracking + Hand Grab + Poke interactors enabled.
2. **Film procedure #1** per `docs/FILMING.md`. Write `ground_truth.json` before running anything. Then
   `.venv/bin/python backend/run.py data/procedures/<id>/p.mp4 --title "..." --ground-truth data/procedures/<id>/ground_truth.json`.
   Expect ~10 min and under $2. The compile gate runs Unity for ~2 min at the end; set `VID2SIM_COMPILE_GATE=0` to skip.
3. **Install Muse Code** (`curl -fsSL https://dev.meta.ai/install.sh | bash`, $15 tier). Until then Spark writes the
   long-tail scripts and the metrics row says so. `MUSE_CODE_CMD` names the binary if it isn't `muse`.
4. **Hot-push test.** With the Quest plugged in: dashboard → Rebuild → Quest. It pushes `spec.json` + textures to
   `/sdcard/Android/data/com.framesixty.vid2sim/files/procedures/<id>/` and the running build reloads within 2 s.
5. Film procedure #2 on Oct 30, run it untouched.

## Things to know before you trust a number

- Test clips: the M151 film is public domain (US government work); the Nazava and GPU-swap clips are CC BY from
  Wikimedia Commons. All attributed in `data/procedures/*/ATTRIBUTION.md`, all for the pipeline only. The demo video
  must use your footage.
- Ground truth on both test clips was written by me from the clips' own captions, not by a trainer.
- The GPU-swap run was not "zero code changes": it exposed three pipeline bugs (1080p mask decoding, a stalled
  box fallback, a missing function), all fixed in the pipeline, none by editing the spec. The README says so.
- The gh token has no extra scopes. Push over SSH (the remote already is).

## Spend so far

$9.21 of API across every run so far, including the aborted ones (diving helmet $3.67, M151 $1.57, Nazava $1.21, GPU swap $2.39, chain check $0.32, smoke test $0.05). Rows in `metrics/runs.csv`.
