#!/usr/bin/env python3
"""Audit a `uiautomator dump` for the three defects four device passes kept finding.

  python tools/scripts/audit_layout.py <dump.xml> [density-dpi] [screen-width-px]

1. **Sub-floor targets** — a clickable node under 48 dp in either dimension (F-03,
   F-26, F-29, F-31, F-34, F-37).
2. **Overlapping targets** — two clickable nodes whose rects intersect, which is how
   F-34 made `Filters` unreachable: the later child simply painted over it.
3. **Clipped targets** — a clickable node running past the screen edge (F-35).

`measure_targets.py` answers 1 alone and stays the smaller tool; this one is what a
sweep across many viewports runs, because 2 and 3 are invisible to a size check.
"""
import io
import re
import sys

FLOOR_DP = 48.0
NODE = re.compile(r'<node[^>]*?>')
BOUNDS = re.compile(r'bounds="\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]"')


def label_of(node):
    text = re.search(r' text="([^"]*)"', node)
    desc = re.search(r'content-desc="([^"]*)"', node)
    return (text.group(1) if text else '') or (desc.group(1) if desc else '')


def clickables(path):
    text = io.open(path, encoding='utf-8').read()
    found = []
    for match in NODE.finditer(text):
        node = match.group(0)
        if 'clickable="true"' not in node:
            continue
        bounds = BOUNDS.search(node)
        label = label_of(node)
        if not bounds or not label:
            continue
        x1, y1, x2, y2 = (int(v) for v in bounds.groups())
        found.append((label, x1, y1, x2, y2))
    return found


def main(path, dpi=450.0, screen_width=0):
    scale = dpi / 160.0
    nodes = clickables(path)
    out = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace', newline='')

    under = []
    for label, x1, y1, x2, y2 in nodes:
        wdp, hdp = (x2 - x1) / scale, (y2 - y1) / scale
        flag = wdp < FLOOR_DP - 0.5 or hdp < FLOOR_DP - 0.5
        if flag:
            under.append((label, wdp, hdp))
        out.write(f'{"** " if flag else "OK "}{label[:52]:54s} '
                  f'[{x1},{y1}][{x2},{y2}]  {wdp:.1f}x{hdp:.1f}dp\n')

    overlaps = []
    for i in range(len(nodes)):
        for j in range(i + 1, len(nodes)):
            a, b = nodes[i], nodes[j]
            ox = min(a[3], b[3]) - max(a[1], b[1])
            oy = min(a[4], b[4]) - max(a[2], b[2])
            if ox > 1 and oy > 1:
                overlaps.append((a[0], b[0], ox / scale, oy / scale))

    clipped = []
    if screen_width:
        for label, x1, y1, x2, y2 in nodes:
            if x1 < 0 or x2 > screen_width:
                clipped.append((label, x1, x2))

    out.write(f'\n{len(nodes)} clickable nodes, {len(under)} under {FLOOR_DP:.0f} dp, '
              f'{len(overlaps)} overlapping pairs, {len(clipped)} clipped\n')
    for label, wdp, hdp in under:
        out.write(f'  UNDER    {label[:52]:54s} {wdp:.1f}x{hdp:.1f}dp\n')
    for one, two, ox, oy in overlaps:
        out.write(f'  OVERLAP  {one[:30]:32s} x {two[:30]:32s} {ox:.1f}x{oy:.1f}dp\n')
    for label, x1, x2 in clipped:
        out.write(f'  CLIPPED  {label[:52]:54s} x {x1}..{x2} vs screen {screen_width}\n')
    out.flush()
    return 1 if (under or overlaps or clipped) else 0


if __name__ == '__main__':
    sys.exit(main(
        sys.argv[1],
        float(sys.argv[2]) if len(sys.argv) > 2 else 450.0,
        int(sys.argv[3]) if len(sys.argv) > 3 else 0))
