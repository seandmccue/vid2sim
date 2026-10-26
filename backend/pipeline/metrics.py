"""Accuracy against the trainer's ground-truth list, and the per-run summary card."""
from __future__ import annotations

import difflib
from pathlib import Path

from .common import Metrics, read_json, write_json


_STOP = {"the", "a", "an", "of", "on", "onto", "into", "in", "to", "it", "its", "with", "and", "then", "this", "that"}


def _words(s: str) -> set[str]:
    return {w for w in "".join(c if c.isalnum() else " " for c in s.lower()).split() if w not in _STOP}


def _sim(a: str, b: str) -> float:
    """Character similarity, or containment of the shorter phrase's words in the longer one.
    'lid' inside 'dark blue circular filter lid' scores 1.0; 'put the lid on' vs 'cover with the lid' ~0.5."""
    ratio = difflib.SequenceMatcher(None, a.lower(), b.lower()).ratio()
    wa, wb = _words(a), _words(b)
    if not wa or not wb:
        return ratio
    small, big = (wa, wb) if len(wa) <= len(wb) else (wb, wa)
    return max(ratio, len(small & big) / len(small))


def compare_to_ground_truth(spec: dict, gt: dict) -> dict:
    """Monotonic best-match alignment: each trainer step is matched to the best later extracted step
    (text similarity against instruction or part name >= 0.45), so one inserted or merged step shifts
    nothing else. A step counts as correct when it matched and the action agrees (custom_action counts).
    Unmatched extracted steps are reported as extras."""
    gt_steps = gt.get("steps", [])
    parts = {p["id"]: p for p in spec["parts"]}
    ext = spec["steps"]
    correct, rows, cursor, used = 0, [], 0, set()
    for g in gt_steps:
        best, best_i = 0.0, None
        for i in range(cursor, len(ext)):
            s = ext[i]
            part_name = parts.get(s["part_id"], {}).get("name", s["part_id"])
            score = max(_sim(s["instruction"], g["text"]), 0.9 * _sim(part_name, g.get("part", "")))
            if score > best:
                best, best_i = score, i
        if best_i is None or best < 0.45:
            rows.append({"order": g["order"], "truth": g["text"], "extracted": None, "action_truth": g.get("action"),
                         "action_extracted": None, "matched": False, "action_ok": False, "ok": False})
            continue
        s = ext[best_i]
        cursor = best_i + 1
        used.add(best_i)
        action_ok = bool(s["action"] == g.get("action") or (s.get("custom_action") and s["custom_action"] == g.get("action")))
        ok = action_ok
        correct += int(ok)
        rows.append({"order": g["order"], "truth": g["text"], "extracted": s["instruction"], "extracted_order": s["order"],
                     "action_truth": g.get("action"), "action_extracted": s.get("custom_action") or s["action"],
                     "similarity": round(best, 2), "matched": True, "action_ok": action_ok, "ok": ok})
    extras = [s["instruction"] for i, s in enumerate(ext) if i not in used]
    return {"steps_truth": len(gt_steps), "steps_extracted": len(ext), "steps_correct": correct,
            "steps_matched": sum(1 for r in rows if r["matched"]),
            "accuracy": round(correct / len(gt_steps), 3) if gt_steps else None, "extra_steps": extras, "rows": rows}


def summary(spec: dict, metrics: Metrics, work: Path, gt_path: Path | None) -> dict:
    stage_rows = [r for r in metrics.rows if r["stage"] != "total"]
    total_row = next((r for r in metrics.rows if r["stage"] == "total"), None)
    tot = {"wall_s": float(total_row["wall_s"]) if total_row else sum(float(r["wall_s"]) for r in stage_rows),
           "cost_usd": sum(float(r["cost_usd"]) for r in stage_rows)}
    by_source = {"concept": 0, "box": 0, "manual": 0}
    for p in spec["parts"]:
        by_source[p["source"]] = by_source.get(p["source"], 0) + 1
    card = {
        "run_id": metrics.run_id,
        "procedure": spec["procedure"]["id"],
        "upload_to_spec_s": round(tot["wall_s"], 1),
        "api_cost_usd": round(tot["cost_usd"], 4),
        "steps": len(spec["steps"]),
        "parts": len(spec["parts"]),
        "parts_found_concept": by_source["concept"],
        "parts_found_box": by_source["box"],
        "parts_manual": by_source["manual"],
        "stages": [{"stage": r["stage"], "model": r["model"], "wall_s": float(r["wall_s"]), "cost_usd": float(r["cost_usd"]), "note": r["note"]} for r in metrics.rows],
    }
    if gt_path and gt_path.exists():
        card["ground_truth"] = compare_to_ground_truth(spec, read_json(gt_path))
    write_json(work / "metrics.json", card)
    return card
