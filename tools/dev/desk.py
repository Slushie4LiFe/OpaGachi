"""desk.py out.png : PrintWindow the desktop (Progman) to see whether it renders wallpaper + icons without other windows."""
import ctypes, sys
from ctypes import wintypes
from PIL import Image
u = ctypes.windll.user32; g = ctypes.windll.gdi32
u.SetProcessDPIAware()
h = u.FindWindowW("Progman", None)
r = wintypes.RECT(); u.GetWindowRect(h, ctypes.byref(r))
w, hh = r.right - r.left, r.bottom - r.top
print("progman", hex(h), r.left, r.top, w, hh)
dc = u.GetWindowDC(h); mdc = g.CreateCompatibleDC(dc); bmp = g.CreateCompatibleBitmap(dc, w, hh); g.SelectObject(mdc, bmp)
ok = u.PrintWindow(h, mdc, 2)
class BMI(ctypes.Structure): _fields_ = [("a", ctypes.c_uint32), ("w", ctypes.c_int32), ("h", ctypes.c_int32), ("p", ctypes.c_uint16), ("b", ctypes.c_uint16), ("rest", ctypes.c_uint32 * 6)]
bi = BMI(40, w, -hh, 1, 32); buf = ctypes.create_string_buffer(w * hh * 4)
g.GetDIBits(mdc, bmp, 0, hh, buf, ctypes.byref(bi), 0)
im = Image.frombuffer("RGBA", (w, hh), buf, "raw", "BGRA", 0, 1).convert("RGB")
print("printwindow", ok, "extrema", im.getextrema())
im.resize((w // 6, hh // 6)).save(sys.argv[1])
