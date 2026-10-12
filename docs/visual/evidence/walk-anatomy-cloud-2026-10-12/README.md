# Anatomically corrected neutral horse Walk

D-007, owner-approved local implementation after review of the backwards adult
front knees and crouched foal support. Built on checkpoint
`d26f2ee9544e3d9ee76c9854d73cb5c90853e66e` in the existing cloud checkout.
No paid generation or Mac execution was used.

## Result and diagnosis

Light's actual exported front carpus bent behind the elbow–fetlock chord
(right foreleg -86.56 to -3.73 mm). Stock inherited this evaluated pose chain
(-87.95 to +0.34 mm). Hoof orientation was almost frozen throughout recovery.
The foal already bent forward but supported its weight with approximately
43–50 degrees of front carpal flexion. These are distinct problems.

The official Mesh2Motion native Walk and Muybridge sequences were inspected,
including side-on joint overlays. Native amplitudes were reference observations,
not a target for these differently proportioned meshes. A direct two-bone IK
switch caused 2.368x Body stretch and was rejected before integration.

- Light now solves the proximal forelimb and forward carpal branch together.
  Stance aims at 5 degrees of carpal flexion; swing at up to 40 degrees.
  A toe-pivot recovery fold peaks near 40 degrees and returns flat for contact.
  Bounded forearm weights prevent the rejected shoulder pinch. Existing exact
  hair roots stay locked; inherited unweighted Tail points and Mane weight
  discontinuities were repaired under the same independent all-component tests.
- Stock transfers the accepted Light global motion through its existing
  family-specific rest fitting, retaining its own surface and procedural coat.
- Foal uses an independent proximal forelimb solve while preserving the existing
  hind gait, body motion and stride. The support carpus is near 5 degrees; swing
  reaches about 75 degrees with a 25-degree recovery hoof fold. Its actual skin
  transforms, rather than approximate interpolated helper markers, define the
  final anatomical measurement.

## Independent measurements

Actual Godot 4.7.2 project-imported animation, 117 phases:

| Measurement | Light | Stock | Foal |
|---|---:|---:|---:|
| Minimum forward carpus offset | 14.957 mm | 13.100 mm | 11.912 mm |
| Maximum planted front flexion | 5.013° | 6.153° | 5.026° |
| Recovery hoof pitch range | 39.790° | 39.790° | 24.866° |
| Maximum planted sole drift, any leg | 1.089 mm | 1.091 mm | 0.491 mm |
| Dense source Body stretch, 581 phases | 1.58497x | 1.55018x | 1.74034x |

Independent raw-glTF evaluation agrees with the native importer. New tests reject
backward carpi, crouched support, frozen recovery hooves and planted hoof
rotation; the historical Stock candidate demonstrably fails the new test.
The original nine Light physical gates remain unchanged and all pass, including
its 1.6x stretch limit. Stock/foal retain their original 2x limit. No contact,
loop, normalized-weight or hair-join threshold was relaxed.

Every component retains its original positions, normals, UVs, coat colors and
four morphs at every exported corner with zero error against the family
foundation. Light additionally has a fresh exact Blend snapshot/UV-corner/morph
proof, preserving the existing signed-detail derivation contract and atlas.
`preservation.json` verifies 53 historical source/evidence/shader/foundation
files and all non-Walk catalog data remain exact. Previous sources, candidates
and evidence remain intact; prior game GLBs are retained in Git history.

## Visual and application checks

- All three full loops were reviewed at 30 phases from side, front and
  three-quarter views. The nine contact sheets and render manifests are here.
- `light-before-after.mp4` uses identical inspection cameras/materials and full
  loops. `corrected-family-loops.mp4` shows all three corrected side loops.
- Actual main-game rendered and headless smoke tests pass every family mapping,
  coats/markings, repeat Walk/Idle transitions, neutral shape, original shape
  restoration, route speed, pause/resume, camera following and evidence guards.
- The separate Light coat/UV runtime proof passes; its full report is losslessly
  compressed here and a readable summary omits only the large contact samples.
- A 50-entry appearance/foal Idle/adult Idle/viewer/detail/Stock/probe regression
  has zero failures. Selected actual game captures are included.
- Warnings-as-errors build: 0 warnings/errors. Domain suite: 190 passed.
  Focused walk tests: 16 passed. Frozen-spec validation: passed.
- An exported PCK loaded from an empty project contains the exact raw assets,
  required new evidence and four-component imported scenes; zero failures.
  This is packaging verification, not a Mac build or launch certification.

## Evidence and reproduction

Canonical candidate files:
`art/derived/production/candidates/{light,stock,foal}-walk-anatomy/`.
Construction-time source reports are separate from later independent acceptance;
any construction-time `runtimeTested: false`/pending review fields do not replace
these completed game reports.

Active game evidence roots are `light-walk-v6-anatomy`, `stock-walk-anatomy`, and
`foal-walk-anatomy` under `game/assets/horse/production/evidence/`. Their hashes
are bound in the normal catalog and fail-closed C# validators. Both foal mappings
use the same juvenile master. Light's preview catalog is bound independently.

Builders: `tools/horse/repair_light_walk_anatomy.py`, `derive_stock_walk.py`
(with `--donor` pointing to the corrected Light blend and `--out` to its new
candidate directory), and `derive_foal_anatomical_walk.py`.

Use Blender 5.2.2 with `audit_walk_candidate.py --samples 581`,
`audit_walk_source_physics.py` for the Light limits,
`audit_light_uv_preservation.py`, and `audit_foal_attachments.py`.
Use `audit_exported_walk_anatomy.py GLB PROFILE --out REPORT` for the independent
raw animation check. In Godot 4.7.2, run `check_imported_walk.gd` with the actual
`res://` asset/profile and `--imported`; run `check_imported_walk_anatomy.gd`
with `GLB PROFILE REPORT` for the actual native skeleton check.

Normal verification commands remain:

```sh
python3 tools/validate_handoff.py
python3 -m unittest discover -s tools/horse -p 'test_*walk*.py'
dotnet build game/Cloverbrook.Game.csproj -warnaserror
dotnet test Cloverbrook.sln --configuration Release -m:1 -nr:false --disable-build-servers
# With the pinned Godot .NET executable in $G:
"$G" --headless --path game --import
"$G" --headless --path game -- --smoke-test
```

The render paths and full regression command are unchanged from the prior
integration evidence. `manifest.json` binds the delivered artifacts and relevant
source/evidence files.

## Scope and caveats

Acceptance remains neutral Walk at unit scale. Idle restores the selected
morphs. Morph-extreme Walk, other gaits and a distinct compact juvenile mesh are
not newly accepted. This preserves the existing stylized geometry, so it does
not promise a redesigned joint silhouette or zero local skin compression.
Owner artistic review and native Mac signing/launch remain separate. The
existing Mac download still contains the prior animation. Publication is left
to the parent checkpoint workflow; this worker did not push.

## Reference sources

- Official native rig/Walk (CC0): https://github.com/Mesh2Motion/mesh2motion-assets/tree/main/rigs/horse
- Official animation GLB: https://github.com/Mesh2Motion/mesh2motion-app/blob/main/static/animations/horse-animations.glb
- Native rig announcement: https://mesh2motion.org/news
- Muybridge, Hansel walking free: https://www.nga.gov/artworks/166994-plate-number-575-hansel-walking-free
- AMNH horse gait sequences: https://www.amnh.org/explore/ology/zoology/horse-gaits-flipbooks-walk-trot-and-gallop
