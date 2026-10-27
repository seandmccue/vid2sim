"""Muse Code for long-tail actions; Muse Image for textures and step cards.

generate_actions(): for any step with a `custom_action` outside the engine vocabulary,
prompt Muse Code with the step, the part and the engine's IStepAction interface, get a
C# MonoBehaviour, run it through a headless Unity compile gate, and only then let it
land in Assets/Generated/. Muse Code is a CLI subscription; MUSE_CODE_CMD names the
binary (default `muse`). If it isn't installed, Spark 1.3 writes the script instead
and the metrics row says so.

generate_textures(): Muse Image edits the keyframe crop into a clean flat-lit texture,
once per part, cached by part id. Step cards for the dashboard come from the same model.
"""
from __future__ import annotations

import base64
import os
import re
import shutil
import subprocess
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from .common import (ACTION_VOCAB, IMAGE_MODEL, PRICE_IMAGE_EACH_EST, SPARK_MODEL, UNITY_GENERATED, Metrics,
                     Timer, client, is_mock, iter_output_text, spark_cost)

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


SDK_GUARD = "#if META_INTERACTION_SDK"
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
    ok = proc.returncode == 0 and "error CS" not in text and "Scripts have compiler errors" not in text
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
        jobs.append((verb, cls, step, parts[step["part_id"]]))
    if not jobs:
        return out

    def produce(job):
        verb, cls, step, part = job
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

    # Gate the batch once: every pending file is renamed in, compiled together, and all rolled back on failure.
    for _, cls, candidate in pending:
        candidate.rename(staging / f"{cls}.cs")
    compiled, note = (True, "gate skipped") if not gate else (True, "mock gate") if is_mock() else headless_compile(unity_project)
    for verb, cls, candidate in pending:
        target = staging / f"{cls}.cs"
        if not compiled:
            target.rename(candidate)
        out.append({"verb": verb, "class": cls, "path": str(target if compiled else candidate), "compiled": compiled, "note": note[-500:]})
    return out


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


def generate_step_cards(spec: dict, work: Path, metrics: Metrics, limit: int | None = None) -> dict[int, Path]:
    cards = work / "bundle" / "cards"
    cards.mkdir(parents=True, exist_ok=True)
    out = {}
    for s in spec["steps"][:limit]:
        target = cards / f"step_{s['order']:02d}.png"
        if target.exists():
            out[s["order"]] = target
            continue
        if is_mock():
            continue
        c = client()
        with Timer() as t:
            gen = c.images.generate(model=IMAGE_MODEL, n=1, size="1024x1024", output_format="png",
                                    prompt=f"Flat vector training card, industrial safety style, illustrating: {s['instruction']} "
                                           f"No text.")
        target.write_bytes(base64.b64decode(gen.data[0].b64_json))
        metrics.log("step_card", IMAGE_MODEL, t.wall, PRICE_IMAGE_EACH_EST, note=f"step {s['order']} (estimate)")
        out[s["order"]] = target
    return out
