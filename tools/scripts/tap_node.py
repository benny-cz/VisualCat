#!/usr/bin/env python3
"""Print the tap centre of an accessibility node, found by label substring.

  python tools/scripts/tap_node.py <dump.xml> "<label substring>"

Prints "<x> <y>" for the first clickable node whose text or content-desc contains
the substring, so a shell can `adb shell input tap $(...)`. Written for the
Android live-test sweeps, where every pane has to be reached without a human.
"""
import io
import re
import sys


def main(path, needle, clickable_only=True):
    text = io.open(path, encoding='utf-8').read()
    for match in re.finditer(r'<node[^>]*?>', text):
        node = match.group(0)
        if clickable_only and 'clickable="true"' not in node:
            continue
        label = re.search(r'text="([^"]*)"', node)
        desc = re.search(r'content-desc="([^"]*)"', node)
        values = [label.group(1) if label else '', desc.group(1) if desc else '']
        if not any(needle.lower() in v.lower() for v in values if v):
            continue
        bounds = re.search(r'bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', node)
        if not bounds:
            continue
        x1, y1, x2, y2 = (int(v) for v in bounds.groups())
        print(f'{(x1 + x2) // 2} {(y1 + y2) // 2}')
        return 0
    print(f'no clickable node matching {needle!r}', file=sys.stderr)
    return 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1], sys.argv[2], '--any' not in sys.argv))
