#!/usr/bin/env python
"""Rebuild data/procedures/<id>/metrics.json from every logged run of that procedure.

    .venv/bin/python backend/metrics_aggregate.py <procedure_id> [...]

Resumed stages log $0 and 0 s, so summing every row across runs gives the honest cost of the
module: API spend is counted once per call, wall time is the sum of stage walls (aborted
attempts included, 'total' rows excluded).
"""
from __future__ import annotations

import csv
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from backend.pipeline.common import METRICS_CSV, Metrics, read_json, write_json  # noqa: E402
from backend.pipeline import metrics as mm  # noqa: E402

DATA = Path(__file__).resolve().parent.parent / "data" / "procedures"


def aggregate(pid: str) -> dict:
    rows = [r for r in csv.DictReader(open(METRICS_CSV)) if r["procedure"] == pid]
    runs = sorted({r["run_id"] for r in rows})
    stage_rows = [r for r in rows if r["stage"] != "total"]
    # wall clock per run: its own 'total' row (stages overlap when they run in parallel), or the stage
    # sum for a run that aborted before logging one; cost is every row, counted once
    wall = 0.0
    for run in runs:
        rr = [r for r in rows if r["run_id"] == run]
        tot = next((r for r in rr if r["stage"] == "total"), None)
        wall += float(tot["wall_s"]) if tot else sum(float(r["wall_s"]) for r in rr)
    work = DATA / pid
    spec = read_json(work / "spec.json")
    old = read_json(work / "metrics.json") if (work / "metrics.json").exists() else {}
    card = mm.summary(spec, Metrics(run_id=runs[-1] if runs else "-", procedure=pid, rows=stage_rows), work,
                      work / "ground_truth.json")
    card["upload_to_spec_s"] = round(wall, 1)
    for k in ("generated_actions", "textures", "streaming_assets"):
        if k in old:
            card[k] = old[k]
    card["runs"] = runs
    card["note"] = f"aggregated across {len(runs)} run(s); resumed stages cost $0 and 0 s"
    write_json(work / "metrics.json", card)
    return card


if __name__ == "__main__":
    for pid in sys.argv[1:]:
        c = aggregate(pid)
        g = c.get("ground_truth") or {}
        print(f"{pid}: {c['upload_to_spec_s']/60:.1f} min  ${c['api_cost_usd']:.2f}  "
              f"{g.get('steps_correct','-')}/{g.get('steps_truth','-')}  parts {c['parts_found_concept']}/{c['parts_found_box']}/{c['parts_manual']}  runs {len(c['runs'])}")
