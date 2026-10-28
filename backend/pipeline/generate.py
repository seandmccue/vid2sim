"""Muse Code for long-tail actions; Muse Image for textures and step cards.

generate_actions(): for any step with a `custom_action` outside the engine vocabulary,
prompt Muse Code with the step, the part and the engine's IStepAction interface, get a
C# MonoBehaviour, run it through a headless Unity compile gate, and only then let it
land in Assets/Generated/. Muse Code is a CLI subscription; MUSE_CODE_CMD names the
binary (default `muse`). If it isn't installed, Spark 1.3 writes the script instead
and the metrics row says so.

generate_textures(): Muse Image edits the keyframe crop into a clean flat-lit texture,
once per part, cached by part id. Step cards for the dashboard come from the same model.

generate_shapes(): Muse Code writes a stylised low-poly model of each part from primitives
(ShapeKit calls) against the engine's IPartShape interface, one class per part, compile-gated
as a batch into Assets/Generated/Shapes/. The engine prefers a shape over the photo card.
"""
from __future__ import annotations

import base64
import json
import os
import re
import shutil
import subprocess
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from .common import (ACTION_VOCAB, IMAGE_MODEL, PRICE_IMAGE_EACH_EST, SPARK_MODEL, UNITY_GENERATED, Metrics,
                     Timer, client, is_mock, iter_output_text, spark_cost, with_retries)

ISTEP_ACTION_INTERFACE = """
// Engine side (Assets/Sim). Generated code implements IStepAction and is added as a component to the
// part's own GameObject, which already carries the Interaction SDK components PartFactory attached
// (HandGrabInteractable + Grabbable for grab/turn parts, PokeInteractable for press parts).
namespace Vid2Sim {
  public interface IStepAction {
    // Call emit(new StepEvent(part.id, step.action)) exactly once when the trainee completes the action.
    void Bind(Part part, Step step, System.Action<StepEvent> emit);
  }
  public readonly struct StepEvent { public StepEvent(string partId, string action); }
  // Plain data classes, NOT components. Field names are lowercase wire names:
  public class Part { public string id, name, prompt_phrase, interaction; public float size_hint_m; }
  public class Step { public int order; public string part_id, action, custom_action, instruction, check, failure, coach; public bool safety; }
}
"""


def _class_name(verb: str) -> str:
    return "".join(w.capitalize() for w in re.split(r"[^a-zA-Z0-9]+", verb) if w) + "Action"


def _codegen_prompt(step: dict, part: dict) -> str:
    return f"""Write a Unity 6 C# MonoBehaviour named {_class_name(step['custom_action'])} in namespace Vid2Sim.Generated
that implements Vid2Sim.IStepAction for the action "{step['custom_action']}" (nearest engine verb: {step['action']}).

Part: {part['name']} (id {part['id']}), interaction type {part['interaction']}, size {part.get('size_hint_m', 0.05)} m.
Step: "{step['instruction']}" Check: "{step['check']}"

Engine interface:
{ISTEP_ACTION_INTERFACE}

Constraints:
- Use only UnityEngine and Oculus.Interaction (Meta XR Interaction SDK) APIs. No third-party packages.
  HandGrabInteractable lives in Oculus.Interaction.HandGrab; GrabTypeFlags in Oculus.Interaction.Grab. Add the usings.
- Detect completion from hand interaction on the part's GameObject (e.g. a HandGrabInteractable's
  WhenStateChanged / WhenPointerEventRaised, or transform rotation/translation thresholds) and call emit once.
- Expose thresholds as [SerializeField] floats with sensible defaults.
- No scene references, no Resources.Load, no coroutine leaks; unsubscribe in OnDestroy.
- Output only the C# file contents, no markdown fences, no commentary.
"""


def _muse_code(prompt: str) -> str | None:
    cmd = os.environ.get("MUSE_CODE_CMD", "muse")
    if shutil.which(cmd) is None:
        return None
    try:
        proc = subprocess.run([cmd, "-p", prompt], capture_output=True, text=True, timeout=600, check=True)
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired):
        return None
    return proc.stdout


CODE_MAX_OUTPUT_TOKENS = int(os.environ.get("SPARK_MAX_OUTPUT_TOKENS", "16000"))


