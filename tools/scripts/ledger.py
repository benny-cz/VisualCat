#!/usr/bin/env python3
"""Update the remediation ledger in docs/ANDROID-LIVE-TEST-REPORT.md.

  python tools/scripts/ledger.py status F-20 "Code done" "pending"
  python tools/scripts/ledger.py record entries.md

`record` reads a file of one or more entries, each introduced by a line
`@@ F-NN Title`; the rest of the block is the entry body in Markdown.
"""
import io
import re
import sys

REPORT = 'docs/ANDROID-LIVE-TEST-REPORT.md'


def read():
    return io.open(REPORT, encoding='utf-8', newline='').read()


def write(text):
    io.open(REPORT, 'w', encoding='utf-8', newline='').write(text)


def set_status(finding, new_status, verified):
    text = read()
    pattern = re.compile(
        r'^(\| ' + re.escape(finding) + r' [^|]*\| [^|]*\| [^|]*\| )([^|]*)(\| )([^|]*)(\|)$',
        re.M)
    if not pattern.search(text):
        sys.exit('no ledger row for ' + finding)
    text = pattern.sub(
        lambda m: m.group(1) + new_status + ' ' + m.group(3) + verified + ' ' + m.group(5),
        text,
        count=1)
    write(text)
    print('status', finding, '->', new_status)


def record(title, body):
    text = read()
    text = text.replace('*(No entries yet.)*\n', '')
    finding = title.split()[0]
    header = '#### ' + finding + ' · ' + title[len(finding):].strip()
    block = header + '\n\n' + body.strip() + '\n\n'
    existing = re.compile(r'^#### ' + re.escape(finding) + r'\b.*?(?=^#### |\Z)', re.M | re.S)
    if existing.search(text):
        text = existing.sub(lambda _: block, text, count=1)
    else:
        text = text.rstrip('\n') + '\n\n' + block
    write(text)
    print('recorded', finding)


def record_file(path):
    text = io.open(path, encoding='utf-8').read()
    blocks = re.split(r'^@@ ', text, flags=re.M)
    for block in blocks:
        block = block.strip('\n')
        if not block:
            continue
        title, _, body = block.partition('\n')
        record(title.strip(), body)


if __name__ == '__main__':
    if sys.argv[1] == 'status':
        set_status(sys.argv[2], sys.argv[3], sys.argv[4])
    elif sys.argv[1] == 'record':
        record_file(sys.argv[2])
    else:
        sys.exit('usage: ledger.py status|record ...')
