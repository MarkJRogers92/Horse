# Visual track status (Godot + horse pipeline)

**Branch:** `claude/godot-horse-visual-pilot` (it includes GPT's backend through `1538833` via a merge commit)
**Owner:** Mark leads integration; GPT adapted the selected horse. Claude's visual foundation is retained.
**Last updated:** 2026-10-10

## Current result — authored adult and foal

The owner-selected CC0 horse replaces the runtime placeholder for **both ages**. The original package in `art/reference/Horse_Reference/` stays intact. The foal is a separate juvenile adaptation with a shorter, shallower barrel, relatively long legs and large head, shorter muzzle, smaller hooves and short mane/tail. Independent peer review checked its new silhouette against the rejected first pass.

| Area | Current state |
| --- | --- |
| Game meshes | `game/assets/horse/reference/horse_{adult,foal}.glb`, 36,162 triangles each, metre units, +X forward, grounded root, Body/Eyes/Mane/Tail, COLOR_0 masks and canonical TEXCOORD_1. |
| Editable source | `art/derived/Horse_Reference/horse_{adult,foal}.blend`: quad-dominant body cage, separate hooves, eyes, mane/tail and 27-bone basic FK rig. Source is outside `game/` to avoid Blender auto-import. |
| Coats | Original fur normal detail connects to the procedural shader; fixed chestnut/blaze/socks are absent from exported albedo. All 26 fixtures (13 classes × two ages) rendered locally. Claude's descriptor recipes and deterministic marks remain the source of colour/markings. |
| Hair | Authored solid clumps use matte shading. Foal mane/forelock is shortened from the crest/poll; tail and tail bones shorten together from the dock. |
| Shapes | Adult and foal differ anatomically. Standard and compact share the same cob build within each age; compact reports this limitation. Only descriptor height is applied; other body/hair morphs remain unapplied. |
| Motion | Authored assets have no clips. Missing viewer options are disabled and capture skips them. Old placeholder skeleton/clips remain available for later retargeting; no gait quality is claimed. |
| Local verification | Game build 0 warnings/errors; both actual Godot imports retained skin, masks/UV2 and normal detail; main-scene smoke passed; real Metal capture produced **131 images, 0 failures** with correct unsupported refusals. |
| CI | New visual/domain CI is pending the integration push. Prior successful placeholder artifacts are preserved for comparison. |

No coat-matrix rows were promoted. These renders and renderer capabilities do not release the domain's live visual catalog or replace owner approval.

### Face-marking finding

The fresh Claude face-fix run [38096698518](https://github.com/MarkJRogers92/Horse/actions/runs/38096698518), `236f36d`, still showed misplaced/absent forehead white. The placeholder had inward normals from an extra winding reversal. `anatomy.py` now preserves marching-cubes winding; corrected legacy geometry was locally re-rendered with forehead white before being replaced in the runtime catalog.

New authored meshes derive the head mask from poll-to-muzzle and face-front axes. Fresh local captures show forehead stars, a central strip and muzzle snip, with matching adult/foal marking keys. Per-family head-camera targets fit these actual proportions. Inspect final CI images before recording remote verification.

### Exact next work and limits

Inspect the new CI clay/coat/marking sheets against baseline [38095905278](https://github.com/MarkJRogers92/Horse/actions/runs/38095905278) (`1124b73`) and record the evidence checkpoint. Matrix approval stays with the owner. Motion clips, adult body-shape morphs, live stable integration and platform exports remain later work.

Grey progression, pearl, silver, champagne, leopard/PATN1, roan, tobiano/sabino and shoulder bars remain Unsupported; tack/cosmetics remain unapplied. Additional CC0 fur listings were researched but their short-coat suitability is unverified; the preferred reference detail remains the default. See `REFERENCE_ASSET_WORKFLOW.md` for source credits, material workflow and editing notes, and `TOOLCHAIN.md` for reproduction.

## Previous placeholder checkpoint (superseded)

The following preserves Claude's earlier handoff for context. Its mesh/animation limitations and next steps describe the former placeholder; the current state is above.

## What works now
| Phase | State |
| --- | --- |
| A. Godot foundation | Godot **4.7.2 .NET** project in `game/`. The C# project references the shared `Cloverbrook.Domain` (no second genetics). Scenes: main menu, horse viewer, stable, capture. Forward+ with soft shadows, SSAO, AgX, MSAA. CI workflow `visual.yml`: build → import → smoke test → headless renders → contact sheets → artifact. |
| B. Horse pipeline | `tools/horse/` builds a **TEMPORARY procedural horse** (adult + foal) headlessly. Anatomical SDF meshing (numpy/scikit-image), then Blender `bpy`: decimation, coat-mask vertex data, canonical marking coordinates, 35-bone rig with skin weights, eyes, mane/tail hair cards, glb export. Adult body 36k triangles, foal 26k. Fully reproducible in CI. |
| B. Licensed asset | Shortlist in `ASSET_SHORTLIST.md`. **Owner decision; nothing purchased.** |
| C. Viewer | Orbit (drag/arrows), zoom (wheel/pinch/±), neutral vs stable lighting, coat picker (13 phase-1 classes + test markings + unsupported probes), adult/foal toggle, animation picker, view presets, turntable, support/markings readout, and a clear "cannot be shown" banner. |
| D. Genetics-driven visuals | The presenter renders `HorseVisualDescriptor` only, through `game/data/visual_catalog.json`. All 26 domain-exported fixtures are supported. Unknown layers, recipes or meshes get an **explicit Unsupported** result with reasons, and no horse is drawn. Markings come deterministically from `markingRealizationKey`, so the adult and foal match. Double cream gets pink skin and blue eyes. Dun gets a dorsal stripe and leg barring; nd1 gets a faint stripe. `DomainBridge.FromPresentation` adapts GPT's `StableBreedingQueries.ResolveVisualDescriptor`. |
| E. Stable | Pastel barn (gable roof, X-brace door, bunting, flower boxes, heart loft window), open-front stall with half door, straw, hay and bucket, oval white paddock fence, flowers, grass tufts, trees and hills. |
| F. Animation | Procedural `idle`, `breathe`, `look`, `walk`, `trot` exist and play. **Quality is unreviewed.** The capture only shows pose samples, which do not prove the motion looks right. |
| G. Evidence | The capture sets are `clay` (grey form review), `sheet` (13 classes × adult/foal × neutral/stable), `detail`, `markings`, `turntable`, `pair` (mare + foal), `stable`, `anim` and `probes`. `contact_sheets.py` assembles labelled sheets and `SUMMARY.md`. Committed snapshots are in `docs/visual/evidence/`. |

**Coat matrix:** no rows were promoted. All 13 phase-1 classes are still `visualStatus: none` and await the owner's review of renders on an accepted (non-placeholder) mesh.

## Known visual problems (honest list)
- **Face markings in the committed evidence images are misplaced.** The forehead was tagged as neck. The fix (head-region reclaim in `anatomy.py`) is committed and the horses are rebuilt, but the fix was **not visually re-verified** before the session ended. The next CI run's `markings_sheet` will show whether it worked.
- The horse is a **procedural placeholder**. Its proportions read as a horse, but surface anatomy is soft and blobby: no real muscle and bone definition, and the joints are simplified. It is not acceptable as the final asset.
- The mane and tail are simple hair cards. They read as hair from a distance but look thin up close. There is no hair physics.
- Eyes have no eyelids or lashes. Ears are simple cupped shapes.
- Only height is applied from the morph weights, as a whole-horse scale. The other morphs are reported as unapplied (the placeholder has no blend shapes).
- `horse.compact.*` body families have no mesh. Descriptors for them will be reported Unsupported, which is correct but means nothing is shown.
- Not implemented (reported Unsupported if requested): shoulder bars on duns, grey progression, pearl, silver, champagne, leopard/PATN1, roan, tobiano/sabino, cosmetics/tack.
- Software (lavapipe) rendering in CI is slow and lacks GPU-only polish. CI images are a functional record, not a beauty benchmark.
- There are no Windows/macOS export builds yet. Export templates are the next CI step.

## Mesh contract for an alternate horse (for GPT's model, or a licensed one)
Make the glb satisfy the following. Then add a `meshFamilies` entry in `visual_catalog.json` (or point the existing IDs at it) and CI renders the identical evidence for comparison.
1. Units are metres, +Y up, facing +X, with the root at the ground between the hooves.
2. Mesh nodes are named `Body`, `Eyes`, `Mane`, `Tail`. The animation names are `idle`, `breathe`, `look`, `walk`, `trot`; missing animations are fine but get reported.
3. `Body` has `COLOR_0` = (r points, g soft-nose/eye skin, b midline [dorsal on trunk/neck, face-front on head], a countershade) and `TEXCOORD_1` = canonical marking coordinates:
   - head: (s 0 poll → 1 muzzle tip, t lateral −1..1)
   - legs: (10 + leg index FL0 FR1 HL2 HR3, height 0 ground → 1 knee/hock; hoof below 0.165)
   - everything else: (−10, 50)
4. `Eyes` have UV.x = outwardness (0 centre plane → 1 outer pole) and UV.y = vertical position.
5. Mane and tail cards have UV.x across the card and UV.y root → tip.

`tools/horse/anatomy.py` (`Horse.vertex_data`) shows how to derive the masks from anatomy. A script for an imported mesh can compute the same values from bone proximity.

## CI
- `visual` workflow: https://github.com/MarkJRogers92/Horse/actions/workflows/visual.yml. Run 1 (https://github.com/MarkJRogers92/Horse/actions/runs/38095905278) passed the domain export, horse rebuild, C# build, import and **smoke test**; rendering was still running at handoff. Artifact `visual-evidence` holds PNGs, sheets, `SUMMARY.md` and `capture_report.json`.
- `domain` workflow (GPT's tests) also runs on this branch.

## Exact next step
1. Owner: review `docs/visual/evidence/sheets/*.png` and choose an asset from `ASSET_SHORTLIST.md` (or compare it with GPT's alternate horse).
2. Visual track: write `tools/horse/import_licensed.py` for the chosen asset, implementing the mesh contract above. Re-run CI, compare clay and coat sheets side by side, then request matrix review.
3. Integration (with GPT): a viewer "My stable" mode that loads a save through `AtomicSaveStore`, lists horses through `StableBreedingQueries`, and presents them via `DomainBridge.FromPresentation`. With the real matrix unreleased this will correctly show Unsupported until rows are approved.
4. CI: add Windows and macOS export presets plus export templates.