def _spark_code(prompt: str, metrics: Metrics, budget: int | None = None, _retry: bool = True) -> str | None:
    """Spark writes the script when the Muse Code CLI is absent. Reasoning shares the output budget: at 8000
    tokens a real run produced three empty files, so the budget is generous and an empty result retries once."""
    c = client()
    budget = budget or CODE_MAX_OUTPUT_TOKENS
    text, uin, uout, status = "", 0, 0, "completed"
    with Timer() as t:
        stream = c.responses.create(model=SPARK_MODEL,
                                    input=[{"type": "message", "role": "user", "content": [{"type": "input_text", "text": prompt}]}],
                                    max_output_tokens=budget, stream=True)
        for kind, payload in iter_output_text(stream):
            if kind == "delta":
                text += payload
            else:
                status = getattr(payload, "status", "completed")
                u = getattr(payload, "usage", None)
                if u:
                    uin, uout = int(u.input_tokens), int(u.output_tokens)
    metrics.log("generate_code", SPARK_MODEL, t.wall, spark_cost(uin, uout), tokens_in=uin, tokens_out=uout,
                note=f"Muse Code CLI not found; Spark wrote the script ({status}, budget {budget})")
    if not text.strip() or "class " not in text:
        if _retry:
            return _spark_code(prompt, metrics, budget * 2, _retry=False)
        return None
    return text


def strip_fences(code: str) -> str:
    m = re.search(r"```(?:csharp|cs)?\s*(.*?)```", code, re.S)
    return (m.group(1) if m else code).strip() + "\n"


SDK_GUARD = "#if META_INTERACTION_SDK && !UNITY_WEBGL"
# Types the generated scripts reach for and the using each one needs. Spark drops a using about one time
# in four; adding the missing line is cheaper than a failed gate and a regeneration.
REQUIRED_USINGS = {
    "HandGrabInteractable": "Oculus.Interaction.HandGrab",
    "GrabTypeFlags": "Oculus.Interaction.Grab",
    "PokeInteractable": "Oculus.Interaction",
    "Grabbable": "Oculus.Interaction",
    "InteractableState": "Oculus.Interaction",
    "PointerEventType": "Oculus.Interaction",
}


def ensure_usings(code: str) -> str:
    present = set(re.findall(r"^\s*using\s+([\w.]+)\s*;", code, re.M))
    needed = [ns for t, ns in REQUIRED_USINGS.items() if re.search(rf"\b{t}\b", code) and ns not in present]
    if not needed:
        return code
    lines = "".join(f"using {ns};\n" for ns in dict.fromkeys(needed))
    m = re.search(r"^\s*using\s+[\w.]+\s*;\s*$", code, re.M)
    return code[:m.start()] + lines + code[m.start():] if m else lines + code


def guard_for_sdk(code: str) -> str:
    """Generated scripts use Oculus.Interaction. Wrap them so a project without the Meta Interaction SDK
    package (CI, the SDK-less compile gate) still compiles; the asmdef defines META_INTERACTION_SDK when
    the package is present."""
    if code.lstrip().startswith(SDK_GUARD) or "Oculus." not in code:
        return code
    return f"{SDK_GUARD}\n{code.rstrip()}\n#endif\n"


def headless_compile(unity_project: Path, unity_exe: str | None = None) -> tuple[bool, str]:
    """Run Unity in batch mode and fail on any compiler error. Requires a Unity editor on PATH or UNITY_EXE."""
    exe = unity_exe or os.environ.get("UNITY_EXE") or shutil.which("Unity") or shutil.which("unity")
    if not exe:
        return False, "no Unity executable found (set UNITY_EXE)"
    log = unity_project / "Logs" / "compile_gate.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    cmd = [exe, "-batchmode", "-nographics", "-quit", "-projectPath", str(unity_project),
           "-executeMethod", "Vid2Sim.Editor.CompileGate.Run", "-logFile", str(log)]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=1800)
    except subprocess.TimeoutExpired:
        return False, "compile gate timed out"
    text = log.read_text(errors="ignore") if log.exists() else proc.stdout
    # Unity 6 sometimes aborts on exit (libc++abi terminate, exit 134) after a successful batch run, so the
    # verdict comes from the log: compiler errors fail it; an explicit success line passes it.
    has_errors = "error CS" in text or "Scripts have compiler errors" in text
    said_ok = "Exiting batchmode successfully" in text or "[CompileGate] finished with 0 error" in text
    ok = not has_errors and (proc.returncode == 0 or said_ok)
    return ok, text[-4000:]


