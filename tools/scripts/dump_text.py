#!/usr/bin/env python3
"""Print the text and content-desc of a `uiautomator dump`, optionally filtered.

  python tools/scripts/dump_text.py <dump.xml> [substring ...]

With no substrings, prints every non-empty label. Written for the Android
live-test remediation so device evidence can be read without a screenshot.
"""
import io
import re
import sys


def main(path, needles):
    text = io.open(path, encoding='utf-8').read()
    out = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace', newline='')
    seen = set()
    for match in re.finditer(r'<node[^>]*?>', text):
        node = match.group(0)
        label = re.search(r'text="([^"]*)"', node)
        desc = re.search(r'content-desc="([^"]*)"', node)
        for value in (label.group(1) if label else '', desc.group(1) if desc else ''):
            if not value or value in seen:
                continue
            if needles and not any(needle.lower() in value.lower() for needle in needles):
                continue
            seen.add(value)
            out.write(value + '\n')
    out.flush()


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2:])
