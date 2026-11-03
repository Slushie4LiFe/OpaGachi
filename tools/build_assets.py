"""Convert sprite template clips (.webm, VP9 with alpha) into .opaclip files for OPA PET.

Run through build-assets.cmd, which uses the tools virtualenv (imageio-ffmpeg, numpy, pillow).

.opaclip layout (little-endian):
  header  : b"OPAC", u16 version, u16 width, u16 height, u16 fps, u32 frame_count, f32 tie_v_ref
  offsets : u32 * (frame_count + 1), relative to the start of the frame data
  frames  : one raw-deflate block per frame, which inflates to
              premultiplied BGRA pixels XOR the previous frame   (width * height * 4 bytes)
              u32 tie_count, then tie_count * (u32 pixel_index, u8 weight, u8 value)
            The tie entries mark the blue tie so the app can recolour it per agent at runtime.
"""
import argparse
import json
import re
import struct
import subprocess
import sys
import time
import zlib
from pathlib import Path

import imageio_ffmpeg
import numpy as np
from PIL import Image, ImageDraw

VERSION = 1

# Source sub-folder (case-insensitive, "/"-separated) -> animation key used by the app.
ANIMATIONS = {
    "idle/loop": "idle",
    "idle/occasion": "idle_occasional",
    "bored": "bored",
    "think": "think",
    "work": "work",
    "imagegen": "imagegen",
    "help": "help",
    "fail": "fail",
    "complete": "complete",
    "thank": "thank",
}

# Tie detection: the template tie is a saturated blue (measured hue 220-255 degrees).
TIE_HUE_CENTER, TIE_HUE_FULL, TIE_HUE_FADE = 237.0, 18.0, 10.0
TIE_SAT = (0.30, 0.50)   # smoothstep range on HSV saturation
TIE_VAL = (0.12, 0.28)   # smoothstep range on HSV value
TIE_MIN_WEIGHT = 8       # out of 255

TIE_DTYPE = np.dtype([("i", "<u4"), ("w", "u1"), ("v", "u1")])
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()


def find_clips(src: Path):
    clips = []
    for path in sorted(src.rglob("*.webm")):
        rel = path.parent.relative_to(src).as_posix().lower()
        anim = ANIMATIONS.get(rel)
        if anim is None:
            print(f"  skip (unknown folder '{rel}'): {path.name}")
            continue
        clips.append((anim, path))
    return clips


def probe_size(path: Path):
    info = subprocess.run([FFMPEG, "-hide_banner", "-i", str(path)], capture_output=True, text=True).stderr
    m = re.search(r"Video: .*?(\d{2,5})x(\d{2,5})", info)
    if not m:
        sys.exit(f"Could not read the video size of {path}")
    return int(m.group(1)), int(m.group(2))


def decode(path: Path, fps: int):
    """Yield straight-alpha RGBA frames (H x W x 4, uint8) at the given frame rate."""
    w, h = probe_size(path)
    cmd = [FFMPEG, "-v", "error", "-c:v", "libvpx-vp9", "-i", str(path),
           "-vf", f"fps={fps}", "-f", "rawvideo", "-pix_fmt", "rgba", "-"]
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    frame_bytes = w * h * 4
    try:
        while True:
            buf = proc.stdout.read(frame_bytes)
            if len(buf) < frame_bytes:
                break
            yield np.frombuffer(buf, np.uint8).reshape(h, w, 4)
    finally:
        if proc.poll() is None:
            proc.kill()  # stopped early (e.g. the icon only needs one frame)
        proc.stdout.close()
        proc.wait()


def union_bbox(clips, pad=2):
    x0 = y0 = 10 ** 9
    x1 = y1 = -1
    size = None
    for _, path in clips:
        for f in decode(path, 4):
            size = f.shape[1], f.shape[0]
            a = f[..., 3] > 8
            ys = np.flatnonzero(a.any(axis=1))
            xs = np.flatnonzero(a.any(axis=0))
            if len(xs):
                x0, x1 = min(x0, xs[0]), max(x1, xs[-1])
                y0, y1 = min(y0, ys[0]), max(y1, ys[-1])
    w, h = size
    x0, y0 = max(0, x0 - pad), max(0, y0 - pad)
    x1, y1 = min(w - 1, x1 + pad), min(h - 1, y1 + pad)
    return [int(x0), int(y0), int(x1 - x0 + 1), int(y1 - y0 + 1)]


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def to_premultiplied(rgba, crop, out_size):
    x, y, w, h = crop
    im = Image.fromarray(np.ascontiguousarray(rgba[y:y + h, x:x + w]), "RGBA").convert("RGBa")
    im = im.resize(out_size, Image.LANCZOS)
    pm = np.array(im)
    np.minimum(pm[..., :3], pm[..., 3:4], out=pm[..., :3])  # keep colour <= alpha after resampling
    return pm