def generate_actions(spec: dict, unity_project: Path, metrics: Metrics, gate: bool = True) -> list[dict]:
    """One script per custom verb, generated in parallel, then one headless compile gate for the batch.
    Returns one record per verb: {verb, class, path, compiled, note}."""
    parts = {p["id"]: p for p in spec["parts"]}
    staging = unity_project / "Assets" / "Generated"
    staging.mkdir(parents=True, exist_ok=True)
    jobs: list[tuple[str, str, dict, dict]] = []
    out: list[dict] = []
    seen: set[str] = set()
    for step in spec["steps"]:
        verb = step.get("custom_action")
        if not verb or verb in ACTION_VOCAB or verb in seen:
            continue
        seen.add(verb)  # one script per verb, however many steps use it
        cls = _class_name(verb)
        if (staging / f"{cls}.cs").exists():
            out.append({"verb": verb, "class": cls, "path": str(staging / f"{cls}.cs"), "compiled": True, "note": "cached"})
            continue
        if (staging / f"{cls}.cs.pending").exists():
            jobs.append((verb, cls, step, parts[step["part_id"]], (staging / f"{cls}.cs.pending").read_text()))  # re-gate, don't regenerate
            continue
        jobs.append((verb, cls, step, parts[step["part_id"]], None))
    if not jobs:
        return out

    def produce(job):
        verb, cls, step, part, existing = job
        if existing:
            metrics.log("generate_code", "cache", 0.0, 0.0, note=f"{verb}: re-gating pended script")
            return verb, cls, existing
        prompt = _codegen_prompt(step, part)
        if is_mock():
            from ..mock import mock_csharp
            code = mock_csharp(cls, verb, step, part)
            metrics.log("generate_code", "muse-code", 0.0, 0.0, note="mock")
        else:
            with Timer() as t:
                code = _muse_code(prompt)
            if code is not None:
                metrics.log("generate_code", "muse-code", t.wall, 0.0, note=f"{verb} -> {cls}")
            else:
                code = _spark_code(prompt, metrics)
        return verb, cls, code

    with ThreadPoolExecutor(max_workers=MAX_PARALLEL) as pool:
        produced = list(pool.map(produce, jobs))

    pending: list[tuple[str, str, Path]] = []
    for verb, cls, code in produced:
        if not code or not code.strip():
            out.append({"verb": verb, "class": cls, "path": "", "compiled": False,
                        "note": "generator returned no code; engine falls back to the vocabulary verb"})
            continue
        candidate = staging / f"{cls}.cs.pending"
        candidate.write_text(guard_for_sdk(ensure_usings(strip_fences(code))))
        pending.append((verb, cls, candidate))
    if not pending:
        return out

    # Gate the batch: every pending file is renamed in and compiled together. If the compiler names
    # specific files, only those are pended and the rest are re-gated once; if that still fails, all roll back.
    for _, cls, candidate in pending:
        candidate.rename(staging / f"{cls}.cs")
    if not gate:
        compiled, note = True, "gate skipped"
    elif is_mock():
        compiled, note = True, "mock gate"
    else:
        compiled, note = headless_compile(unity_project)
    failed: set[str] = set()
    if not compiled:
        failed = set(re.findall(r"Assets/Generated/([A-Za-z0-9_]+)\.cs\(", note))
        if failed and len(failed) < len(pending):
            for verb, cls, candidate in pending:
                if cls in failed:
                    (staging / f"{cls}.cs").rename(candidate)
            compiled2, note2 = headless_compile(unity_project)
            if compiled2:
                compiled, note = True, f"{len(failed)} script(s) pended, rest compiled"
            else:
                failed = {cls for _, cls, _ in pending}
                note = note2
        else:
            failed = {cls for _, cls, _ in pending}
    for verb, cls, candidate in pending:
        target = staging / f"{cls}.cs"
        ok = cls not in failed
        if not ok and target.exists():
            target.rename(candidate)
        out.append({"verb": verb, "class": cls, "path": str(target if ok else candidate), "compiled": ok,
                    "note": ("failed the compile gate; engine falls back to the vocabulary verb" if not ok else note)[-500:]})
    return out


IPART_SHAPE_INTERFACE = """
// Engine side (Assets/Sim/PartShapes.cs).
namespace Vid2Sim {
  public interface IPartShape {
    // root: empty GameObject at the part's spawn pose; add primitives as children (local space, metres).
    // size: longest side of the part in metres; keep everything inside it. accent: palette colour for the part type.
    void Build(UnityEngine.GameObject root, float size, UnityEngine.Color accent);
  }
  [System.AttributeUsage(System.AttributeTargets.Class)]
  public class PartShapeAttribute : System.Attribute { public PartShapeAttribute(string procedureId, string partId); }
  public static class ShapeKit {
    // Colours: Steel, DarkSteel, Rubber, Yellow, Red, Blue, Green, Cream, Black (UnityEngine.Color)
    public static GameObject Box(GameObject root, Vector3 centre, Vector3 size, Color color, Vector3? euler = null);
    public static GameObject Cylinder(GameObject root, Vector3 centre, float diameter, float height, Color color, Vector3? euler = null); // axis along local Y
    public static GameObject Sphere(GameObject root, Vector3 centre, float diameter, Color color);
    public static GameObject Capsule(GameObject root, Vector3 centre, float diameter, float height, Color color, Vector3? euler = null); // axis along local Y
  }
}
"""


