"""wheel.py "<title>" x y notches : post mouse-wheel notches (negative = down) to the control at (x, y) in the window."""
import ctypes, sys, time
from ctypes import wintypes
u = ctypes.windll.user32
u.SetProcessDPIAware()
h = u.FindWindowW(None, sys.argv[1])
if not h: sys.exit("no window")
x, y, n = int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
pt = wintypes.POINT(x, y)
u.ClientToScreen(h, ctypes.byref(pt))
target = u.WindowFromPoint(pt)
for _ in range(abs(n)):
    delta = -120 if n < 0 else 120
    u.PostMessageW(target, 0x20A, (delta & 0xFFFF) << 16, (pt.y << 16) | (pt.x & 0xFFFF))
    time.sleep(0.05)
print("wheel", hex(target), n)
