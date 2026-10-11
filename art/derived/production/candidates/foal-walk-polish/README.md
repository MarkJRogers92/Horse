# Foal Walk, polished neutral candidate

Use this separate candidate for the next integration step. It supersedes the initial `foal-walk` preview; it does not replace the accepted foundation or activate a game family.

## What changed
The juvenile keeps its own proportions and 20 cm stance travel. Revised anatomical forelimb hinge placement and four midpoint knee helpers spread joint rotation; velocity-matched swing curves remove foot-speed discontinuities. No stride reduction was used to pass deformation checks.

## Verified
- Four original meshes, original geometry/normals, all four morphs, native UV0/Canonical UV1 and coat-mask colors compare exactly to the original exported family at every indexed corner.
- Actual Godot 4.6.3 import advances Walk, closes the loop, resolves all 176 rigid sole witnesses and reports no zero/nonunit skin weights in any component.
- 61-phase imported-skin maximum Body edge stretch: 1.646696x. Worst measured adjacent stance motion: 0.4910 mm.
- Every component was also evaluated over 581 source phases. Maximum stretches: Body 1.648770x, Eyes 1.000224x, Mane 1.000211x, Tail 1.001028x.
- Hair joins remain closed: Stock has 222 exact Body/hair pairs; foal has 235 fixed surface witnesses with less than 0.030 mm gap drift. Actual source loop frames were rendered for review with plain inspection materials.

## Playback and integration requirements
- Import animation at **60 fps**. The default 30 fps rebake loses contact fidelity. Keep animation optimization disabled unless the same contact/deformation checks pass after optimization.
- Set `Walk` to linear looping explicitly; glTF alone does not encode the loop flag.
- Duration is 58/60 seconds. Horizontal source motion is in place. Apply one +X actor speed of **0.3448275862068966 m/s**, and no second root-motion translation.
- Use unit scale and neutral morphs while walking. Restore the original descriptor/morphs when returning to Idle.
- Keep each family's native coat UVs and masks. Do not assign Light's signed-detail atlas to Stock or foal.
- Follow `docs/visual/REMAINING_WALKS_INTEGRATION_PREP.md`; the staging manifest is deliberately disabled pending actual .NET game checks.

## Scope
This is a tested neutral-shape asset and import proof. Morph extremes, the application coat shader, UI/Idle transitions and the full .NET game/Mac build still need integration validation. Some soft skin compression remains; this is not owner art approval. No paid generation was used.

## SHA-256
- Blend: `5f94f6d90946d6a899c57239861ec79ef17f27767dd318e039938f5ed70cc96d`
- GLB: `219061fb162cfb227a9c0a6e025e12c83c8af6e834ba8517e3754ac8e3dd0769`

Source-only audit fields do not claim runtime tests; separate final-hash runtime/surface reports and preview manifests are in `docs/visual/evidence/walk-polish-cloud-2026-10-11/`.
