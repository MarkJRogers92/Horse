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
