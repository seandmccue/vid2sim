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


def _align(gt_steps: list[dict], ext: list[dict], parts: dict, floor: float = 0.45) -> list[tuple[int, int, float]]:
    """Monotonic alignment maximising total similarity (dynamic programming, like sequence alignment with
    gaps free). Returns (gt_index, ext_index, similarity) pairs. A greedy best-match shifted every later
    pair when the extraction was finer-grained than the trainer's list, which is the common case."""
    n, m = len(gt_steps), len(ext)
    sim = [[0.0] * m for _ in range(n)]
    for i, g in enumerate(gt_steps):
        for j, s in enumerate(ext):
            part_name = parts.get(s["part_id"], {}).get("name", s["part_id"])
            v = max(_sim(s["instruction"], g["text"]), 0.9 * _sim(part_name, g.get("part", "")))
            sim[i][j] = v if v >= floor else 0.0
    # dp[i][j] = best total using gt[:i] and ext[:j]
    dp = [[0.0] * (m + 1) for _ in range(n + 1)]
    for i in range(1, n + 1):
        for j in range(1, m + 1):
            dp[i][j] = max(dp[i - 1][j], dp[i][j - 1], dp[i - 1][j - 1] + sim[i - 1][j - 1])
    pairs = []
    i, j = n, m
    while i > 0 and j > 0:
        if sim[i - 1][j - 1] > 0 and dp[i][j] == dp[i - 1][j - 1] + sim[i - 1][j - 1]:
            pairs.append((i - 1, j - 1, sim[i - 1][j - 1]))
            i, j = i - 1, j - 1
        elif dp[i - 1][j] >= dp[i][j - 1]:
            i -= 1
        else:
            j -= 1
    return list(reversed(pairs))


def compare_to_ground_truth(spec: dict, gt: dict) -> dict:
    """Each trainer step is aligned (in order) to the best extracted step; a step counts as correct when it
    aligned and the action agrees (custom_action counts). Unaligned extracted steps are reported as extras:
    a finer-grained extraction is not penalised, a missing or re-ordered trainer step is."""
    gt_steps = gt.get("steps", [])
    parts = {p["id"]: p for p in spec["parts"]}
    ext = spec["steps"]
    pairs = {gi: (ej, v) for gi, ej, v in _align(gt_steps, ext, parts)}
    correct, rows = 0, []
    for gi, g in enumerate(gt_steps):
        if gi not in pairs:
            rows.append({"order": g["order"], "truth": g["text"], "extracted": None, "action_truth": g.get("action"),
                         "action_extracted": None, "matched": False, "action_ok": False, "ok": False})
            continue
        ej, v = pairs[gi]
        s = ext[ej]
        action_ok = bool(s["action"] == g.get("action") or (s.get("custom_action") and s["custom_action"] == g.get("action")))
        correct += int(action_ok)
        rows.append({"order": g["order"], "truth": g["text"], "extracted": s["instruction"], "extracted_order": s["order"],
                     "action_truth": g.get("action"), "action_extracted": s.get("custom_action") or s["action"],
                     "similarity": round(v, 2), "matched": True, "action_ok": action_ok, "ok": action_ok})
    used = {ej for ej, _ in pairs.values()}
    extras = [s["instruction"] for j, s in enumerate(ext) if j not in used]
    return {"steps_truth": len(gt_steps), "steps_extracted": len(ext), "steps_correct": correct,
            "steps_matched": len(pairs),
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
