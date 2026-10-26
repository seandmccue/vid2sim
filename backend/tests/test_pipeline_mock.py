"""Whole pipeline in mock mode on a synthetic clip. No network, no key, a few seconds."""
import json
from pathlib import Path

from backend.pipeline.common import probe
from backend.pipeline.segment import parse_sam
from backend.pipeline.validate import validate_spec
from backend.run import run


def test_probe(synthetic_video):
    info = probe(synthetic_video)
    assert (info.width, info.height) == (320, 240)
    assert 29 < info.fps < 31 and info.nb_frames >= 85 and info.has_audio


def test_parse_sam_real_protocol_text(tmp_path):
    from backend.mock import mock_sam_output
    hits = parse_sam(mock_sam_output("red rocker switch", 10), trim_start=100, masks_dir=tmp_path / "m", part_id="sw",
                     frame_size=(320, 334))
    assert len(hits) == 6
    assert hits[0].frame == 100 and hits[-1].frame == 105
    assert hits[0].box == (211, 228, 270, 254)
    assert hits[0].confidence == 0.9
    assert hits[0].mask_png and hits[0].mask_png.exists()
    from PIL import Image
    img = Image.open(hits[0].mask_png)
    assert img.size == (320, 334)                                   # full-frame canvas
    assert img.getpixel((5, 5)) == 0                                # nothing outside the box
    assert img.crop((211, 228, 271, 255)).getextrema()[1] == 255    # mask lives inside its bounds


def test_parse_sam_empty_is_not_an_error(tmp_path):
    assert parse_sam("", 0, None, "x") == []


def test_full_mock_run(synthetic_video, tmp_path):
    work = tmp_path / "work"
    card = run(synthetic_video, work=work, streaming_root=tmp_path / "streaming", unity_project=tmp_path / "unity",
               ground_truth=Path("data/procedures/p1/ground_truth.json").resolve())
    spec = json.loads((work / "spec.json").read_text())
    assert validate_spec(spec) == []
    assert len(spec["steps"]) == 6 and len(spec["parts"]) == 5
    # the "dirty" filter concept prompt misses in mock -> box fallback recorded as a metric
    by_id = {p["id"]: p for p in spec["parts"]}
    assert by_id["old_filter"]["source"] == "box"
    assert by_id["power_switch"]["source"] == "concept"
    assert (work / "transcript.json").exists() and (work / "review.json").exists() and (work / "metrics.json").exists()
    assert (tmp_path / "streaming" / spec["procedure"]["id"] / "spec.json").exists()
    assert json.loads((tmp_path / "streaming" / "index.json").read_text())["procedures"] == [spec["procedure"]["id"]]
    assert card["ground_truth"]["steps_correct"] == 6
    assert card["parts_found_box"] == 1 and card["parts_found_concept"] == 4
    # custom action 'unscrew' produced a generated script
    assert any(g["verb"] == "unscrew" and g["compiled"] for g in card["generated_actions"])
