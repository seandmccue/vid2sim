"""SAM 3.1 per part: trim, concept prompt, parse, box fallback.

One request per part (no multi-concept syntax; lists return zero). Each clip is trimmed
to the step's frame range plus a 15-frame pad before upload so a 6-step procedure stays
under ~3,000 billed frames. Results are keyed by the <Nf> frame marker, never by line
order. Zero matches is a valid response, not an error: it triggers the box fallback,
where Spark returns a box on the keyframe and SAM re-runs with propagation forward.
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path

from .common import (PRICE_SPARK_IN_PER_M, SAM_MODEL, SPARK_MODEL, Metrics, Timer, VideoInfo, client,
                     extract_frame, is_mock, iter_output_text, keyframe_jpeg_b64, sam_cost, spark_cost, trim,
                     upload_video, with_retries)

PAD_FRAMES = 15
CONFIDENCE_FLOOR = 0.5
MAX_FRAMES_PER_REQUEST = 15000
_LUT = bytes.maketrans(b"\x01", b"\xff")


@dataclass
class FrameHit:
    frame: int                      # source-video frame index
    box: tuple[int, int, int, int]  # inclusive x1,y1,x2,y2 in source pixels
    confidence: float | None
    mask_png: Path | None = None    # written by segment() when a mask record exists


@dataclass
class PartResult:
    part_id: str
    source: str                     # "concept" | "box" | "none"
    hits: list[FrameHit] = field(default_factory=list)
    frames_billed: int = 0
    raw_path: Path | None = None
    mean_confidence: float = 0.0
    trim_start: int = 0

    @property
    def best(self) -> FrameHit | None:
        if not self.hits:
            return None
        return max(self.hits, key=lambda h: (h.confidence if h.confidence is not None else 0.0, -h.frame))


def _box_prompt(keyframe_local: int, box: tuple[int, int, int, int], size: tuple[int, int]) -> str:
    from meta_sam_parser import FramePrompt, PromptBox, PromptObject, build_object_prompt
    x1, y1, x2, y2 = box
    # PromptBox is half-open; spec boxes are inclusive, so right = x2 + 1, bottom = y2 + 1
    return build_object_prompt(size=size, objects=[
        PromptObject(id=1, prompts=[FramePrompt(frame=keyframe_local, box=PromptBox(x1, y1, x2 + 1, y2 + 1))])])


def _sam_call(file_id: str, prompt: str, propagation: str | None, metrics: Metrics, stage: str,
              frames: int) -> str:
    """Stream one SAM request and return the raw output_text."""
    if is_mock():
        from ..mock import mock_sam_output
        metrics.log(stage, SAM_MODEL, 0.0, 0.0, frames=frames, note="mock")
        return mock_sam_output(prompt, frames)
    metadata = {"mask_encoding": "one_bit", "include_confidence": "true"}
    if propagation:
        metadata["propagation_direction"] = propagation
    c = client()

    def once() -> str:
        out = ""
        stream = c.responses.create(
            model=SAM_MODEL,
            input=[{"type": "message", "role": "user", "content": [
                {"type": "input_text", "text": prompt},
                {"type": "input_video", "file_id": file_id},
            ]}],
            metadata=metadata,
            stream=True,
        )
        for kind, payload in iter_output_text(stream):
            if kind == "delta":
                out += payload
        return out

    with Timer() as t:
        text = with_retries(once, label=f"SAM {stage}")
    metrics.log(stage, SAM_MODEL, t.wall, sam_cost(frames), frames=frames, note=prompt[:60])
    return text


_LINE_RE = re.compile(r"^<(\d+)f>(.*)$")
_BOX_RE = re.compile(r"(\d+)<\|box;x1=(-?\d+(?:\.\d+)?);y1=(-?\d+(?:\.\d+)?);x2=(-?\d+(?:\.\d+)?);y2=(-?\d+(?:\.\d+)?);w=\d+;h=\d+(?:;c=([0-9.]+))?\|>")


def parse_boxes(raw: str, trim_start: int) -> dict[str, list[FrameHit]]:
    """Boxes for every frame and object, straight from the wire text (<Nf>id<|box;...|><|mask;...|>, ...).
    meta-sam-parser validates every mask payload on push, which is pure Python and takes minutes for a
    1080p part, so the box pass never goes through it. x2/y2 are inclusive as SAM reports them."""
    by_obj: dict[str, list[FrameHit]] = {}
    for line in raw.splitlines():
        m = _LINE_RE.match(line.strip())
        if not m:
            continue
        local = int(m.group(1))
        for b in _BOX_RE.finditer(m.group(2)):
            oid, x1, y1, x2, y2, c = b.groups()
            by_obj.setdefault(oid, []).append(FrameHit(
                frame=trim_start + local,
                box=(int(round(float(x1))), int(round(float(y1))), int(round(float(x2))), int(round(float(y2)))),
                confidence=float(c) if c is not None else None))
    return by_obj


def _line_for_frame(raw: str, local_frame: int) -> str | None:
    prefix = f"<{local_frame}f>"
    for line in raw.splitlines():
        if line.startswith(prefix):
            return line
    return None


def decode_mask_png(raw: str, trim_start: int, source_frame: int, object_id: str, frame_size: tuple[int, int],
                    out_png: Path) -> Path | None:
    """Decode one frame's mask for one object into a full-frame PNG. Only that line is pushed through the
    parser, so cost is one mask, not the whole track. Returns None when the frame has no mask record."""
    from meta_sam_parser import CompletedOutcome, SegmentationMaskRecord, decode_mask_to_raster, video_segmentation_format
    line = _line_for_frame(raw, source_frame - trim_start)
    if line is None:
        return None
    parser = video_segmentation_format().create_parser()
    for _ in parser.push(line + "\n"):
        pass
    result = parser.finish(CompletedOutcome()).result
    for r in result.records:
        if isinstance(r, SegmentationMaskRecord) and r.object_id == object_id:
            raster = decode_mask_to_raster(r.mask)
            from PIL import Image
            tile = Image.frombytes("L", (r.mask.width, r.mask.height), bytes(raster).translate(_LUT))
            bd = r.bounds
            left, top = int(round(bd.left)), int(round(bd.top))
            bw, bh = max(1, int(round(bd.right)) - left), max(1, int(round(bd.bottom)) - top)
            if (bw, bh) != tile.size:
                tile = tile.resize((bw, bh), Image.NEAREST)
            img = Image.new("L", frame_size, 0)
            img.paste(tile, (left, top))
            out_png.parent.mkdir(parents=True, exist_ok=True)
            img.save(out_png, compress_level=3)
            return out_png
    return None


def parse_sam(raw: str, trim_start: int, masks_dir: Path | None, part_id: str,
              frame_size: tuple[int, int] | None = None, mask_frames: set[int] | None = None) -> list[FrameHit]:
    """Parse SAM output: boxes for every frame (fast), masks only for `mask_frames` (source frame indices).
    A mask raster covers only its box bounds (the parser README example: a 60x27 box carries a 27-row,
    60-column mask), so each PNG is a full-frame canvas with the raster pasted at its bounds.
    The engine needs one object per part: the object id with the most frames wins."""
    by_obj = parse_boxes(raw, trim_start)
    if not by_obj:
        return []
    best_oid = max(by_obj, key=lambda o: (len(by_obj[o]), sum((h.confidence or 0) for h in by_obj[o])))
    hits = sorted(by_obj[best_oid], key=lambda h: h.frame)
    if masks_dir is not None and mask_frames and frame_size:
        for h in hits:
            if h.frame in mask_frames:
                out = masks_dir / f"{part_id}_f{h.frame:06d}_o{best_oid}.png"
                try:
                    h.mask_png = out if out.exists() else decode_mask_png(raw, trim_start, h.frame, best_oid, frame_size, out)
                except Exception:
                    h.mask_png = None
    return hits


def _spark_box_fallback(video: Path, keyframe: int, info: VideoInfo, part: dict, work: Path,
                        metrics: Metrics) -> tuple[int, int, int, int] | None:
    """Ask Spark for a box on the keyframe. Returns inclusive x1,y1,x2,y2 or None."""
    png = extract_frame(video, keyframe, info.fps, work / f"kf_{part['id']}.png")
    if is_mock():
        from ..mock import mock_box
        metrics.log("box_fallback", SPARK_MODEL, 0.0, 0.0, note="mock")
        return mock_box(part, info)
    schema = {"type": "object", "additionalProperties": False,
              "required": ["found", "x1", "y1", "x2", "y2"],
              "properties": {"found": {"type": "boolean"}, "x1": {"type": "integer"}, "y1": {"type": "integer"},
                             "x2": {"type": "integer"}, "y2": {"type": "integer"}}}
    # send a 1280-wide JPEG, not a 1080p PNG: the base64 payload drops ~20x and the call stops stalling
    b64, sw, sh = keyframe_jpeg_b64(png)
    scale_x, scale_y = info.width / sw, info.height / sh
    c = client()

    def once():
        txt, st, ui, uo = "", "completed", 0, 0
        stream = c.responses.create(
            model=SPARK_MODEL,
            input=[{"type": "message", "role": "user", "content": [
                {"type": "input_text", "text":
                    f"This frame is {sw}x{sh} pixels. Find the {part['name']} ({part['prompt_phrase']}). "
                    f"Return a tight pixel bounding box with inclusive corners, origin top-left. If it is not visible, found=false."},
                {"type": "input_image", "image_url": f"data:image/jpeg;base64,{b64}"},
            ]}],
            text={"format": {"type": "json_schema", "name": "box", "schema": schema, "strict": True}},
            max_output_tokens=4000, stream=True)  # reasoning shares the budget; 2000 hit the cap on a real run
        for kind, payload in iter_output_text(stream):
            if kind == "delta":
                txt += payload
            else:
                st = getattr(payload, "status", "completed")
                u = getattr(payload, "usage", None)
                if u:
                    ui, uo = int(u.input_tokens), int(u.output_tokens)
        return txt, st, ui, uo

    with Timer() as t:
        text, status, uin, uout = with_retries(once, label=f"box fallback {part['id']}")
    metrics.log("box_fallback", SPARK_MODEL, t.wall, spark_cost(uin, uout), tokens_in=uin, tokens_out=uout, note=part["id"])
    if status != "completed" or not text:
        return None
    d = json.loads(text)
    if not d.get("found"):
        return None
    # back to source pixels
    bx1, by1, bx2, by2 = (int(round(d["x1"] * scale_x)), int(round(d["y1"] * scale_y)),
                          int(round(d["x2"] * scale_x)), int(round(d["y2"] * scale_y)))
    x1, y1 = max(0, min(bx1, bx2)), max(0, min(by1, by2))
    x2, y2 = min(info.width - 1, max(bx1, bx2)), min(info.height - 1, max(by1, by2))
    if x2 <= x1 or y2 <= y1:
        return None
    return x1, y1, x2, y2


def segment_part(video: Path, info: VideoInfo, part: dict, steps: list[dict], work: Path,
                 metrics: Metrics, resume: bool = False) -> PartResult:
    """Concept prompt first; box fallback when empty or mean confidence < 0.5."""
    using = [s for s in steps if s["part_id"] == part["id"]] or [{"start_frame": part["keyframe"], "end_frame": part["keyframe"]}]
    start = max(0, min(s["start_frame"] for s in using) - PAD_FRAMES)
    end = min(info.nb_frames - 1, max(s["end_frame"] for s in using) + PAD_FRAMES)
    if end - start + 1 > MAX_FRAMES_PER_REQUEST:
        end = start + MAX_FRAMES_PER_REQUEST - 1
    keyframe = min(max(part["keyframe"], start), end)
    frames = end - start + 1

    trims = work / "trims"
    raw_dir = work / "bundle" / "raw"
    raw_dir.mkdir(parents=True, exist_ok=True)
    masks_dir = work / "bundle" / "masks"
    concept_raw = raw_dir / f"{part['id']}_concept.txt"
    box_raw = raw_dir / f"{part['id']}_box.txt"
    trim_start = start
    file_id = None

    def ensure_upload() -> str:
        nonlocal file_id, trim_start
        if file_id is None:
            clip, trim_start = trim(video, start, end, info.fps, trims / f"{part['id']}.mp4")
            file_id = upload_video(clip, metrics)
        return file_id

    if resume and concept_raw.exists():
        raw = concept_raw.read_text()
        metrics.log("segment_concept", SAM_MODEL, 0.0, 0.0, frames=frames, note=f"resumed from {concept_raw.name}")
    else:
        raw = _sam_call(ensure_upload(), part["prompt_phrase"], None, metrics, "segment_concept", frames)
        concept_raw.write_text(raw)
    hits = parse_sam(raw, trim_start, None, part["id"], (info.width, info.height))
    confs = [h.confidence for h in hits if h.confidence is not None]
    mean_c = sum(confs) / len(confs) if confs else 0.0
    result = PartResult(part_id=part["id"], source="concept", hits=hits, frames_billed=frames,
                        raw_path=concept_raw, mean_confidence=mean_c, trim_start=trim_start)

    if hits and mean_c >= CONFIDENCE_FLOOR:
        return _with_keyframe_mask(result, raw, trim_start, info, masks_dir)

    box_meta = raw_dir / f"{part['id']}_box.json"
    if resume and box_meta.exists() and json.loads(box_meta.read_text()).get("box") is None:
        metrics.log("box_fallback", SPARK_MODEL, 0.0, 0.0, note=f"resumed: {part['id']} not found before")
        result.source = "concept" if hits else "none"
        return result
    if resume and box_raw.exists():
        raw2 = box_raw.read_text()
        box = tuple(json.loads(box_meta.read_text())["box"]) if box_meta.exists() else None
        metrics.log("segment_box", SAM_MODEL, 0.0, 0.0, note=f"resumed from {box_raw.name}")
    else:
        box = _spark_box_fallback(video, keyframe, info, part, work, metrics)
        if box is None:
            box_meta.write_text(json.dumps({"box": None, "keyframe": keyframe}))
            result.source = "concept" if hits else "none"
            return result
        prompt = _box_prompt(keyframe - trim_start, box, (info.width, info.height))
        raw2 = _sam_call(ensure_upload(), prompt, "forward", metrics, "segment_box", max(1, end - keyframe + 1))
        box_raw.write_text(raw2)
        box_meta.write_text(json.dumps({"box": list(box), "keyframe": keyframe}))
    if box is None:
        result.source = "concept" if hits else "none"
        return result
    hits2 = parse_sam(raw2, trim_start, None, part["id"], (info.width, info.height))
    if not hits2:
        # SAM returned nothing on the box; keep Spark's box as the hit so the engine still has geometry
        hits2 = [FrameHit(frame=keyframe, box=box, confidence=None)]
    confs2 = [h.confidence for h in hits2 if h.confidence is not None]
    result2 = PartResult(part_id=part["id"], source="box", hits=hits2, frames_billed=frames + max(1, end - keyframe + 1),
                         raw_path=box_raw, mean_confidence=sum(confs2) / len(confs2) if confs2 else 0.0,
                         trim_start=trim_start)
    chosen = result2 if (len(hits2) >= len(hits) or mean_c < CONFIDENCE_FLOOR) else result
    return _with_keyframe_mask(chosen, raw2 if chosen is result2 else raw, trim_start, info, masks_dir)


def _with_keyframe_mask(result: PartResult, raw: str, trim_start: int, info: VideoInfo, masks_dir: Path) -> PartResult:
    """Decode exactly one mask per part at pipeline time: the keyframe the engine and the crop use.
    Other frames are decoded on demand by the backend (GET .../mask/{part}/{frame}.png)."""
    best = result.best
    if best is None or best.confidence is None and result.source == "box" and not raw:
        return result
    if best is not None and raw:
        by_obj = parse_boxes(raw, trim_start)
        oid = max(by_obj, key=lambda o: (len(by_obj[o]), sum((h.confidence or 0) for h in by_obj[o]))) if by_obj else None
        if oid is not None:
            out = masks_dir / f"{result.part_id}_f{best.frame:06d}_o{oid}.png"
            try:
                best.mask_png = out if out.exists() else decode_mask_png(raw, trim_start, best.frame, oid, (info.width, info.height), out)
            except Exception:
                best.mask_png = None
    return result


def segment(video: Path, info: VideoInfo, draft: dict, work: Path, metrics: Metrics,
            resume: bool = False) -> dict[str, PartResult]:
    results = {}
    for part in draft["parts"]:
        results[part["id"]] = segment_part(video, info, part, draft["steps"], work, metrics, resume=resume)
    return results
