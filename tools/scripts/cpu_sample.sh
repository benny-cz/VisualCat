#!/usr/bin/env bash
# Average CPU% of a process over a wall-clock window, from /proc deltas.
#
#   tools/scripts/cpu_sample.sh <package> [seconds]
#
# `top -n 1` reports an instantaneous slice and swings between 0% and 20% on the
# same steady workload, which is why the live-test report could only call the
# figure "bursty" (finding F-15). utime+stime deltas over a fixed window are the
# comparable number.
set -u
ADB="${ADB:-E:/Android/Sdk/platform-tools/adb.exe}"
PKG="${1:?package}"
WINDOW="${2:-10}"

PID=$(MSYS_NO_PATHCONV=1 "$ADB" shell pidof "$PKG" | tr -d '\r')
if [ -z "$PID" ]; then
  echo "not running: $PKG" >&2
  exit 1
fi

read_ticks() {
  MSYS_NO_PATHCONV=1 "$ADB" shell "cat /proc/$PID/stat" 2>/dev/null |
    awk '{ print $14 + $15 }' | tr -d '\r'
}

HZ=$(MSYS_NO_PATHCONV=1 "$ADB" shell getconf CLK_TCK | tr -d '\r')
HZ=${HZ:-100}

BEFORE=$(read_ticks)
sleep "$WINDOW"
AFTER=$(read_ticks)

if [ -z "$BEFORE" ] || [ -z "$AFTER" ]; then
  echo "process ended during the sample" >&2
  exit 1
fi

awk -v b="$BEFORE" -v a="$AFTER" -v w="$WINDOW" -v hz="$HZ" \
  'BEGIN { printf "pid %s  %.2f%% CPU over %ds\n", "'"$PID"'", (a - b) / hz / w * 100, w }'
