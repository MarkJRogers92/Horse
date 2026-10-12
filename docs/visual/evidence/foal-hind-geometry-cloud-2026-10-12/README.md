# Foal hind articulation and bounded geometry correction

The owner approved bringing the upper hind shape forward under the pelvis, then called the geometry-only comparison “much better.” This checkpoint preserves that reviewed direction without more cosmetic tuning. Local game integration is verified; publication is a separate parent-owned action.

## Result and ancestry

- Baseline: `acd4698db835067689cbd72598d9876d6421c37f`, canonical `foal-walk-refined`.
- Isolated rig step: `foal-walk-stifle`, Blend `4aa253f8c6a63f1fc377801460db3d4e4129513c4ee2a2fa2eefee155f62869d`, GLB `28d2893d1de080f80ca95f946b2872a18d74beb4f12428340e8cd328f32812e3`. Adds independent thigh/stifle/gaskin motion while retaining the previous hoof paths. Its historical rest-surface proof remains in that candidate directory.
- Final: `foal-walk-hind-geometry`, Blend `6183af14b955d9fa20636deabaf38ca8aa1527e4a13af6216653ede6152bfcca`, GLB `5f302d885e7a331de7cbc7f494846becac5eceae062e8ffed805ab2ff31beddc`.
- `geometry-only-before-after.mp4`: left isolated stifle rig; right final localized shape correction. `current-game-before-after.mp4`: left baseline refined foal; right both steps. Both are actual Blender renders of the corresponding assets, not synthesized images.
- Side/front/rear/quarter sheets each contain 30 phases. All 120 poses were inspected. The final game captures include bay/chestnut/cremello and face/leg markings for both foal family mappings.

## Honest geometry contract

The former exact-position claim does **not** apply to the final foal. A separate `horse.foal_walk.bounded_geometry.v1` branch explicitly requires `allPositionsExact:false` and `protectedMeshDataExact:false`. Historical exact proofs and prior candidates are retained unchanged.

The independent audit lists every affected original vertex and before/after offset: **1,361 Body vertices**, maximum **0.050000012 m**. Their original coordinates satisfy x < -0.24 m and 0.40 < z < 0.91 m. The authoring report contains 1,366 intended nonzero offsets; five rounded away at mesh precision, so the independently observed count is authoritative. Local hock smoothing and forward upper-limb displacement leave topology, UVs, colors, weights, object transforms, untouched positions and untouched shape-key positions exact. The same offset was applied to Basis and four morphology targets; relative displacement error is at most 5.96e-8 m, below the explicit 1e-7 m bound. Custom normals are recomputed only in recorded local smoothing support; packed normals and exported normals outside that support remain exact.

The indexed GLB-corner audit independently checks UV/color/topology/skin attributes, exported positions, morph displacements and untouched normals. CI also recomputes the bounded region from actual GLB arrays without Blender, with adversarial mutation regressions. Runtime pins all final assets/reports and additionally limits the observed maximum offset to 0.050001 m. Stock and Light retain their existing exact-surface contracts.

## Motion and game checks

- 34 Python walk tests; 190 Release domain tests; zero-warning/error game build; frozen-spec validator.
- Direct and actual project-imported contact, deformation, loop and hair-join checks passed. Original physical thresholds are unchanged.
- 117-phase exported/native front anatomy and placement checks passed. New 581-phase imported hind check confirms independent stifle movement, forward stifle/caudal hock orientation and joint coincidence within 0.135 mm (1 mm gate).
- 581-phase dense source deformation: maximum 1.68411, below unchanged 2.0 limit. Exact evaluated source endpoint closure.
- Against baseline, all 540 non-hind animation curves are exact. Measured front surface change is at most 0.0341 mm, unchanged-component change 0.0437 mm, and hoof witness change 0.000736 mm. These tiny posed differences are disclosed, not called exact or assigned an unproven cause.
- Full headless and rendered four-family smoke passed, including Walk/Idle, coat bindings, paused route, camera and viewer controls. The 50-entry appearance/Idle/viewer/detail/Stock/probe regression has zero failures. Independent exported PCK load verified all raw assets/evidence and imported four-component scenes.
- Independent review reproduced the full Blender bounded report byte-for-byte, checked final GLB animation/skin/node/material arrays against the stifle ancestor, and inspected four loop sheets and game captures without finding a substantive issue.
- `unchanged-family-and-history-report.json` checks 130 existing protected files, including adult assets, all original candidates and frozen handoff files, plus exact adult catalog entries.

## Reference basis and limits

`reference-review.md` is the preceding historical photo investigation; `photo-reference-index.json` identifies the 18 inspected breeder photos and age/view context. Three- and four-month foals informed the forward-thigh/backward-gaskin silhouette. Neonatal and clinical examples were distinguished, and oblique rear photos were not used as precise width measurements. `original-hind-diagnosis.png` documents the original articulation problem.

This remains the established neutral-shape/unit-scale Walk workflow. Idle restores the original static/morph asset, so its original rest geometry remains unchanged. The model retains its stylized surface and existing lighting. No Mac operation, paid generation, new atlas, topology change or publication was performed here. Numeric gates supplement the owner's visual judgment.

## Reproduce key checks

From repository root, use Blender 5.2.2 with `tools/horse/audit_foal_geometry_region.py -- --source-blend art/derived/production/candidates/foal-walk-stifle/horse_foal_walk.blend --candidate-blend art/derived/production/candidates/foal-walk-hind-geometry/horse_foal_walk.blend --out /absolute/report.json`. The auditor also reads each sibling GLB. `audit_foal_motion_preservation.py` compares final motion against the refined baseline and writes a separate report. Neither auditor modifies source assets.

Run `python3 -m unittest discover -s tools/horse -p 'test_*walk*.py'`. Godot CI imports the final project and executes both `check_imported_walk.gd` and the new `check_imported_foal_hind.gd`. The latter takes `res://assets/horse/production/horse_foal_walk.glb` and an absolute output JSON path. Actual command logs and captures are retained here; authoritative runtime reports are under `game/assets/horse/production/evidence/foal-walk-anatomy/`.

Stored text logs have trailing whitespace normalized; command results are otherwise retained. Original unnormalized logs remain in the cloud working output directory.
