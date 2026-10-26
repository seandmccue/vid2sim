#!/usr/bin/env python
"""Oct 26 exit test: one Spark call and one SAM call succeeding on your own footage.

    .venv/bin/python backend/smoke.py data/procedures/p1/p1.mp4 --concept "air filter"

Uploads once, asks Spark 1.3 to list the steps (streaming, max_output_tokens 4000), runs SAM 3.1 with one
concept prompt (one_bit, confidence on), confirms the parser decodes a mask, and logs wall-clock and cost
for both to metrics/runs.csv. Raw outputs are saved next to the video.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from backend.pipeline.common import (SAM_MODEL, SPARK_MODEL, Metrics, Timer, client, iter_output_text, probe,  # noqa: E402
                                     sam_cost, spark_cost, upload_video)
from backend.pipeline.segment import parse_sam  # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("video", type=Path)
    ap.add_argument("--concept", default="air filter", help="most visible part, as a concrete noun phrase")
    a = ap.parse_args()
    info = probe(a.video)
    print(f"{a.video.name}: {info.width}x{info.height} @ {info.fps:.2f} fps, {info.nb_frames} frames, audio={info.has_audio}")
    if info.nb_frames > 15000:
        print("clip is over 15,000 frames; SAM will reject it. Trim first."); return 2
    metrics = Metrics.start(a.video.parent.name)
    c = client()

    file_id = upload_video(a.video, metrics)
    print("file_id:", file_id)

    text, status, uin, uout = "", "?", 0, 0
    with Timer() as t:
        stream = c.responses.create(
            model=SPARK_MODEL, max_output_tokens=4000, stream=True,
            input=[{"type": "message", "role": "user", "content": [
                {"type": "input_text", "text": "List the steps in this video, one per line, with the second each starts."},
                {"type": "input_file", "file_id": file_id}]}])
        for kind, payload in iter_output_text(stream):
            if kind == "delta":
                text += payload; print(payload, end="", flush=True)
            else:
                status = getattr(payload, "status", "?")
                u = getattr(payload, "usage", None)
                if u:
                    uin, uout = int(u.input_tokens), int(u.output_tokens)
    print(f"\n[spark] status={status} {t.wall:.1f}s tokens in/out {uin}/{uout} cost ${spark_cost(uin, uout):.4f}")
    metrics.log("smoke_spark", SPARK_MODEL, t.wall, spark_cost(uin, uout), tokens_in=uin, tokens_out=uout, note=status)
    (a.video.parent / "smoke_spark.txt").write_text(text)

    raw = ""
    with Timer() as t:
        stream = c.responses.create(
            model=SAM_MODEL, stream=True,
            input=[{"type": "message", "role": "user", "content": [
                {"type": "input_text", "text": a.concept},
                {"type": "input_video", "file_id": file_id}]}],
            metadata={"mask_encoding": "one_bit", "include_confidence": "true"})
        for kind, payload in iter_output_text(stream):
            if kind == "delta":
                raw += payload
    (a.video.parent / "smoke_sam.txt").write_text(raw)
    hits = parse_sam(raw, 0, a.video.parent / "smoke_masks", "smoke", (info.width, info.height))
    confs = [h.confidence for h in hits if h.confidence is not None]
    print(f"[sam] '{a.concept}': {len(raw)} chars, {len(hits)} frames tracked, mean c={sum(confs)/len(confs) if confs else 0:.2f}, "
          f"masks decoded={sum(1 for h in hits if h.mask_png)}, {t.wall:.1f}s, cost ${sam_cost(info.nb_frames):.4f}")
    metrics.log("smoke_sam", SAM_MODEL, t.wall, sam_cost(info.nb_frames), frames=info.nb_frames, note=f"{a.concept}: {len(hits)} frames")
    print("logged to metrics/runs.csv as", metrics.run_id)
    return 0 if hits else 1


if __name__ == "__main__":
    raise SystemExit(main())
