"""Maximise (3) or restore (9) a window by exact title. Usage: winstate.py "<title>" 3|9"""
import ctypes, sys
u = ctypes.windll.user32
h = u.FindWindowW(None, sys.argv[1])
if not h: sys.exit("no window")
u.ShowWindow(h, int(sys.argv[2]))
