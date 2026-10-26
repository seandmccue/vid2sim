"""SAM 3.1 per part: trim, concept prompt, parse, box fallback.

One request per part (no multi-concept syntax; lists return zero). Each clip is trimmed
to the step's frame range plus a 15-frame pad before upload so a 6-step procedure stays
under ~3,000 billed frames. Results are keyed by the <Nf> frame marker, never by line
order. Zero matches is a valid response, not an error: it triggers the box fallback,
where Spark returns a box on the keyframe and SAM re-runs with propagation forward.
"""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

from .common import (PRICE_SPARK_IN_PER_M, SAM_MODEL, SPARK_MODEL, Metrics, Timer, VideoInfo, client,
                     extract_frame, is_mock, iter_output_text, sam_cost, spark_cost, trim, upload_video)

PAD_FRAMES = 15
CONFIDENCE_FLOOR = 0.5
MAX_FRAMES_PER_REQUEST = 15000


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
    text = ""
    with Timer() as t:
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
                text += payload
    metrics.log(stage, SAM_MODEL, t.wall, sam_cost(frames), frames=frames, note=prompt[:60])
    return text


def parse_sam(raw: str, trim_start: int, masks_dir: Path | None, part_id: str) -> list[FrameHit]:
    """Parse SAM output with meta-sam-parser; map trim-local frames back to source frames."""
    from meta_sam_parser import CompletedOutcome, SegmentationBoxRecord, SegmentationMaskRecord, \
        decode_mask_to_raster, video_segmentation_format
    parser = video_segmentation_format().create_parser()
    if raw:
        for _ in parser.push(raw):
            pass
    finished = parser.finish(CompletedOutcome())
    records = finished.result.records
    hits: dict[tuple[int, str], FrameHit] = {}
    for r in records:
        if isinstance(r, SegmentationBoxRecord):
            local = r.frame.frame_index if r.frame else 0
            key = (local, r.object_id)
            # the parser exposes half-open right/bottom; the spec keeps SAM's inclusive x2/y2
            hits[key] = FrameHit(frame=trim_start + local,
                                 box=(int(round(r.left)), int(round(r.top)),
                                      int(round(r.right)) - 1, int(round(r.bottom)) - 1),
                                 confidence=r.confidence)
    if masks_dir is not None:
        masks_dir.mkdir(parents=True, exist_ok=True)
        for r in records:
            if isinstance(r, SegmentationMaskRecord):
                local = r.frame.frame_index if r.frame else 0
                key = (local, r.object_id)
                hit = hits.get(key)
                if hit is None:
                    continue
                try:
                    raster = decode_mask_to_raster(r.mask)
                    from PIL import Image
                    img = Image.frombytes("L", (r.mask.width, r.mask.height), bytes(255 if b else 0 for b in raster))
                    out = masks_dir / f"{part_id}_f{trim_start + local:06d}_o{r.object_id}.png"
                    img.save(out)
                    hit.mask_png = out
                    if hit.confidence is None and r.confidence is not None:
                        hit.confidence = r.confidence
                except Exception:
                    pass
    # the engine needs one object per part; keep the object id with the most frames
    by_obj: dict[str, list[FrameHit]] = {}
    for (local, oid), h in hits.items():
        by_obj.setdefault(oid, []).append(h)
    if not by_obj:
        return []
    best_oid = max(by_obj, key=lambda o: (len(by_obj[o]), sum((h.confidence or 0) for h in by_obj[o])))
    return sorted(by_obj[best_oid], key=lambda h: h.frame)


