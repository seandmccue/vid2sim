# unity/TrainingSim

Data-driven Quest sim engine. Six scripts, no scene edits per procedure: a new procedure is a new
`Assets/StreamingAssets/procedures/<id>/spec.json`, nothing else.

| Script | Does |
| --- | --- |
| `SpecLoader` | Loads + validates spec.json (persistentDataPath first for hot reload, then StreamingAssets) |
| `PartFactory` | One object per part: the Muse Code model (`[PartShape]` class in `Assets/Generated/Shapes`, plus its `"faulty"` variant hidden until a fault is injected), else a photo card of the real part (`bundle/cutouts`), else a primitive by `interaction`. With a `[HostShape]` class for the procedure (`Assets/Generated/Hosts`) the machine body is built at real scale, scaled onto the bench (`hostSize`, 0.8 m) and each part spawns at its `ShapeKit.Anchor`, facing out of the surface, sized from the anchor's real size; parts without an anchor are loose items in a row at the front. Without a host: rows of five on the bench |
| `PartShapes` | `IPartShape` / `IHostShape`, the `ShapeKit` primitives and `Anchor` the generated code calls, `HostAnchor`, reflection lookup of generated classes, translucent clones for ghosts and snap zones |
| `FaultInjector` / `VerdictPanel` | Inspect steps are judgments: each inspected part spawns good or faulty (never a flawless exam), the trainee calls it on the bench buttons, the HUD or keys 1 / 2; a wrong call is coached from the step's `check` / `failure` text |
| `ExamMode` | Test mode (`?mode=exam`, the EXAM block on the bench, or T): no walkthrough, no how-to, no lit part, no arrow or ghost, the check text kept back, every verdict final and nothing revealed until the end. `Scorer` then scores 100 × faults caught / faults total, −15 per false alarm, −10 per wrong-part touch; pass = every fault caught and no false alarm; the result JSON carries `mode`, `faults_missed`, `false_alarms`, a `verdict` per inspect step and a `reveal` list the HUD shows at the end |
| `MotionHint` | Cyan arrow on the current step's part: up for lift, toward the trainee for pull, an arc for a turn, a line to the zone for place |
| `StepSequencer` | Current step, expected part + action, advances on the matching `StepEvent`, raises `WrongPart`; safety skips fail the attempt |
| `Scorer` | Order, seconds per step, wrong-part touches, safety skips; writes `results/*.json` |
| `CoachUI` | World-space panel: step text, progress, pass/fail, one coaching line; Android TTS |
| `SessionFlow` | Cold start, pause/resume, restart, recenter, dev hot reload when the dashboard pushes a spec |

`StepSequencer` raises `Stalled` once per step after `stallSeconds` (20 s) with no progress; `CoachUI` repeats the
instruction, names the part and pulses it yellow (`PartBehaviour.Highlight`). `ActionDetector` maps the closed vocabulary to completion rules (press = poke select, pull = moved 10 cm,
lift = raised 8 cm, place = released in the snap zone, turn_cw/ccw = ±45°, inspect = the trainee's verdict matches the part's injected state). Pulls, hint arrows and demo ghosts move along `PartBehaviour.Outward`: toward the trainee on the bench, out of the surface on a host model. A step with a
`custom_action` binds `Assets/Generated/<Verb>Action.cs` (Muse Code output, compile-gated) through `IStepAction`.

## Interaction backends

`Interaction/MetaInteractionBackend.cs` compiles only when the Meta XR Interaction SDK package is present
(asmdef version define `META_INTERACTION_SDK`; `META_CORE_SDK` gates `OVRInput` in `SessionFlow`). Without it,
`SimpleInteractionBackend` drives parts with the mouse so the whole step flow runs in the editor and the XR Simulator.

With the SDK: press parts get a `PokeInteractable` on their top face; turn parts a `HandGrabInteractable` with a
`OneGrabRotateTransformer` about their up axis; everything else a `HandGrabInteractable` with the default free
transformer. Hover/select/unselect flow into `PartBehaviour` from the SDK's pointer events.

## Packages (pinned, verified)

`Packages/manifest.json` pulls these from the Meta scoped registry `https://npm.developer.oculus.com` (scope
`com.meta.xr`). The registry serves v59–v85 and v201–v207; v207.0.1 is `latest` and core requires Unity 6000.0.66f2+.

| Package | Version |
| --- | --- |
| `com.meta.xr.sdk.core` | 207.0.1 |
| `com.meta.xr.sdk.interaction` | 207.0.1 |
| `com.meta.xr.sdk.interaction.ovr` | 207.0.1 |
| `com.unity.xr.openxr` | 1.14.3 |
| `com.unity.xr.management` | 4.5.1 |
| `com.unity.ugui` (TextMeshPro) | 2.0.0 |

The manifest lists the full set of built-in modules on purpose: the Meta packages reference Animation, AI and
AssetBundle, and a trimmed module list fails inside the SDK with `CS1069` before any project code compiles.
`Packages/packages-lock.json` is committed so a fresh clone resolves the same versions.

