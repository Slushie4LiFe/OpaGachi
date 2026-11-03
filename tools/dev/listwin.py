"""listwin.py [prefix] : top-level windows whose title starts with prefix (handle, visible, rect); no prefix lists every titled window."""
import ctypes, sys
from ctypes import wintypes
u = ctypes.windll.user32
u.SetProcessDPIAware()
found = []
@ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
def cb(h, l):
    buf = ctypes.create_unicode_buffer(256)
    u.GetWindowTextW(h, buf, 256)
    if buf.value and buf.value.startswith(sys.argv[1] if len(sys.argv) > 1 else ""):
        r = wintypes.RECT(); u.GetWindowRect(h, ctypes.byref(r))
        found.append((hex(h), buf.value, bool(u.IsWindowVisible(h)), (r.left, r.top, r.right, r.bottom)))
    return True
u.EnumWindows(cb, 0)
for f in found: print(f)
