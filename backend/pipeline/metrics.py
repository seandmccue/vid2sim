"""Accuracy against the trainer's ground-truth list, and the per-run summary card."""
from __future__ import annotations

import difflib
from pathlib import Path

from .common import Metrics, read_json, write_json


def _sim(a: str, b: str) -> float:
    return difflib.SequenceMatcher(None, a.lower(), b.lower()).ratio()


def compare_to_ground_truth(spec: dict, gt: dict) -> dict:
    """A step counts as correct when the same ordinal has the same action and the instruction
    or part name matches the trainer's text at >= 0.6 similarity."""
    gt_steps = gt.get("steps", [])
    parts = {p["id"]: p for p in spec["parts"]}
    correct, rows = 0, []
    for i, g in enumerate(gt_steps):
        s = spec["steps"][i] if i < len(spec["steps"]) else None
        if s is None:
            rows.append({"order": g["order"], "truth": g["text"], "extracted": None, "action_ok": False, "text_ok": False})
            continue
        part_name = parts.get(s["part_id"], {}).get("name", s["part_id"])
        action_ok = s["action"] == g.get("action")
        text_ok = max(_sim(s["instruction"], g["text"]), _sim(part_name, g.get("part", ""))) >= 0.6
        ok = action_ok and text_ok
        correct += int(ok)
        rows.append({"order": g["order"], "truth": g["text"], "extracted": s["instruction"],
                     "action_truth": g.get("action"), "action_extracted": s["action"], "action_ok": action_ok, "text_ok": text_ok, "ok": ok})
    return {"steps_truth": len(gt_steps), "steps_extracted": len(spec["steps"]), "steps_correct": correct,
            "accuracy": round(correct / len(gt_steps), 3) if gt_steps else None, "rows": rows}


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