def _shape_class(procedure_id: str, part_id: str) -> str:
    clean = lambda s: re.sub(r"[^a-zA-Z0-9]+", "_", s).strip("_")
    return f"Shape_{clean(procedure_id)}__{clean(part_id)}"


def _shape_prompt(spec: dict, part: dict) -> str:
    cls = _shape_class(spec["procedure"]["id"], part["id"])
    box = part.get("box") or [0, 0, 1, 1]
    w, h = max(1, box[2] - box[0] + 1), max(1, box[3] - box[1] + 1)
    aspect = "wider than tall" if w > h * 1.3 else ("taller than wide" if h > w * 1.3 else "about square")
    steps = [s["instruction"] for s in spec["steps"] if s["part_id"] == part["id"]][:3]
    return f"""Write a Unity 6 C# class named {cls} in namespace Vid2Sim.Generated.Shapes that implements Vid2Sim.IPartShape
and carries the attribute [Vid2Sim.PartShape("{spec['procedure']['id']}", "{part['id']}")].

It builds a stylised low-poly 3D model of this part from primitives, the kind of clean toy-like model you see in a
Spline or Blender low-poly scene: a recognisable silhouette, a main body plus one to three distinguishing details,
flat pastel colours, 4 to 14 primitives in total.

Part: {part['name']} (id {part['id']})
Looks like: {part.get('prompt_phrase', part['name'])}
How it is used: interaction "{part['interaction']}"; steps: {steps}
Procedure: {spec['procedure'].get('title', spec['procedure']['id'])}
Proportions in the video: {aspect}. Longest side = size (the Build argument), typically {part.get('size_hint_m', 0.1)} m.

Engine interface:
{IPART_SHAPE_INTERFACE}

Constraints:
- using UnityEngine; using Vid2Sim; Only call ShapeKit.Box / Cylinder / Sphere / Capsule with root as the first
  argument. No other Unity API calls, no materials, no colliders, no Resources.Load, no coroutines, no fields.
- Local space: X = width (left-right as the trainee sees it), Y = up, Z = depth; the trainee looks along +Z, so the
  front of the part faces -Z. Put the bottom of the model at y = 0 and keep every primitive inside a box of
  size x size x size centred on x = 0, z = 0.
- Express every dimension as a fraction of size (e.g. size * 0.4f), never a literal metre value.
- Use the accent colour for the main body or the one feature the trainee interacts with; ShapeKit colours for the rest.
- Output only the C# file contents, no markdown fences, no commentary.
"""


def _faulty_prompt(spec: dict, part: dict, good_code: str, step: dict) -> str:
    cls = _shape_class(spec["procedure"]["id"], part["id"]) + "_faulty"
    return f"""Below is a Unity 6 C# class that builds a stylised low-poly model of a {part['name']} from ShapeKit primitives.
Write a second class named {cls} in the same namespace, implementing Vid2Sim.IPartShape, with the attribute
[Vid2Sim.PartShape("{spec['procedure']['id']}", "{part['id']}", "faulty")], that builds the SAME model but with one
defect clearly visible: the thing a trainee doing this check would catch.

The check: "{step.get('check', '')}"
What goes wrong when it is missed: "{step.get('failure', '')}"

Make the defect obvious at a glance but plausible (a missing pin, a frayed or kinked hose section, a flat or cut tyre,
a loose or missing bolt, a cracked or bent part, a dark/dead lamp, corroded terminals). Draw the defect feature with
ShapeKit.Red or ShapeKit.Rubber as appropriate; keep every other primitive exactly as in the good model so the two
look identical apart from the defect. Same constraints as the original: only ShapeKit calls, local metres as
fractions of size, bottom at y = 0, inside size x size x size.

Good model:
{good_code}

Output only the C# file contents of the new class, no markdown fences, no commentary.
"""


