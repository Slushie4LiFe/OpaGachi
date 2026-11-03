"""closewin.py "<title>" : post WM_CLOSE to a window."""
import ctypes, sys
u = ctypes.windll.user32
h = u.FindWindowW(None, sys.argv[1])
if not h: sys.exit("no window")
u.PostMessageW(h, 0x10, 0, 0)
print("closed", hex(h))
