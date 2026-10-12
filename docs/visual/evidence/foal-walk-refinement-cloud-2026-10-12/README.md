# Foal walk refinement, 2026-10-12

The owner's visual feedback was the acceptance driver: the previous foal passed anatomy/contact tests but its legs still looked too spread and its front recovery looked odd. This checkpoint adjusts only the juvenile Walk. It is a locally verified candidate for the owner's visual judgment, not a claim of final artistic approval.

## Review first

- `foal-side-and-front-before-after.mp4`: synchronized before/after side and front loops, identical neutral material and camera per view. Left is the previous accepted foal, right is this refinement.
- `side-loop.jpg`, `front-loop.jpg`, `rear-loop.jpg`, `quarter-loop.jpg`: all 30 sampled poses of one complete new loop in each view, inspected before game integration. Source Blend hash is in `render-manifest.json`.
- `horse.standard.foal.v1-*.png`: actual integrated game, checked with coat/marking shader. The game retains its existing lighting and stylized surface/limb shapes; this is not a geometry redesign.
- `reference-review.md`: actual footage sources, inspected video timestamps and limitations. No paid source, Meshy generation or third-party reference footage is included in this repository.

## Finding and targeted correction

The old spread was both lateral and front–back. Exported rigid-sole centroid measurements (`trajectory-comparison.json`) distinguish this from camera perspective:

| Metric | Previous foal | Refined foal | Current Light adult (unchanged) |
|---|---:|---:|---:|
| Front lane width, approximately | 21.9 cm | 10.9 cm | 14.3 cm |
| Hind lane width, approximately | 24.5 cm | 17.5 cm | 28.5 cm |
| Mean fore–hind foot span | 85.3 cm | 75.4 cm | 112.6 cm |
| Stance travel | 20.0 cm | 28.0 cm | 43.5 cm |
| Target peak front carpal fold | 75° | 40° | 40° |

The foal's rest hip/limb height is roughly two-thirds of the adult's. The new stride is about 64% of the adult's, while retaining juvenile geometry. These are authored rig choices, **not calibrated measurements of real foals**. Rear perspective footage and tentative newborn steps do not establish numerical width standards.

- Move front hoof lanes inward 5.5 cm per side and 2 cm caudally; hind lanes inward 3.5 cm per side and 8 cm cranially.
- Reduce front recovery fold to the adult-style 40° target; retain 5° planted flexion and independent proximal solve. Restore a compact 40° toe-pivot recovery pitch, with flat planted hooves.
- Increase stance travel and the corresponding runtime movement speed together: 0.48275862068965525 m/s, 58 frames at 60 fps, duty factor 0.6. Four-beat contact ordering and ground clearance remain unchanged.
- No mesh positions, topology, UVs, normals, color attributes, or morph arrays changed. The original sources and previous foal candidate remain intact. `preservation-report.json` verifies preserved files and adult catalog entries; the live surface proof has zero error for all 47 indexed-corner attribute/morph arrays across four components.

## Evidence and regression

Editable source: `art/derived/production/candidates/foal-walk-refined/horse_foal_walk.blend`.
Builder: `tools/horse/derive_foal_refined_walk.py`, derived conservatively from the preserved prior foal anatomy builder.
Integrated GLB SHA-256: `7a3a78e1eb0cb30ec2fe989218f82bc429cc369acc60f70d471676025099b188`.
Blend SHA-256: `ac23bb9241663b6129faf5cac89e33ad68a67db59deca3d000508e8eb1057109`.

Both foal family mappings use that asset. The existing game evidence root `foal-walk-anatomy` now binds the refined candidate, while the old source candidate and prior evidence documentation are historical and unchanged. The source audit's `runtimeTested:false`/`visualReview:pending` describe the construction stage only; separate subsequent imported/rendered reports record the checks actually performed.

- Raw and actual project-imported anatomy: 117 phases, no failures. Minimum forward carpal offset about 11.9 mm; maximum planted flexion 5.014°; maximum recovery fold 39.898°; hoof pitch range 39.787°; planted pitch variation 0.066°.
- Dense source deformation: 581 phases, maximum body edge ratio 1.68411, below the unchanged 2.0 limit. Imported maximum about 1.68259.
- 176 sole witnesses: worst planted drift below 0.70 mm, ground penetration below 0.019 mm, central swing clearance above 41 mm. All component weights and original loop/stretch/contact gates pass.
- Hair joins: 361 mane and 34 tail witnesses; maximum gap drift below 0.059 mm. Imported attachment proof passes.
- Added signed hoof-lane measurement and bounded juvenile-style checks. The old foal explicitly fails these new style tests despite passing the old anatomy test. Crossing, excess splay, over-folding, and non-finite values are rejected. Existing anatomy and physical thresholds are not weakened.
- 22 Python walk tests, 190 Release domain tests, warnings-as-errors game build, frozen-spec validator, direct and actual project import checks: pass.
- Headless and rendered normal-game smoke: final all-four-family line passes. Full requested appearance/Idle/viewer/detail/Stock/probe regression: 50 entries, zero failures. Existing intentional unsupported-shape/coat/family warnings are expected negative tests.
- Independent exported PCK check: zero missing/changed asset or proof failures, including the new placement proof. This checks packaging on Linux, not execution on macOS.
- Independent read-only review confirmed preserved arrays/adult files, current hash chain, unchanged gates, and all four visual sheets. No publication or Mac access was performed.

## Reproduce checks

From repository root, with Blender 5.2.2 and Godot 4.7.2 .NET:

```sh
python3 tools/validate_handoff.py
python3 -m unittest discover -s tools/horse -p 'test_*walk*.py'
dotnet build game/Cloverbrook.Game.csproj -warnaserror
dotnet test Cloverbrook.sln --configuration Release -m:1 -nr:false --disable-build-servers
"$G" --headless --path game --import
"$G" --headless --path game --script "$PWD/tools/horse/check_imported_walk.gd" -- res://assets/horse/production/horse_foal_walk.glb /absolute/output/import.json res://assets/horse/production/evidence/foal-walk-anatomy/walk-motion-profile.json --imported
"$G" --headless --path game --script "$PWD/tools/horse/check_imported_walk_anatomy.gd" -- res://assets/horse/production/horse_foal_walk.glb res://assets/horse/production/evidence/foal-walk-anatomy/walk-motion-profile.json /absolute/output/anatomy.json
```

Animation remains checked only at neutral shape and unit scale. Idle restores the original selectable morphology. Visual taste and any further motion change remain the owner's decision; numerical success alone is not final visual acceptance.