def generate_shapes(spec: dict, unity_project: Path, metrics: Metrics, gate: bool = True) -> list[dict]:
    """One stylised primitive model per part, generated in parallel, compile-gated as a batch.
    Returns one record per part: {part, class, path, compiled, note}. Cached by class name."""
    staging = unity_project / "Assets" / "Generated" / "Shapes"
    staging.mkdir(parents=True, exist_ok=True)
    pid = spec["procedure"]["id"]
    jobs: list[tuple[dict, str, str | None]] = []
    out: list[dict] = []
    for part in spec["parts"]:
        cls = _shape_class(pid, part["id"])
        if (staging / f"{cls}.cs").exists():
            out.append({"part": part["id"], "class": cls, "path": str(staging / f"{cls}.cs"), "compiled": True, "note": "cached"})
            continue
        pend = staging / f"{cls}.cs.pending"
        jobs.append((part, cls, pend.read_text() if pend.exists() else None))
    # no early return: the faulty variants below may still be missing when every good model is cached

    def produce(job):
        part, cls, existing = job
        if existing:
            metrics.log("generate_shape", "cache", 0.0, 0.0, note=f"{part['id']}: re-gating pended shape")
            return part, cls, existing
        prompt = _shape_prompt(spec, part)
        if is_mock():
            from ..mock import mock_shape_csharp
            metrics.log("generate_shape", "muse-code", 0.0, 0.0, note="mock")
            return part, cls, mock_shape_csharp(cls, pid, part)
        with Timer() as t:
            code = _muse_code(prompt)
        if code is not None:
            metrics.log("generate_shape", "muse-code", t.wall, 0.0, note=f"{part['id']} -> {cls}")
        else:
            code = _spark_code(prompt, metrics)
        return part, cls, code

    with ThreadPoolExecutor(max_workers=MAX_PARALLEL) as pool:
        produced = list(pool.map(produce, jobs)) if jobs else []

    pending: list[tuple[dict, str, Path]] = []
    good_code: dict[str, str] = {}
    for part, cls, code in produced:
        if not code or "class " not in code:
            out.append({"part": part["id"], "class": cls, "path": "", "compiled": False, "note": "generator returned no code; engine uses the photo card"})
            continue
        candidate = staging / f"{cls}.cs.pending"
        candidate.write_text(ensure_usings(strip_fences(code)))
        pending.append((part, cls, candidate))
        good_code[part["id"]] = candidate.read_text()

    # faulty variants for every part an inspect step looks at (judgment steps), written from the good model
    inspect_steps = {s["part_id"]: s for s in spec["steps"] if s["action"] == "inspect"}
    fjobs: list[tuple[dict, str, str, dict]] = []
    for part in spec["parts"]:
        if part["id"] not in inspect_steps:
            continue
        cls = _shape_class(pid, part["id"]) + "_faulty"
        if (staging / f"{cls}.cs").exists():
            out.append({"part": part["id"], "class": cls, "path": str(staging / f"{cls}.cs"), "compiled": True, "note": "cached"})
            continue
        pend = staging / f"{cls}.cs.pending"
        if pend.exists():
            pending.append((part, cls, pend)); good_code.setdefault(part["id"], "")
            continue
        good = good_code.get(part["id"]) or ((staging / f"{_shape_class(pid, part['id'])}.cs").read_text() if (staging / f"{_shape_class(pid, part['id'])}.cs").exists() else "")
        if good:
            fjobs.append((part, cls, good, inspect_steps[part["id"]]))

    def produce_faulty(job):
        part, cls, good, step = job
        if is_mock():
            from ..mock import mock_shape_csharp
            metrics.log("generate_shape", "muse-code", 0.0, 0.0, note="mock faulty")
            return part, cls, mock_shape_csharp(cls, pid, part, faulty=True)
        prompt = _faulty_prompt(spec, part, good, step)
        with Timer() as t:
            code = _muse_code(prompt)
        if code is not None:
            metrics.log("generate_shape", "muse-code", t.wall, 0.0, note=f"{part['id']} faulty -> {cls}")
        else:
            code = _spark_code(prompt, metrics)
        return part, cls, code

    if fjobs:
        with ThreadPoolExecutor(max_workers=MAX_PARALLEL) as pool:
            for part, cls, code in pool.map(produce_faulty, fjobs):
                if not code or "class " not in code:
                    out.append({"part": part["id"], "class": cls, "path": "", "compiled": False, "note": "no faulty variant; engine marks the fault generically"})
                    continue
                candidate = staging / f"{cls}.cs.pending"
                candidate.write_text(ensure_usings(strip_fences(code)))
                pending.append((part, cls, candidate))
    if not pending:
        return out

    for _, cls, candidate in pending:
        candidate.rename(staging / f"{cls}.cs")
    if not gate:
        compiled, note = True, "gate skipped"
    elif is_mock():
        compiled, note = True, "mock gate"
    else:
        compiled, note = headless_compile(unity_project)
    failed: set[str] = set()
    if not compiled:
        failed = set(re.findall(r"Assets/Generated/Shapes/([A-Za-z0-9_]+)\.cs\(", note))
        if failed and len(failed) < len(pending):
            for _, cls, candidate in pending:
                if cls in failed:
                    (staging / f"{cls}.cs").rename(candidate)
            compiled2, note2 = headless_compile(unity_project)
            if compiled2:
                compiled, note = True, f"{len(failed)} shape(s) pended, rest compiled"
            else:
                failed = {cls for _, cls, _ in pending}
                note = note2
        else:
            failed = {cls for _, cls, _ in pending}
    for part, cls, candidate in pending:
        target = staging / f"{cls}.cs"
        ok = cls not in failed
        if not ok and target.exists():
            target.rename(candidate)
        out.append({"part": part["id"], "class": cls, "path": str(target if ok else candidate), "compiled": ok,
                    "note": ("failed the compile gate; engine uses the photo card" if not ok else note)[-500:]})
    return out


