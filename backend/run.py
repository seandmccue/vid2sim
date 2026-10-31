#!/usr/bin/env python
"""One command: python backend/run.py data/procedures/p1/p1.mp4

Phone video in; spec.json, masks, crops, textures, StreamingAssets payload and a metrics
card out. Set VID2SIM_MOCK=1 to run the whole thing with no API key.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from backend.pipeline import bundle as bundle_mod  # noqa: E402
from backend.pipeline import cutouts as cutouts_mod  # noqa: E402
from backend.pipeline import extract as extract_mod  # noqa: E402
from backend.pipeline import generate as generate_mod  # noqa: E402
from backend.pipeline import metrics as metrics_mod  # noqa: E402
from backend.pipeline import package as package_mod  # noqa: E402
from backend.pipeline import segment as segment_mod  # noqa: E402
from backend.pipeline import transcribe as transcribe_mod  # noqa: E402
from backend.pipeline.common import UNITY_PROJECT, REPO_ROOT, Metrics, Timer, is_mock, probe, require_ffmpeg, upload_video, write_json  # noqa: E402
from backend.pipeline.validate import validate_spec  # noqa: E402


def run(video: Path, *, work: Path | None = None, title: str | None = None, ground_truth: Path | None = None,
        reference_width_m: float = bundle_mod.DEFAULT_REFERENCE_WIDTH_M, skip_segment: bool = False,
        skip_generate: bool = False, unity_project: Path | None = None, streaming_root: Path | None = None,
        resume: bool = False, step_cards: bool = False, shapes_on: bool = True, progress=print) -> dict:
    require_ffmpeg()
    video = video.resolve()
    if not video.exists():
        raise SystemExit(f"video not found: {video}")
    work = (work or video.parent).resolve()
    work.mkdir(parents=True, exist_ok=True)
    info = probe(video)
    metrics = Metrics.start(video.parent.name)
    progress(f"[{metrics.run_id}] {video.name}: {info.width}x{info.height} @ {info.fps:.2f} fps, {info.nb_frames} frames"
             + (" (MOCK)" if is_mock() else ""))

    with Timer() as total:
        progress("1/6 upload")
        file_id = upload_video(video, metrics, cache=work / "upload.json")
        progress("2/6 transcribe (Voice Transcribe)")
        transcript = transcribe_mod.transcribe(video, info, work, metrics, resume=resume)
        progress("3/6 extract + verify (Spark 1.3, two passes)")
        draft = extract_mod.extract(file_id, info, transcript, work, metrics, title_hint=title, resume=resume)
        review = extract_mod.verify(draft, transcript, work, metrics, resume=resume, file_id=file_id)
        if review.get("flagged_count"):
            progress(f"    verify flagged {review['flagged_count']} step(s); see review.json")
        progress(f"4/6 segment (SAM 3.1, {len(draft['parts'])} parts)")
        results = {} if skip_segment else segment_mod.segment(video, info, draft, work, metrics, resume=resume)
        progress("5/6 bundle")
        spec = bundle_mod.bundle(video, info, file_id, draft, results, work, reference_width_m)
        errors = validate_spec(spec)
        if errors:
            write_json(work / "errors.json", [str(e) for e in errors])
            raise SystemExit("spec failed validation:\n  " + "\n  ".join(str(e) for e in errors))
        progress("6/6 generate + package")
        unity = unity_project or UNITY_PROJECT
        if unity_project is None and not unity.exists() and not skip_generate:   # archived project absent: nothing to generate for
            progress("    no Unity project (archived); skipping Muse Code generation")
            skip_generate = True
        generated = [] if skip_generate else generate_mod.generate_actions(spec, unity, metrics, gate=not skip_generate and os.environ.get("VID2SIM_COMPILE_GATE", "1") == "1")
        shapes = [] if (skip_generate or not shapes_on) else generate_mod.generate_shapes(spec, unity, metrics, gate=os.environ.get("VID2SIM_COMPILE_GATE", "1") == "1")
        host = {} if (skip_generate or not shapes_on) else generate_mod.generate_host(spec, unity, metrics, gate=os.environ.get("VID2SIM_COMPILE_GATE", "1") == "1", work=work)
        textures = {} if skip_generate else generate_mod.generate_textures(spec, work, metrics)
        cards = generate_mod.generate_step_cards(spec, work, metrics) if (step_cards and not skip_generate) else {}
        cutouts = cutouts_mod.generate_cutouts(spec, work)   # photo cards for the engine; no API cost
        out_dir = package_mod.package(spec, work, streaming_root)
    metrics.log("total", "pipeline", total.wall, 0.0, note="upload to StreamingAssets")

    card = metrics_mod.summary(spec, metrics, work, ground_truth or (work / "ground_truth.json"))
    card["generated_actions"] = generated
    card["textures"] = len(textures)
    card["shapes"] = sum(1 for r in shapes if r.get("compiled"))
    card["host"] = host.get("compiled", False)
    card["host_subject"] = host.get("subject", "") or (host.get("decision") or {}).get("subject", "")
    card["cutouts"] = len(cutouts)
    card["step_cards"] = len(cards)
    card["streaming_assets"] = str(out_dir)
    write_json(work / "metrics.json", card)
    progress(f"done in {total.wall:.1f}s, est. ${card['api_cost_usd']:.3f}; spec -> {work / 'spec.json'}; payload -> {out_dir}")
    if "ground_truth" in card:
        g = card["ground_truth"]
        progress(f"steps correct vs ground truth: {g['steps_correct']}/{g['steps_truth']}")
    return card


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("video", type=Path)
    ap.add_argument("--work", type=Path, help="output folder (default: next to the video)")
    ap.add_argument("--title", help="hint for the procedure title")
    ap.add_argument("--ground-truth", type=Path, help="trainer's step list JSON for the accuracy metric")
    ap.add_argument("--reference-width-m", type=float, default=bundle_mod.DEFAULT_REFERENCE_WIDTH_M,
                    help="real-world width the frame covers, for size hints")
    ap.add_argument("--skip-segment", action="store_true", help="no SAM calls; parts get placeholder boxes")
    ap.add_argument("--skip-generate", action="store_true", help="no Muse Code / Muse Image calls")
    ap.add_argument("--mock", action="store_true", help="same as VID2SIM_MOCK=1")
    ap.add_argument("--resume", action="store_true", help="reuse transcript, draft, review, uploads and saved SAM output in the work folder")
    ap.add_argument("--step-cards", action="store_true", help="Muse Image step card per step for the dashboard (cents each; also VID2SIM_STEP_CARDS=1)")
    ap.add_argument("--streaming-root", type=Path, help="override the Unity StreamingAssets/procedures folder")
    ap.add_argument("--no-shapes", action="store_true", help="skip the Muse Code primitive models (engine uses photo cards)")
    a = ap.parse_args(argv)
    if a.mock:
        os.environ["VID2SIM_MOCK"] = "1"
    card = run(a.video, work=a.work, title=a.title, ground_truth=a.ground_truth, reference_width_m=a.reference_width_m,
               skip_segment=a.skip_segment, skip_generate=a.skip_generate, streaming_root=a.streaming_root, resume=a.resume,
               step_cards=a.step_cards or os.environ.get("VID2SIM_STEP_CARDS", "0") == "1", shapes_on=not a.no_shapes)
    return 0 if card else 1


if __name__ == "__main__":
    raise SystemExit(main())
