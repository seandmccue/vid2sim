"""Shared plumbing: paths, API client, ffmpeg wrappers, cost table, metrics log."""
from __future__ import annotations

import csv
import json
import os
import shutil
import subprocess
import time
import uuid
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterator

from dotenv import load_dotenv

REPO_ROOT = Path(__file__).resolve().parents[2]
BACKEND_ROOT = REPO_ROOT / "backend"
SCHEMA_PATH = BACKEND_ROOT / "schema" / "procedure.schema.json"
METRICS_CSV = REPO_ROOT / "metrics" / "runs.csv"
UNITY_STREAMING = REPO_ROOT / "unity" / "TrainingSim" / "Assets" / "StreamingAssets" / "procedures"
UNITY_GENERATED = REPO_ROOT / "unity" / "TrainingSim" / "Assets" / "Generated"

load_dotenv(REPO_ROOT / ".env")

BASE_URL = os.environ.get("MODEL_API_BASE_URL", "https://api.meta.ai/v1")
SPARK_MODEL = "muse-spark-1.3"
SAM_MODEL = "sam-3.1"
IMAGE_MODEL = "muse-image-1.0"
ASR_MODEL = "muse-voice-transcribe-1.0"

# Published prices, Oct 2026. Transcribe and Image are per-unit estimates until the
# billing page confirms them; they are labelled as estimates in metrics notes.
PRICE_SPARK_IN_PER_M = 1.25
PRICE_SPARK_OUT_PER_M = 4.25
PRICE_SAM_PER_1K_FRAMES = 0.20
SAM_MIN_BILLED_FRAMES = 50
PRICE_ASR_PER_MIN_EST = 0.006
PRICE_IMAGE_EACH_EST = 0.04

ACTION_VOCAB = ["press", "grab", "pull", "lift", "place", "turn_cw", "turn_ccw", "inspect"]
INTERACTION_FOR_ACTION = {
    "press": "press", "grab": "grab", "pull": "grab", "lift": "grab", "place": "grab",
    "turn_cw": "turn", "turn_ccw": "turn", "inspect": "inspect",
}


def is_mock() -> bool:
    return os.environ.get("VID2SIM_MOCK", "0") not in ("", "0", "false", "False")


def api_key() -> str:
    key = os.environ.get("MODEL_API_KEY", "")
    if not key and not is_mock():
        raise RuntimeError("MODEL_API_KEY is not set. Put it in .env or run with VID2SIM_MOCK=1.")
    return key


def client():
    """One OpenAI-SDK client pointed at the Meta Model API. Covers Spark, SAM, Image, Files."""
    from openai import OpenAI
    return OpenAI(base_url=BASE_URL, api_key=api_key(), timeout=600)


def spark_cost(tokens_in: int, tokens_out: int) -> float:
    return tokens_in / 1e6 * PRICE_SPARK_IN_PER_M + tokens_out / 1e6 * PRICE_SPARK_OUT_PER_M


def sam_cost(frames: int) -> float:
    return max(frames, SAM_MIN_BILLED_FRAMES) / 1000 * PRICE_SAM_PER_1K_FRAMES


def load_schema() -> dict:
    return json.loads(SCHEMA_PATH.read_text())


# ---------------------------------------------------------------- metrics

@dataclass
class Metrics:
    """Appends one row per stage call to metrics/runs.csv. The metric story starts on day one."""
    run_id: str
    procedure: str
    rows: list[dict] = field(default_factory=list)
    csv_path: Path = METRICS_CSV

    @classmethod
    def start(cls, procedure: str, csv_path: Path | None = None) -> "Metrics":
        env_csv = os.environ.get("VID2SIM_METRICS_CSV")
        return cls(run_id=time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6],
                   procedure=procedure, csv_path=csv_path or (Path(env_csv) if env_csv else METRICS_CSV))

    def log(self, stage: str, model: str, wall_s: float, cost_usd: float = 0.0,
            frames: int = 0, tokens_in: int = 0, tokens_out: int = 0, note: str = "") -> None:
        row = {"run_id": self.run_id, "procedure": self.procedure, "stage": stage, "model": model,
               "wall_s": f"{wall_s:.2f}", "cost_usd": f"{cost_usd:.4f}", "frames": frames,
               "tokens_in": tokens_in, "tokens_out": tokens_out, "note": note}
        self.rows.append(row)
        self.csv_path.parent.mkdir(parents=True, exist_ok=True)
        new = not self.csv_path.exists()
        with self.csv_path.open("a", newline="") as fh:
            w = csv.DictWriter(fh, fieldnames=list(row.keys()))
            if new:
                w.writeheader()
            w.writerow(row)

    def totals(self) -> dict[str, float]:
        rows = [r for r in self.rows if r["stage"] != "total"]
        return {
            "wall_s": sum(float(r["wall_s"]) for r in rows),
            "cost_usd": sum(float(r["cost_usd"]) for r in rows),
        }


