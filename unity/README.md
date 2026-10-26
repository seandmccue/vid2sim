# unity/TrainingSim

Data-driven Quest sim engine. Six scripts, no scene edits per procedure: a new procedure is a new
`Assets/StreamingAssets/procedures/<id>/spec.json`, nothing else.

| Script | Does |
| --- | --- |
| `SpecLoader` | Loads + validates spec.json (persistentDataPath first for hot reload, then StreamingAssets) |
| `PartFactory` | One primitive per part by `interaction`, scaled by `size_hint_m`, textured, Interaction SDK attached; seated bench layout |
| `StepSequencer` | Current step, expected part + action, advances on the matching `StepEvent`, raises `WrongPart`; safety skips fail the attempt |
| `Scorer` | Order, seconds per step, wrong-part touches, safety skips; writes `results/*.json` |
| `CoachUI` | World-space panel: step text, progress, pass/fail, one coaching line; Android TTS |
| `SessionFlow` | Cold start, pause/resume, restart, recenter, dev hot reload when the dashboard pushes a spec |

`ActionDetector` maps the closed vocabulary to completion rules (press = poke select, pull = moved 10 cm,
lift = raised 8 cm, place = released in the snap zone, turn_cw/ccw = ±45°, inspect = 1.5 s hover). A step with a
`custom_action` binds `Assets/Generated/<Verb>Action.cs` (Muse Code output, compile-gated) through `IStepAction`.

## Interaction backends

`Interaction/MetaInteractionBackend.cs` compiles only when the Meta XR Interaction SDK package is present
(asmdef version define `META_INTERACTION_SDK`). Without it, `SimpleInteractionBackend` drives parts with the
mouse so the whole step flow runs in the editor and the XR Simulator.

## Setup (Unity 6, once)

1. Open `unity/TrainingSim` in Unity 6000.3. `Packages/manifest.json` pulls Meta XR Core + Interaction SDK from
   the Meta scoped registry; adjust the version to what the Hub offers.
2. Menu **Vid2Sim → Create Sim Scene**, then add the Meta XR Building Blocks: Camera Rig, Hand Tracking,
   Hand Grab Interactor, Poke Interactor. Set `Bootstrap.rigOrigin` to the rig root (or leave empty).
3. Hand tracking on, controllers optional. Build Android (ARM64, IL2CPP), or **Vid2Sim → Build Android APK**.

## Headless gates

```bash
Unity -batchmode -nographics -quit -projectPath unity/TrainingSim -executeMethod Vid2Sim.Editor.CompileGate.Run
Unity -batchmode -nographics -quit -projectPath unity/TrainingSim -executeMethod Vid2Sim.Editor.BuildScript.BuildAndroid
```

## Debug keys (editor) / buttons (Quest)

R / A restart · P / B pause · N / Y skip (safety steps fail) · L / X reload spec and recenter.
