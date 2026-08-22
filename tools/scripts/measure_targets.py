#!/usr/bin/env python3
"""Measure clickable accessibility nodes in a `uiautomator dump` against the 48 dp floor.

  python tools/scripts/measure_targets.py <dump.xml> [density-dpi]

Prints one line per clickable node with a name, flagged `**` when either dimension
is under 48 dp. Written for the Android live-test remediation (findings F-03, F-26).
"""
import io
import re
import sys

FLOOR_DP = 48.0


def main(path, dpi=450.0):
    text = io.open(path, encoding='utf-8').read()
    scale = dpi / 160.0
    rows = []
    for match in re.finditer(r'<node[^>]*?>', text):
        node = match.group(0)
        if 'clickable="true"' not in node:
            continue
        bounds = re.search(r'bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', node)
        if not bounds:
            continue
        x1, y1, x2, y2 = (int(v) for v in bounds.groups())
        width, height = x2 - x1, y2 - y1
        text_attr = re.search(r'text="([^"]*)"', node)
        desc = re.search(r'content-desc="([^"]*)"', node)
        label = (text_attr.group(1) if text_attr else '') or (desc.group(1) if desc else '')
        if not label:
            continue
        under = width / scale < FLOOR_DP - 0.5 or height / scale < FLOOR_DP - 0.5
        rows.append((under, label, width, height, width / scale, height / scale))

    out = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace', newline='')
    for under, label, width, height, wdp, hdp in rows:
        flag = '** ' if under else 'OK '
        out.write(f'{flag}{label[:52]:54s} {width}x{height}px  {wdp:.1f}x{hdp:.1f}dp\n')
    failures = sum(1 for row in rows if row[0])
    out.write(f'\n{len(rows)} clickable nodes, {failures} under {FLOOR_DP:.0f} dp\n')
    out.flush()
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1], float(sys.argv[2]) if len(sys.argv) > 2 else 450.0))
