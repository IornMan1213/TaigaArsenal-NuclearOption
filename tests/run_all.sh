#!/usr/bin/env bash
# Runs the firing tests in tests/ingame (A to N on the MiG-29 and su30; or the ones named: run_all.sh c g) through the MiG-29 pack's test driver.
# Each run starts and quits the game, so only run it when nobody is playing.
set -u
here=$(cd "$(dirname "$0")" && pwd)
pack=${MIG29PACK:-$here/../../MiG29Pack-NuclearOption}
runs=${*:-a b c d e f g h i j k l m n su30}
fails=0
for r in $runs; do
    echo "=== run $r"
    out=$(timeout 560 "$pack/tests/ingame/run.sh" "$here/ingame/run_$r.txt" 540 2>&1)
    echo "$out" | grep -E "^\[TEST\]|failed checks"
    echo "$out" | grep -q "failed checks: 0" || fails=$((fails + 1))
    # -x: an exact process name, so the match never hits a command line that only mentions the folder.
    while pgrep -x "NuclearOption.e" >/dev/null; do sleep 3; done
done
echo "runs with failures: $fails"
[ "$fails" = 0 ]
