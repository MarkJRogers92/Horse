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
Headless follow-up exits 0 with 33 checks, but explicit scene/cache cleanup did
not reliably solve software Vulkan shutdown. A managed-GC experiment and material
culling contrast also failed to provide a reliable correction; both were removed.

### Supported-renderer comparison

Run https://github.com/MarkJRogers92/Horse/actions/runs/39192466774 compared the
same full yard and 35 gallery checks on two fresh software Vulkan runners and two
fresh OpenGL Compatibility runners. Compatibility exited 0 twice; Vulkan passed
once and timed out once. Both Compatibility gallery image pairs were inspected,
were byte-identical between attempts, and matched the successful Vulkan view
(maximum RGB difference 3/255). No blank portraits or missing UI were observed.

Gallery/navigation CI now explicitly uses Compatibility, the renderer already
selected for macOS by project.godot. Production renderer configuration is unchanged.
The dedicated equipment-placement test and the remaining visual/material captures
retain Vulkan. All assertions and the 180-second timeout remain enforced.

Known limitation: Mesa lavapipe software Vulkan can intermittently stall at process
exit after the gallery has passed, especially with the new scenery. This change
does not claim to fix Vulkan engine/driver shutdown or establish hardware-Vulkan
behavior. No erroneous asset ownership or geometry was found. Temporary diagnostic
workflows/debugger code are removed; the linked runs retain the investigation.
Final ordinary workflow results are recorded in the PR and STATUS.md.
