"""Finds the frames in each .opaclip where the character is back in its starting pose.

The app lets a looping animation hand over to the next one only at these frames (or at the clip's
end), so a state change never cuts a movement off halfway. One-shot animations (thank, fail,
idle fidgets) always play to the end regardless.

Writes rest.json next to the clips: {"idle/idle_1": [96], ...} (frame indices, 0-based).

    python tools/find_rest_frames.py [sprite_folder]
"""
import json
import struct
import sys
import zlib
from pathlib import Path

import numpy as np

MIN_GAP_S = 1.0     # seconds between rest points
EDGE_S = 0.5        # ignore matches this close to the start or end (the end is always allowed)


def frames(path: Path):
    data = path.read_bytes()
    _, _, w, h, fps, n = struct.unpack("<4sHHHHI", data[:16])
    offsets = struct.unpack("<%dI" % (n + 1), data[20:20 + 4 * (n + 1)])
    start = 20 + 4 * (n + 1)
    current = np.zeros(w * h * 4, np.uint8)
    for i in range(n):
        raw = zlib.decompress(data[start + offsets[i]:start + offsets[i + 1]], -15)
        current ^= np.frombuffer(raw[:w * h * 4], np.uint8)
        yield current.reshape(h, w, 4)[::4, ::4].astype(np.float32)   # 1/16 of the pixels is plenty


def rest_frames(path: Path):
    fps = struct.unpack("<H", path.read_bytes()[10:12])[0]
    k = max(1, round(fps / 12))   # compare frames 1/12 s apart whatever the frame rate, so 24 fps clips find the same poses
    min_gap, edge = round(MIN_GAP_S * fps), round(EDGE_S * fps)
    clip = list(frames(path))
    first = clip[0]
    diff = [float(np.abs(f - first).mean()) for f in clip]
    steps = [float(np.abs(clip[i] - clip[i - k]).mean()) for i in range(k, len(clip))]
    threshold = max(1.0, float(np.median(steps)) * 1.5)   # "as close to the start as one frame is to the next"
    found = []
    for i, d in enumerate(diff):
        if i < edge or i >= len(clip) - edge or d >= threshold:
            continue
        if found and i - found[-1] < min_gap:
            continue
        found.append(i)
    return found


def main():
    folder = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent / "assets" / "sprites" / "_template"
    result = {"_note": "Frames where each clip is back in its starting pose; loops may hand over here. Written by tools/find_rest_frames.py."}
    for path in sorted(folder.glob("*/*.opaclip")):
        key = f"{path.parent.name}/{path.stem}"
        result[key] = rest_frames(path)
        print(f"{key:28s} {result[key]}")
    (folder / "rest.json").write_text(json.dumps(result, indent=1))
    print("wrote", folder / "rest.json")


if __name__ == "__main__":
    main()