Fallback if the registry is unreachable: Window → Package Manager → "+" → "Add package by name" with the names
above (needs the scoped registry entry in the manifest), or import "Meta XR Core SDK" and "Meta XR Interaction SDK"
from the Asset Store; the asmdef version defines fire on the package name either way.

## Setup (Unity 6000.3.21f1, once)

1. Open `unity/TrainingSim`; the Package Manager resolves the pins above (first open downloads ~400 MB and imports
   for several minutes).
2. **Vid2Sim → Apply Meta Project Setup (Android)**. Scripted, idempotent: turns the OpenXR loader on for Android
   and Standalone in XR Plug-in Management, sets hand tracking support to Controllers And Hands, runs the Meta
   Project Setup Tool's "Fix All" for Android (Linear colour, Vulkan, ASTC, min API 32 / target 34, GameActivity,
   stereo instancing, MSAA, …) and regenerates `Assets/Plugins/Android/AndroidManifest.xml` with the
   `com.oculus.permission.HAND_TRACKING` entry. Everything it writes is committed, so a clean clone needs it only
   when settings drift.
3. **Vid2Sim → Create Sim Scene**. Writes `Assets/Scenes/Sim.unity` with the Bootstrap object and installs the Meta
   XR Building Blocks through the SDK's own installer: Camera Rig, Hand Tracking, Interactions Rig, Hand
   Interactions, Grab Interactor, Hand Poke Interactor. `Bootstrap.rigOrigin` is set to the Camera Rig root. The
   scene is committed; rerun the menu only to rebuild it. Without the Meta SDK the same menu makes a plain camera
   scene for the mouse backend.
4. Build Android (ARM64, IL2CPP) with **Vid2Sim → Build Android APK**, or File → Build Profiles → Android.

If the Building Blocks install ever fails (the SDK's block installer is `internal`; the menu drives it by
reflection and logs `[SceneSetup] ... SDK API changed` if a future version renames it), add them by hand:
Meta → Tools → Building Blocks, then "Camera Rig", "Hand Tracking", "Interactions Rig", "Hand Interactions",
"Grab Interactor", "Hand Poke Interactor"; set `Bootstrap.rigOrigin` on the Vid2Sim object to `[BuildingBlock]
Camera Rig`.

## Web output

`Assets/Scenes/Web.unity` (menu **Vid2Sim → Create Web Scene**) is a plain camera with `WebCameraRig` plus `Bootstrap`:
no Meta rig. `BuildScript.BuildWebGL` builds it to `Builds/web/` (compression off, so any static server works) and
`backend/app.py` serves it at `/sim/`. On WebGL `InteractionBackends.Create` always returns the mouse backend, the
loader reads `?spec=&textures=&cutouts=` from the page URL, and `Speech` uses `window.speechSynthesis` through
`Plugins/WebGL/Vid2SimWeb.jslib`. Generated scripts are guarded `#if META_INTERACTION_SDK && !UNITY_WEBGL`. Mouse mapping: click = press, drag = grab / pull / lift /
place (drops snap onto the zone when the cursor is over it); click a part to select it and the bottom bar's buttons (or Q / E turn, W / S lift / lower, X pull out) move it; hover = inspect; `?debug=1`
shows a hover/click readout. Engine rules found by the web runs: a step's pull/lift/turn thresholds are measured from the
part's pose when the step armed, not from spawn, so earlier fiddling never completes a later step.

## Player-build assets

`Vid2Sim → Ensure Project Assets` (called by both build methods) imports the TextMeshPro essentials and creates
`Assets/Sim/Resources/Vid2SimPart.mat` and `Vid2SimZone.mat`. Without them a player build throws on the first
spawned part (`Shader.Find` returns null once unreferenced shaders are stripped) and on every coach label; the
editor hides both. `Player_build_assets_are_present` guards it in the test suite.

## Headless gates

```bash
U=/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity
$U -batchmode -nographics -quit -projectPath unity/TrainingSim -executeMethod Vid2Sim.Editor.CompileGate.Run -logFile /tmp/gate.log
$U -batchmode -nographics -projectPath unity/TrainingSim -executeMethod Vid2Sim.Editor.SceneSetup.ApplyMetaProjectSetupHeadless -logFile /tmp/setup.log
$U -batchmode -nographics -projectPath unity/TrainingSim -executeMethod Vid2Sim.Editor.SceneSetup.CreateSimSceneHeadless -logFile /tmp/scene.log
$U -batchmode -nographics -quit -projectPath unity/TrainingSim -executeMethod Vid2Sim.Editor.BuildScript.BuildAndroid -logFile /tmp/build.log
```

The compile gate exits 0 only with zero `error CS` lines (Unity aborts batchmode with exit 1 on compiler errors
before the method runs). The two `SceneSetup` commands take no `-quit`: they await the SDK's async installers and
exit the editor themselves; Unity sometimes aborts with code 134 while tearing down afterwards, so read the
`[SceneSetup] saved …` / `[SceneSetup] Meta project setup applied` log lines, not the exit code.

## Debug keys (editor) / buttons (Quest)

R / A restart · P / B pause · N / Y skip (safety steps fail) · L / X reload spec and recenter.
