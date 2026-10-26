"""FastAPI backend: upload, run, status, spec get/put, bundle files, build.

    uvicorn backend.app:app --reload --port 8000

The dashboard (Vite dev server on 5173, or the built dist/ served from /) talks only to
this. State lives on disk under data/procedures/<id>/ so a restart loses nothing.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import threading
import time
from pathlib import Path

from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles

from backend.pipeline import package as package_mod
from backend.pipeline.common import REPO_ROOT, UNITY_STREAMING, extract_frame, faststart, probe, read_json, slugify, write_json
from backend.pipeline.validate import validate_spec
from backend.run import run as run_pipeline

DATA = REPO_ROOT / "data" / "procedures"
DASHBOARD_DIST = REPO_ROOT / "dashboard" / "dist"
UNITY_PROJECT = REPO_ROOT / "unity" / "TrainingSim"
BUNDLE_ID = os.environ.get("UNITY_BUNDLE_ID", "com.framesixty.vid2sim")

app = FastAPI(title="vid2sim", version="0.1")
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])

_jobs: dict[str, dict] = {}
_lock = threading.Lock()


def _dir(pid: str) -> Path:
    d = DATA / pid
    if not d.exists():
        raise HTTPException(404, f"unknown procedure '{pid}'")
    return d


def _status_path(pid: str) -> Path:
    return DATA / pid / "status.json"


def _set_status(pid: str, **patch) -> dict:
    with _lock:
        st = _jobs.get(pid) or (read_json(_status_path(pid)) if _status_path(pid).exists() else {"id": pid, "state": "idle", "log": []})
        st.update(patch)
        st["updated_at"] = time.time()
        _jobs[pid] = st
        write_json(_status_path(pid), st)
        return st


def _video_for(pid: str) -> Path | None:
    d = DATA / pid
    for name in ("p.mp4", "raw.mp4"):
        if (d / name).exists():
            return d / name
    return next(iter(sorted(d.glob("*.mp4"))), None)


# ------------------------------------------------------------------ procedures

@app.get("/api/health")
def health():
    return {"ok": True, "mock": os.environ.get("VID2SIM_MOCK", "0"), "has_key": bool(os.environ.get("MODEL_API_KEY"))}


@app.get("/api/procedures")
def list_procedures():
    out = []
    DATA.mkdir(parents=True, exist_ok=True)
    for d in sorted(DATA.iterdir()):
        if not d.is_dir():
            continue
        spec = read_json(d / "spec.json") if (d / "spec.json").exists() else None
        st = read_json(d / "status.json") if (d / "status.json").exists() else {"state": "idle"}
        out.append({"id": d.name, "title": spec["procedure"]["title"] if spec else d.name,
                    "steps": len(spec["steps"]) if spec else 0, "parts": len(spec["parts"]) if spec else 0,
                    "state": st.get("state", "idle"), "has_video": _video_for(d.name) is not None,
                    "has_spec": spec is not None})
    return out


@app.post("/api/upload")
async def upload(video: UploadFile = File(...), title: str = Form(""), procedure_id: str = Form("")):
    if not (video.filename or "").lower().endswith((".mp4", ".mov")):
        raise HTTPException(400, "upload an MP4 or MOV")
    pid = slugify(procedure_id or title or Path(video.filename).stem, "-")
    d = DATA / pid
    d.mkdir(parents=True, exist_ok=True)
    raw = d / "raw.mp4"
    with raw.open("wb") as fh:
        shutil.copyfileobj(video.file, fh)
    try:
        faststart(raw, d / "p.mp4")  # the service doesn't have to remux
        info = probe(d / "p.mp4")
    except Exception as e:
        raise HTTPException(400, f"ffmpeg could not read the file: {e}")
    if title:
        write_json(d / "meta.json", {"title": title})
    _set_status(pid, state="uploaded", log=[f"uploaded {video.filename} ({info.width}x{info.height}, {info.duration_s:.1f}s)"])
    return {"id": pid, "width": info.width, "height": info.height, "fps": info.fps, "duration_s": info.duration_s,
            "frames": info.nb_frames}


def _run_job(pid: str, opts: dict):
    d = DATA / pid
    video = _video_for(pid)
    log: list[str] = []
    t0 = time.time()

    def progress(msg: str):
        log.append(f"[{time.time() - t0:6.1f}s] {msg}")
        _set_status(pid, state="running", log=list(log), elapsed_s=round(time.time() - t0, 1))

    try:
        title = (read_json(d / "meta.json").get("title") if (d / "meta.json").exists() else None)
        gt = d / "ground_truth.json"
        card = run_pipeline(video, work=d, title=title, ground_truth=gt if gt.exists() else None,
                            skip_segment=bool(opts.get("skip_segment")), skip_generate=bool(opts.get("skip_generate")),
                            unity_project=UNITY_PROJECT, progress=progress)
        _set_status(pid, state="done", log=log, elapsed_s=round(time.time() - t0, 1), card=card)
    except BaseException as e:  # SystemExit from validation included
        log.append(f"ERROR: {e}")
        _set_status(pid, state="error", log=log, error=str(e), elapsed_s=round(time.time() - t0, 1))


@app.post("/api/run/{pid}")
def run(pid: str, skip_segment: bool = False, skip_generate: bool = False, mock: bool | None = None):
    _dir(pid)
    if _video_for(pid) is None:
        raise HTTPException(400, "no video uploaded for this procedure")
    st = _jobs.get(pid)
    if st and st.get("state") == "running":
        raise HTTPException(409, "already running")
    if mock is not None:
        os.environ["VID2SIM_MOCK"] = "1" if mock else "0"
    _set_status(pid, state="running", log=["queued"], error=None, card=None)
    threading.Thread(target=_run_job, args=(pid, {"skip_segment": skip_segment, "skip_generate": skip_generate}), daemon=True).start()
    return {"id": pid, "state": "running"}


@app.get("/api/status/{pid}")
def status(pid: str):
    _dir(pid)
    st = _jobs.get(pid)
    if st is None and _status_path(pid).exists():
        st = read_json(_status_path(pid))
    return st or {"id": pid, "state": "idle", "log": []}


# ------------------------------------------------------------------ spec

@app.get("/api/procedures/{pid}/spec")
def get_spec(pid: str):
    d = _dir(pid)
    if not (d / "spec.json").exists():
        raise HTTPException(404, "no spec yet; run the pipeline")
    return read_json(d / "spec.json")


@app.put("/api/procedures/{pid}/spec")
def put_spec(pid: str, spec: dict):
    """Trainer edit. Validates, saves, repackages. Re-segmentation only when a prompt_phrase changed."""
    d = _dir(pid)
    errors = validate_spec(spec)
    if errors:
        return JSONResponse(status_code=422, content={"errors": [{"path": e.path, "message": e.message} for e in errors]})
    old = read_json(d / "spec.json") if (d / "spec.json").exists() else None
    needs_resegment = []
    if old:
        old_parts = {p["id"]: p for p in old["parts"]}
        for p in spec["parts"]:
            o = old_parts.get(p["id"])
            if o is None or o["prompt_phrase"] != p["prompt_phrase"]:
                needs_resegment.append(p["id"])
        if old != spec:
            hist = d / "history"
            hist.mkdir(exist_ok=True)
            write_json(hist / f"spec_{time.strftime('%Y%m%d-%H%M%S')}.json", old)
    write_json(d / "spec.json", spec)
    out = package_mod.package(spec, d)
    _set_status(pid, state="edited", log=(_jobs.get(pid, {}).get("log") or []) + [f"spec saved by trainer; repackaged -> {out.name}"])
    return {"ok": True, "repackaged": str(out), "needs_resegment": needs_resegment}


@app.get("/api/procedures/{pid}/review")
def get_review(pid: str):
    d = _dir(pid)
    return read_json(d / "review.json") if (d / "review.json").exists() else {"steps": [], "flagged_count": 0}


@app.get("/api/procedures/{pid}/metrics")
def get_metrics(pid: str):
    d = _dir(pid)
    return read_json(d / "metrics.json") if (d / "metrics.json").exists() else {}


@app.get("/api/procedures/{pid}/transcript")
def get_transcript(pid: str):
    d = _dir(pid)
    return read_json(d / "transcript.json") if (d / "transcript.json").exists() else {"turns": [], "transcript": ""}


@app.get("/api/procedures/{pid}/bundle")
def get_bundle(pid: str):
    """Per-part tracking data for the overlay: crops, masks, box per frame."""
    d = _dir(pid)
    p = d / "bundle" / "parts.json"
    return read_json(p) if p.exists() else []


@app.get("/api/procedures/{pid}/files/{path:path}")
def get_file(pid: str, path: str):
    d = _dir(pid)
    f = (d / path).resolve()
    if not str(f).startswith(str(d.resolve())) or not f.is_file():
        raise HTTPException(404)
    return FileResponse(f)


@app.get("/api/procedures/{pid}/frame/{n}.jpg")
def get_frame(pid: str, n: int):
    """Keyframe image for the mask overlay, extracted on demand and cached."""
    d = _dir(pid)
    video = _video_for(pid)
    if video is None:
        raise HTTPException(404, "no video")
    out = d / "frames" / f"{n:06d}.jpg"
    if not out.exists():
        info = probe(video)
        extract_frame(video, max(0, min(n, info.nb_frames - 1)), info.fps, out)
    return FileResponse(out)


# ------------------------------------------------------------------ build

@app.post("/api/procedures/{pid}/build")
def build(pid: str, push: bool = True, apk: bool = False):
    """Package StreamingAssets; hot-push the spec to a running dev build over adb; optionally build the APK."""
    d = _dir(pid)
    spec = get_spec(pid)
    out = package_mod.package(spec, d)
    log = [f"packaged -> {out}"]
    pushed = False
    if push and shutil.which("adb"):
        remote = f"/sdcard/Android/data/{BUNDLE_ID}/files/procedures/{spec['procedure']['id']}"
        try:
            subprocess.run(["adb", "shell", "mkdir", "-p", remote + "/textures"], check=True, capture_output=True, timeout=20)
            subprocess.run(["adb", "push", str(out / "spec.json"), remote + "/spec.json"], check=True, capture_output=True, timeout=60)
            for tex in (out / "textures").glob("*.png"):
                subprocess.run(["adb", "push", str(tex), f"{remote}/textures/{tex.name}"], check=True, capture_output=True, timeout=60)
            subprocess.run(["adb", "shell", "am", "broadcast", "-a", f"{BUNDLE_ID}.RELOAD", "--es", "procedure", spec["procedure"]["id"]],
                           capture_output=True, timeout=20)
            pushed = True
            log.append(f"pushed to headset {remote} and broadcast reload")
        except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as e:
            log.append(f"adb push failed: {getattr(e, 'stderr', b'') or e}")
    elif push:
        log.append("adb not found; skipped headset push")
    built = None
    if apk:
        exe = os.environ.get("UNITY_EXE") or shutil.which("Unity")
        if not exe:
            log.append("UNITY_EXE not set; skipped APK build")
        else:
            cmd = [exe, "-batchmode", "-nographics", "-quit", "-projectPath", str(UNITY_PROJECT),
                   "-executeMethod", "Vid2Sim.Editor.BuildScript.BuildAndroid", "-logFile", str(UNITY_PROJECT / "Logs" / "build.log")]
            proc = subprocess.run(cmd, capture_output=True, text=True, timeout=3600)
            built = proc.returncode == 0
            log.append(f"unity build {'ok' if built else 'failed'} (see Logs/build.log)")
    return {"ok": True, "packaged": str(out), "pushed": pushed, "apk_built": built, "log": log}


# serve the built dashboard when present; API routes above take precedence
if DASHBOARD_DIST.exists():
    app.mount("/", StaticFiles(directory=DASHBOARD_DIST, html=True), name="dashboard")
