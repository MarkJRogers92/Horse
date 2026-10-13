# Horse animation experiments — archived and rejected

The owner paused animation work on 2026-10-10 after reviewing the previews and rejecting the knee and surface deformation. These experiments are preserved on [`archive/horse-animation-experiments-2026-10-10`](https://github.com/MarkJRogers92/Horse/tree/archive/horse-animation-experiments-2026-10-10), commit `674e7908d706ae0309b5b93f0beb3ab73846fa23`.

That archive preserves the experimental Blender/GLB outputs, source motion samples, generation and validation scripts, Godot playback capture code, and CI changes. The results are not approved for the game. Playback and numeric loop checks did not establish acceptable leg deformation; the owner specifically rejected the knees. Do not copy those generated assets into the active game branch or treat the clips as accepted.

The active game branch uses the static adult and foal assets restored from `74f5094bc05681479c15887ca08591793034eeb9`. They have no animation clips. Their workflow and validation evidence are documented in `REFERENCE_ASSET_WORKFLOW.md` and `VISUAL_STATUS.md`. Rejected preview and source comparison images retained on the active branch are under `docs/visual/evidence/animation_paused_2026-10-10/`.

Animation work remains paused until the owner requests it again. No coat-matrix rows or animation quality claims were approved.

## Runtime playback of the shipped Idle and Walk (owner request 2026-10-13)

The owner asked for the horse animation in the current game to be fixed and improved. This covers how the *existing* checked Idle and Walk clips are played; it adds no new clip, mesh or rig, and the deformation of those clips is unchanged and still awaits owner art review.

- **Pace matches ground speed.** A Walk clip's feet only stay planted when the horse travels at the clip's checked speed times the clip's playback speed. `HorsePresenter.SetPace` scales the clip and `EffectiveSpeedMetersPerSecond` is the matching ground speed. The stable yard moves a horse by that value only, so turning and braking no longer skate.
- **Walk starts and stops use the rest-pose bridge** (`HorsePresenter.TransitionTo`) instead of reloading the model. It now works for both adult families as well as the foal: each family's Idle and Walk scenes have identical rest surfaces (Body, Mane, Tail and Eyes vertices compared at 0.000 mm), and the skinned rest swap measures 0.19 mm raw / 0.003 mm weight-normalised for adults (0.07 mm for foals).
- **`HorsePresenter.CanWalk(descriptor)`** says whether a descriptor has a checked Walk, so callers no longer load and clear a model just to be refused.
- **A missing clip logs a warning** instead of silently leaving the bind pose.
- **Viewer:** the bounded walk route used to snap the horse and camera back every four cycles. The interactive viewer now dips to a dark veil across that jump (`HorseStage.FadeAtRouteWrap`); captures leave it off.
- **Yard:** horses start their idle breathing at different points of the loop.
- **Checks:** `YardWalkProbe` (four simulated minutes of yard wandering) and `FoalTransitionProbe` (now also both adult families) run in `.github/workflows/visual.yml`.

Still not done: trot, canter, gallop and graze clips (they need new rigged animation and the D-007 art track), and a Walk for body shapes other than the neutral one.
