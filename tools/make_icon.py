"""Build assets/icon.ico from design/newicon-source.jpg (the user's red-ring icon, 2026-11-01).

The source is a JPEG with a fake transparency checkerboard baked in, so the rounded square is cut out:
the mask is every row's and column's span of dark pixels (the black rim runs all the way round, so the
spans also cover the red ring and white swooshes), eroded 2 px so no checker halo survives, then each
size is downsampled from that (small sizes get a light sharpen so the ring stays crisp).
The previous pet-face icon is kept as design/icon-gen1.7-face.ico.

Run with the project's venv:  .tools\\venv\\Scripts\\python tools\\make_icon.py [--preview out.png]
"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "design" / "newicon-source.jpg"
SIZES = [256, 64, 48, 32, 24, 20, 16]


def cut_out(path):
    """The icon as RGBA on a transparent square canvas."""
    rgb = Image.open(path).convert("RGB")
    lum = np.asarray(rgb).astype(int).mean(axis=2)
    dark = lum < 70
    h, w = dark.shape
    rows = np.zeros_like(dark)
    for y in range(h):
        xs = np.flatnonzero(dark[y])
        if xs.size: rows[y, xs[0]:xs[-1] + 1] = True
    cols = np.zeros_like(dark)
    for x in range(w):
        ys = np.flatnonzero(dark[:, x])
        if ys.size: cols[ys[0]:ys[-1] + 1, x] = True
    mask = Image.fromarray(((rows & cols) * 255).astype(np.uint8), "L")
    mask = mask.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(1.2))   # erode 2 px, soften the edge
    rgba = rgb.convert("RGBA")
    rgba.putalpha(mask)
    box = mask.point(lambda v: 255 if v > 8 else 0).getbbox()
    rgba = rgba.crop(box)
    side = max(rgba.size)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(rgba, ((side - rgba.width) // 2, (side - rgba.height) // 2))
    return square


def build(preview=None):
    out = ROOT / "assets" / "icon.ico"
    art = cut_out(SOURCE)
    frames = {}
    for size in SIZES:
        img = art.resize((size, size), Image.LANCZOS)
        if size <= 32: img = img.filter(ImageFilter.UnsharpMask(radius=1, percent=60, threshold=2))
        frames[size] = img
    frames[256].save(out, sizes=[(s, s) for s in SIZES], append_images=[frames[s] for s in SIZES[1:]])
    print("wrote", out, "sizes", SIZES)
    if preview:
        tiles = []
        for s in (16, 20, 24, 32, 48, 64):
            for bg in ((32, 32, 36, 255), (205, 210, 220, 255)):
                tiles.append(Image.alpha_composite(Image.new("RGBA", (s, s), bg), frames[s]).resize((s * 4, s * 4), Image.NEAREST))
        sheet = Image.new("RGB", (sum(t.width + 6 for t in tiles), max(t.height for t in tiles)), (255, 0, 255))
        x = 0
        for t in tiles:
            sheet.paste(t.convert("RGB"), (x, 0))
            x += t.width + 6
        sheet.save(preview)
        print("preview", preview)


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--preview", default=None, help="also write a zoomed contact sheet here")
    args = ap.parse_args()
    build(args.preview)
