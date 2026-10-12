#!/usr/bin/env bash
set -euo pipefail
cd /workspace/scratch/46c24ac297cd/horse-walk-cloud/repo
source ../integration/env.sh
export LD_LIBRARY_PATH="$PWD/../toolchain/display/usr/lib/x86_64-linux-gnu:${LD_LIBRARY_PATH:-}"
export DISPLAY=localhost:89
export XAUTHORITY=$(mktemp "$PWD/../foal-transition-ci-fix/xauth.XXXXXX")
cookie=$(python3 -c 'import secrets;print(secrets.token_hex(16))')
../toolchain/display/usr/bin/xauth -f "$XAUTHORITY" add "$DISPLAY" MIT-MAGIC-COOKIE-1 "$cookie"
../toolchain/display/usr/bin/Xvfb :89 -screen 0 1280x720x24 -listen tcp -auth "$XAUTHORITY" -nolisten unix > ../foal-transition-ci-fix/xvfb.log 2>&1 &
xpid=$!
trap 'kill "$xpid" 2>/dev/null || true; rm -f "$XAUTHORITY"' EXIT
sleep 2
kill -0 "$xpid"
export VK_ICD_FILENAMES="$PWD/../toolchain/display/usr/share/vulkan/icd.d/lvp_icd.json"
timeout 300 "$G" --path game --rendering-driver vulkan --resolution 1280x720 -- --smoke-test --walk-evidence="$PWD/../foal-transition-ci-fix/before-captures" > ../foal-transition-ci-fix/before-smoke.log 2>&1
