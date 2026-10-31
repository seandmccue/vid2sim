"""Verify mode: grade a trainee's video against the expert spec.

The same pipeline front end (upload, Voice Transcribe, Spark 1.3 on the video) with one change to the prompt:
the expert's steps go in and Spark maps what it sees to their ids. diff() then says, per expert step, done /
missed / out_of_order; a missed safety step fails the attempt whatever the score. Thumbnails come from the
trainee's own video, one per matched step. The report is the same session shape Guide writes.
"""
from __future__ import annotations

import json
from pathlib import Path

from .common import SPARK_MODEL, Metrics, Timer, VideoInfo, extract_frame, is_mock, probe, upload_video, write_json
from .transcribe import transcribe, transcript_as_text

OBSERVATION_SCHEMA = {
    "type": "object",
    "additionalProperties": False,
    "required": ["observed_steps", "extra_actions", "summary"],
    "properties": {
        "observed_steps": {
            "type": "array",
            "items": {
                "type": "object", "additionalProperties": False,
                "required": ["step_order", "start_frame", "end_frame", "note", "confidence"],
                "properties": {
                    "step_order": {"type": ["integer", "null"], "description": "the expert step this action accomplishes, or null if it is not in the procedure"},
                    "start_frame": {"type": "integer"}, "end_frame": {"type": "integer"},
                    "note": {"type": "string", "description": "what the trainee did, one short sentence"},
                    "confidence": {"type": "number", "minimum": 0, "maximum": 1},
                },
            },
        },
        "extra_actions": {"type": "array", "items": {"type": "string"}, "description": "things done that are not in the procedure"},
        "summary": {"type": "string"},
    },
}


def observation_prompt(spec: dict, info: VideoInfo, transcript_text: str) -> str:
    steps = "\n".join(f"  {s['order']}. {s['instruction']} [part: {s['part_id']}; done when: {s.get('check', '')}]"
                      + ("  (SAFETY)" if s.get("safety") else "") for s in spec["steps"])
    return f"""An expert's procedure is listed below. The attached video shows a TRAINEE attempting the same procedure.
Watch the whole video and list, in the order they happen, every distinct physical action the trainee performs.
For each action give the expert step number it accomplishes (step_order), or null if it is not part of the procedure.

Rules:
- Do not assume the trainee did every step. A step that never happens in the video is simply absent from your list.
- A step done twice appears twice. A step done out of the expert's order is listed where it actually happens.
- Only count a step as accomplished when the video shows the action itself (hands on the part, the state changing),
  not when the trainee merely looks toward it or the narration mentions it.
- start_frame / end_frame are source frame indices (frame = seconds x {info.fps:.2f}); the video has {info.nb_frames} frames ({info.duration_s:.1f} s).
- extra_actions: anything done that is not in the procedure, one short phrase each.
- summary: two sentences on how the attempt went.

Expert procedure: {spec['procedure']['title']}
{steps}

Trainee narration, if any (frame-stamped):
{transcript_text or '(no narration)'}
"""


def observe(file_id: str, info: VideoInfo, transcript: dict, spec: dict, metrics: Metrics) -> dict:
    if is_mock():
        from ..mock import mock_observation
        metrics.log("verify_observe", SPARK_MODEL, 0.0, 0.0, note="mock")
        return mock_observation(spec, info)
    from .extract import _responses_structured
    return _responses_structured(
        [{"type": "input_text", "text": observation_prompt(spec, info, transcript_as_text(transcript))},
         {"type": "input_file", "file_id": file_id}],
        OBSERVATION_SCHEMA, "trainee_observation", metrics, "verify_observe")


def _lis_positions(seq: list[int]) -> set[int]:
    """Indices of a longest strictly increasing subsequence (patience sorting with parent links)."""
    import bisect
    tails: list[int] = []; tails_idx: list[int] = []; parent = [-1] * len(seq)
    for i, v in enumerate(seq):
        k = bisect.bisect_left(tails, v)
        if k == len(tails):
            tails.append(v); tails_idx.append(i)
        else:
            tails[k] = v; tails_idx[k] = i
        parent[i] = tails_idx[k - 1] if k > 0 else -1
    out = set()
    i = tails_idx[-1] if tails_idx else -1
    while i != -1:
        out.add(i); i = parent[i]
    return out


