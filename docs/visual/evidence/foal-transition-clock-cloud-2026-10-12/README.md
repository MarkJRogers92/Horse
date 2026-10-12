# Slow-renderer transition deadline correction

The old exact Vulkan smoke failed locally at the same first asynchronous foal Walk-to-Idle transition as [CI run 38206811463](https://github.com/MarkJRogers92/Horse/actions/runs/38206811463). The old live transition probe also failed when the actual engine was limited to one FPS. Both pre-fix logs are retained.

The bridge advances through two 0.28-second process-time halves. Godot caps `_Process` delta at TimeScale × MaxPhysicsStepsPerFrame / PhysicsTicksPerSecond; accumulated process time can therefore lag real time at low rendering FPS. Both live test waiters incorrectly compared this animation with a three-second wall-clock limit. The failing coat selection only immediately presents Walk; selecting Idle afterward starts the smoke's first actual foal bridge. This did not establish a coat-change bug.

The shared **test-only** wait uses a three-second SceneTree simulation-time budget plus an independent 30-second wall watchdog. The timer updates after node processing, so the before-process signal cannot incorrectly count an upcoming update as completed. Success requires actual completion before both deadlines. The production bridge, its curves/duration, presenter/viewer behavior, and all endpoint/geometry/UV/morph/contact/stretch tests remain unchanged. No renderer fallback or warning suppression is used.

## Verification

- Clean Godot.NET.Sdk 4.7.2 warnings-as-errors build: exit zero, zero warnings/errors.
- The same Vulkan Forward+ smoke command, 1280×720 and all original capture arguments reaches the final all-four-family success line with 24 images. Cloud uses authenticated TCP Xvfb and installed llvmpipe LLVM 19.1.7/Vulkan 1.4.305; CI uses LLVM 20.1.2/Vulkan 1.4.318. The renderer mode is the same. The passing run benefited from warmed shader caches, so controlled low-FPS regressions independently prove the timing correction.
- 2,122 transition checks pass. Both real viewer family mappings perform Walk → chestnut coat → Idle at one FPS without manually invoking `_Process`. They complete in 6–7 frames, 5.990–6.985 wall seconds and 0.800–0.809 simulation seconds, restoring selected morphology.
- Disabling presenter processing while the SceneTree continues is rejected at three simulation seconds. A separate shortened watchdog test rejects the same stalled state by wall time. These adversarial cases prevent treating a genuinely stuck animation as success.
- All original 19 measured physical bridge cases retain their prior checks. The extra four measurements are two slow-viewer cases and two negative deadline cases, not additional anatomical trials.
- 56 Python evidence/walk tests, frozen-spec validator and whitespace checks pass. Production assets, adult entries, UV/morph sources and runtime transition implementation are unchanged.

The final main viewer foal image was visually inspected. All raw captures remain in cloud working outputs; the four viewer images and hashes of all 24 files are included. No push, Mac operation or spending.

API references: [Godot process delta](https://docs.godotengine.org/en/stable/classes/class_node.html#class-node-private-method-process), [SceneTreeTimer update ordering](https://docs.godotengine.org/en/stable/classes/class_scenetreetimer.html). Installed GodotSharp 4.7.2 documentation confirms the same contracts.
