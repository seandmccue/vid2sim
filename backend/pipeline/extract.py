"""Spark 1.3 extraction: frames + transcript -> draft procedure spec, in two passes.

Pass 1 (extract): Responses API, input_file by file_id plus the transcript as input_text,
structured output against a draft schema derived from procedure.schema.json (parts without
the SAM-owned fields), stream=True, max_output_tokens >= 4000 because reasoning shares the
budget. Pass 2 (verify): send the draft back with the transcript and ask which steps the
narration does not support. Flags land in review.json, never in the frozen spec.
"""
from __future__ import annotations

import copy
import json
import os
from pathlib import Path

from .common import (ACTION_VOCAB, INTERACTION_FOR_ACTION, SPARK_MODEL, Metrics, Timer, VideoInfo,
                     client, is_mock, iter_output_text, load_schema, slugify, spark_cost, with_retries, write_json)
from .transcribe import transcript_as_text

SAM_OWNED_PART_FIELDS = ("box", "size_hint_m", "confidence", "source")


def draft_schema() -> dict:
    """The schema Spark fills: the frozen spec minus fields SAM and the bundler own."""
    full = load_schema()
    draft = {
        "type": "object", "additionalProperties": False,
        "required": ["procedure", "parts", "steps"],
        "properties": {
            "procedure": {
                "type": "object", "additionalProperties": False, "required": ["id", "title"],
                "properties": {
                    "id": full["properties"]["procedure"]["properties"]["id"],
                    "title": full["properties"]["procedure"]["properties"]["title"],
                },
            },
            "parts": copy.deepcopy(full["properties"]["parts"]),
            "steps": copy.deepcopy(full["properties"]["steps"]),
        },
    }
    part_item = draft["properties"]["parts"]["items"]
    for f in SAM_OWNED_PART_FIELDS:
        part_item["properties"].pop(f, None)
    part_item["required"] = [r for r in part_item["required"] if r not in SAM_OWNED_PART_FIELDS]
    part_item["properties"]["keyframe"]["description"] = (
        "Source frame index where this part is most clearly visible and unoccluded")
    step_item = draft["properties"]["steps"]["items"]
    # custom_action is optional in the spec; strict structured output needs every key required,
    # so make it required-but-nullable in the draft and strip nulls afterwards.
    step_item["properties"]["custom_action"] = {
        "type": ["string", "null"],
        "description": "A verb outside the vocabulary if none fits exactly (e.g. 'unscrew'); else null",
    }
    step_item["required"] = sorted(set(step_item["required"]) | {"custom_action"})
    return draft


VERIFY_SCHEMA = {
    "type": "object", "additionalProperties": False, "required": ["steps"],
    "properties": {
        "steps": {
            "type": "array",
            "items": {
                "type": "object", "additionalProperties": False,
                "required": ["order", "supported", "evidence", "suggested_fix"],
                "properties": {
                    "order": {"type": "integer"},
                    "supported": {"type": "boolean", "description": "True if the narration supports this step as written"},
                    "evidence": {"type": "string", "description": "The narration phrase that supports or contradicts it"},
                    "suggested_fix": {"type": "string", "description": "Empty if supported, else the corrected instruction"},
                },
            },
        }
    },
}


def extraction_prompt(info: VideoInfo, transcript_text: str, title_hint: str | None) -> str:
    return f"""You are writing a hands-on training procedure from a video of a technician doing it once.
Return the step list a trainer would write: one step per physical action, in order, nothing merged.

Video: {info.width}x{info.height} at {info.fps:.2f} fps, {info.nb_frames} frames, {info.duration_s:.1f} s.
{('Title hint: ' + title_hint) if title_hint else ''}

Rules:
- `action` must be one of: {', '.join(ACTION_VOCAB)}. Pick the closest. If no verb fits exactly
  (e.g. "unscrew", "clip", "slide"), still pick the closest and also put the exact verb in `custom_action`.
- `interaction` on each part: press -> "press"; turn_cw/turn_ccw -> "turn"; inspect -> "inspect"; everything else -> "grab".
- `prompt_phrase` is what a segmentation model will search for. It must be a concrete noun phrase naming
  one object with a visible attribute: "red rocker switch", "brass drain valve". Never "the switch you turn off",
  never "A or B", never spatial words like "left" or "top".
- Every step needs `start_frame` and `end_frame` in source frame indices (frame = seconds x fps). Use the
  narration timestamps below to anchor them.
- `keyframe` on a part is the frame where it is most visible and unoccluded.
- `safety` is true when skipping the step could injure someone or damage the machine (power off, bleed pressure, lockout).
- `check` is what the trainee should observe when the step is done. `failure` is what goes wrong if it is skipped.
  `coach` is one short spoken line a good trainer would say.
- Part ids and step part_ids are snake_case and must match. Procedure id is kebab-case.

Narration (transcribed separately, with frame stamps):
{transcript_text or '(no narration)'}
"""


