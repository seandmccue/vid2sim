"""Guide mode: the trainee points a phone at the real machine and the current step's part is found live.

One SAM 3.1 call per camera frame, `input_image` instead of `input_video`, the part's `prompt_phrase` as the
only prompt, boxes and confidence back in the frame's own pixels. Server-side only: the Model API key never
reaches the browser. Every call appends a row to metrics/runs.csv (stage "guide_find").
"""
from __future__ import annotations

import base64
import io
import time
from dataclasses import asdict, dataclass

from .pipeline.common import SAM_MODEL, Metrics, Timer, client, is_mock, iter_output_text, with_retries

PRICE_SAM_PER_IMAGE = 2.50 / 1000     # image pricing: $2.50 per 1k images
CONFIDENCE_FLOOR = 0.5                # below this a match is "not found" (the dashboard phrase-fix is the remedy)
MAX_SIDE = 1280                       # frames arrive at 720p from the page; anything larger is downscaled here


@dataclass
class Match:
    x1: float
    y1: float
    x2: float
    y2: float
    confidence: float


@dataclass
class FindResult:
    phrase: str
    width: int
    height: int
    matches: list[Match]
    wall_ms: int
    cost_usd: float
    raw_count: int           # matches before the confidence floor
    note: str = ""

    def to_dict(self) -> dict:
        d = asdict(self)
        d["found"] = len(self.matches) > 0
        return d


def _prepare(jpeg: bytes) -> tuple[bytes, int, int]:
    """Decode once to learn the size; downscale when a client sent more than MAX_SIDE. Returns JPEG bytes."""
    from PIL import Image, ImageOps
    im = Image.open(io.BytesIO(jpeg))
    im = ImageOps.exif_transpose(im)   # a phone upload carries rotation in EXIF; the page sends canvas frames, already upright
    if max(im.size) > MAX_SIDE:
        im.thumbnail((MAX_SIDE, MAX_SIDE))
    if im.mode != "RGB":
        im = im.convert("RGB")
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=85)
    return buf.getvalue(), im.width, im.height


def find(jpeg: bytes, phrase: str, metrics: Metrics | None = None, floor: float = CONFIDENCE_FLOOR) -> FindResult:
    """Find `phrase` in one frame. Matches sorted by confidence, floor applied; raw_count keeps the unfiltered count."""
    data, w, h = _prepare(jpeg)
    if is_mock():
        m = [Match(w * 0.3, h * 0.3, w * 0.6, h * 0.7, 0.91)] if phrase else []
        if metrics: metrics.log("guide_find", SAM_MODEL, 0.0, 0.0, frames=1, note=f"mock {phrase[:40]}")
        return FindResult(phrase, w, h, m, 0, 0.0, len(m), "mock")
    b64 = base64.b64encode(data).decode()
    c = client()

    def once() -> str:
        out = ""
        stream = c.responses.create(
            model=SAM_MODEL,
            input=[{"type": "message", "role": "user", "content": [
                {"type": "input_text", "text": phrase},
                {"type": "input_image", "image_url": f"data:image/jpeg;base64,{b64}"},
            ]}],
            metadata={"mask_encoding": "one_bit", "include_confidence": "true"},
            stream=True,
        )
        for kind, payload in iter_output_text(stream):
            if kind == "delta":
                out += payload
        return out

    with Timer() as t:
        raw = with_retries(once, label="SAM guide_find", tries=2, base_delay=0.5)
    matches, note = parse_image_result(raw)
    kept = [m for m in sorted(matches, key=lambda m: -m.confidence) if m.confidence >= floor]
    res = FindResult(phrase, w, h, kept, int(t.wall * 1000), PRICE_SAM_PER_IMAGE, len(matches), note)
    if metrics:
        best = kept[0].confidence if kept else (max((m.confidence for m in matches), default=0.0))
        metrics.log("guide_find", SAM_MODEL, t.wall, PRICE_SAM_PER_IMAGE, frames=1,
                    note=f"{phrase[:40]} | matches={len(kept)}/{len(matches)} best={best:.2f}")
    return res


def parse_image_result(raw: str) -> tuple[list[Match], str]:
    """SAM's image output through meta-sam-parser: one box (or mask with bounds) per object, confidence attached."""
    from meta_sam_parser import CompletedOutcome, SegmentationBoxRecord, SegmentationMaskRecord, image_segmentation_format
    parser = image_segmentation_format().create_parser()
    for _ in parser.push(raw if raw.endswith("\n") else raw + "\n"):
        pass
    result = parser.finish(CompletedOutcome()).result
    out: dict[object, Match] = {}
    for r in result.records:
        conf = float(getattr(r, "confidence", None) or 0.0)
        if isinstance(r, SegmentationBoxRecord):
            out[r.object_id] = Match(float(r.left), float(r.top), float(r.right), float(r.bottom), conf)
        elif isinstance(r, SegmentationMaskRecord) and r.object_id not in out:
            b = r.bounds
            out[r.object_id] = Match(float(b.left), float(b.top), float(b.right), float(b.bottom), conf)
    diags = "; ".join(d.message for d in result.diagnostics if str(getattr(d.severity, "name", d.severity)).lower().startswith("err"))
    return list(out.values()), diags[:200]


# ------------------------------------------------------------------ what the page needs

def step_and_part(spec: dict, step_order: int) -> tuple[dict, dict]:
    step = next((s for s in spec["steps"] if s["order"] == step_order), None)
    if step is None:
        raise KeyError(f"no step {step_order}")
    part = next((p for p in spec["parts"] if p["id"] == step["part_id"]), None)
    if part is None:
        raise KeyError(f"step {step_order} names unknown part {step['part_id']}")
    return step, part


def procedure_card(pid: str, spec: dict, d) -> dict:
    """One landing-page entry: title, description from the first step, a thumbnail, whether sample footage exists."""
    first = spec["steps"][0] if spec["steps"] else None
    thumb_part = first["part_id"] if first else (spec["parts"][0]["id"] if spec["parts"] else None)
    crop = (d / "bundle" / "crops" / f"{thumb_part}.png") if thumb_part else None
    return {
        "id": pid,
        "title": spec["procedure"]["title"],
        "description": first["instruction"] if first else "",
        "steps": len(spec["steps"]),
        "parts": len(spec["parts"]),
        "thumbnail": f"/api/procedures/{pid}/files/bundle/crops/{thumb_part}.png" if crop and crop.exists() else None,
        "sample_video": f"/api/procedures/{pid}/files/sample.mp4" if (d / "sample.mp4").exists() else None,
        "safety_steps": sum(1 for s in spec["steps"] if s.get("safety")),
    }


def decode_image(image: str) -> bytes:
    """A data URL or bare base64 from the page's canvas."""
    if "," in image and image.lstrip().startswith("data:"):
        image = image.split(",", 1)[1]
    return base64.b64decode(image)


def save_session(sessions_dir, payload: dict) -> dict:
    """Guide results, one JSON per session, the same shape Verify will write: steps with seconds and attempts."""
    import json, uuid
    sessions_dir.mkdir(parents=True, exist_ok=True)
    sid = payload.get("session_id") or (time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6])
    payload["session_id"] = sid
    payload.setdefault("saved_at", time.strftime("%Y-%m-%dT%H:%M:%S"))
    (sessions_dir / f"{sid}.json").write_text(json.dumps(payload, indent=2))
    return {"session_id": sid, "path": str(sessions_dir / f"{sid}.json")}
