#!/usr/bin/env python
"""Render docs/metrics-card.png from data/procedures/*/metrics.json. Measured, not claimed.

    .venv/bin/python backend/metrics_card.py [procedure_id ...]
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "data" / "procedures"
OUT = ROOT / "docs" / "metrics-card.png"


def _font(size: int, bold: bool = False):
    for name in (["/System/Library/Fonts/SFNS.ttf", "/System/Library/Fonts/Helvetica.ttc", "/Library/Fonts/Arial.ttf"]):
        try:
            return ImageFont.truetype(name, size)
        except Exception:
            continue
    return ImageFont.load_default()


def load(ids: list[str]) -> list[dict]:
    rows = []
    for pid in ids:
        m = DATA / pid / "metrics.json"
        if not m.exists():
            continue
        card = json.loads(m.read_text())
        g = card.get("ground_truth") or {}
        rows.append({
            "id": pid,
            "title": json.loads((DATA / pid / "spec.json").read_text())["procedure"]["title"] if (DATA / pid / "spec.json").exists() else pid,
            "minutes": card["upload_to_spec_s"] / 60,
            "cost": card["api_cost_usd"] + card.get("api_cost_usd_codegen_rerun", 0.0),
            "steps": f"{g.get('steps_correct', '-')}/{g.get('steps_truth', '-')}" if g else str(card["steps"]),
            "parts": f"{card['parts_found_concept']} / {card['parts_found_box']} / {card['parts_manual']}",
        })
    return rows


def render(rows: list[dict], out: Path = OUT) -> Path:
    W, pad = 1600, 60
    rowh, head = 150, 260
    H = head + rowh * max(1, len(rows)) + 150
    img = Image.new("RGB", (W, H), (15, 17, 21))
    d = ImageDraw.Draw(img)
    d.text((pad, 50), "vid2sim", font=_font(56, True), fill=(230, 233, 239))
    d.text((pad, 125), "Phone video in. Hands-first Quest training module out. Measured on the Meta Model API.", font=_font(28), fill=(139, 147, 163))
    cols = [("Procedure", pad), ("Upload → playable", 720), ("API cost", 1000), ("Steps vs trainer", 1200), ("Parts text / box / manual", 1330)]
    y = head - 50
    for label, x in cols:
        d.text((x, y), label.upper(), font=_font(20), fill=(139, 147, 163))
    d.line([(pad, y + 40), (W - pad, y + 40)], fill=(38, 43, 53), width=2)
    y = head
    for r in rows:
        d.text((pad, y + 20), r["title"][:40], font=_font(30, True), fill=(230, 233, 239))
        d.text((pad, y + 65), r["id"], font=_font(20), fill=(139, 147, 163))
        d.text((720, y + 20), f"{r['minutes']:.1f} min", font=_font(44, True), fill=(76, 195, 255))
        d.text((1000, y + 20), f"${r['cost']:.2f}", font=_font(44, True), fill=(61, 220, 132))
        d.text((1200, y + 20), r["steps"], font=_font(44, True), fill=(230, 233, 239))
        d.text((1330, y + 20), r["parts"], font=_font(36, True), fill=(230, 233, 239))
        y += rowh
        d.line([(pad, y), (W - pad, y)], fill=(38, 43, 53), width=1)
    d.text((pad, H - 90), "Spark understands · SAM segments · Muse Code builds · Muse Image paints", font=_font(30, True), fill=(76, 195, 255))
    d.text((pad, H - 45), "Prices: Spark $1.25/M in, $4.25/M out · SAM $0.20 per 1k frames · Transcribe and Image rows are estimates", font=_font(20), fill=(139, 147, 163))
    out.parent.mkdir(parents=True, exist_ok=True)
    img.save(out)
    return out


if __name__ == "__main__":
    ids = sys.argv[1:] or sorted(p.name for p in DATA.iterdir() if (p / "metrics.json").exists())
    rows = load(ids)
    print(render(rows), "with", [r["id"] for r in rows])