IHOST_SHAPE_INTERFACE = """
// Engine side (Assets/Sim/PartShapes.cs).
namespace Vid2Sim {
  public interface IHostShape {
    // root: empty GameObject; add primitives and anchors as children, in metres at real scale.
    void Build(UnityEngine.GameObject root);
  }
  [System.AttributeUsage(System.AttributeTargets.Class)]
  public class HostShapeAttribute : System.Attribute { public HostShapeAttribute(string procedureId); }
  public static class ShapeKit {
    // Colours: Steel, DarkSteel, Rubber, Yellow, Red, Blue, Green, Cream, Black (UnityEngine.Color)
    public static GameObject Box(GameObject root, Vector3 centre, Vector3 size, Color color, Vector3? euler = null);
    public static GameObject Cylinder(GameObject root, Vector3 centre, float diameter, float height, Color color, Vector3? euler = null); // axis along local Y
    public static GameObject Sphere(GameObject root, Vector3 centre, float diameter, Color color);
    public static GameObject Capsule(GameObject root, Vector3 centre, float diameter, float height, Color color, Vector3? euler = null); // axis along local Y
    // Where a part lives: centre of the part (metres, same local space), outward = unit direction from the machine's
    // surface toward whoever looks at the part, size = the part's real longest side in metres.
    public static GameObject Anchor(GameObject root, string partId, Vector3 centre, Vector3 outward, float size = 0f);
  }
}
"""

HOST_DECISION_SCHEMA = {
    "type": "object",
    "additionalProperties": False,
    "required": ["host", "subject", "length_m", "width_m", "height_m", "reason"],
    "properties": {
        "host": {"type": "boolean", "description": "true when the parts are attached to one machine or object the trainee works around"},
        "subject": {"type": "string", "description": "what that machine or object is, e.g. 'counterbalance forklift'; empty when host is false"},
        "length_m": {"type": "number"}, "width_m": {"type": "number"}, "height_m": {"type": "number"},
        "reason": {"type": "string"},
    },
}


def _host_class(procedure_id: str) -> str:
    return "Host_" + re.sub(r"[^a-zA-Z0-9]+", "_", procedure_id).strip("_")


def _host_decision(spec: dict, metrics: Metrics) -> dict:
    """Spark says whether the parts belong to one machine (a forklift, an engine) that should be modelled with the
    parts placed on it, or are loose items handled on a table (a filter kit, a graphics card), and how big it is."""
    if is_mock():
        return {"host": True, "subject": spec["procedure"].get("title", "machine"), "length_m": 2.0, "width_m": 1.2,
                "height_m": 1.0, "reason": "mock"}
    from .extract import _responses_structured
    parts = "\n".join(f"- {p['id']}: {p['name']} ({p['interaction']}, about {p.get('size_hint_m', 0.1)} m)" for p in spec["parts"])
    steps = "\n".join(f"- {s['instruction']}" for s in spec["steps"][:12])
    prompt = f"""A training sim is being built from a video of this procedure. Decide how to stage it.

Procedure: {spec['procedure'].get('title', spec['procedure']['id'])}
Parts the trainee touches or inspects:
{parts}
Steps:
{steps}

Question: are these parts attached to ONE machine or large object that the trainee works around or sits at (a forklift,
a compressor, a car engine, a boiler)? If so host = true, name it, and give its real overall size in metres. If instead
the parts are loose items handled on a table or held in the hands (a filter kit, a graphics card into a PC, a helmet
assembly), host = false. Small items that travel with the operator (a manual, a clipboard, a tool) never decide this.
"""
    return _responses_structured([{"type": "input_text", "text": prompt}], HOST_DECISION_SCHEMA, "host_decision", metrics, "host_decision")


