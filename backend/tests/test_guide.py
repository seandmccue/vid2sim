"""Guide mode: the live /guide/find call, the landing list and session saving, in mock mode (no API)."""
import base64
import io
import json
import os

import pytest
from PIL import Image

os.environ["VID2SIM_MOCK"] = "1"


def _jpeg_data_url(w=640, h=480):
    im = Image.new("RGB", (w, h), (120, 140, 160))
    buf = io.BytesIO(); im.save(buf, "JPEG")
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def test_find_parses_sam_image_output():
    from backend.guide import parse_image_result
    raw = ("<0f>0<|box;x1=189;y1=121;x2=533;y2=480;w=720;h=720;c=0.9766|>"
           "<|mask;x=0;y=0;c=0.9766;data=360,345,!!!!|>\n")
    matches, note = parse_image_result(raw)
    assert len(matches) == 1 and matches[0].confidence == pytest.approx(0.9766, abs=1e-3)
    assert (matches[0].x1, matches[0].y1, matches[0].x2, matches[0].y2) == (189, 121, 534, 481)   # the parser makes x2/y2 exclusive (+1 on SAM's inclusive corners)
    assert parse_image_result("")[0] == []   # nothing found is an empty output, not an error


def test_find_mock_returns_a_box_in_frame_pixels():
    from backend.guide import decode_image, find
    res = find(decode_image(_jpeg_data_url(800, 600)), "laptop")
    assert res.width == 800 and res.height == 600 and res.found if hasattr(res, "found") else res.matches
    d = res.to_dict()
    assert d["found"] and d["matches"][0]["x2"] <= 800 and d["raw_count"] == 1


def test_guide_routes(tmp_path, monkeypatch):
    from fastapi.testclient import TestClient
    from backend import app as app_mod
    # a procedure with a spec, a crop and sample footage, in a temp data dir
    data = tmp_path / "procedures"; d = data / "desk-demo"; (d / "bundle" / "crops").mkdir(parents=True)
    spec = {"schema_version": "1.0", "procedure": {"id": "desk-demo", "title": "Shut down for the night", "source_video": "x",
            "fps": 30, "width": 1280, "height": 720, "duration_s": 10},
            "parts": [{"id": "laptop", "name": "Laptop", "prompt_phrase": "laptop", "interaction": "grab", "keyframe": 0,
                       "box": [0, 0, 100, 100], "size_hint_m": 0.3, "confidence": 0.9, "source": "manual"}],
            "steps": [{"order": 1, "part_id": "laptop", "action": "press", "instruction": "Close the laptop lid.", "start_frame": 0,
                       "end_frame": 10, "check": "laptop lid is closed", "failure": "", "safety": False, "coach": ""}]}
    (d / "spec.json").write_text(json.dumps(spec))
    Image.new("RGB", (40, 40)).save(d / "bundle" / "crops" / "laptop.png")
    (d / "sample.mp4").write_bytes(b"\x00")
    monkeypatch.setattr(app_mod, "DATA", data)
    monkeypatch.setattr(app_mod, "SESSIONS_DIR", tmp_path / "sessions")
    c = TestClient(app_mod.app)

    cards = c.get("/guide/procedures").json()
    assert cards[0]["id"] == "desk-demo" and cards[0]["thumbnail"].endswith("/crops/laptop.png") and cards[0]["sample_video"]

    r = c.post("/guide/find", json={"procedure_id": "desk-demo", "step_order": 1, "image": _jpeg_data_url()})
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["part_name"] == "Laptop" and body["phrase"] == "laptop" and body["found"] and body["width"] == 640
    assert c.post("/guide/find", json={"procedure_id": "desk-demo", "step_order": 9, "image": _jpeg_data_url()}).status_code == 404
    assert c.post("/guide/find", json={"procedure_id": "desk-demo", "step_order": 1, "image": "not-an-image"}).status_code == 400

    s = c.post("/guide/sessions", json={"mode": "guide", "procedure_id": "desk-demo", "steps": [{"step_order": 1, "seconds": 3.2}]}).json()
    assert (tmp_path / "sessions" / f"{s['session_id']}.json").exists()
    assert c.get("/guide/sessions").json()[0]["procedure_id"] == "desk-demo"
    assert c.get("/guide/").status_code == 200 and "Apprentice" in c.get("/guide/").text


