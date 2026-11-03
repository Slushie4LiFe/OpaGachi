"""Generate the per-agent tie patterns that aren't plain colours (seamless tiles).

  chatgpt-galaxy.png      astral galaxy purple, stars taken from the provided gemini-stars preset
  gemini-pearl.png        pearlescent pink-white with a soft iridescent sheen
  grok-black-stripes.png  the provided claude-stripes preset recoloured black / charcoal

Run: .tools\\venv\\Scripts\\python tools\\make_ties.py
"""
from pathlib import Path

import numpy as np
from PIL import Image

N = 128
PRESETS = Path(__file__).resolve().parent.parent / "assets" / "ties" / "presets"


def periodic_noise(size, blur, seed):
    """Smooth noise that tiles seamlessly (Gaussian low-pass of white noise in the FFT domain)."""
    rng = np.random.default_rng(seed)
    spectrum = np.fft.fft2(rng.standard_normal((size, size)))
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    spectrum *= np.exp(-((fx ** 2 + fy ** 2) * blur ** 2))
    out = np.real(np.fft.ifft2(spectrum))
    return (out - out.min()) / (out.max() - out.min())


def rgb(hex_):
    return np.array([int(hex_[i:i + 2], 16) for i in (1, 3, 5)], np.float32)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def square(path):
    im = Image.open(path).convert("RGB")
    side = min(im.size)
    return im.crop((0, 0, side, side)).resize((N, N), Image.LANCZOS)


def galaxy():
    n1, n2, n3 = (periodic_noise(N, b, s) for b, s in ((18, 1), (10, 2), (14, 3)))
    base = rgb("#1A0B33")[None, None] + (rgb("#5C2BA0") - rgb("#1A0B33"))[None, None] * (n1 ** 1.3)[..., None]
    base += rgb("#C06BE0")[None, None] * (smoothstep(0.62, 0.92, n2) * 0.55)[..., None]
    base += rgb("#4A63E6")[None, None] * (smoothstep(0.66, 0.95, n3) * 0.35)[..., None]
    stars = np.asarray(square(PRESETS / "gemini-stars.png")).astype(np.float32)
    mask = (stars[..., 0] > 170) & (stars[..., 1] > 140) & (stars[..., 2] < 140)      # the preset's yellow stars
    rng = np.random.default_rng(7)
    mask |= rng.random((N, N)) > 0.994                                                 # plus a few faint specks
    base[mask] = base[mask] * 0.2 + rgb("#FFF4FF") * 0.8
    return base


def pearl():
    n1, n2 = periodic_noise(N, 16, 11), periodic_noise(N, 9, 12)
    y, x = np.mgrid[0:N, 0:N] / N
    phase = 2 * np.pi * (n1 * 1.4 + (x + y))                                            # integer frequency: tiles
    base = np.ones((N, N, 3), np.float32) * rgb("#F6E4EC")
    for colour, offset in (("#F0B5CC", 0.0), ("#D9C6F4", 2.1), ("#CDEDE5", 4.2)):
        base += (rgb(colour) - rgb("#F6E4EC")) * (0.5 + 0.5 * np.cos(phase + offset))[..., None] * 0.5
    base += (255 - base) * (smoothstep(0.7, 1.0, n2) * 0.45)[..., None]                 # soft sheen
    return base


def black_stripes():
    # Diagonal stripes in the style of the provided stripes preset, drawn with a period that divides
    # the tile (the preset itself doesn't tile cleanly, so the seams showed).
    y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    phase = ((x + y) % 32) / 32.0
    stripe = smoothstep(0.0, 0.06, phase) * (1 - smoothstep(0.36, 0.42, phase))       # anti-aliased edges
    return rgb("#0D0D10") + (rgb("#3C3C44") - rgb("#0D0D10")) * stripe[..., None]


def main():
    for name, make in (("chatgpt-galaxy", galaxy), ("gemini-pearl", pearl), ("grok-black-stripes", black_stripes)):
        Image.fromarray(np.clip(make(), 0, 255).astype(np.uint8), "RGB").save(PRESETS / (name + ".png"))
        print("  wrote", name + ".png")


if __name__ == "__main__":
    main()
