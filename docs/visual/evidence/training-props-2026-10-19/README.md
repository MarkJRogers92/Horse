# Training-prop review evidence

The reproducible runtime probe is `tools/scenery/check_training_props.gd`.
Run after the .NET build and Godot import:

    godot --headless --path game --script "$PWD/tools/scenery/check_training_props.gd"

For actual screenshots, run with a graphics display (or Xvfb and Mesa Vulkan):

    godot --path game --script "$PWD/tools/scenery/check_training_props.gd" -- --out="$PWD/evidence/training-props"

`Training prop placement` CI publishes `training-prop-placement`, containing a
normal yard-camera context image, a focused equipment image and the probe log.
The screenshot pass uses the actual C# yard with its normal scenery and lights;
no Blender render is represented as an in-game screenshot.

Local red/green: the probe first failed for all missing equipment groups and the
old show-jump still being present. Offline asset tests first failed for all five
missing GLBs. After integration, both tests pass. Local Godot headless checks
exercise geometry and placement; they do not render images. Local Xvfb cannot
open sockets in this sandbox, so rendered review is performed using CI.

## Rendered review and CI follow-up

Run 39186048636 passed on remote commit 75e9544 (the exact tested tree). Artifact:
https://github.com/MarkJRogers92/Horse/actions/runs/39186048636/artifacts/12158740172
Both 1280×720 PNGs were inspected. Close-up includes all equipment, grounded and
separated; the yard-context view naturally hides low pieces behind fence/trough.
These are scenery-only captures, not a full gameplay acceptance claim.

Broad visual run 39186048587 timed out twice after the unchanged gallery probe
reported 33 successful checks. Later broad checks were skipped. A narrow harness
follow-up explicitly unloads its final 3D menu and releases caches before quitting;
the new final-scene assertion failed before that change and passes afterward.
Headless follow-up exits 0 with 33 checks; rendered follow-up is pending CI.
No test or exit-124 handling has been relaxed.
