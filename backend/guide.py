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

import json

from .pipeline.common import SAM_MODEL, SPARK_MODEL, Metrics, Timer, client, is_mock, iter_output_text, spark_cost, with_retries

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


# ------------------------------------------------------------------ the step check (Spark looks at the frame)

CHECK_SCHEMA = {
    "type": "object", "additionalProperties": False, "required": ["verdict", "note"],
    "properties": {
        "verdict": {"type": "string", "enum": ["yes", "no", "unclear"]},
        "note": {"type": "string", "description": "one short sentence on what you see that decides it"},
    },
}
CHECK_MAX_OUTPUT_TOKENS = 600


@dataclass
class CheckResult:
    verdict: str          # yes | no | unclear
    note: str
    wall_ms: int
    cost_usd: float
    tokens_in: int = 0
    tokens_out: int = 0


def check(jpeg: bytes, check_text: str, part_name: str, metrics: Metrics | None = None) -> CheckResult:
    """Muse Spark 1.3 on the current frame: is the step's `check` statement true in this photo? The check is a
    visible binary state ("switch is in the OFF position"), so the answer is yes / no / unclear plus one sentence.
    Low reasoning effort: this should come back in a second or two."""
    data, w, h = _prepare(jpeg)
    if is_mock():
        if metrics: metrics.log("guide_check", SPARK_MODEL, 0.0, 0.0, note=f"mock: {check_text[:40]}")
        return CheckResult("yes", "mock", 0, 0.0)
    b64 = base64.b64encode(data).decode()
    prompt = (f"Look at this photo of a {part_name}. Is the following statement true right now: \"{check_text}\". "
              f"Answer yes only if the photo clearly shows it, no if the photo clearly shows it is not the case, "
              f"and unclear if the {part_name} is not visible well enough to tell. Then one short sentence on what you see.")
    c = client()

    def once():
        txt, st, ui, uo = "", "completed", 0, 0
        stream = c.responses.create(
            model=SPARK_MODEL,
            input=[{"type": "message", "role": "user", "content": [
                {"type": "input_text", "text": prompt},
                {"type": "input_image", "image_url": f"data:image/jpeg;base64,{b64}"},
            ]}],
            text={"format": {"type": "json_schema", "name": "step_check", "schema": CHECK_SCHEMA, "strict": True}},
            reasoning={"effort": "low"},
            max_output_tokens=CHECK_MAX_OUTPUT_TOKENS, stream=True)
        for kind, payload in iter_output_text(stream):
            if kind == "delta":
                txt += payload
            else:
                st = getattr(payload, "status", "completed")
                u = getattr(payload, "usage", None)
                if u:
                    ui, uo = int(getattr(u, "input_tokens", 0)), int(getattr(u, "output_tokens", 0))
        return txt, st, ui, uo

    with Timer() as t:
        txt, status, ui, uo = with_retries(once, label="Spark guide_check", tries=2, base_delay=0.5)
    verdict, note = "unclear", ""
    try:
        parsed = json.loads(txt) if txt.strip() else {}
        verdict = parsed.get("verdict", "unclear") if parsed.get("verdict") in ("yes", "no", "unclear") else "unclear"
        note = str(parsed.get("note", ""))[:200]
    except json.JSONDecodeError:
        low = txt.strip().lower()
        verdict = "yes" if low.startswith("yes") else "no" if low.startswith("no") else "unclear"
        note = txt.strip()[:200]
    cost = spark_cost(ui, uo)
    if metrics:
        metrics.log("guide_check", SPARK_MODEL, t.wall, cost, tokens_in=ui, tokens_out=uo,
                    note=f"{check_text[:40]} -> {verdict} ({status})")
    return CheckResult(verdict, note, int(t.wall * 1000), cost, ui, uo)


def check_response(step: dict, part: dict, res: CheckResult) -> dict:
    """What the page needs for each verdict: advance, coach, or ask for a closer look."""
    out = asdict(res)
    out.update({"step_order": step["order"], "part_name": part["name"], "check": step.get("check", "")})
    if res.verdict == "yes":
        out["advance"] = True; out["message"] = "Done. " + (res.note or "")
    elif res.verdict == "no":
        out["advance"] = False; out["message"] = step.get("coach") or f"Not yet: {step.get('check', '')}."
    else:
        out["advance"] = False; out["message"] = f"Can't tell. Hold the phone closer to the {part['name']} and tap Done again."
    return out
