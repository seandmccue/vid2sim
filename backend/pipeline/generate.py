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
from pathlib import Path

from .common import (ACTION_VOCAB, IMAGE_MODEL, PRICE_IMAGE_EACH_EST, SPARK_MODEL, UNITY_GENERATED, Metrics,
                     Timer, client, is_mock, iter_output_text, spark_cost)

ISTEP_ACTION_INTERFACE = """
// Assets/Sim/IStepAction.cs (engine side; generated code implements this)
namespace Vid2Sim {
  public interface IStepAction {
    // Bind to the spawned part and the step it serves; call emit(new StepEvent(part.Id, step.action))
    // exactly once when the trainee completes the action. Must work with hand tracking only.
    void Bind(Part part, Step step, System.Action<StepEvent> emit);
  }
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


def _spark_code(prompt: str, metrics: Metrics) -> str:
    c = client()
    text, uin, uout = "", 0, 0
    with Timer() as t:
        stream = c.responses.create(model=SPARK_MODEL,
                                    input=[{"type": "message", "role": "user", "content": [{"type": "input_text", "text": prompt}]}],
                                    max_output_tokens=8000, stream=True)
        for kind, payload in iter_output_text(stream):
            if kind == "delta":
                text += payload
            else:
                u = getattr(payload, "usage", None)
                if u:
                    uin, uout = int(u.input_tokens), int(u.output_tokens)
    metrics.log("generate_code", SPARK_MODEL, t.wall, spark_cost(uin, uout), tokens_in=uin, tokens_out=uout,
                note="Muse Code CLI not found; Spark wrote the script")
    return text


def strip_fences(code: str) -> str:
    m = re.search(r"```(?:csharp|cs)?\s*(.*?)```", code, re.S)
    return (m.group(1) if m else code).strip() + "\n"


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
    """Returns one record per custom action: {verb, class, path, compiled, note}."""
    parts = {p["id"]: p for p in spec["parts"]}
    out = []
    staging = unity_project / "Assets" / "Generated"
    staging.mkdir(parents=True, exist_ok=True)
    for step in spec["steps"]:
        verb = step.get("custom_action")
        if not verb or verb in ACTION_VOCAB:
            continue
        cls = _class_name(verb)
        target = staging / f"{cls}.cs"
        if target.exists():
            out.append({"verb": verb, "class": cls, "path": str(target), "compiled": True, "note": "cached"})
            continue
        prompt = _codegen_prompt(step, parts[step["part_id"]])
        if is_mock():
            from ..mock import mock_csharp
            code = mock_csharp(cls, verb, step, parts[step["part_id"]])
            metrics.log("generate_code", "muse-code", 0.0, 0.0, note="mock")
        else:
            with Timer() as t:
                code = _muse_code(prompt)
            if code is not None:
                metrics.log("generate_code", "muse-code", t.wall, 0.0, note=f"{verb} -> {cls}")
            else:
                code = _spark_code(prompt, metrics)
        code = strip_fences(code)
        candidate = staging / f"{cls}.cs.pending"
        candidate.write_text(code)
        compiled, note = (True, "gate skipped") if not gate else (True, "mock gate") if is_mock() else headless_compile(unity_project)
        if compiled:
            candidate.rename(target)
        out.append({"verb": verb, "class": cls, "path": str(target if compiled else candidate), "compiled": compiled, "note": note[-500:]})
    return out


def generate_textures(spec: dict, work: Path, metrics: Metrics) -> dict[str, Path]:
    """One texture per part from its keyframe crop. Cached by part id under bundle/textures."""
    tex_dir = work / "bundle" / "textures"
    tex_dir.mkdir(parents=True, exist_ok=True)
    out = {}
    for p in spec["parts"]:
        target = tex_dir / f"{p['id']}.png"
        crop = work / "bundle" / "crops" / f"{p['id']}.png"
        if target.exists():
            out[p["id"]] = target
            continue
        if not crop.exists():
            continue
        if is_mock():
            shutil.copy(crop, target)
            metrics.log("texture", IMAGE_MODEL, 0.0, 0.0, note="mock, crop copied")
            out[p["id"]] = target
            continue
        c = client()
        with Timer() as t:
            with crop.open("rb") as fh:
                edit = c.images.edit(model=IMAGE_MODEL, image=fh, n=1, output_format="png",
                                     prompt=f"Clean product texture of this {p['name']}, flat even lighting, "
                                            f"no hands, no background, centred, photographic")
        target.write_bytes(base64.b64decode(edit.data[0].b64_json))
        metrics.log("texture", IMAGE_MODEL, t.wall, PRICE_IMAGE_EACH_EST, note=f"{p['id']} (cost is an estimate)")
        out[p["id"]] = target
    return out


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
