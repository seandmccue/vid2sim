"""Voice Transcribe: narration -> transcript.json with per-turn timestamps.

Spark 1.3's audio path is flagged degraded in the docs, so the narration never goes
through Spark. Batch endpoint: POST /v1/asr/transcribe, multipart with a JSON `request`
part and a mono 16 kHz WAV `audio` part. ENDPOINTING mode returns `turns` with
startMs/endMs; PUSH_TO_TALK returns none, so we always ask for ENDPOINTING.
"""
from __future__ import annotations

import json
from pathlib import Path

import requests

from .common import (ASR_MODEL, BASE_URL, PRICE_ASR_PER_MIN_EST, Metrics, Timer, VideoInfo,
                     api_key, extract_audio_wav, is_mock, write_json)


def _turns_to_transcript(payload: dict, fps: float) -> dict:
    turns = []
    for t in payload.get("turns", []) or []:
        start_ms, end_ms = int(t.get("startMs", 0)), int(t.get("endMs", 0))
        turns.append({
            "turn_id": t.get("turnId"),
            "start_ms": start_ms, "end_ms": end_ms,
            "start_frame": int(start_ms / 1000 * fps), "end_frame": int(end_ms / 1000 * fps),
            "text": (t.get("transcript") or "").strip(),
        })
    return {
        "model": ASR_MODEL,
        "transcript": (payload.get("transcript") or "").strip(),
        "audio_duration_ms": int(payload.get("audioDurationMs", 0)),
        "fps": fps,
        "turns": turns,
    }


def transcribe(video: Path, info: VideoInfo, out_dir: Path, metrics: Metrics, resume: bool = False) -> dict:
    out_json = out_dir / "transcript.json"
    if resume and out_json.exists():
        metrics.log("transcribe", ASR_MODEL, 0.0, 0.0, note="resumed from transcript.json")
        return json.loads(out_json.read_text())
    if not info.has_audio:
        data = {"model": ASR_MODEL, "transcript": "", "audio_duration_ms": 0, "fps": info.fps, "turns": [],
                "note": "source video has no audio track"}
        write_json(out_json, data)
        metrics.log("transcribe", ASR_MODEL, 0.0, 0.0, note="no audio track")
        return data

    wav = extract_audio_wav(video, out_dir / "audio.wav")
    if is_mock():
        from ..mock import mock_transcript
        data = _turns_to_transcript(mock_transcript(info), info.fps)
        write_json(out_json, data)
        metrics.log("transcribe", ASR_MODEL, 0.0, 0.0, note="mock")
        return data

    # The batch endpoint accepts at most 10 minutes / 32 MB per request; longer audio goes in 9-minute
    # chunks whose turns are merged with their offsets.
    with Timer() as t:
        payload = _transcribe_chunked(wav, info.duration_s)
    data = _turns_to_transcript(payload, info.fps)
    write_json(out_json, data)
    minutes = data["audio_duration_ms"] / 60000
    metrics.log("transcribe", ASR_MODEL, t.wall, minutes * PRICE_ASR_PER_MIN_EST,
                note=f"{minutes:.2f} min, cost is an estimate")
    return data


CHUNK_SECONDS = 540


def _post_wav(wav: Path) -> dict:
    request_part = {"mode": "ENDPOINTING", "model": ASR_MODEL, "audioEncoding": "WAV", "languageBias": ["English"]}
    with wav.open("rb") as fh:
        resp = requests.post(
            f"{BASE_URL}/asr/transcribe",
            headers={"Authorization": f"Bearer {api_key()}", "Accept": "application/json"},
            files={"request": (None, json.dumps(request_part), "application/json"),
                   "audio": ("audio.wav", fh, "audio/wav")},
            timeout=600,
        )
    resp.raise_for_status()
    return resp.json()


def _transcribe_chunked(wav: Path, duration_s: float) -> dict:
    if duration_s <= CHUNK_SECONDS:
        return _post_wav(wav)
    import subprocess
    merged = {"sessionId": "chunked", "transcript": "", "audioDurationMs": int(duration_s * 1000), "turns": []}
    start, idx = 0.0, 0
    while start < duration_s:
        part = wav.with_name(f"{wav.stem}_chunk{idx}.wav")
        subprocess.run(["ffmpeg", "-y", "-v", "error", "-ss", f"{start:.3f}", "-t", str(CHUNK_SECONDS), "-i", str(wav),
                        "-acodec", "copy", str(part)], check=True, capture_output=True)
        payload = _post_wav(part)
        offset_ms = int(start * 1000)
        for turn in payload.get("turns", []) or []:
            turn = dict(turn)
            turn["startMs"] = int(turn.get("startMs", 0)) + offset_ms
            turn["endMs"] = int(turn.get("endMs", 0)) + offset_ms
            turn["turnId"] = f"{idx}-{turn.get('turnId', len(merged['turns']))}"
            merged["turns"].append(turn)
        merged["transcript"] = (merged["transcript"] + " " + (payload.get("transcript") or "").strip()).strip()
        part.unlink(missing_ok=True)
        start += CHUNK_SECONDS
        idx += 1
    return merged


def transcript_as_text(transcript: dict) -> str:
    """Narration with frame stamps, the form Spark gets alongside the frames."""
    if transcript.get("turns"):
        return "\n".join(f"[frame {t['start_frame']}-{t['end_frame']}] {t['text']}" for t in transcript["turns"] if t["text"])
    return transcript.get("transcript", "")