class Timer:
    def __enter__(self):
        self.t0 = time.perf_counter()
        return self

    def __exit__(self, *exc):
        self.wall = time.perf_counter() - self.t0

    @property
    def elapsed(self) -> float:
        return time.perf_counter() - self.t0


# ---------------------------------------------------------------- ffmpeg

def _run(cmd: list[str], **kw) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, check=True, capture_output=True, text=True, **kw)


def require_ffmpeg() -> None:
    for tool in ("ffmpeg", "ffprobe"):
        if shutil.which(tool) is None:
            raise RuntimeError(f"{tool} not found on PATH")


@dataclass
class VideoInfo:
    width: int
    height: int
    fps: float
    duration_s: float
    nb_frames: int
    has_audio: bool


def probe(video: Path) -> VideoInfo:
    out = _run(["ffprobe", "-v", "error", "-print_format", "json", "-show_streams", "-show_format", str(video)]).stdout
    data = json.loads(out)
    vs = next(s for s in data["streams"] if s["codec_type"] == "video")
    has_audio = any(s["codec_type"] == "audio" for s in data["streams"])
    num, den = vs.get("r_frame_rate", "30/1").split("/")
    fps = float(num) / float(den or 1)
    duration = float(data["format"].get("duration") or vs.get("duration") or 0)
    nb = int(vs.get("nb_frames") or round(duration * fps))
    # phone footage carries a rotation tag; SAM and Spark see the displayed size
    rotation = 0
    for sd in vs.get("side_data_list", []) or []:
        if "rotation" in sd:
            rotation = int(sd["rotation"])
    w, h = int(vs["width"]), int(vs["height"])
    if rotation % 180 != 0:
        w, h = h, w
    return VideoInfo(width=w, height=h, fps=fps, duration_s=duration, nb_frames=nb, has_audio=has_audio)


def extract_audio_wav(video: Path, out_wav: Path) -> Path:
    """Mono 16 kHz 16-bit PCM WAV, the only format Voice Transcribe batch accepts."""
    out_wav.parent.mkdir(parents=True, exist_ok=True)
    _run(["ffmpeg", "-y", "-v", "error", "-i", str(video), "-vn", "-ac", "1", "-ar", "16000",
          "-acodec", "pcm_s16le", str(out_wav)])
    return out_wav


def extract_frame(video: Path, frame: int, fps: float, out_png: Path) -> Path:
    out_png.parent.mkdir(parents=True, exist_ok=True)
    t = max(frame, 0) / fps
    _run(["ffmpeg", "-y", "-v", "error", "-ss", f"{t:.4f}", "-i", str(video), "-frames:v", "1", str(out_png)])
    return out_png


def trim(video: Path, start_frame: int, end_frame: int, fps: float, out_mp4: Path) -> tuple[Path, int]:
    """Frame-accurate trim. Re-encodes (fast preset) so frame 0 of the trim is exactly start_frame;
    a stream copy would snap to the previous keyframe and shift every SAM frame index."""
    out_mp4.parent.mkdir(parents=True, exist_ok=True)
    start_frame = max(0, start_frame)
    t0, t1 = start_frame / fps, (end_frame + 1) / fps
    _run(["ffmpeg", "-y", "-v", "error", "-ss", f"{t0:.4f}", "-to", f"{t1:.4f}", "-i", str(video),
          "-an", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p",
          "-movflags", "+faststart", str(out_mp4)])
    return out_mp4, start_frame


