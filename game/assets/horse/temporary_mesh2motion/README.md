# Temporary Mesh2Motion donor preview asset

This is a local, temporary preview asset. It is not part of the production horse catalog. The GLB was exported from the pinned CC0 Mesh2Motion source while preserving the original rig and linked `Walk` action; no retargeting, weight repair, or geometry edit was made.

## Source and export provenance

- Upstream repository: `https://github.com/Mesh2Motion/mesh2motion-assets`
- Pinned source revision: `c5b0b6c821dbf2fa1dfe49590a6f431fd9f707ce`
- Rig source: `rigs/rig-horse.blend`, SHA-256 `585258436808e70a565d76ac493fb018b08c1087798a8c75ba13125f817c0bd9`
- Linked Walk source: `rigs/horse/animation-horse-walk.blend`, SHA-256 `eae76f96316e22b426e2b513db7a621a345fe336a8ac207458d4dbf42db2e5fa`
- Exported preview: `mesh2motion_horse_walk.glb`, SHA-256 `1a68f1fbb8996473273acb620ce2daa8b79b76f5d5265aa4ebee31e44d2bffa1`
- License: CC0-1.0; see the copied upstream root `LICENSE` in this folder.

The standard Blender glTF export bakes the linked evaluated animation, preserves the original geometry and skinning, and requests all skin influences (`export_all_influences=True`). This avoids intentionally trimming the source weights during export. The preview's own scene applies only a shared root rotation, a 32 mm vertical presentation offset, and a shared forward presentation route; none is baked into this asset.

## Limits and local preview

Godot 4.7.2 imports an eight-influence skin surface and does not expose the source's third JOINTS/WEIGHTS set. The source has nine active influences on four vertices, so this preview does not preserve every source influence exactly. Floor/contact alignment, foot slip, gait quality, and production suitability remain unaccepted diagnostics.

To open the isolated preview, run `../../outputs/horse-production-review/temporary-donor-preview/Open Temporary Donor Walk.command` from this checkout. Escape closes the window. The default game scene and production catalog continue to use the existing production assets.