# Spark is a reasoning model and reasoning shares the output budget. On a real 79 s clip the first
# extraction burned 6000 tokens of reasoning and emitted 0 chars of JSON, so the default is generous
# and an incomplete response is retried once at double the budget.
DEFAULT_MAX_OUTPUT_TOKENS = int(os.environ.get("SPARK_MAX_OUTPUT_TOKENS", "16000"))
REASONING_EFFORT = os.environ.get("SPARK_REASONING_EFFORT", "")  # e.g. low | medium | high; empty = API default


# Open-weight bonus: route the verify pass to Muse Spark Open on Baseten or Modal by setting
# VERIFY_BASE_URL, VERIFY_API_KEY and VERIFY_MODEL (e.g. meta-models/muse-spark-open-102 on Baseten).
# Extraction stays on Spark 1.3 because it needs native video input.
VERIFY_BASE_URL = os.environ.get("VERIFY_BASE_URL", "")
VERIFY_MODEL = os.environ.get("VERIFY_MODEL", "")


def _verify_client():
    if VERIFY_BASE_URL and VERIFY_MODEL:
        from openai import OpenAI
        return OpenAI(base_url=VERIFY_BASE_URL, api_key=os.environ.get("VERIFY_API_KEY", "") or os.environ.get("MODEL_API_KEY", ""), timeout=600), VERIFY_MODEL
    return client(), SPARK_MODEL


def _responses_structured(prompt_blocks: list[dict], schema: dict, name: str, metrics: Metrics,
                          stage: str, max_output_tokens: int | None = None, _retry: bool = True) -> dict:
    c, model = _verify_client() if stage == "verify" else (client(), SPARK_MODEL)
    budget = max_output_tokens or DEFAULT_MAX_OUTPUT_TOKENS
    text = ""
    usage_in = usage_out = 0
    status = "completed"
    kwargs = {}
    if REASONING_EFFORT:
        kwargs["reasoning"] = {"effort": REASONING_EFFORT}
    def once():
        txt, st, ui, uo = "", "completed", 0, 0
        stream = c.responses.create(
            model=model,
            input=[{"type": "message", "role": "user", "content": prompt_blocks}],
            text={"format": {"type": "json_schema", "name": name, "schema": schema, "strict": False}},
            max_output_tokens=budget,
            stream=True,
            **kwargs,
        )
        for kind, payload in iter_output_text(stream):
            if kind == "delta":
                txt += payload
            else:
                st = getattr(payload, "status", "completed")
                u = getattr(payload, "usage", None)
                if u:
                    ui, uo = int(getattr(u, "input_tokens", 0)), int(getattr(u, "output_tokens", 0))
        return txt, st, ui, uo

    with Timer() as t:
        text, status, usage_in, usage_out = with_retries(once, label=f"Spark {stage}")
    metrics.log(stage, model, t.wall, spark_cost(usage_in, usage_out),
                tokens_in=usage_in, tokens_out=usage_out, note=f"{status}, budget {budget}")
    if status != "completed" or not text.strip():
        if _retry:
            return _responses_structured(prompt_blocks, schema, name, metrics, stage, budget * 2, _retry=False)
        raise RuntimeError(f"Spark {stage} returned status={status!r} with {len(text)} chars after a retry at "
                           f"{budget} output tokens; raise SPARK_MAX_OUTPUT_TOKENS or lower SPARK_REASONING_EFFORT")
    return json.loads(text)


