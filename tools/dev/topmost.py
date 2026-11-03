"""topmost.py "<title>" on|off : pin a window above others without activating it (test aid)."""
import ctypes, sys
u = ctypes.windll.user32
h = u.FindWindowW(None, sys.argv[1])
if not h: sys.exit("no window")
HWND_TOPMOST, HWND_NOTOPMOST = -1, -2
u.SetWindowPos(h, HWND_TOPMOST if sys.argv[2] == "on" else HWND_NOTOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | 0x10)
print("ok")
