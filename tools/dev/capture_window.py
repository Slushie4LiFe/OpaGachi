"""Capture a window by exact title to a PNG (PrintWindow, works under a fullscreen game; restores it if minimised).
Usage: capture_window.py "OPA PET — Settings" out.png"""
import ctypes, sys
from ctypes import wintypes
from PIL import Image
u = ctypes.windll.user32; g = ctypes.windll.gdi32
u.SetProcessDPIAware()
h = u.FindWindowW(None, sys.argv[1])
if not h: sys.exit("no window")
if u.IsIconic(h): u.ShowWindow(h, 9); import time; time.sleep(0.6)
r = wintypes.RECT(); u.GetWindowRect(h, ctypes.byref(r))
w, hh = r.right - r.left, r.bottom - r.top
dc = u.GetWindowDC(h); mdc = g.CreateCompatibleDC(dc); bmp = g.CreateCompatibleBitmap(dc, w, hh); g.SelectObject(mdc, bmp)
u.PrintWindow(h, mdc, 2)
class BMI(ctypes.Structure): _fields_ = [("a", ctypes.c_uint32), ("w", ctypes.c_int32), ("h", ctypes.c_int32), ("p", ctypes.c_uint16), ("b", ctypes.c_uint16), ("rest", ctypes.c_uint32 * 6)]
bi = BMI(40, w, -hh, 1, 32); buf = ctypes.create_string_buffer(w * hh * 4)
g.GetDIBits(mdc, bmp, 0, hh, buf, ctypes.byref(bi), 0)
Image.frombuffer("RGBA", (w, hh), buf, "raw", "BGRA", 0, 1).convert("RGB").save(sys.argv[2])
