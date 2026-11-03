"""Cut sprites out of an image drawn on a solid key colour (default magenta #FF00FF).

Finds each separate shape (left to right), removes the key colour (soft edges, magenta spill
removed), optionally scales each piece to a target height, and saves them as PNGs.

  python tools/cut_keyed.py badges.png assets/badges --names help,complete,fail,think,bored --height 26
"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image


def key_out(rgb, key, inner, outer):
    dist = np.sqrt(((rgb.astype(np.float32) - key) ** 2).sum(-1))
    alpha = np.clip((dist - inner) / (outer - inner), 0.0, 1.0)
    # Remove key-colour spill from soft edge pixels: out = (c - key*(1-a)) / a
    a = np.maximum(alpha, 1e-3)[..., None]
    straight = np.clip((rgb.astype(np.float32) - key * (1 - alpha[..., None])) / a, 0, 255)
    return straight, alpha


def pieces(alpha, min_size, gap):
    """Bounding boxes of shapes separated by fully transparent columns (left to right)."""
    cols = np.flatnonzero((alpha > 0.5).sum(0) > 0)
    boxes, start = [], None
    for i, c in enumerate(cols):
        if start is None:
            start = c
        if i + 1 == len(cols) or cols[i + 1] - c > gap:
            x0, x1 = start, c
            rows = np.flatnonzero((alpha[:, x0:x1 + 1] > 0.5).any(1))
            if x1 - x0 + 1 >= min_size and len(rows):
                boxes.append((x0, rows[0], x1 + 1, rows[-1] + 1))
            start = None
    return boxes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("out")
    ap.add_argument("--names", required=True, help="comma-separated output names, left to right")
    ap.add_argument("--height", type=int, default=0, help="scale each piece to this height (0 = keep)")
    ap.add_argument("--key", default="FF00FF")
    ap.add_argument("--pad", type=int, default=2)
    args = ap.parse_args()

    key = np.array([int(args.key[i:i + 2], 16) for i in (0, 2, 4)], np.float32)
    rgb = np.asarray(Image.open(args.src).convert("RGB"))
    straight, alpha = key_out(rgb, key, 60.0, 140.0)
    boxes = pieces(alpha, min_size=12, gap=4)
    names = args.names.split(",")
    if len(boxes) != len(names):
        raise SystemExit("Found %d shapes but got %d names" % (len(boxes), len(names)))

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    for name, (x0, y0, x1, y1) in zip(names, boxes):
        p = args.pad
        x0, y0 = max(0, x0 - p), max(0, y0 - p)
        x1, y1 = min(rgb.shape[1], x1 + p), min(rgb.shape[0], y1 + p)
        rgba = np.dstack([straight[y0:y1, x0:x1], alpha[y0:y1, x0:x1] * 255]).astype(np.uint8)
        im = Image.fromarray(rgba, "RGBA")
        if args.height:
            w = max(1, round(im.width * args.height / im.height))
            im = im.convert("RGBa").resize((w, args.height), Image.LANCZOS).convert("RGBA")
        im.save(out / (name + ".png"))
        print("  %-14s %dx%d" % (name, im.width, im.height))


if __name__ == "__main__":
    main()