def faststart(video: Path, out_mp4: Path) -> Path:
    out_mp4.parent.mkdir(parents=True, exist_ok=True)
    _run(["ffmpeg", "-y", "-v", "error", "-i", str(video), "-c", "copy", "-movflags", "+faststart", str(out_mp4)])
    return out_mp4


# ---------------------------------------------------------------- files api

def upload_video(video: Path, metrics: Metrics | None = None, cache: Path | None = None) -> str:
    """Upload once with purpose=user_data; Spark and SAM both take the returned file_id.
    With `cache` (a JSON path), a previous upload of the same bytes (size + mtime) is reused."""
    if is_mock():
        return "file-mock-" + video.stem
    key = f"{video.stat().st_size}:{int(video.stat().st_mtime)}"
    if cache and cache.exists():
        try:
            c = json.loads(cache.read_text())
            if c.get("key") == key and c.get("file_id"):
                if metrics:
                    metrics.log("upload", "files", 0.0, 0.0, note=f"reused {c['file_id']}")
                return c["file_id"]
        except Exception:
            pass
    with Timer() as t:
        with video.open("rb") as fh:
            f = with_retries(lambda: client().files.create(purpose="user_data", file=fh,
                                                           expires_after={"anchor": "created_at", "seconds": 7 * 24 * 3600}),
                             label="upload")
    if metrics:
        metrics.log("upload", "files", t.wall, 0.0, note=video.name)
    if cache:
        write_json(cache, {"key": key, "file_id": f.id, "name": video.name})
    return f.id


# ---------------------------------------------------------------- retries

def with_retries(fn, *, tries: int = 3, base_delay: float = 2.0, label: str = "call"):
    """Run fn() with exponential backoff on transient API errors (timeouts, 5xx, connection drops).
    Everything that talks to the Model API goes through this so one dropped stream never kills a run."""
    last: Exception | None = None
    for attempt in range(1, tries + 1):
        try:
            return fn()
        except Exception as e:  # noqa: BLE001 - the SDK raises several families; retry all but auth/4xx
            status = getattr(e, "status_code", None)
            if status is not None and 400 <= int(status) < 500 and int(status) != 429:
                raise
            last = e
            if attempt == tries:
                break
            delay = base_delay * (2 ** (attempt - 1))
            print(f"    retry {attempt}/{tries - 1} for {label} after {type(e).__name__}: {str(e)[:120]} (sleep {delay:.0f}s)")
            time.sleep(delay)
    raise RuntimeError(f"{label} failed after {tries} attempts: {last}") from last


def keyframe_jpeg_b64(png: Path, max_px: int = 1280) -> tuple[str, int, int]:
    """Downscale a keyframe for a vision prompt. A 1080p PNG is ~3 MB of base64; a 1280-wide JPEG is ~150 KB.
    Returns (base64, width, height) of the image actually sent, so boxes can be scaled back."""
    import base64
    import io
    from PIL import Image
    img = Image.open(png).convert("RGB")
    w0, h0 = img.size
    if max(w0, h0) > max_px:
        img.thumbnail((max_px, max_px), Image.LANCZOS)
    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=88)
    return base64.b64encode(buf.getvalue()).decode(), img.size[0], img.size[1]


# ---------------------------------------------------------------- misc

def slugify(text: str, sep: str = "_") -> str:
    out = []
    for ch in text.lower().strip():
        if ch.isalnum():
            out.append(ch)
        elif out and out[-1] != sep:
            out.append(sep)
    s = "".join(out).strip(sep)
    return s or "part"


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n")


def read_json(path: Path) -> Any:
    return json.loads(path.read_text())


def iter_output_text(stream) -> Iterator[tuple[str, Any]]:
    """Yield ('delta', text) for output text and ('done', response) at completion.
    Works for both Spark and SAM streams from the Responses API."""
    for ev in stream:
        et = getattr(ev, "type", None) or (ev.get("type") if isinstance(ev, dict) else None)
        if et == "response.output_text.delta":
            yield "delta", ev.delta
        elif et in ("response.completed", "response.incomplete", "response.failed"):
            yield "done", ev.response
