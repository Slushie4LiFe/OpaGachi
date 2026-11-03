"""Post a left click to whatever child control sits at (x, y) in the window's client area, without moving
the real mouse. Usage: clickat.py "<exact window title>" x y"""
import ctypes, sys, time
from ctypes import wintypes
u = ctypes.windll.user32
u.SetProcessDPIAware()
h = u.FindWindowW(None, sys.argv[1])
if not h: sys.exit("no window")
x, y = int(sys.argv[2]), int(sys.argv[3])
pt = wintypes.POINT(x, y)
u.ClientToScreen(h, ctypes.byref(pt))
target = h
while True:   # deepest child under the point
    local = wintypes.POINT(pt.x, pt.y)
    u.ScreenToClient(target, ctypes.byref(local))
    child = u.RealChildWindowFromPoint(target, local)
    if not child or child == target: break
    target = child
local = wintypes.POINT(pt.x, pt.y)
u.ScreenToClient(target, ctypes.byref(local))
lp = (local.y << 16) | (local.x & 0xFFFF)
u.PostMessageW(target, 0x0201, 1, lp)   # WM_LBUTTONDOWN
time.sleep(0.05)
u.PostMessageW(target, 0x0202, 0, lp)   # WM_LBUTTONUP
print("clicked", hex(target), local.x, local.y)
