# Stock and foal Walk integration preparation

This is staging guidance, not authorization to silently enable an untested scene.
The owner's October 11 request is to polish the two cloud-created walks and then
prepare integration. The Mac build and accepted Light path stay unchanged during
this phase. Use the hash-bound manifest produced by `prepare_walk_integration.py`
for the final asset versions; do not copy hashes from an earlier preview.

## Family mapping and preserved appearance

- Stock maps to `horse.compact.adult.v1`.
- The existing single foal source serves `horse.standard.foal.v1` and
  `horse.compact.foal.v1`; both mappings must be tested separately.
- Preserve each family's own UV0, Canonical/UV1, COLOR_0 masks, four body morphs,
  and descriptor-driven procedural coat. Stock and Light UV0 differ. The Light
  v5 signed-detail atlas is NOT a Stock or foal texture and must not be reused.
- Neutral-only Walk means all body morph and height values zero, scale one.
  Restore the actual selected descriptor/shape when switching back to Idle.
  Preservation of morph targets does not mean their animated extremes are tested.

## Exact integration points

1. `game/scripts/Visual/VisualCatalog.cs`: `ParseWalkAnimationVariant` currently
   accepts only a hardcoded Light family, source/profile/receipt/audit hashes,
   29 frames at 30 fps and the Light signed-detail binding. Add explicit,
   evidence-bound acceptance records for the new families rather than bypassing
   those checks or globally accepting arbitrary catalog paths/hashes. Stock/foal
   evidence schemas differ and must be read intentionally. Keep accepted Light
   logic and its exact hashes intact.
2. `WalkAnimationVariant.BodySignedDetail` is currently non-nullable. The new
   families use the already-supported procedural coat with no signed atlas;
   model this explicitly with nullable/optional detail and keep Light's mandatory
   validated detail requirement. `HorsePresenter.Present` must retain proper
   material binding when the variant has no detail map.
3. `HorsePresenter.HasNeutralWalkShape` currently rejects every foal and requires
   adult age. Permit only specifically validated family/age pairs, preserve
   neutral-shape checks, and update the adult-only diagnostic text. Do not relax
   unsupported-probe or descriptor validation. Set Walk to looping exactly as
   the existing presenter does. glTF does not encode engine loop settings.
4. `HorseViewer.CanUseCheckedLightWalk` currently allows only Light adults.
   Generalize to checked family variants after catalog proof validation; preserve
   probe rejection and neutral override/Idle restoration. Update the status text
   that currently says 'base adult shape'.
5. Use the final per-family profile's forward speed, duration and fps. Existing
   `HorseStage` movement/camera-follow code consumes these generically. Do not
   reuse the adult's 0.75 m/s for the foal, or move root motion twice.
6. Foal import MUST use `animation/fps=60` (or final profile fps if revised).
   Raw `GLTFDocument.generate_scene` also defaults to 30 and must receive 60.
   A 60-fps export downsampled to 30 previously produced 5.31 mm stance drift;
   matching the import rate reduced it to 2.95 mm. Preserve named skin binds and
   sufficient influences, and disable destructive animation optimization if the
   actual engine contact/loop check needs it. Set loop mode explicitly.
7. `game/addons/horse_asset_evidence/plugin.gd` exports only four exact raw files
   today. Add both new raw GLBs and any raw evidence dependencies required by
   runtime hashes, while retaining all four accepted originals. A working editor
   import does not prove the packaged app contains the raw bytes.
8. `game/scripts/Scenes/MainMenu.cs` smoke tests currently assert that Stock/foal
   Walk is unavailable. Change only validated families to positive checks and
   retain refusal tests for unsupported probes and untested shapes. Extend the
   isolated walk/contact harness to accept family-bound evidence, rather than
   feeding Stock/foal through hardcoded Light evidence constants.

## Required activation gate

- Validate source/export hashes and all source preservation reports. Confirm
  exported COLOR_0 values, not just attribute presence: an early Stock export
  contained all-white colors even though the Blender source masks were intact.
- Import in the game's pinned Godot 4.7.2 .NET build. Cloud asset checks use
  standalone Godot 4.6.3 and do not substitute for the actual C# application.
- All four imported components need normalized, nonzero skin weights on every
  referenced vertex. Stock polish repaired eight unweighted donor Tail vertices.
  Leave accepted Light files untouched; audit that inherited source issue before
  any later Light revision.
- On final imported meshes: all sole witnesses register to real Skin binds;
  max per-axis stance movement <=5 mm, sole penetration >=-2 mm, planted sole
  minimum <=3 mm, central swing clearance >=10 mm; unchanged animation loop and
  Body edge stretch <=2x. Check the closed hair/skin joins across the cycle.
- Render bay/chestnut/cremello, face markings and leg whites on each family while
  moving. Confirm UV/mask identity and no borrowed Light atlas.
- Test Walk -> pause -> resume -> Idle, repeated toggles, coat changes, both foal
  mappings, neutral-shape override/restoration, camera follow and bounded route.
- Test invalid/stale asset hashes, profile fps/speed mismatch, missing proof,
  unsupported probes, and nonneutral morphs. Invalid evidence must fail closed.
- Run warnings-as-errors build, frozen spec validator, domain suite, applicable
  visual suite and actual main-entry smoke. Then export, freshly unpack and run
  the app; verify raw evidence and signature. The existing M2 Mac build is not
  changed or retested by this cloud-only preparation.

No release-matrix promotion, main merge, public release, new paid generation or
Mac execution is included in this staging phase.
