#!/usr/bin/env python3
"""Compare a rectangle of two raw `adb exec-out screencap` dumps.

  python rawdiff.py A.raw B.raw x1 y1 x2 y2
Prints the fraction of pixels in the rectangle that differ, with no image library.
"""
import struct, sys

def load(path):
    b = open(path, 'rb').read()
    w, h, f = struct.unpack_from('<III', b, 0)
    for header in (16, 12):           # Android >= 9 adds a colour-space word
        if w * h * 4 + header == len(b):
            return w, h, memoryview(b)[header:]
    raise SystemExit(f'{path}: {w}x{h} fmt {f}, {len(b)} bytes — unrecognised layout')

def main(a, b, x1, y1, x2, y2):
    wa, ha, pa = load(a)
    wb, hb, pb = load(b)
    if (wa, ha) != (wb, hb):
        raise SystemExit(f'different sizes: {wa}x{ha} vs {wb}x{hb}')
    x1, y1 = max(0, x1), max(0, y1)
    x2, y2 = min(wa, x2), min(ha, y2)
    total = changed = 0
    for y in range(y1, y2):
        row = y * wa * 4
        ra = pa[row + x1 * 4: row + x2 * 4]
        rb = pb[row + x1 * 4: row + x2 * 4]
        if ra == rb:
            total += x2 - x1
            continue
        for i in range(0, len(ra), 4):
            total += 1
            if ra[i:i + 4] != rb[i:i + 4]:
                changed += 1
    print(f'{changed}/{total} pixels differ = {100.0 * changed / max(1, total):.2f}%')

main(sys.argv[1], sys.argv[2], *(int(v) for v in sys.argv[3:7]))
