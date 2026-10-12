# Corrected foal shape across Idle, Walk and morphs

Owner request: keep the reviewed hind correction when returning to Idle and make sure the UV/morph workflow still works. This checkpoint updates the static foal, both juvenile mappings and the interactive transition. Accepted Walk and adult assets are unchanged.

## Active sources and exact surface evidence

- Idle/morph master: `art/derived/production/candidates/foal-idle-hind-geometry/horse_foal.blend`, SHA256 `bef2fe3a18f2d742d0a5e4976dd25d489959be381e408214bfd4323bd725fe31`.
- Active Idle GLB: `422d320d4c976411e3f6d4346d74202b606f438bade44111b325686575c86291`.
- Accepted Walk remains Blend `6183af14b955d9fa20636deabaf38ca8aa1527e4a13af6216653ede6152bfcca`, GLB `5f302d885e7a331de7cbc7f494846becac5eceae062e8ffed805ab2ff31beddc`.
- Original Idle export and manifest remain in `foal-idle-foundation`; original editable `art/derived/production/horse_foal.blend` remains unchanged. The active manifest and production workflow now identify the corrected versioned source instead of that historical foundation.

The exact accepted Walk rest positions and all four shape-key endpoints were transferred to the original Idle rig. An independent audit confirms indexed topology, rest positions, UV0, UV1, coat masks/colors, normals and all four exported morph POSITION/NORMAL arrays match between the active modes. The original Idle nodes, rig, four-influence weights, assigned materials and animation remain exact. One unused orphan material datablock was dropped during save; no assigned/exported material changed. Session identity counters are not asset data.

The original bounded edit is disclosed honestly: 1,361 Body vertices differ from the historical static foundation, maximum 0.050000012 m. Untouched regions and UV/topology/mask contracts remain exact. The source/export region audit is separate from the cross-mode surface audit; the different Idle and Walk skins are not falsely declared equal. Neither old geometry nor a hash-only substitute is used on return to Idle.

All four morphology fields (`body_length`, `chest_width`, `neck_length`, `muscle`) remain editable on Body/Eyes/Mane/Tail. Twenty-five neutral, single-extreme and combined-extreme cases were checked at 121 Idle phases each for finite surfaces, deformation, hoof support, fixed hair attachments and loop closure. No original physical threshold was relaxed.

## Interactive transition

Foal animation controls now use a 0.28 s outgoing and 0.28 s incoming bridge. The outgoing animation pauses, its current pose/morphs/height ease toward shared corrected neutral rest, the rig changes at that rest surface, and the incoming rig eases into its start pose and selected shape before animation/locomotion resume. Repeated requests rebase from the current visible state. Appearance/family/age changes cancel stale work. Route position and camera orbit/zoom stay put.

Transition-only toe arcs initially dipped below the floor. The final bridge computes current imported skinned lower-hoof positions, including morph/height, and adds only the temporary Y clearance needed to stay at least -1.5 mm. The maximum measured lift is 6.234 mm; both target endpoints return exactly to zero. This is a short state-change clearance adjustment, not a change to the accepted gait or its hoof paths.

The focused probe exercises both foal mappings, 60/120 Hz, seven additional Walk stop phases, reversed/repeated toggles, nonneutral Walk refusal, shape restoration, stale evidence rejection, and actual viewer controls. It measures every skinned triangle corner across rig swaps, frame steps, all-component edge stretch and floor height. Nineteen measured bridges pass: maximum stretch 1.668427x (unchanged 2x gate), lowest Body point -1.5000002 mm (unchanged -2 mm floor bound). The raw imported rest-swap discrepancy is 0.073560 mm, below the explicit new 0.1 mm continuity bound. Normalizing imported weights in diagnostic math reduces that to 0.000774 mm; measured imported weight-sum errors and bind-rest residuals are retained in the report. Raw asset positions/UV/morphs themselves match exactly.

Steady Walk still requires neutral morphs and unit scale. Idle restores selected morphology gradually on the **corrected** hind base. Direct `Present` and adult behavior retain their prior immediate semantics. These clips and the bridge are still stylized animation, not a gait simulation or a new artistic approval.

## Verification and evidence

Authoritative hash-bound static reports are under `game/assets/horse/production/evidence/foal-idle-hind-geometry/`. Main and Light-preview catalogs both bind that evidence; missing/stale static proofs fail closed. Export packaging includes the raw corrected foal GLB and all required proofs. Python CI independently recomputes live GLB surface consistency and uses adversarial changes to reject old rest shape, changed UVs/colors/morphs/normals, changed Idle tracks/skin/materials and malformed data.

The capture runner's old Idle pose sampling depended on screenshot timing. On a slow renderer it labeled time 3.716 s as the quarter sample and missed the intended pose extrema. It now retains the real playback-advancement check, then explicitly seeks and freezes 0/quarter/midpoint poses before measuring and capturing them. Original movement, fixed-root and small-pose bounds remain unchanged.

Final check results, rendered clips/sheets, game coat captures, 50-image regression and package report accompany this document. Stored text logs may have trailing whitespace normalized; original logs remain in cloud working outputs. No user-computer operation, spending, push or platform launch was performed by this worker.

### Final local result

- 56 Python walk/evidence tests and 190 Release domain tests passed; warnings-as-errors game build and frozen-spec validation passed.
- Corrected native Idle, unchanged Walk contact/deformation/loop/attachments, and imported hind articulation passed.
- 2,111 headless transition checks and 3,552 rendered checks passed, including 19 measured bridges and 1,440 rendered frames. Eight six-second clips and contact sheets cover bay/cremello side/front/rear/quarter views.
- Final headless and rendered main smoke reached the all-four-family success line. The complete 50-image coat/Idle/viewer/detail/Stock/probe regression has zero failures; actual-game closeups retain face and leg markings.
- A freshly exported PCK loads independently from an empty project, verifying 29 raw asset/proof entries plus all six family scene loads, including both corrected static-foal consumers and their Idle/four-morph resources.
- All 214 existing protected asset/evidence/foundation files and both catalogs' adult mappings remain exact against `ab34e605`. The only existing production asset replaced is static `horse_foal.glb` and its honest new manifest.

See `final-checks.json`, `checks/`, `transitions/`, `game-captures/`, and `regression/`. Render-frame hashes are retained in `render-evidence-manifest.json`; full uncompressed working frames and the checked PCK remain in the cloud output folder rather than inflating the source repository. User-facing proof can use `transitions/bay-side.mp4` and `transitions/cremello-rear.mp4`.
