"""winrect.py "<title>": print the window's handle and screen rectangle."""
import ctypes, sys
from ctypes import wintypes
u = ctypes.windll.user32
u.SetProcessDPIAware()
h = u.FindWindowW(None, sys.argv[1])
if not h: sys.exit("no window")
r = wintypes.RECT()
u.GetWindowRect(h, ctypes.byref(r))
print(hex(h), r.left, r.top, r.right, r.bottom)
