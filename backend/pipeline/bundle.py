"""Bundle: merge the draft with SAM results into a schema-valid spec, plus crops and size hints.

For each part: best keyframe (highest confidence), crop from the box with 10% padding,
size hint from the box against a reference width, and the mask PNG stack for the dashboard.
"""
from __future__ import annotations

from pathlib import Path

from .common import VideoInfo, extract_frame, write_json
from .segment import PartResult

# A phone filming a bench procedure at arm's length frames roughly this much real width.
# Override per procedure with --reference-width-m when a tape measure is in shot.
DEFAULT_REFERENCE_WIDTH_M = 0.6
SIZE_MIN_M, SIZE_MAX_M = 0.03, 0.40


def size_hint_m(box: tuple[int, int, int, int], info: VideoInfo, reference_width_m: float) -> float:
    x1, y1, x2, y2 = box
    longest_px = max(x2 - x1 + 1, y2 - y1 + 1)
    m = longest_px / info.width * reference_width_m
    return round(min(max(m, SIZE_MIN_M), SIZE_MAX_M), 3)


def crop_part(video: Path, info: VideoInfo, keyframe: int, box: tuple[int, int, int, int], out_png: Path) -> Path:
    from PIL import Image
    frame_png = extract_frame(video, keyframe, info.fps, out_png.with_name(out_png.stem + "_frame.png"))
    img = Image.open(frame_png).convert("RGB")
    x1, y1, x2, y2 = box
    pad_x, pad_y = int((x2 - x1 + 1) * 0.10), int((y2 - y1 + 1) * 0.10)
    crop = img.crop((max(0, x1 - pad_x), max(0, y1 - pad_y), min(img.width, x2 + 1 + pad_x), min(img.height, y2 + 1 + pad_y)))
    crop.save(out_png)
    frame_png.unlink(missing_ok=True)
    return out_png


def bundle(video: Path, info: VideoInfo, file_id: str, draft: dict, results: dict[str, PartResult],
           work: Path, reference_width_m: float = DEFAULT_REFERENCE_WIDTH_M) -> dict:
    """Returns the full spec (schema 1.0) and writes spec.json, bundle/parts.json and crops."""
    crops = work / "bundle" / "crops"
    crops.mkdir(parents=True, exist_ok=True)
    parts_out, parts_meta = [], []
    for p in draft["parts"]:
        r = results.get(p["id"])
        best = r.best if r else None
        if best is None:
            # no geometry at all: centre box so the engine still spawns something, flagged for the trainer
            w, h = info.width // 6, info.height // 6
            box = (info.width // 2 - w // 2, info.height // 2 - h // 2, info.width // 2 + w // 2, info.height // 2 + h // 2)
            keyframe, conf, source = p["keyframe"], 0.0, "manual"
        else:
            box, keyframe, conf, source = best.box, best.frame, (best.confidence or 0.0), r.source
            if source == "none":
                source = "manual"
        box = (max(0, box[0]), max(0, box[1]), min(info.width - 1, box[2]), min(info.height - 1, box[3]))
        crop_png = crop_part(video, info, keyframe, box, crops / f"{p['id']}.png")
        parts_out.append({
            "id": p["id"], "name": p["name"], "prompt_phrase": p["prompt_phrase"], "interaction": p["interaction"],
            "keyframe": int(keyframe), "box": [int(v) for v in box],
            "size_hint_m": size_hint_m(box, info, reference_width_m),
            "confidence": round(float(conf), 3), "source": source,
        })
        parts_meta.append({
            "id": p["id"], "crop": str(crop_png.relative_to(work)), "frames_tracked": len(r.hits) if r else 0,
            "frames_billed": r.frames_billed if r else 0, "mean_confidence": round(r.mean_confidence, 3) if r else 0.0,
            "masks": [str(h.mask_png.relative_to(work)) for h in (r.hits if r else []) if h.mask_png],
            "raw": str(r.raw_path.relative_to(work)) if r and r.raw_path else None,
            "trim_start": (r.hits[0].frame - 0) if False else (getattr(r, "trim_start", None) if r else None),
            "track": [{"frame": h.frame, "box": list(h.box), "c": h.confidence} for h in (r.hits if r else [])],
        })
    spec = {
        "schema_version": "1.0",
        "procedure": {
            "id": draft["procedure"]["id"], "title": draft["procedure"]["title"], "source_video": file_id,
            "fps": round(info.fps, 3), "width": info.width, "height": info.height, "duration_s": round(info.duration_s, 2),
        },
        "parts": parts_out,
        "steps": draft["steps"],
    }
    write_json(work / "spec.json", spec)
    write_json(work / "bundle" / "parts.json", parts_meta)
    return spec
