# D-015 — Authored gallop and the practice race in 3D

Accepted under the owner's 2026-10-13 instruction ("all 4 … use blender … you have my permission for all").

- `tools/horse/author_gallop.py` (Blender 5.2.2 `bpy`) authors an in-place transverse gallop
  (16 frames at 24 fps, left lead, suspension after the leading fore) on the Light and Stock
  walk rigs, which share deform-bone names. Joint angles are armature-space rotations about
  the lateral axis; the back flexes once per stride, the neck pumps, the tail streams.
- Only the armature and the "Gallop" action are exported (`horse_{light,stock}_gallop.glb`,
  about 70 KB each). The audited Walk meshes, morphs and textures are not re-exported.
  `HorsePresenter.PresentGallop` loads the Walk model and adds the clip to its library after
  checking that every track resolves; otherwise it keeps walking.
- Review: side-view Workbench renders of both families at eight stride phases,
  `docs/visual/evidence/gallop-2026-10-13/`. Known limits: a stylised gallop, not motion
  capture; hooves are not IK-planted, so ground speed (6.4 m/s × height) is an estimate.
  Foals have no gallop (they do not race).
- The practice dialog shows `RaceView`: a 3D track (sand, rails, Meshy oaks, chequered
  finish) where entrants gallop to their recorded progress while the camera follows the
  leader; the 2D lanes remain as the exact read-out. Results come only from the domain frames.