def diff(spec: dict, observed_steps: list[dict], extra_actions: list[str] | None = None, min_confidence: float = 0.4) -> dict:
    """Per expert step: done (in order), out_of_order, or missed. Score = steps done in order / total.
    Any missed safety step fails the attempt; otherwise pass needs every step done (in order or not) and score >= 80."""
    obs = sorted([o for o in observed_steps if o.get("step_order") is not None and float(o.get("confidence", 1)) >= min_confidence],
                 key=lambda o: int(o.get("start_frame", 0)))
    first_seen: dict[int, dict] = {}
    for o in obs:
        first_seen.setdefault(int(o["step_order"]), o)
    chron = sorted(first_seen.values(), key=lambda o: int(o.get("start_frame", 0)))
    orders = [int(o["step_order"]) for o in chron]
    in_order = {orders[i] for i in _lis_positions(orders)}
    steps = []
    for s in spec["steps"]:
        o = first_seen.get(s["order"])
        if o is None:
            status = "missed"
        elif s["order"] in in_order:
            status = "done"
        else:
            status = "out_of_order"
        steps.append({"order": s["order"], "part_id": s["part_id"], "instruction": s["instruction"], "safety": bool(s.get("safety")),
                      "status": status, "start_frame": int(o["start_frame"]) if o else None, "end_frame": int(o["end_frame"]) if o else None,
                      "note": (o or {}).get("note", ""), "confidence": float((o or {}).get("confidence", 0) or 0),
                      "failure": s.get("failure", "")})
    total = len(steps)
    done = sum(1 for s in steps if s["status"] == "done")
    missed_safety = [s["order"] for s in steps if s["status"] == "missed" and s["safety"]]
    missed = [s["order"] for s in steps if s["status"] == "missed"]
    out_of_order = [s["order"] for s in steps if s["status"] == "out_of_order"]
    score = round(100 * done / total) if total else 0
    passed = not missed_safety and not missed and score >= 80
    return {"steps": steps, "score": score, "passed": passed, "steps_total": total, "steps_done_in_order": done,
            "missed": missed, "out_of_order": out_of_order, "missed_safety": missed_safety,
            "extra_actions": list(extra_actions or []),
            "verdict": ("FAIL: safety step skipped" if missed_safety else "FAIL" if not passed else "PASS")}


def verify_video(video: Path, spec: dict, work: Path, metrics: Metrics | None = None, progress=print) -> dict:
    """Trainee video + expert spec → report JSON (written to work/report.json) with thumbnails in work/thumbs."""
    work.mkdir(parents=True, exist_ok=True)
    metrics = metrics or Metrics.start(f"verify:{spec['procedure']['id']}")
    info = probe(video)
    with Timer() as total:
        progress("1/4 upload")
        file_id = upload_video(video, metrics, cache=work / "upload.json")
        progress("2/4 transcribe")
        transcript = transcribe(video, info, work, metrics)
        progress("3/4 Spark watches the trainee against the expert's steps")
        observation = observe(file_id, info, transcript, spec, metrics)
        write_json(work / "observation.json", observation)
        progress("4/4 diff and thumbnails")
        report = diff(spec, observation.get("observed_steps", []), observation.get("extra_actions", []))
    thumbs = work / "thumbs"
    for s in report["steps"]:
        if s["start_frame"] is None:
            continue
        mid = (int(s["start_frame"]) + int(s["end_frame"])) // 2
        try:
            extract_frame(video, max(0, min(mid, info.nb_frames - 1)), info.fps, thumbs / f"step_{s['order']}.jpg")
            s["thumbnail"] = f"thumbs/step_{s['order']}.jpg"
        except Exception:
            s["thumbnail"] = None
    report.update({"mode": "verify", "procedure_id": spec["procedure"]["id"], "procedure_title": spec["procedure"]["title"],
                   "video": video.name, "duration_s": round(info.duration_s, 1), "summary": observation.get("summary", ""),
                   "wall_s": round(total.wall, 1), "cost_usd": round(metrics.totals()["cost_usd"], 4), "metrics_run_id": metrics.run_id})
    write_json(work / "report.json", report)
    return report
