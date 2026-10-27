"""Cutouts: one RGBA image per part that looks like the real object, for the engine's photo cards.

Sources, best first:
  1. the Muse Image texture (a clean product shot on a plain background): the background is removed by
     flood-filling near-white pixels from the border, so white areas inside the object are kept
  2. the keyframe crop with the SAM 3.1 keyframe mask as alpha (crop and mask share the padded box)
  3. the keyframe crop alone with a feathered rounded-rectangle alpha

No API calls; runs after textures and is cheap enough to redo on every run.
"""
from __future__ import annotations

from pathlib import Path

MAX_PX = 512
WHITE_THRESHOLD = 232      # per-channel floor for "background" in a Muse product shot
SATURATION_MAX = 28        # max channel spread for a background pixel (grey shadows still count)


def _bbox_with_pad(alpha, pad_frac: float = 0.04) -> tuple[int, int, int, int] | None:
    bbox = alpha.getbbox()
    if bbox is None:
        return None
    x1, y1, x2, y2 = bbox
    pad = max(2, int(max(x2 - x1, y2 - y1) * pad_frac))
    return max(0, x1 - pad), max(0, y1 - pad), min(alpha.width, x2 + pad), min(alpha.height, y2 + pad)


def _finish(rgba, out_png: Path) -> Path:
    from PIL import Image
    alpha = rgba.getchannel("A")
    box = _bbox_with_pad(alpha)
    if box:
        rgba = rgba.crop(box)
    rgba.thumbnail((MAX_PX, MAX_PX), Image.LANCZOS)
    out_png.parent.mkdir(parents=True, exist_ok=True)
    rgba.save(out_png, compress_level=6)
    return out_png


def cutout_from_texture(texture_png: Path, out_png: Path) -> Path | None:
    """Remove the plain background of a product shot. Returns None when nothing but background was found."""
    from PIL import Image, ImageDraw, ImageFilter
    img = Image.open(texture_png).convert("RGB")
    w, h = img.size
    px = img.load()

    def is_bg(x: int, y: int) -> bool:
        r, g, b = px[x, y]
        return min(r, g, b) >= WHITE_THRESHOLD and max(r, g, b) - min(r, g, b) <= SATURATION_MAX

    # flood fill from every border pixel over background-coloured pixels
    seen = bytearray(w * h)
    stack = [(x, 0) for x in range(w)] + [(x, h - 1) for x in range(w)] + [(0, y) for y in range(h)] + [(w - 1, y) for y in range(h)]
    while stack:
        x, y = stack.pop()
        i = y * w + x
        if seen[i] or not is_bg(x, y):
            continue
        seen[i] = 1
        if x > 0: stack.append((x - 1, y))
        if x < w - 1: stack.append((x + 1, y))
        if y > 0: stack.append((x, y - 1))
        if y < h - 1: stack.append((x, y + 1))
    alpha = Image.frombytes("L", (w, h), bytes(0 if s else 255 for s in seen))
    if alpha.getbbox() is None:
        return None
    # the shot's soft drop shadow is background-coloured only near the floor; a small erode + blur
    # keeps the edge clean without eating thin parts
    alpha = alpha.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.8))
    rgba = img.convert("RGBA")
    rgba.putalpha(alpha)
    kept = sum(1 for s in seen if not s) / (w * h)
    if kept < 0.01:
        return None
    return _finish(rgba, out_png)


def cutout_from_mask(crop_png: Path, mask_png: Path, box: list[int], out_png: Path) -> Path | None:
    """Crop (padded box at the keyframe, as bundle.crop_part writes it) + full-frame SAM mask as alpha."""
    from PIL import Image, ImageFilter
    crop = Image.open(crop_png).convert("RGBA")
    mask = Image.open(mask_png).convert("L")
    x1, y1, x2, y2 = box
    pad_x, pad_y = int((x2 - x1 + 1) * 0.10), int((y2 - y1 + 1) * 0.10)
    region = (max(0, x1 - pad_x), max(0, y1 - pad_y), min(mask.width, x2 + 1 + pad_x), min(mask.height, y2 + 1 + pad_y))
    m = mask.crop(region)
    if m.size != crop.size:
        m = m.resize(crop.size, Image.NEAREST)    # crop_part may have thumbnailed a large crop
    if m.getbbox() is None:
        return None
    m = m.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(1.0))
    crop.putalpha(m)
    return _finish(crop, out_png)


def cutout_from_crop(crop_png: Path, out_png: Path) -> Path:
    """Last resort: the crop with feathered rounded corners, so it still reads as a photo of the part."""
    from PIL import Image, ImageDraw, ImageFilter
    crop = Image.open(crop_png).convert("RGBA")
    w, h = crop.size
    r = max(4, min(w, h) // 6)
    alpha = Image.new("L", (w, h), 0)
    ImageDraw.Draw(alpha).rounded_rectangle((1, 1, w - 2, h - 2), radius=r, fill=255)
    alpha = alpha.filter(ImageFilter.GaussianBlur(max(1.0, min(w, h) / 40)))
    crop.putalpha(alpha)
    return _finish(crop, out_png)


def generate_cutouts(spec: dict, work: Path) -> dict[str, Path]:
    """One cutouts/<part>.png per part with any image source; the source used is recorded per part."""
    out_dir = work / "bundle" / "cutouts"
    out_dir.mkdir(parents=True, exist_ok=True)
    masks_dir = work / "bundle" / "masks"
    done: dict[str, Path] = {}
    for p in spec["parts"]:
        pid, target = p["id"], out_dir / f"{p['id']}.png"
        tex = work / "bundle" / "textures" / f"{pid}.png"
        crop = work / "bundle" / "crops" / f"{pid}.png"
        result = None
        try:
            if tex.exists():
                result = cutout_from_texture(tex, target)
            if result is None and crop.exists():
                mask = masks_dir / f"{pid}_f{int(p['keyframe']):06d}_o0.png"
                candidates = sorted(masks_dir.glob(f"{pid}_f{int(p['keyframe']):06d}_o*.png")) if masks_dir.exists() else []
                if candidates:
                    result = cutout_from_mask(crop, candidates[0], p["box"], target)
            if result is None and crop.exists():
                result = cutout_from_crop(crop, target)
        except Exception as e:   # a bad image never stops the run; the engine falls back to a primitive
            print(f"[cutouts] {pid}: {e}")
            result = None
        if result is not None:
            done[pid] = result
    return done
