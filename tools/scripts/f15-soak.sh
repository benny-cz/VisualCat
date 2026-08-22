#!/bin/sh
# F-15 screen-off soak: idle control, then an untouched own-app live capture.
# Appends to the log after every sample so an interrupted run keeps its evidence.
export MSYS_NO_PATHCONV=1
S=${ANDROID_SERIAL:-0A031FDD400365}
OUT=${F15_LOG:-artifacts/live-test/f15-soak.log}
mkdir -p "$(dirname "$OUT")"
say() { echo "$(date -u '+%Y-%m-%dT%H:%M:%SZ') $*" | tee -a "$OUT"; }
sh_() { adb -s $S shell "$@" 2>/dev/null | tr -d '\r'; }

# ticks(pid) -> utime+stime in clock ticks
ticks() { sh_ "cat /proc/$1/stat" | awk '{print $14+$15}'; }
now()   { sh_ "date +%s"; }

sample() { # sample <pid> <seconds> <label>
  P=$1; SEC=$2; LBL=$3
  T0=$(ticks $P); C0=$(now)
  sleep $SEC
  T1=$(ticks $P); C1=$(now)
  if [ -z "$T1" ]; then say "$LBL: PID $P GONE after ${SEC}s"; return 1; fi
  DT=$((T1-T0)); DC=$((C1-C0))
  say "$LBL: ${DC}s window, $DT ticks -> $(awk "BEGIN{printf \"%.2f\", ($DT/100.0)/$DC*100}")% CPU"
}

PID=$(sh_ "pidof com.barebit.visualcat")
say "=== soak start; app PID=$PID ==="
say "battery=$(sh_ "dumpsys battery | grep level" ) thermal=$(sh_ "dumpsys thermalservice | grep -i 'Thermal Status'")"

say "-- phase 1: idle control, screen off, no capture --"
sh_ "input keyevent 223"; sleep 5
say "screen: $(sh_ "dumpsys power | grep 'Display Power'")"
sample $PID 300 "idle-1"
sample $PID 300 "idle-2"
sh_ "input keyevent 224"; sleep 3
say "-- phase 1 done; PID now $(sh_ "pidof com.barebit.visualcat") --"
