"""Canned model responses so the whole pipeline runs with VID2SIM_MOCK=1: no key, no cost.

The mock SAM output is real protocol text (from the meta-sam-parser README) so the parser
path is exercised for real; only the model calls are faked.
"""
from __future__ import annotations

import json
from pathlib import Path

from ..pipeline.common import VideoInfo

_P1 = Path(__file__).resolve().parents[2] / "data" / "procedures" / "p1" / "spec.handwritten.json"


def _p1() -> dict:
    return json.loads(_P1.read_text())


def mock_transcript(info: VideoInfo) -> dict:
    spec = _p1()
    n = max(1, len(spec["steps"]))
    span = max(info.duration_s, 1.0) * 1000 / n
    turns = []
    for i, s in enumerate(spec["steps"]):
        turns.append({"turnId": f"t{i}", "startMs": int(i * span), "endMs": int((i + 1) * span) - 200,
                      "transcript": s["instruction"]})
    return {"sessionId": "mock", "transcript": " ".join(s["instruction"] for s in spec["steps"]),
            "audioDurationMs": int(info.duration_s * 1000), "turns": turns}


def mock_draft(info: VideoInfo) -> dict:
    spec = _p1()
    n = max(1, len(spec["steps"]))
    per = max(1, info.nb_frames // n)
    steps = []
    for i, s in enumerate(spec["steps"]):
        s = {k: v for k, v in s.items() if k not in ("start_frame", "end_frame")}
        s["start_frame"] = i * per
        s["end_frame"] = min(info.nb_frames - 1, (i + 1) * per - 1)
        s["custom_action"] = "pry" if s["order"] == 3 else None
        steps.append(s)
    parts = []
    for p in spec["parts"]:
        kf = next((st["start_frame"] for st in steps if st["part_id"] == p["id"]), 0)
        parts.append({"id": p["id"], "name": p["name"], "prompt_phrase": p["prompt_phrase"],
                      "interaction": p["interaction"], "keyframe": min(kf + 2, info.nb_frames - 1)})
    return {"procedure": {"id": spec["procedure"]["id"], "title": spec["procedure"]["title"]}, "parts": parts, "steps": steps}


# Two real one-frame SAM lines (lossless masks) from the parser README; w/h are the README's.
_SAM_LINE = (
    "<{f}f>0<|box;x1=211;y1=228;x2=270;y2=254;w=320;h=334;c={c}|>"
    "<|mask;x=0;y=0;c={c};data=27,60,~!!!!M!0c[0o91w?q1!pIH4pPRVp2B3'7`e.ioeAf6-k/#Xd8%dX9x(|>\n"
)


def mock_sam_output(prompt: str, frames: int) -> str:
    # concept prompts for the "dirty" filter miss on purpose so the box fallback path runs
    if "dirty" in prompt and "<|box" not in prompt:
        return ""
    c = 0.72 if "<|box" in prompt else 0.9
    return "".join(_SAM_LINE.format(f=f, c=c) for f in range(0, min(frames, 6)))


def mock_box(part: dict, info: VideoInfo) -> tuple[int, int, int, int]:
    return (info.width // 3, info.height // 3, info.width // 3 + info.width // 6, info.height // 3 + info.height // 6)


def mock_csharp(cls: str, verb: str, step: dict, part: dict) -> str:
    return f"""using System;
using UnityEngine;

namespace Vid2Sim.Generated
{{
    /// <summary>Mock-generated action for '{verb}' on {part['name']}. Completes after the part has rotated past a threshold.</summary>
    public class {cls} : MonoBehaviour, IStepAction
    {{
        [SerializeField] private float angleThresholdDeg = 90f;
        private Part _part; private Step _step; private Action<StepEvent> _emit;
        private Quaternion _start; private bool _done;

        public void Bind(Part part, Step step, Action<StepEvent> emit)
        {{
            _part = part; _step = step; _emit = emit; _start = transform.rotation; _done = false;
        }}

        private void Update()
        {{
            if (_done || _emit == null) return;
            if (Quaternion.Angle(_start, transform.rotation) >= angleThresholdDeg)
            {{
                _done = true;
                _emit(new StepEvent(_part.id, _step.action));
            }}
        }}
    }}
}}
"""


def mock_shape_csharp(cls: str, procedure_id: str, part: dict, faulty: bool = False) -> str:
    """A body plus one detail, the shape of what Muse Code returns, so the gate and the engine are exercised.
    The faulty variant adds a red crack so the judgment step has something to catch."""
    variant = ', "faulty"' if faulty else ""
    defect = "\n            ShapeKit.Box(root, new Vector3(size * 0.2f, size * 0.3f, -size * 0.31f), new Vector3(size * 0.05f, size * 0.5f, size * 0.02f), ShapeKit.Red);" if faulty else ""
    return f"""using UnityEngine;
using Vid2Sim;

namespace Vid2Sim.Generated.Shapes
{{
    [PartShape("{procedure_id}", "{part['id']}"{variant})]
    public class {cls} : IPartShape
    {{
        public void Build(GameObject root, float size, Color accent)
        {{
            ShapeKit.Box(root, new Vector3(0f, size * 0.3f, 0f), new Vector3(size, size * 0.6f, size * 0.6f), accent);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.75f, 0f), size * 0.3f, size * 0.3f, ShapeKit.DarkSteel);{defect}
        }}
    }}
}}
"""
