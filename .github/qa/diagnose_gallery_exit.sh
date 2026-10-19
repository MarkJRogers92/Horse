#!/usr/bin/env bash
# Diagnostic only: keep the real process exit code and the existing 180s deadline.
set -euo pipefail
G=$(find "$HOME/godot" -name 'Godot_*.x86_64' -type f | head -1)
mkdir -p evidence/gallery-exit
printf 'yard=%s\ncommit=%s\n' "$VARIANT" "$(git rev-parse HEAD)" > evidence/gallery-exit/context.txt
dotnet build game/Cloverbrook.Game.csproj -warnaserror
"$G" --headless --import --path game > evidence/gallery-exit/import.log 2>&1
set +e
timeout 180 xvfb-run -a -s '-screen 0 1280x720x24' python3 .github/qa/launch_gallery_debugger.py "$G" --path game --rendering-driver vulkan --resolution 1280x720 --script "$PWD/tools/portraits/check_gallery.gd" -- --out="$PWD/evidence/gallery-exit" > evidence/gallery-exit/gallery.log 2>&1
code=$?
cat evidence/gallery-exit/gallery.log
printf 'GALLERY_PROCESS_EXIT=%s\n' "$code" | tee evidence/gallery-exit/exit.txt
exit "$code"
