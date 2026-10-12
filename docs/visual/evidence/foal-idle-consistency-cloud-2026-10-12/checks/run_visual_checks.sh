#!/usr/bin/env bash
set -euo pipefail
cd /workspace/scratch/46c24ac297cd/horse-walk-cloud/repo
source ../integration/env.sh
export LD_LIBRARY_PATH="$PWD/../toolchain/display/usr/lib/x86_64-linux-gnu:${LD_LIBRARY_PATH:-}"
export DISPLAY=localhost:89
export XAUTHORITY=$(mktemp "$PWD/../foal-idle-consistency/xauth.XXXXXX")
cookie=$(python3 -c 'import secrets;print(secrets.token_hex(16))')
../toolchain/display/usr/bin/xauth -f "$XAUTHORITY" add "$DISPLAY" MIT-MAGIC-COOKIE-1 "$cookie"
../toolchain/display/usr/bin/Xvfb :89 -screen 0 1280x720x24 -listen tcp -auth "$XAUTHORITY" -nolisten unix > ../foal-idle-consistency/xvfb.log 2>&1 &
xpid=$!
trap 'kill "$xpid" 2>/dev/null || true; rm -f "$XAUTHORITY"' EXIT
sleep 2
kill -0 "$xpid"
"$G" --path game --rendering-method gl_compatibility --rendering-driver opengl3 --audio-driver Dummy --resolution 960x540 res://scenes/tools/FoalTransitionProbe.tscn -- --out="$PWD/../foal-idle-consistency/rendered-transitions" --render > ../foal-idle-consistency/rendered-transitions.log 2>&1
"$G" --path game --rendering-method gl_compatibility --rendering-driver opengl3 --audio-driver Dummy --resolution 1280x720 -- --smoke-test --walk-evidence="$PWD/../foal-idle-consistency/game-captures" > ../foal-idle-consistency/smoke-rendered.log 2>&1
"$G" --path game --rendering-method gl_compatibility --audio-driver Dummy --resolution 1280x720 res://scenes/capture.tscn -- --out "$PWD/../foal-idle-consistency/regression" --sets appearance,foal_idle,adult_idle_import,viewer_age_toggle,detail_runtime,stock_runtime,probes > ../foal-idle-consistency/regression.log 2>&1
