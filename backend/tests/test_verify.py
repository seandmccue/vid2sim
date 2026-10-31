"""Verify mode: the diff rules, the mock observation, and the upload → grade → report routes (mock, no API)."""
import json
import os
import subprocess

import pytest

os.environ["VID2SIM_MOCK"] = "1"


def _spec(n=6, safety=(2, 5)):
    return {"schema_version": "1.0", "procedure": {"id": "x", "title": "X", "source_video": "x", "fps": 30, "width": 64, "height": 64, "duration_s": 6},
            "parts": [{"id": f"p{i}", "name": f"P{i}", "prompt_phrase": f"p{i}", "interaction": "grab", "keyframe": 0, "box": [0, 0, 1, 1], "size_hint_m": 0.1, "confidence": 1, "source": "manual"} for i in range(1, n + 1)],
            "steps": [{"order": i, "part_id": f"p{i}", "action": "grab", "instruction": f"Step {i}", "start_frame": (i - 1) * 30, "end_frame": i * 30 - 1,
                       "check": "", "failure": f"fail {i}", "safety": i in safety, "coach": ""} for i in range(1, n + 1)]}


def _obs(*orders):
    return [{"step_order": o, "start_frame": k * 100, "end_frame": k * 100 + 50, "note": "", "confidence": 0.9} for k, o in enumerate(orders)]


def test_diff_rules():
    from backend.pipeline.verify import diff
    spec = _spec()
    assert diff(spec, _obs(1, 2, 3, 4, 5, 6))["verdict"] == "PASS"
    r = diff(spec, _obs(1, 3, 2, 4, 5, 6))
    assert r["verdict"] == "PASS" and r["out_of_order"] == [3] and r["score"] == 83
    r = diff(spec, _obs(1, 3, 4, 6))
    assert r["verdict"] == "FAIL: safety step skipped" and r["missed_safety"] == [2, 5] and r["passed"] is False
    r = diff(spec, _obs(1, 2, 4, 5, 6))
    assert r["verdict"] == "FAIL" and r["missed"] == [3]            # a non-safety miss still fails
    r = diff(spec, _obs(None, 1, 2, 3, 4, 5, 6, 2), ["wiped it"])
    assert r["verdict"] == "PASS" and r["extra_actions"] == ["wiped it"]
    low = [dict(o, confidence=0.2) for o in _obs(1)] + _obs(2, 3, 4, 5, 6)
    assert 1 in diff(spec, low)["missed"]                           # low-confidence sightings do not count


def test_verify_video_mock_and_routes(tmp_path, monkeypatch, synthetic_video):
    from fastapi.testclient import TestClient
    from backend import app as app_mod
    from backend.pipeline.verify import verify_video
    spec = _spec()
    report = verify_video(synthetic_video, spec, tmp_path / "w")
    assert report["verdict"] == "PASS" and (tmp_path / "w" / "report.json").exists()
    assert all(s["thumbnail"] for s in report["steps"])
    monkeypatch.setenv("VID2SIM_MOCK_SKIP_STEPS", "2")
    report = verify_video(synthetic_video, spec, tmp_path / "w2")
    assert report["verdict"] == "FAIL: safety step skipped" and report["missed_safety"] == [2]
    monkeypatch.delenv("VID2SIM_MOCK_SKIP_STEPS")

    data = tmp_path / "procedures"; (data / "x").mkdir(parents=True)
    (data / "x" / "spec.json").write_text(json.dumps(spec))
    monkeypatch.setattr(app_mod, "DATA", data)
    monkeypatch.setattr(app_mod, "VERIFY_DIR", tmp_path / "verify")
    monkeypatch.setattr(app_mod, "SESSIONS_DIR", tmp_path / "sessions")
    c = TestClient(app_mod.app)
    r = c.post("/verify/x", files={"video": ("take.mp4", synthetic_video.read_bytes(), "video/mp4")})
    assert r.status_code == 200, r.text
    sid = r.json()["session_id"]
    import time
    for _ in range(100):
        st = c.get(f"/verify/{sid}").json()
        if st["state"] in ("done", "error"):
            break
        time.sleep(0.2)
    assert st["state"] == "done", st
    assert st["report"]["verdict"] == "PASS" and st["report"]["session_id"] == sid
    assert c.get(f"/verify/{sid}/thumbs/step_1.jpg").status_code == 200
    assert c.get("/verify").json()[0]["session_id"] == sid
    assert (tmp_path / "sessions" / f"{sid}.json").exists()
    assert c.post("/verify/x", files={"video": ("notes.txt", b"x", "text/plain")}).status_code == 400
    assert "Verify" in c.get("/guide/verify.html").text
