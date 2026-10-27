"""Package: write the StreamingAssets payload the Unity engine reads.

unity/TrainingSim/Assets/StreamingAssets/procedures/<id>/spec.json + textures/<part>.png + cutouts/<part>.png.
Validated on write; a spec that fails never lands.
"""
from __future__ import annotations

import shutil
from pathlib import Path

from .common import UNITY_STREAMING, write_json
from .validate import assert_valid


def package(spec: dict, work: Path, streaming_root: Path | None = None) -> Path:
    assert_valid(spec)
    root = (streaming_root or UNITY_STREAMING) / spec["procedure"]["id"]
    if root.exists():
        shutil.rmtree(root)
    (root / "textures").mkdir(parents=True)
    write_json(root / "spec.json", spec)
    for p in spec["parts"]:
        for src in (work / "bundle" / "textures" / f"{p['id']}.png", work / "bundle" / "crops" / f"{p['id']}.png"):
            if src.exists():
                shutil.copy(src, root / "textures" / f"{p['id']}.png")
                break
        cut = work / "bundle" / "cutouts" / f"{p['id']}.png"
        if cut.exists():
            (root / "cutouts").mkdir(exist_ok=True)
            shutil.copy(cut, root / "cutouts" / f"{p['id']}.png")
    # index so the engine can list procedures without directory enumeration on Android
    index = (streaming_root or UNITY_STREAMING) / "index.json"
    ids = sorted(d.name for d in (streaming_root or UNITY_STREAMING).iterdir() if d.is_dir() and (d / "spec.json").exists())
    write_json(index, {"procedures": ids})
    return root