def tie_entries(pm):
    a = pm[..., 3].astype(np.float32)
    u = pm[..., :3].astype(np.float32) / np.maximum(a, 1.0)[..., None]  # straight colour, 0..1
    u = np.clip(u, 0.0, 1.0)
    r, g, b = u[..., 0], u[..., 1], u[..., 2]
    mx, mn = u.max(-1), u.min(-1)
    d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0.0)
    h = np.zeros_like(mx)
    m = d > 1e-6
    rr = m & (mx == r)
    gg = m & (mx == g) & ~rr
    bb = m & ~rr & ~gg
    h[rr] = ((g - b)[rr] / d[rr]) % 6
    h[gg] = (b - r)[gg] / d[gg] + 2
    h[bb] = (r - g)[bb] / d[bb] + 4
    h *= 60.0
    hue_w = np.clip(1.0 - (np.abs(h - TIE_HUE_CENTER) - TIE_HUE_FULL) / TIE_HUE_FADE, 0.0, 1.0)
    w = hue_w * smoothstep(*TIE_SAT, s) * smoothstep(*TIE_VAL, mx) * (a > 0)
    w8 = np.round(w * 255).astype(np.uint8).ravel()
    idx = np.flatnonzero(w8 >= TIE_MIN_WEIGHT)
    entries = np.empty(len(idx), TIE_DTYPE)
    entries["i"] = idx
    entries["w"] = w8[idx]
    entries["v"] = np.round(mx.ravel()[idx] * 255).astype(np.uint8)
    return entries


def build_clip(path, out_path, crop, out_size, fps, denoise):
    out_w, out_h = out_size
    blocks = []
    prev_pm = None
    prev_bgra = np.zeros((out_h, out_w, 4), np.uint8)
    strong_values = []
    middle = None
    frames = list(decode(path, fps))
    if not frames:
        sys.exit(f"Could not decode any frames from {path} (is it VP9 .webm?)")
    for n, rgba in enumerate(frames):
        pm = to_premultiplied(rgba, crop, out_size)
        if denoise and prev_pm is not None:
            keep = np.abs(pm.astype(np.int16) - prev_pm.astype(np.int16)) <= denoise
            pm = np.where(keep, prev_pm, pm)
            np.minimum(pm[..., :3], pm[..., 3:4], out=pm[..., :3])
        prev_pm = pm
        if n == len(frames) // 2:
            middle = pm
        ties = tie_entries(pm)
        strong_values.append(ties["v"][ties["w"] >= 200])
        bgra = np.ascontiguousarray(pm[..., [2, 1, 0, 3]])
        payload = np.bitwise_xor(bgra, prev_bgra).tobytes() + struct.pack("<I", len(ties)) + ties.tobytes()
        prev_bgra = bgra
        c = zlib.compressobj(9, zlib.DEFLATED, -15)
        blocks.append(c.compress(payload) + c.flush())

    strong = np.concatenate(strong_values) if strong_values else np.array([])
    v_ref = float(np.percentile(strong, 75)) / 255.0 if len(strong) else 0.85

    offsets = [0]
    for blk in blocks:
        offsets.append(offsets[-1] + len(blk))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "wb") as f:
        f.write(b"OPAC")
        f.write(struct.pack("<HHHHIf", VERSION, out_w, out_h, fps, len(blocks), v_ref))
        f.write(struct.pack(f"<{len(offsets)}I", *offsets))
        for blk in blocks:
            f.write(blk)
    return len(blocks), out_path.stat().st_size, v_ref, middle


