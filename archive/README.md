# Archive: the VR training sim (pre-pivot)

Until Oct 31, 2026 the output of the pipeline was a hands-first Quest training sim plus a WebGL twin: a data-driven Unity engine under `archive/unity/TrainingSim` that read `spec.json`, Muse Code models of every part (and the machine they sit on), judgment steps with injected faults, an exam mode, photo cards cut from the video, and headless APK / WebGL builds. It worked (24 PlayMode tests, six procedures in the APK) and it was cut from the demo because SAM gives 2D masks, not geometry, so the rebuilt machine never looked like the machine. The pipeline still packages StreamingAssets for it when the project is present; nothing in the demo depends on it.

The sections below are the pre-pivot README text for that engine.

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
| Browser, mouse-first | `BuildScript.BuildWebGL` → `Builds/web/`, served at `/sim/` | click = press or select, drag = move (snaps onto the zone); the selected part has buttons for turn / lift / pull (keys Q/E, W/S, X), a cyan arrow on the part shows the motion the step needs, hover = inspect | dashboard **Play in browser** → `/api/procedures/<id>/play`; a saved edit hot-reloads in the open page |

Both outputs guide the trainee: at each step the expected part pulses and a translucent ghost of it performs the
motion twice while the panel says how to do it with the input in use ("Click it." / "Poke it with a finger."). H (web)
replays the demo; it also replays on a stall. The web build opens with a start screen that lists the controls.
Sequencer, scorer, coach panel and spoken lines are identical; the web build uses the browser's own speech synthesis.
Only the interaction backend and the camera differ, selected at compile time (`InteractionBackends.Create`).
Verified in a browser: the six-step compressor procedure played start to finish with the mouse to
"Procedure complete. Score 100, 0 wrong-part touches", with a wrong-part touch called out by the coach along the way.
Append `&debug=1` to the sim URL for a hover/click readout.

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

