# Static adult and foal restored — 2026-10-10

The user requested restoring the good static models and saving the animation experiments separately.

- Eleven asset/runtime/export/capture/workflow files are byte-identical to `74f5094`; SHA-256 and baseline checks are in `source_equality.json`.
- Both actual Godot 4.7.2 imports have the original 27-bone skeleton, all four named skinned meshes, body coat masks and marking UV2, normal detail, and zero animation clips. Import reports passed with no validation errors.
- C# build with warnings treated as errors passed with zero warnings/errors.
- Actual Metal rendering captured 12 clay views and four adult/foal coat pairs: 16 images, zero capture failures. Adult and foal side views and bay pair were visually inspected; selected images and full report are here. This is a restoration check, not a new animation or matrix approval.
- Parent independently checked the worker diff, exact restored files, unchanged catalog mapping, imported assets and rendered evidence. No protected handoff/domain/matrix files changed.

All experimental files remain in branch `archive/horse-animation-experiments-2026-10-10` at `674e7908d706ae0309b5b93f0beb3ab73846fa23` and the existing complete local backup. Animation-only generators/capture code have been removed from the active branch; the restored CI regenerates static meshes. No animation work resumed.

Earlier full 131-image CI evidence is linked from VISUAL_STATUS.md. A new CI run is not claimed by this local restoration evidence.