def write_icon(src_clips, out_ico):
    """Tray/app icon: the character's head from the first idle frame."""
    path = next((p for a, p in src_clips if a == "idle"), src_clips[0][1])
    rgba = next(decode(path, 1))
    a = rgba[..., 3] > 8
    ys, xs = np.flatnonzero(a.any(axis=1)), np.flatnonzero(a.any(axis=0))
    top, bottom = ys[0], ys[0] + int((ys[-1] - ys[0]) * 0.48)
    head = a[top:bottom]
    hx = np.flatnonzero(head.any(axis=0))
    cx, side = (hx[0] + hx[-1]) // 2, max(bottom - top, hx[-1] - hx[0]) + 8
    x0, y0 = max(0, cx - side // 2), max(0, top - 4)
    im = Image.fromarray(np.ascontiguousarray(rgba[y0:y0 + side, x0:x0 + side]), "RGBA")
    out_ico.parent.mkdir(parents=True, exist_ok=True)
    im.save(out_ico, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])


def write_preview(items, out_png):
    """Contact sheet of each clip's middle frame on a checkerboard (for checking crops and tie masks)."""
    if not items:
        return
    h, w = items[0][1].shape[:2]
    cols = 5
    rows = (len(items) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * w, rows * (h + 16)), (40, 40, 40))
    chk = ((np.indices((h, w)).sum(0) // 8) % 2 * 50 + 150).astype(np.float32)
    draw = ImageDraw.Draw(sheet)
    for n, (label, pm) in enumerate(items):
        a = pm[..., 3:4].astype(np.float32) / 255.0
        rgb = pm[..., :3].astype(np.float32) + chk[..., None] * (1 - a)
        x, y = (n % cols) * w, (n // cols) * (h + 16)
        sheet.paste(Image.fromarray(rgb.clip(0, 255).astype(np.uint8)), (x, y + 16))
        draw.text((x + 3, y + 2), label, fill=(230, 230, 230))
    sheet.save(out_png)


def main():
    here = Path(__file__).resolve().parent.parent
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--src", default=str(here.parent / "1TEMPLATES"), help="folder of source .webm clips")
    ap.add_argument("--out", default=str(here / "assets" / "sprites" / "_template"), help="output folder")
    ap.add_argument("--height", type=int, default=160, help="sprite height in pixels")
    ap.add_argument("--fps", type=int, default=12, help="playback frame rate baked into the clips")
    ap.add_argument("--denoise", type=int, default=2, help="temporal noise threshold (0 = off)")
    ap.add_argument("--crop", help="x,y,w,h crop in source pixels (default: union of all clips)")
    ap.add_argument("--icon", default=str(here / "assets" / "icon.ico"), help="icon output ('' to skip)")
    args = ap.parse_args()

    src, out = Path(args.src), Path(args.out)
    clips = find_clips(src)
    if not clips:
        sys.exit(f"No clips found under {src}")
    print(f"{len(clips)} clips in {src}")

    t0 = time.time()
    crop = [int(v) for v in args.crop.split(",")] if args.crop else union_bbox(clips)
    out_size = (round(crop[2] * args.height / crop[3]), args.height)
    print(f"crop {crop} -> {out_size[0]}x{out_size[1]} @ {args.fps} fps")

    for old in out.rglob("*.opaclip"):
        old.unlink()
    manifest = {"source": str(src), "crop": crop, "width": out_size[0], "height": out_size[1],
                "fps": args.fps, "denoise": args.denoise, "animations": {}}
    previews = []
    total = 0
    for anim, path in clips:
        name = re.sub(r"[^a-z0-9]+", "_", path.stem.lower()).strip("_") + ".opaclip"
        dest = out / anim / name
        n, size, v_ref, middle = build_clip(path, dest, crop, out_size, args.fps, args.denoise)
        total += size
        manifest["animations"].setdefault(anim, []).append(f"{anim}/{name}")
        previews.append((f"{anim}/{path.stem}", middle))
        print(f"  {anim:16} {path.name:18} {n:4} frames  {size / 1e6:5.2f} MB  tie_v_ref={v_ref:.2f}")

    (out / "manifest.json").write_text(json.dumps(manifest, indent=2))
    write_preview(previews, out / "preview.png")
    if args.icon:
        write_icon(clips, Path(args.icon))
    subprocess.run([sys.executable, str(Path(__file__).with_name("find_rest_frames.py")), str(out)], check=True)
    print(f"done: {total / 1e6:.1f} MB in {time.time() - t0:.0f}s -> {out}")


if __name__ == "__main__":
    main()
