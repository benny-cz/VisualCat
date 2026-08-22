#!/bin/sh
# F-15 screen-off soak, both legs, parameterised for any device.
#
#   ANDROID_SERIAL=<serial> sh tools/scripts/f15-soak-samsung.sh idle
#   ANDROID_SERIAL=<serial> sh tools/scripts/f15-soak-samsung.sh capture
#
# `idle`    two 300 s windows, screen off, no capture — the control.
# `capture` six 600 s windows, screen off, with a capture already running and
#           untouched. Start the capture by hand first; this leg must not touch
#           the UI, because touching it is what the finding is about.
#
# Every sample is appended as it is taken, so an interrupted run keeps its
# evidence and resumes at the next window (finding F-15, §8.5).
export MSYS_NO_PATHCONV=1
S=${ANDROID_SERIAL:-RFCRC0A9GND}
OUT=${F15_LOG:-artifacts/live-test/20260822-samsung-audit/evidence/F15-soak.log}
mkdir -p "$(dirname "$OUT")"
say() { echo "$(date -u '+%Y-%m-%dT%H:%M:%SZ') $*" | tee -a "$OUT"; }
sh_() { adb -s $S shell "$@" 2>/dev/null | tr -d '\r'; }

# utime+stime for one pid, in clock ticks (USER_HZ 100 on this ABI).
ticks() { sh_ "cat /proc/$1/stat" | awk '{print $14+$15}'; }
now() { sh_ "date +%s"; }
therm() { sh_ "dumpsys thermalservice | grep -i 'Thermal Status'"; }
batt() { sh_ "dumpsys battery | grep '  level'"; }

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
[ -z "$PID" ] && { say "!! app is not running; start it first"; exit 1; }

case "${1:-idle}" in
  idle)
    say "=== F-15 idle control (screen off, no capture); PID=$PID; $(therm); $(batt) ==="
    say "children: $(sh_ "ps -A -o ARGS" | grep -c 'logcat -b')"
    sh_ "input keyevent 223"; sleep 5
    say "display: $(sh_ "dumpsys power | grep -m1 'Display Power'")"
    sample $PID 300 "idle-1"
    sample $PID 300 "idle-2"
    sh_ "input keyevent 224"; sleep 3
    say "=== idle done; PID now $(sh_ 'pidof com.barebit.visualcat'); $(therm); $(batt) ==="
    ;;
  capture)
    CHILD=$(sh_ "ps -A -o PID,ARGS" | grep 'logcat -b' | grep -v grep | awk '{print $1}' | head -1)
    [ -z "$CHILD" ] && { say "!! no capture child; start a capture first"; exit 1; }
    say "=== F-15 capture leg (screen off, untouched); PID=$PID child=$CHILD; $(therm); $(batt) ==="
    sh_ "input keyevent 223"; sleep 5
    say "display: $(sh_ "dumpsys power | grep -m1 'Display Power'")"
    for i in 1 2 3 4 5 6; do
      sample $PID 600 "capture-$i" || break
      say "   child alive=$(sh_ "ps -p $CHILD -o PID=" | grep -c .) $(therm) $(batt)"
    done
    sh_ "input keyevent 224"; sleep 4
    say "=== capture leg done; PID now $(sh_ 'pidof com.barebit.visualcat'); children $(sh_ "ps -A -o ARGS" | grep -c 'logcat -b'); $(therm); $(batt) ==="
    ;;
  *) echo "usage: $0 [idle|capture]" >&2; exit 2 ;;
esac
