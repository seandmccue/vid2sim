import json
import time

import pytest
from fastapi.testclient import TestClient


@pytest.fixture
def client(tmp_path, monkeypatch):
    from backend import app as appmod
    monkeypatch.setattr(appmod, "DATA", tmp_path / "procedures")
    monkeypatch.setattr(appmod, "UNITY_PROJECT", tmp_path / "unity")
    monkeypatch.setattr(appmod.package_mod, "UNITY_STREAMING", tmp_path / "streaming")
    appmod._jobs.clear()
    return TestClient(appmod.app)


def _wait(client, pid, timeout=60):
    t0 = time.time()
    while time.time() - t0 < timeout:
        st = client.get(f"/api/status/{pid}").json()
        if st["state"] in ("done", "error"):
            return st
        time.sleep(0.2)
    raise AssertionError("timed out")


def test_upload_run_edit_build(client, synthetic_video):
    r = client.post("/api/upload", files={"video": ("p1.mp4", synthetic_video.read_bytes(), "video/mp4")},
                    data={"title": "Replace the intake filter"})
    assert r.status_code == 200, r.text
    pid = r.json()["id"]
    assert pid == "replace-the-intake-filter"

    assert client.post(f"/api/run/{pid}").json()["state"] == "running"
    st = _wait(client, pid)
    assert st["state"] == "done", st
    assert st["card"]["steps"] == 6

    spec = client.get(f"/api/procedures/{pid}/spec").json()
    assert len(spec["parts"]) == 5
    assert client.get(f"/api/procedures/{pid}/bundle").json()[0]["id"] == spec["parts"][0]["id"]
    assert client.get(f"/api/procedures/{pid}/frame/3.jpg").status_code == 200
    # masks are decoded on demand from the saved SAM output; a frame without a record is a 404
    tracked = next(b for b in client.get(f"/api/procedures/{pid}/bundle").json() if b["track"])
    assert client.get(f"/api/procedures/{pid}/mask/{tracked['id']}/{tracked['track'][0]['frame']}.png").status_code == 200
    assert client.get(f"/api/procedures/{pid}/mask/{tracked['id']}/999999.png").status_code == 404
    assert client.get(f"/api/procedures/{pid}/files/bundle/crops/{spec['parts'][0]['id']}.png").status_code == 200
    assert client.get(f"/api/procedures/{pid}/files/../../../etc/passwd").status_code == 404

    # trainer edit: rename a part and a step; invalid edit is refused with the path
    bad = json.loads(json.dumps(spec)); bad["steps"][0]["action"] = "unscrew"
    r = client.put(f"/api/procedures/{pid}/spec", json=bad)
    assert r.status_code == 422 and r.json()["errors"][0]["path"] == "/steps/0/action"
    good = json.loads(json.dumps(spec)); good["parts"][0]["name"] = "main power switch"; good["parts"][1]["prompt_phrase"] = "brass petcock"
    r = client.put(f"/api/procedures/{pid}/spec", json=good)
    assert r.status_code == 200 and r.json()["needs_resegment"] == [good["parts"][1]["id"]]
    assert client.get(f"/api/procedures/{pid}/spec").json()["parts"][0]["name"] == "main power switch"

    # play endpoint: 503 until the WebGL build exists, then a redirect carrying spec + textures URLs
    r = client.get(f"/api/procedures/{pid}/play", follow_redirects=False)
    assert r.status_code in (303, 307, 503)
    if r.status_code != 503:
        assert "spec=" in r.headers["location"] and "textures=" in r.headers["location"]

    b = client.post(f"/api/procedures/{pid}/build?push=false").json()
    assert b["ok"] and b["pushed"] is False
    assert client.get("/api/procedures").json()[0]["state"] == "edited"
