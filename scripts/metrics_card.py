#!/usr/bin/env python
"""The metrics card for the video and the submission page, straight from metrics/runs.csv and data/sessions.

    .venv/bin/python scripts/metrics_card.py            # markdown to stdout
    .venv/bin/python scripts/metrics_card.py --json     # the numbers

Capture: expert video → spec (minutes, dollars) per procedure, from the pipeline's 'total' rows and every
stage row. Guide: SAM first-phrase hit rate and round-trip on live frames (guide_find), Spark step checks
(guide_check), voice (guide_hear / guide_ask). Verify: graded videos and cost. Nothing here is typed by hand.
"""
from __future__ import annotations

import argparse
import csv
import json
import re
import statistics
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSV = ROOT / "metrics" / "runs.csv"
SESSIONS = ROOT / "data" / "sessions"
PROCEDURES = ROOT / "data" / "procedures"


def _rows() -> list[dict]:
    if not CSV.exists():
        return []
    return list(csv.DictReader(CSV.open()))


def _median_ms(rows):
    w = [float(r["wall_s"]) * 1000 for r in rows if float(r["wall_s"]) > 0]
    return round(statistics.median(w)) if w else None


def capture(rows: list[dict]) -> list[dict]:
    """One line per procedure that has a spec: minutes from upload to spec, dollars, steps vs ground truth."""
    out = []
    for d in sorted(PROCEDURES.iterdir()) if PROCEDURES.exists() else []:
        spec_p = d / "spec.json"
        if not d.is_dir() or not spec_p.exists():
            continue
        spec = json.loads(spec_p.read_text())
        rr = [r for r in rows if r["procedure"] == d.name and not r["stage"].startswith(("guide_", "verify_observe"))]
        if not rr:
            continue
        runs = sorted({r["run_id"] for r in rr})
        wall = 0.0
        for run in runs:
            one = [r for r in rr if r["run_id"] == run]
            tot = next((r for r in one if r["stage"] == "total"), None)
            wall += float(tot["wall_s"]) if tot else sum(float(r["wall_s"]) for r in one)
        cost = sum(float(r["cost_usd"]) for r in rr if r["stage"] != "total")
        card = json.loads((d / "metrics.json").read_text()) if (d / "metrics.json").exists() else {}
        gt = (card.get("ground_truth") or {})
        out.append({"procedure": d.name, "title": spec["procedure"]["title"], "steps": len(spec["steps"]), "parts": len(spec["parts"]),
                    "video_s": round(spec["procedure"].get("duration_s", 0)), "minutes": round(wall / 60, 1), "cost_usd": round(cost, 2),
                    "steps_correct": gt.get("steps_correct"), "steps_expected": gt.get("steps_truth"),
                    "runs": len(runs)})
    return out


def guide(rows: list[dict]) -> dict:
    finds = [r for r in rows if r["stage"] == "guide_find"]
    hit = sum(1 for r in finds if re.search(r"matches=([1-9]\d*)/", r["note"] or ""))
    checks = [r for r in rows if r["stage"] == "guide_check"]
    verdicts = {}
    for r in checks:
        m = re.search(r"-> (yes|no|unclear)", r["note"] or "")
        if m: verdicts[m.group(1)] = verdicts.get(m.group(1), 0) + 1
    hears = [r for r in rows if r["stage"] == "guide_hear"]
    asks = [r for r in rows if r["stage"] == "guide_ask"]
    sessions = []
    if SESSIONS.exists():
        for p in SESSIONS.glob("*.json"):
            try:
                s = json.loads(p.read_text())
                if s.get("mode") == "guide": sessions.append(s)
            except Exception:
                pass
    return {
        "find_calls": len(finds), "find_hit_rate": round(hit / len(finds), 2) if finds else None, "find_median_ms": _median_ms(finds),
        "find_cost_usd": round(sum(float(r["cost_usd"]) for r in finds), 3),
        "check_calls": len(checks), "check_median_ms": _median_ms(checks), "check_verdicts": verdicts,
        "check_cost_usd": round(sum(float(r["cost_usd"]) for r in checks), 3),
        "hear_calls": len(hears), "hear_median_ms": _median_ms(hears), "ask_calls": len(asks), "ask_median_ms": _median_ms(asks),
        "sessions": len(sessions),
        "sessions_completed_steps": sum(len(s.get("steps", [])) for s in sessions),
        "overrides": sum(int(s.get("overrides", 0) or 0) for s in sessions),
    }


def verify(rows: list[dict]) -> dict:
    obs = [r for r in rows if r["stage"] == "verify_observe"]
    reports = []
    if SESSIONS.exists():
        for p in SESSIONS.glob("*.json"):
            try:
                s = json.loads(p.read_text())
                if s.get("mode") == "verify": reports.append(s)
            except Exception:
                pass
    return {"videos_graded": len(reports), "passed": sum(1 for r in reports if r.get("passed")),
            "safety_fails": sum(1 for r in reports if r.get("missed_safety")),
            "observe_median_s": round(statistics.median([float(r["wall_s"]) for r in obs]), 1) if obs else None,
            "cost_per_video_usd": round(statistics.median([float(r.get("cost_usd", 0)) for r in reports]), 3) if reports else None}


def card() -> dict:
    rows = _rows()
    return {"capture": capture(rows), "guide": guide(rows), "verify": verify(rows),
            "total_api_spend_usd": round(sum(float(r["cost_usd"]) for r in rows), 2)}


def markdown(c: dict) -> str:
    lines = ["| Procedure | Video | Expert video → spec | Steps vs ground truth |", "| --- | --- | --- | --- |"]
    for p in c["capture"]:
        gt = f"{p['steps_correct']}/{p['steps_expected']}" if p.get("steps_correct") is not None and p.get("steps_expected") else "—"
        lines.append(f"| {p['title']} | {p['video_s']} s | {p['minutes']} min, ${p['cost_usd']:.2f} | {gt} |")
    g, v = c["guide"], c["verify"]
    lines += ["", "| Live on the phone | |", "| --- | --- |",
              f"| SAM first-phrase hit rate on live frames | {int(g['find_hit_rate'] * 100) if g['find_hit_rate'] is not None else '—'}% of {g['find_calls']} frames, median {g['find_median_ms']} ms, ${g['find_cost_usd']} |",
              f"| Spark step checks | {g['check_calls']} checks, median {g['check_median_ms']} ms, verdicts {g['check_verdicts']} |",
              f"| Voice | {g['hear_calls']} utterances heard (median {g['hear_median_ms']} ms), {g['ask_calls']} questions answered (median {g['ask_median_ms']} ms) |",
              f"| Guide sessions | {g['sessions']} sessions, {g['sessions_completed_steps']} steps, {g['overrides']} overrides |",
              f"| Verify | {v['videos_graded']} videos graded, {v['passed']} passed, {v['safety_fails']} failed on a skipped safety step; Spark watch median {v['observe_median_s']} s; about ${v['cost_per_video_usd']} per video |",
              f"| Total Meta API spend, all of it | ${c['total_api_spend_usd']} |"]
    return "\n".join(lines)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", action="store_true")
    a = ap.parse_args()
    c = card()
    print(json.dumps(c, indent=2) if a.json else markdown(c))
