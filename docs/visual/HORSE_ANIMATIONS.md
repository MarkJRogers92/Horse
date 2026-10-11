# Horse animation experiments — archived and rejected

The owner paused animation work on 2026-10-10 after reviewing the previews and rejecting the knee and surface deformation. These experiments are preserved on [`archive/horse-animation-experiments-2026-10-10`](https://github.com/MarkJRogers92/Horse/tree/archive/horse-animation-experiments-2026-10-10), commit `674e7908d706ae0309b5b93f0beb3ab73846fa23`.

That archive preserves the experimental Blender/GLB outputs, source motion samples, generation and validation scripts, Godot playback capture code, and CI changes. The results are not approved for the game. Playback and numeric loop checks did not establish acceptable leg deformation; the owner specifically rejected the knees. Do not copy those generated assets into the active game branch or treat the clips as accepted.

The active game branch uses the static adult and foal assets restored from `74f5094bc05681479c15887ca08591793034eeb9`. They have no animation clips. Their workflow and validation evidence are documented in `REFERENCE_ASSET_WORKFLOW.md` and `VISUAL_STATUS.md`. Rejected preview and source comparison images retained on the active branch are under `docs/visual/evidence/animation_paused_2026-10-10/`.

Animation work remains paused until the owner requests it again. No coat-matrix rows or animation quality claims were approved.
