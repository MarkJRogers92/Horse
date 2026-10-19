#!/usr/bin/env bash
# Diagnostic only: keep the real process exit code and the existing 180s deadline.
set -euo pipefail
G=$(find "$HOME/godot" -name 'Godot_*.x86_64' -type f | head -1)
mkdir -p evidence/gallery-exit
printf 'yard=%s\ncommit=%s\n' "$VARIANT" "$(git rev-parse HEAD)" > evidence/gallery-exit/context.txt
dotnet build game/Cloverbrook.Game.csproj -warnaserror
"$G" --headless --import --path game > evidence/gallery-exit/import.log 2>&1
set +e
timeout 180 xvfb-run -a -s '-screen 0 1280x720x24' "$G" --path game --rendering-driver vulkan --resolution 1280x720 --script "$PWD/tools/portraits/check_gallery.gd" -- --out="$PWD/evidence/gallery-exit" > evidence/gallery-exit/gallery.log 2>&1 &
run=$!
while kill -0 "$run" 2>/dev/null; do
    if grep -q '\[portrait-gallery\] OK' evidence/gallery-exit/gallery.log; then
        sleep 15
        if kill -0 "$run" 2>/dev/null; then
            pid=$(pgrep -f "^$G --path game.*check_gallery.gd" | head -1)
            if [ -n "$pid" ]; then
                ps -L -p "$pid" -o pid,tid,comm,wchan > evidence/gallery-exit/threads.txt
                # A read-only backtrace of this ephemeral test process; no security settings changed.
                timeout 15 gdb --batch -ex 'set pagination off' -ex 'thread apply all bt 12' -p "$pid" > evidence/gallery-exit/native-backtrace.txt 2>&1
            fi
        fi
        break
    fi
    sleep 3
done
wait "$run"
code=$?
cat evidence/gallery-exit/gallery.log
printf 'GALLERY_PROCESS_EXIT=%s\n' "$code" | tee evidence/gallery-exit/exit.txt
exit "$code"