def normalize_draft(draft: dict, info: VideoInfo) -> dict:
    """Make Spark's draft safe for the rest of the pipeline without changing its meaning."""
    draft = copy.deepcopy(draft)
    draft["procedure"]["id"] = slugify(draft["procedure"].get("id") or draft["procedure"]["title"], "-")
    seen: dict[str, str] = {}
    for p in draft["parts"]:
        new_id = slugify(p["id"])
        seen[p["id"]] = new_id
        p["id"] = new_id
        p["keyframe"] = int(min(max(p.get("keyframe", 0), 0), max(info.nb_frames - 1, 0)))
    for i, s in enumerate(draft["steps"], start=1):
        s["order"] = i
        s["part_id"] = seen.get(s["part_id"], slugify(s["part_id"]))
        if s["action"] not in ACTION_VOCAB:
            s["custom_action"] = s.get("custom_action") or slugify(s["action"])
            s["action"] = "grab"
        if s.get("custom_action") in (None, "", s["action"]):
            s.pop("custom_action", None)
        s["start_frame"] = int(min(max(s["start_frame"], 0), max(info.nb_frames - 1, 0)))
        s["end_frame"] = int(min(max(s["end_frame"], s["start_frame"]), max(info.nb_frames - 1, 0)))
    # interaction follows the step that uses the part; a part used by turn steps is a turn part
    actions_by_part: dict[str, list[str]] = {}
    for s in draft["steps"]:
        actions_by_part.setdefault(s["part_id"], []).append(s["action"])
    for p in draft["parts"]:
        acts = actions_by_part.get(p["id"], [])
        if acts:
            p["interaction"] = INTERACTION_FOR_ACTION[acts[0]]
    # drop parts no step references; add stub parts for steps that reference unknown parts
    used = {s["part_id"] for s in draft["steps"]}
    known = {p["id"] for p in draft["parts"]}
    draft["parts"] = [p for p in draft["parts"] if p["id"] in used]
    for pid in sorted(used - known):
        step = next(s for s in draft["steps"] if s["part_id"] == pid)
        draft["parts"].append({"id": pid, "name": pid.replace("_", " "), "prompt_phrase": pid.replace("_", " "),
                               "interaction": INTERACTION_FOR_ACTION[step["action"]], "keyframe": step["start_frame"]})
    return draft


def extract(file_id: str, info: VideoInfo, transcript: dict, out_dir: Path, metrics: Metrics,
            title_hint: str | None = None, resume: bool = False) -> dict:
    """Pass 1. Returns the normalized draft and writes draft.json."""
    if resume and (out_dir / "draft.json").exists():
        metrics.log("extract", SPARK_MODEL, 0.0, 0.0, note="resumed from draft.json")
        return json.loads((out_dir / "draft.json").read_text())
    ttext = transcript_as_text(transcript)
    if is_mock():
        from ..mock import mock_draft
        draft = mock_draft(info)
        metrics.log("extract", SPARK_MODEL, 0.0, 0.0, note="mock")
    else:
        draft = _responses_structured(
            [{"type": "input_text", "text": extraction_prompt(info, ttext, title_hint)},
             {"type": "input_file", "file_id": file_id}],
            draft_schema(), "procedure_draft", metrics, "extract")
    draft = normalize_draft(draft, info)
    write_json(out_dir / "draft.json", draft)
    return draft


def verify(draft: dict, transcript: dict, out_dir: Path, metrics: Metrics, resume: bool = False) -> dict:
    """Pass 2. Flags steps the narration does not support. Writes review.json."""
    if resume and (out_dir / "review.json").exists():
        metrics.log("verify", SPARK_MODEL, 0.0, 0.0, note="resumed from review.json")
        return json.loads((out_dir / "review.json").read_text())
    ttext = transcript_as_text(transcript)
    if is_mock():
        review = {"steps": [{"order": s["order"], "supported": True, "evidence": "mock", "suggested_fix": ""}
                            for s in draft["steps"]]}
        metrics.log("verify", SPARK_MODEL, 0.0, 0.0, note="mock")
    elif not ttext.strip():
        review = {"steps": [], "note": "no narration to verify against"}
        metrics.log("verify", SPARK_MODEL, 0.0, 0.0, note="skipped, no narration")
    else:
        prompt = f"""Below is a draft training procedure extracted from a video, followed by the narration transcript.
For each step, decide whether the narration supports it as written. A step is unsupported if the narration
describes a different part, a different action, a different order, or never mentions it. Quote the narration.

DRAFT:
{json.dumps(draft, indent=1)}

NARRATION:
{ttext}
"""
        review = _responses_structured([{"type": "input_text", "text": prompt}], VERIFY_SCHEMA,
                                       "procedure_review", metrics, "verify")
    flagged = [r for r in review.get("steps", []) if not r.get("supported", True)]
    review["flagged_count"] = len(flagged)
    write_json(out_dir / "review.json", review)
    return review