def _spark_box_fallback(video: Path, keyframe: int, info: VideoInfo, part: dict, work: Path,
                        metrics: Metrics) -> tuple[int, int, int, int] | None:
    """Ask Spark for a box on the keyframe. Returns inclusive x1,y1,x2,y2 or None."""
    png = extract_frame(video, keyframe, info.fps, work / f"kf_{part['id']}.png")
    if is_mock():
        from ..mock import mock_box
        metrics.log("box_fallback", SPARK_MODEL, 0.0, 0.0, note="mock")
        return mock_box(part, info)
    import base64
    schema = {"type": "object", "additionalProperties": False,
              "required": ["found", "x1", "y1", "x2", "y2"],
              "properties": {"found": {"type": "boolean"}, "x1": {"type": "integer"}, "y1": {"type": "integer"},
                             "x2": {"type": "integer"}, "y2": {"type": "integer"}}}
    b64 = base64.b64encode(png.read_bytes()).decode()
    c = client()
    text, status, uin, uout = "", "completed", 0, 0
    with Timer() as t:
        stream = c.responses.create(
            model=SPARK_MODEL,
            input=[{"type": "message", "role": "user", "content": [
                {"type": "input_text", "text":
                    f"This frame is {info.width}x{info.height} pixels. Find the {part['name']} ({part['prompt_phrase']}). "
                    f"Return a tight pixel bounding box with inclusive corners, origin top-left. If it is not visible, found=false."},
                {"type": "input_image", "image_url": f"data:image/png;base64,{b64}"},
            ]}],
            text={"format": {"type": "json_schema", "name": "box", "schema": schema, "strict": True}},
            max_output_tokens=2000, stream=True)
        for kind, payload in iter_output_text(stream):
            if kind == "delta":
                text += payload
            else:
                status = getattr(payload, "status", "completed")
                u = getattr(payload, "usage", None)
                if u:
                    uin, uout = int(u.input_tokens), int(u.output_tokens)
    metrics.log("box_fallback", SPARK_MODEL, t.wall, spark_cost(uin, uout), tokens_in=uin, tokens_out=uout, note=part["id"])
    if status != "completed" or not text:
        return None
    d = json.loads(text)
    if not d.get("found"):
        return None
    x1, y1 = max(0, min(d["x1"], d["x2"])), max(0, min(d["y1"], d["y2"]))
    x2, y2 = min(info.width - 1, max(d["x1"], d["x2"])), min(info.height - 1, max(d["y1"], d["y2"]))
    if x2 <= x1 or y2 <= y1:
        return None
    return x1, y1, x2, y2


def segment_part(video: Path, info: VideoInfo, part: dict, steps: list[dict], work: Path,
                 metrics: Metrics) -> PartResult:
    """Concept prompt first; box fallback when empty or mean confidence < 0.5."""
    using = [s for s in steps if s["part_id"] == part["id"]] or [{"start_frame": part["keyframe"], "end_frame": part["keyframe"]}]
    start = max(0, min(s["start_frame"] for s in using) - PAD_FRAMES)
    end = min(info.nb_frames - 1, max(s["end_frame"] for s in using) + PAD_FRAMES)
    if end - start + 1 > MAX_FRAMES_PER_REQUEST:
        end = start + MAX_FRAMES_PER_REQUEST - 1
    keyframe = min(max(part["keyframe"], start), end)
    frames = end - start + 1

    trims = work / "trims"
    clip, trim_start = trim(video, start, end, info.fps, trims / f"{part['id']}.mp4")
    file_id = upload_video(clip, metrics)
    raw_dir = work / "bundle" / "raw"
    raw_dir.mkdir(parents=True, exist_ok=True)
    masks_dir = work / "bundle" / "masks"

    raw = _sam_call(file_id, part["prompt_phrase"], None, metrics, "segment_concept", frames)
    (raw_dir / f"{part['id']}_concept.txt").write_text(raw)
    hits = parse_sam(raw, trim_start, masks_dir, part["id"])
    confs = [h.confidence for h in hits if h.confidence is not None]
    mean_c = sum(confs) / len(confs) if confs else 0.0
    result = PartResult(part_id=part["id"], source="concept", hits=hits, frames_billed=frames,
                        raw_path=raw_dir / f"{part['id']}_concept.txt", mean_confidence=mean_c)

    if hits and mean_c >= CONFIDENCE_FLOOR:
        return result

    box = _spark_box_fallback(video, keyframe, info, part, work, metrics)
    if box is None:
        result.source = "concept" if hits else "none"
        return result
    prompt = _box_prompt(keyframe - trim_start, box, (info.width, info.height))
    raw2 = _sam_call(file_id, prompt, "forward", metrics, "segment_box", max(1, end - keyframe + 1))
    (raw_dir / f"{part['id']}_box.txt").write_text(raw2)
    hits2 = parse_sam(raw2, trim_start, masks_dir, part["id"])
    if not hits2:
        # SAM returned nothing on the box; keep Spark's box as the hit so the engine still has geometry
        hits2 = [FrameHit(frame=keyframe, box=box, confidence=None)]
    confs2 = [h.confidence for h in hits2 if h.confidence is not None]
    result2 = PartResult(part_id=part["id"], source="box", hits=hits2, frames_billed=frames + max(1, end - keyframe + 1),
                         raw_path=raw_dir / f"{part['id']}_box.txt",
                         mean_confidence=sum(confs2) / len(confs2) if confs2 else 0.0)
    return result2 if (len(hits2) >= len(hits) or mean_c < CONFIDENCE_FLOOR) else result


def segment(video: Path, info: VideoInfo, draft: dict, work: Path, metrics: Metrics) -> dict[str, PartResult]:
    results = {}
    for part in draft["parts"]:
        results[part["id"]] = segment_part(video, info, part, draft["steps"], work, metrics)
    return results