def _host_prompt(spec: dict, decision: dict) -> str:
    pid = spec["procedure"]["id"]
    cls = _host_class(pid)
    how = {}
    for s in spec["steps"]:
        how.setdefault(s["part_id"], []).append(s["instruction"])
    parts = "\n".join(f"- {p['id']}: {p['name']} ({p['interaction']}; {'; '.join(how.get(p['id'], [])[:2])})" for p in spec["parts"])
    L, W, H = decision.get("length_m", 2.0), decision.get("width_m", 1.0), decision.get("height_m", 1.5)
    return f"""Write a Unity 6 C# class named {cls} in namespace Vid2Sim.Generated.Hosts that implements Vid2Sim.IHostShape
and carries the attribute [Vid2Sim.HostShape("{pid}")].

It builds a stylised low-poly 3D model of the whole {decision.get('subject', 'machine')} from primitives at REAL scale in
metres: about {L} m long, {W} m wide, {H} m tall. The clean toy-like look of a Spline or Blender low-poly scene: a
recognisable silhouette, 25 to 70 primitives, flat colours from the ShapeKit palette (a forklift is Yellow with Black
tyres and DarkSteel mast; pick what the real machine looks like).

Then it marks where each of these parts lives with ShapeKit.Anchor(root, partId, centre, outward, size):
  centre  = the part's centre, metres, same local space as the model
  outward = unit direction from the machine's surface toward whoever looks at the part
            (right flank -> new Vector3(1,0,0); floor pedals or a seat -> (0,1,0); the front -> (0,0,-1))
  size    = the part's real longest side in metres
Parts (id: name (interaction; what the trainee does)):
{parts}

Rules for the parts:
- Do NOT model a part that gets an anchor: the engine places its own model of it at the anchor, so leave its space.
- Where a part is one of several identical items (one tyre of four, one fork of two), model the others and leave the
  anchor for the inspected one, in a plausible spot (the drive tyre at the front, the steer tyre at the back).
- Everything else that makes the machine recognisable IS modelled: chassis, counterweight, mast uprights, overhead
  guard, seat, dashboard, other wheels, exhaust, and so on.
- A part that is not fixed to the machine (an operator manual, a clipboard, a hand tool, a spare) gets NO anchor.
- Anchors must be on or just outside the surface they belong to, never buried inside a primitive.
- Put inspected parts where a walk-around would find them: spread around the machine, not all on one side.

Procedure: {spec['procedure'].get('title', pid)}

Engine interface:
{IHOST_SHAPE_INTERFACE}

Constraints:
- using UnityEngine; using Vid2Sim; Only call ShapeKit.Box / Cylinder / Sphere / Capsule / Anchor with root as the
  first argument. No other Unity API calls, no materials, no colliders, no Resources.Load, no coroutines, no fields.
- Local space: X = width, Y = up with the bottom of the wheels at y = 0, Z = length; the machine's FRONT faces -Z
  (forks toward -Z, counterweight toward +Z). Centre the footprint on x = 0, z = 0. Literal metres are fine.
- Output only the C# file contents, no markdown fences, no commentary.
"""


def generate_host(spec: dict, unity_project: Path, metrics: Metrics, gate: bool = True, work: Path | None = None) -> dict:
    """The machine the parts belong to, as one generated class at real scale with an anchor per part, compile-gated.
    Returns {host, class, path, compiled, note, decision}. Cached by class name; the decision is cached in work."""
    pid = spec["procedure"]["id"]
    cls = _host_class(pid)
    staging = unity_project / "Assets" / "Generated" / "Hosts"
    staging.mkdir(parents=True, exist_ok=True)
    target, pend = staging / f"{cls}.cs", staging / f"{cls}.cs.pending"
    if target.exists():
        return {"host": True, "class": cls, "path": str(target), "compiled": True, "note": "cached"}

    decision_path = (work / "host_decision.json") if work else None
    if decision_path and decision_path.exists():
        decision = json.loads(decision_path.read_text())
    else:
        with Timer() as t:
            decision = _host_decision(spec, metrics)
        if decision_path:
            decision_path.write_text(json.dumps(decision, indent=2))
    if not decision.get("host"):
        metrics.log("generate_host", SPARK_MODEL, 0.0, 0.0, note=f"no host: {decision.get('reason', '')[:120]}")
        return {"host": False, "class": "", "path": "", "compiled": False, "note": decision.get("reason", "loose items on a bench"), "decision": decision}

    if pend.exists():
        code = pend.read_text()
        metrics.log("generate_host", "cache", 0.0, 0.0, note="re-gating pended host")
    elif is_mock():
        from ..mock import mock_host_csharp
        code = mock_host_csharp(cls, pid, spec["parts"])
        metrics.log("generate_host", "muse-code", 0.0, 0.0, note="mock")
    else:
        prompt = _host_prompt(spec, decision)
        with Timer() as t:
            code = _muse_code(prompt)
        if code is not None:
            metrics.log("generate_host", "muse-code", t.wall, 0.0, note=f"{decision.get('subject')} -> {cls}")
        else:
            code = _spark_code(prompt, metrics, budget=CODE_MAX_OUTPUT_TOKENS * 2)
    if not code or "class " not in code:
        return {"host": True, "class": cls, "path": "", "compiled": False, "note": "generator returned no code; parts stay on the bench", "decision": decision}
    pend.write_text(ensure_usings(strip_fences(code)))
    anchors = len(re.findall(r"ShapeKit\.Anchor\(", pend.read_text()))
    pend.rename(target)
    if not gate:
        compiled, note = True, "gate skipped"
    elif is_mock():
        compiled, note = True, "mock gate"
    else:
        compiled, note = headless_compile(unity_project)
    if not compiled:
        target.rename(pend)
    return {"host": True, "class": cls, "path": str(target if compiled else pend), "compiled": compiled,
            "anchors": anchors, "subject": decision.get("subject", ""),
            "note": ("failed the compile gate; parts stay on the bench" if not compiled else f"{anchors} anchors; {note}")[-500:],
            "decision": decision}


