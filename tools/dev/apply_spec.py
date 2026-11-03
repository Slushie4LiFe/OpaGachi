# Applies exact replacements from a spec file to a source file, keeping its line endings (also in files that mix
# CRLF and LF lines: the old text matches either, and the new text takes the ending of the lines it replaces).
# Spec: blocks separated by lines "=====" (old) / "-----" (new) / "=====" ...; each block: old text, then "-----", then new text.
# Usage: rep.py target spec
import re, sys
target, spec = sys.argv[1], sys.argv[2]
s = open(target, encoding='utf-8', newline='').read()
crlf = '\r\n' in s
raw = open(spec, encoding='utf-8').read().replace('\r\n', '\n')
blocks = [b for b in raw.split('\n=====\n')]
if blocks and blocks[0].startswith('=====\n'): blocks[0] = blocks[0][6:]
n = 0
for b in blocks:
    if not b.strip(): continue
    if b.endswith('\n-----'): old, new = b[:-5], ''   # deletion (the line break goes too)
    elif '\n-----\n' not in b: sys.exit('bad block: ' + b[:60])
    else: old, new = b.split('\n-----\n', 1)
    if new.endswith('\n=====') : new = new[:-6]
    hits = list(re.finditer(r'\r?\n'.join(re.escape(line) for line in old.split('\n')), s))
    if len(hits) != 1: sys.exit('count %d for: %s' % (len(hits), old[:100]))
    m = hits[0]
    nl = '\r\n' if '\r\n' in m.group(0) else '\n' if '\n' in m.group(0) else '\r\n' if crlf else '\n'
    s = s[:m.start()] + new.replace('\n', nl) + s[m.end():]
    n += 1
open(target, 'w', encoding='utf-8', newline='').write(s)
print('applied', n)
