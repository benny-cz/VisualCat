#!/bin/sh
# F-15 phase 2: an untouched own-app (P0) live capture, screen off, one hour.
export MSYS_NO_PATHCONV=1
S=${ANDROID_SERIAL:-0A031FDD400365}
OUT=${F15_LOG:-artifacts/live-test/f15-soak.log}
mkdir -p "$(dirname "$OUT")"
say() { echo "$(date -u '+%Y-%m-%dT%H:%M:%SZ') $*" | tee -a "$OUT"; }
sh_() { adb -s $S shell "$@" 2>/dev/null | tr -d '\r'; }
ticks() { sh_ "cat /proc/$1/stat" | awk '{print $14+$15}'; }
now() { sh_ "date +%s"; }
sample() {
  P=$1; SEC=$2; LBL=$3
  T0=$(ticks $P); C0=$(now); sleep $SEC; T1=$(ticks $P); C1=$(now)
  if [ -z "$T1" ]; then say "$LBL: PID $P GONE"; return 1; fi
  DT=$((T1-T0)); DC=$((C1-C0))
  say "$LBL: ${DC}s, $DT ticks -> $(awk "BEGIN{printf \"%.2f\", ($DT/100.0)/$DC*100}")% CPU"
}

PID=$(sh_ "pidof com.barebit.visualcat")
CHILD=$(sh_ "ps -A -o PID,ARGS | grep 'logcat -b' | grep -v grep" | awk '{print $1}')
say "-- phase 2: own-app live capture, screen off --"
say "app PID=$PID, capture child=$CHILD"
say "battery=$(sh_ 'dumpsys battery | grep level') thermal=$(sh_ "dumpsys thermalservice | grep -i 'Thermal Status'")"
sh_ "input keyevent 223"; sleep 5
say "screen off; interactive=$(sh_ 'dumpsys power | grep mWakefulness=')"
for i in 1 2 3 4 5 6; do
  sample $PID 600 "capture-$i" || break
  say "   child alive=$(sh_ "ps -p $CHILD -o PID=" | wc -l) thermal=$(sh_ "dumpsys thermalservice | grep -i 'Thermal Status'") battery=$(sh_ 'dumpsys battery | grep level')"
done
sh_ "input keyevent 224"; sleep 4
say "-- phase 2 done; app PID now $(sh_ 'pidof com.barebit.visualcat'), child $(sh_ "ps -A -o PID,ARGS | grep 'logcat -b' | grep -v grep" | wc -l) --"
say "battery=$(sh_ 'dumpsys battery | grep level') thermal=$(sh_ "dumpsys thermalservice | grep -i 'Thermal Status'")"