MAX_PARALLEL = int(os.environ.get("VID2SIM_PARALLEL", "4"))
TEXTURE_MAX_PX = int(os.environ.get("VID2SIM_TEXTURE_MAX_PX", "1024"))


def shrink_texture(path: Path, max_px: int = TEXTURE_MAX_PX) -> None:
    """Muse Image returns ~2000 px PNGs (~1.4 MB each). A primitive on a bench never needs more than 1024."""
    from PIL import Image
    img = Image.open(path)
    if max(img.size) <= max_px:
        return
    img.thumbnail((max_px, max_px), Image.LANCZOS)
    img.save(path, optimize=True)


def _texture_one(p: dict, work: Path, tex_dir: Path, metrics: Metrics) -> tuple[str, Path | None]:
    target = tex_dir / f"{p['id']}.png"
    crop = work / "bundle" / "crops" / f"{p['id']}.png"
    if target.exists():
        return p["id"], target
    if not crop.exists():
        return p["id"], None
    if is_mock():
        shutil.copy(crop, target)
        shrink_texture(target)
        metrics.log("texture", IMAGE_MODEL, 0.0, 0.0, note="mock, crop copied")
        return p["id"], target
    c = client()
    with Timer() as t:
        with crop.open("rb") as fh:
            edit = c.images.edit(model=IMAGE_MODEL, image=fh, n=1, output_format="png",
                                 prompt=f"Clean product texture of this {p['name']}, flat even lighting, "
                                        f"no hands, no background, centred, photographic")
    target.write_bytes(base64.b64decode(edit.data[0].b64_json))
    shrink_texture(target)
    metrics.log("texture", IMAGE_MODEL, t.wall, PRICE_IMAGE_EACH_EST, note=f"{p['id']} (cost is an estimate)")
    return p["id"], target


def generate_textures(spec: dict, work: Path, metrics: Metrics) -> dict[str, Path]:
    """One texture per part from its keyframe crop, cached by part id, up to MAX_PARALLEL requests at once."""
    tex_dir = work / "bundle" / "textures"
    tex_dir.mkdir(parents=True, exist_ok=True)
    with ThreadPoolExecutor(max_workers=MAX_PARALLEL) as pool:
        results = list(pool.map(lambda p: _texture_one(p, work, tex_dir, metrics), spec["parts"]))
    return {pid: path for pid, path in results if path is not None}


def _card_one(s: dict, spec: dict, cards: Path, metrics: Metrics) -> tuple[int, Path | None]:
    target = cards / f"step_{s['order']:02d}.png"
    if target.exists():
        return s["order"], target
    if is_mock():
        return s["order"], None
    part = next((p for p in spec["parts"] if p["id"] == s["part_id"]), None)
    c = client()
    with Timer() as t:
        gen = with_retries(lambda: c.images.generate(
            model=IMAGE_MODEL, n=1, size="1024x1024", output_format="png",
            prompt=f"Flat vector training card, industrial safety manual style, clean line art, one clear action: "
                   f"{s['instruction']} The part is a {part['name'] if part else s['part_id']}. No text, no hands cut off."),
            label=f"step card {s['order']}")
    target.write_bytes(base64.b64decode(gen.data[0].b64_json))
    shrink_texture(target, 768)
    metrics.log("step_card", IMAGE_MODEL, t.wall, PRICE_IMAGE_EACH_EST, note=f"step {s['order']} (estimate)")
    return s["order"], target


def generate_step_cards(spec: dict, work: Path, metrics: Metrics, limit: int | None = None) -> dict[int, Path]:
    """One Muse Image card per step for the dashboard (the Oct 31 'cut first' item: optional, cents each)."""
    cards = work / "bundle" / "cards"
    cards.mkdir(parents=True, exist_ok=True)
    with ThreadPoolExecutor(max_workers=MAX_PARALLEL) as pool:
        results = list(pool.map(lambda s: _card_one(s, spec, cards, metrics), spec["steps"][:limit]))
    return {order: path for order, path in results if path is not None}
