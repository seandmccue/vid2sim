import os
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))


@pytest.fixture(scope="session")
def synthetic_video(tmp_path_factory) -> Path:
    """3-second 320x240 test pattern with a sine tone, enough to exercise every ffmpeg path."""
    d = tmp_path_factory.mktemp("vid")
    out = d / "p1.mp4"
    subprocess.run(["ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=30",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=16000", "-t", "3",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", "-movflags", "+faststart", str(out)],
                   check=True)
    return out


@pytest.fixture(autouse=True)
def mock_env(monkeypatch, tmp_path):
    monkeypatch.setenv("VID2SIM_MOCK", "1")
    monkeypatch.setenv("VID2SIM_METRICS_CSV", str(tmp_path / "runs.csv"))
    monkeypatch.setenv("VID2SIM_COMPILE_GATE", "0")
