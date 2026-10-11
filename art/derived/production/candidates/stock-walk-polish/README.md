# Stock Walk, polished neutral candidate

Use this separate candidate for the next integration step. It supersedes the initial `stock-walk` preview; it does not replace the accepted foundation or activate a game family.

## What changed
Fitted Light-derived motion at 60 fps. Eight inherited empty Tail weight vectors are filled from immediate neighbors. Mane weights are harmonically extended from all 172 fixed Body-coincident attachment roots, removing inherited nearest-vertex scalp/shoulder discontinuities. Motion, Body skin and mesh identity are unchanged by the hair repair.

## Verified
- Four original meshes, original geometry/normals, all four morphs, native UV0/Canonical UV1 and coat-mask colors compare exactly to the original exported family at every indexed corner.
- Actual Godot 4.6.3 import advances Walk, closes the loop, resolves all 429 rigid sole witnesses and reports no zero/nonunit skin weights in any component.
- 61-phase imported-skin maximum Body edge stretch: 1.461977x. Worst measured adjacent stance motion: 1.0922 mm.
- Every component was also evaluated over 581 source phases. Maximum stretches: Body 1.470894x, Eyes 1.001271x, Mane 1.288491x, Tail 1.268832x.
- Hair joins remain closed: Stock has 222 exact Body/hair pairs; foal has 235 fixed surface witnesses with less than 0.030 mm gap drift. Actual source loop frames were rendered for review with plain inspection materials.

## Playback and integration requirements
- Import animation at **60 fps**. The default 30 fps rebake loses contact fidelity. Keep animation optimization disabled unless the same contact/deformation checks pass after optimization.
- Set `Walk` to linear looping explicitly; glTF alone does not encode the loop flag.
- Duration is 58/60 seconds. Horizontal source motion is in place. Apply one +X actor speed of **0.749997329711914 m/s**, and no second root-motion translation.
- Use unit scale and neutral morphs while walking. Restore the original descriptor/morphs when returning to Idle.
- Keep each family's native coat UVs and masks. Do not assign Light's signed-detail atlas to Stock or foal.
- Follow `docs/visual/REMAINING_WALKS_INTEGRATION_PREP.md`; the staging manifest is deliberately disabled pending actual .NET game checks.

## Scope
This is a tested neutral-shape asset and import proof. Morph extremes, the application coat shader, UI/Idle transitions and the full .NET game/Mac build still need integration validation. Some soft skin compression remains; this is not owner art approval. No paid generation was used.

## SHA-256
- Blend: `4efbbb61a865c08367d7bcdba8b7c756cf2100a8ad1ec90125e1641a7bc1d5b0`
- GLB: `3850db293a2b50d084fc9a4c2359c751e71f5456ba01219bf2dd1449dbda2e9e`

Source-only audit fields do not claim runtime tests; separate final-hash runtime/surface reports and preview manifests are in `docs/visual/evidence/walk-polish-cloud-2026-10-11/`.