def test_check_route_mock_advances_and_builds_messages(tmp_path, monkeypatch):
    from fastapi.testclient import TestClient
    from backend import app as app_mod
    from backend.guide import CheckResult, check_response
    step = {"order": 2, "check": "charger plug is out of the wall socket", "coach": "Pull the plug straight out.", "instruction": "Unplug it."}
    part = {"name": "Phone charger"}
    assert check_response(step, part, CheckResult("yes", "plug is out", 900, 0.001))["advance"] is True
    no = check_response(step, part, CheckResult("no", "still plugged in", 900, 0.001))
    assert no["advance"] is False and no["message"] == "Pull the plug straight out."
    unclear = check_response(step, part, CheckResult("unclear", "", 900, 0.001))
    assert "closer to the Phone charger" in unclear["message"]

    data = tmp_path / "procedures"; d = data / "x"; d.mkdir(parents=True)
    spec = {"schema_version": "1.0", "procedure": {"id": "x", "title": "x", "source_video": "x", "fps": 30, "width": 10, "height": 10, "duration_s": 1},
            "parts": [{"id": "p", "name": "Part", "prompt_phrase": "part", "interaction": "press", "keyframe": 0, "box": [0, 0, 1, 1], "size_hint_m": 0.1, "confidence": 1, "source": "manual"}],
            "steps": [{"order": 1, "part_id": "p", "action": "press", "instruction": "Do it.", "start_frame": 0, "end_frame": 1, "check": "it is done", "failure": "", "safety": False, "coach": "c"}]}
    (d / "spec.json").write_text(json.dumps(spec))
    monkeypatch.setattr(app_mod, "DATA", data)
    c = TestClient(app_mod.app)
    r = c.post("/guide/check", json={"procedure_id": "x", "step_order": 1, "image": _jpeg_data_url()})
    assert r.status_code == 200 and r.json()["verdict"] == "yes" and r.json()["advance"] is True


def test_voice_intents_and_routes(tmp_path, monkeypatch):
    from fastapi.testclient import TestClient
    from backend import app as app_mod
    from backend.guide import classify
    assert classify("done") == "done" and classify("Okay, next.") == "done" and classify("finished") == "done"
    assert classify("which one is the filter?") == "question" and classify("where is the switch") == "question"
    assert classify("uh huh") == "none" and classify("") == "none"

    data = tmp_path / "procedures"; d = data / "x"; d.mkdir(parents=True)
    spec = {"schema_version": "1.0", "procedure": {"id": "x", "title": "x", "source_video": "x", "fps": 30, "width": 10, "height": 10, "duration_s": 1},
            "parts": [{"id": "p", "name": "Bypass valve", "prompt_phrase": "red valve", "interaction": "turn", "keyframe": 0, "box": [0, 0, 1, 1], "size_hint_m": 0.1, "confidence": 1, "source": "manual"}],
            "steps": [{"order": 1, "part_id": "p", "action": "turn_cw", "instruction": "Close it.", "start_frame": 0, "end_frame": 1, "check": "valve is closed", "failure": "", "safety": False, "coach": "c"}]}
    (d / "spec.json").write_text(json.dumps(spec))
    (tmp_path / "guide.json").write_text(json.dumps({"featured": [{"id": "nope"}, {"id": "x", "blurb": "Featured blurb"}]}))
    monkeypatch.setattr(app_mod, "DATA", data)
    c = TestClient(app_mod.app)

    cards = c.get("/guide/procedures").json()
    assert cards[0]["id"] == "x" and cards[0]["featured"] is True and cards[0]["description"] == "Featured blurb"

    # a tiny real WAV through /guide/hear (mock ASR says "done"); ffmpeg converts it on the way
    import struct, wave
    wav = tmp_path / "a.wav"
    with wave.open(str(wav), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(16000); w.writeframes(struct.pack("<" + "h" * 8000, *([0] * 8000)))
    r = c.post("/guide/hear", files={"audio": ("a.wav", wav.read_bytes(), "audio/wav")}, data={"procedure_id": "x", "step_order": "1"})
    assert r.status_code == 200 and r.json()["intent"] == "done"
    assert c.post("/guide/hear", files={"audio": ("a.wav", b"", "audio/wav")}).status_code == 400

    r = c.post("/guide/ask", json={"procedure_id": "x", "step_order": 1, "question": "which one is the bypass valve?", "image": _jpeg_data_url()})
    assert r.status_code == 200 and "Bypass valve" in r.json()["answer"]
