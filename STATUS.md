# Project status / checkpoint

**Last updated:** 2026-10-10 by GPT (P2-02 stable-day prototype implemented and locally verified).
**Phase:** P2-02 calendar, daily care, operating costs, and saved End Day are implemented as a bounded playable slice. Full domain suite passed 165/165 locally; GitHub verification is pending for this checkpoint. P1-01 static adult/foal integration and P1-07/P1-09 remain preserved.
**Branch:** `claude/godot-horse-visual-pilot` (domain checkpoint history below originated on `claude/great-euler-hq83x3`).

## Current gameplay state — stable day prototype

The main menu now offers **Manage stable**, a sandbox management slot with Juniper, Maple, and 500 coins. Feed/bedding are mandatory, turnout/grooming optional. Review shows upkeep and any assistance before End Day publishes one calendar day, care, payment, and receipt atomically through the existing store. Closing the game has no time penalty. Shortfalls use assistance rather than negative balances or horse loss. See `docs/stable/PLAYTEST.md`, `daily_model.md`, and decision D-004.

Save wire/schema v2 explicitly migrates genuine v1 saves without rerolling horses, pregnancies, wallet history, capacity, or any RNG stream. Exact retries cannot charge twice. Generic saves cannot advance the calendar or rewrite daily history. Due births and juvenile growth boundaries are visibly blocked until their lifecycle tasks are implemented. Inventory, training/readiness effects, income, adoption, and racing remain later work.

Local acceptance: 165/165 full Release tests, 34/34 parent-run focused Stable tests, one final guard test, warnings-as-errors game build, validator, three actual Metal screen captures, and real mouse cancel/double-click/reload/navigation checks. Independent Sol persistence and Luna UI reviews were completed. Evidence: `docs/stable/evidence/2026-10-10/`. Horse visuals and animation development stayed frozen.

## Current visual state — static adult and foal restored

The game branch now uses the known-good static adult and foal assets from `74f5094bc05681479c15887ca08591793034eeb9`. The Blender sources, GLBs, manifests, importer, presenter, capture runner, contact-sheet builder and visual CI workflow were restored byte-for-byte from that checkpoint. The latest rejected animation experiments remain saved on `archive/horse-animation-experiments-2026-10-10` at `674e7908d706ae0309b5b93f0beb3ab73846fa23`; that branch is preservation-only and is not the active game implementation.

The rejected knee/deformation images and source gait comparison are retained under `docs/visual/evidence/animation_paused_2026-10-10/`. The active horse assets have no animation clips. Existing CI evidence below records the previously verified static checkpoint; restoration verification passed: build 0 warnings/errors, both Godot imports retained 27 bones and zero clips, and 16 Metal captures completed with zero failures. See `docs/visual/evidence/static_restored_2026-10-10/`. Restoration CI at `2e16c95` subsequently passed [visual run 38105574504](https://github.com/MarkJRogers92/Horse/actions/runs/38105574504) and [domain run 38105574529](https://github.com/MarkJRogers92/Horse/actions/runs/38105574529). No animation quality approval, full P1-02 completion, coat-matrix promotion or merge is claimed.

## Static visual checkpoint evidence (74f5094)
The owner selected the CC0 reference horse in `art/reference/Horse_Reference/`. Both adult and foal now use it; the foal has a shorter, shallower body, proportionally long legs, shorter muzzle, smaller hooves and short mane/tail. Editable sources are in `art/derived/Horse_Reference/`, game exports in `game/assets/horse/reference/`. Claude's descriptor presenter, recipes, shaders, scenes and evidence pipeline are preserved. See `docs/visual/VISUAL_STATUS.md` and `REFERENCE_ASSET_WORKFLOW.md` for exact paths and limitations.

Local evidence: warnings-as-errors game build passed; both actual Godot imports retained masks/UV2, normal detail and a 27-bone skinned rig; main-scene smoke passed; Metal captured 131 images with zero capture failures, covering all 13 fixture classes at both ages and three correct unsupported refusals. The editable foal reopened in Blender, passed a bounded FK posing exercise and has six calibrated orthographic reference views. Independent peer review checked the juvenile silhouette, supporting import/code evidence and final CI images.

Remote evidence: [visual run 38099550595](https://github.com/MarkJRogers92/Horse/actions/runs/38099550595) at `f0c5b10` passed build/import/smoke and 131 captures with zero failures; [domain run 38099550598](https://github.com/MarkJRogers92/Horse/actions/runs/38099550598) passed 147/147. Downloaded logs and actual clay/coat/marking/probe images were inspected and compared with the preserved placeholder using the same llvmpipe renderer. See `docs/visual/evidence/reference_f0c5b10/README.md`. No gait clips or adult breed-shape morphs are claimed. No domain/frozen handoff/matrix edits, purchases or other-branch merges.

## Task state
| Task | State |
| --- | --- |
| P0-02a / P0-03 / P1-04 / P1-05 / P1-06 / P1-07 | Complete scoped headless slices, including preserved unified forecasts |
| P1-09 snapshot + codec + atomic store | Verified in final combined suite: 147/147 on macOS and Linux CI |
| P1-09 numeric/rolling-backup regressions | 51f26128 preserved: 138/138 CI; final suite 147/147 |
| Public query API + integration/performance | Complete: a15266e3, 147/147 in CI run 38094410593 and local macOS |
| P2-02 calendar / care / operating-cost prototype | Implemented bounded slice; 165/165 full local tests, 34/34 focused tests, real Godot screen and pointer checks; CI pending |

## Coordination / accepted decisions
Mark leads integration. Concurrent persistence checkpoints c8d4eb93/714e92d9/30bee9ea/51f26128 and unified forecasts fa67666b are preserved. The public query API uses that single codec/store. Use expected-head non-forced pushes and read current STATUS. One canonical horse/RNG/resolver/save system.

D-001 parallel genetics/art accepted; D-002 licensed horse trial accepted (owner chooses/purchases); D-003 Godot 4 .NET. Actions approved. An isolated .NET 8.0.425 SDK under /tmp now also verifies the combined suite on this Mac with two xUnit threads. The table below is inspected Actions evidence. C# 9/netstandard2.1, net8.0 tests. No outside AI transfer, purchase, deployment, main merge, frozen handoff edit or real matrix promotion.

## Next step
1. P1-07 and P1-09 are complete for the authorized headless scope. Do not recreate their records, RNG, forecasts, conception transition, codec or filesystem store. Code commit a15266e3fbe6453f2489f8043fb7d5a1b0d7576d passed validator + 147/147 tests in https://github.com/MarkJRogers92/Horse/actions/runs/38094410593 (job 114337226950).
2. Visual pilot: CI and the placeholder comparison are recorded in the current evidence checkpoint. The authored meshes have static FK rigs; motion clips and body-shape morphs are later work. P1-10 live-stable integration should use docs/genetics/godot_domain_api.md, StableBreedingQueries and AtomicSaveStore after the real visual catalog is approved.
3. Continue other systems after the P2-02 checkpoint: capacity/home management and adoption are useful next bounded packages; birth/growth then needs a safe capacity transition. Keep the real live catalog disabled until its visuals are approved. Horse animations remain paused. Income, inventory, external studs, and racing are later tasks.
4. Current save limits: 32 MiB payload and 4096-entry collections. Wire/schema v2 has an explicit genuine-v1 migration tested with files produced by the previous codec, including committed conception history. Mac/Linux real-file fault injection does not establish process-kill, universal power-loss, network-filesystem, or Windows guarantees.

## Verified evidence
All listed jobs passed the frozen-spec validator and Release dotnet test. Older complete task evidence remains in preceding STATUS commits.
| Checkpoint | Commit | Run / job | Passed | TRX |
| --- | --- | --- | --- | --- |
| P1-06 descriptor export | e54b26c7 | 38091748382 / 114329407287 | 79/79 | 11684063483 |
| P1-07 forecasts | fb328c7e | 38092285736 / 114331006946 | 87/87 | 11684382756 |
| P1-07 closure/conception | 211154d0 | 38092969914 / 114333004779 | 93/93 | 11684154261 |
| P1-09 state | c8d4eb93 | 38093208018 / 114333697222 | 100/100 | 11684792047 |
| Unified forecasts | fa67666b | [38093450520](https://github.com/MarkJRogers92/Horse/actions/runs/38093450520) / 114334415711 | 105/105 | successful test-results artifact |
| Unified forecasts + codec | 714e92d9 | 38093588233 / 114334800799 | 118/118 | 11685535024 |
| Atomic store / headless end-to-end | 30bee9ea | 38094043405 / 114336150637 | 134/134 | 11685635596 |
| Numeric/rolling-backup boundaries | 51f26128 | 38094262761 | 138/138 | successful test-results artifact |
| Public API + final combined proof | a15266e3 | [38094410593](https://github.com/MarkJRogers92/Horse/actions/runs/38094410593) / 114337226950 | 147/147 | 11685351742 |
Latest verified descriptor artifact: 11685376690 (run 38094410593). Initial P1-07 CS0119 was fixed before these green runs; no failed build was called a pass.

## Completion handoff — P1-07/P1-09 implemented scope
- Public-knowledge and hypothetical forecasts remain separate, with exact conditional inheritance ranges and labeled sample uncertainty. Unified coat/variant/trait/pedigree forecast work is preserved.
- All-generation conservative coat closure checks supported recombinants before sampling. No live catalog or art release was enabled.
- One authoritative conception fixes unborn genome, identity, appearance and development, with historical parents/provenance. Saved event position, not UI request IDs, determines the random outcome.
- Atomic disk publication includes foal, pregnancy, debit, capacity hold and stream advance together. Repeating the same committed request returns its existing receipt without another charge or foal.
- Versioned bounded codec round-trips phase, markings, pedigree, knowledge and all immutable evidence. Unknown formats require compatibility; corrupt primary recovery requires acknowledgment of a verified backup.
- Real-file tests passed for partial/flushed/pre/post-publication interruption, first-write failure, lock/revision conflicts, corruption, missing copies, future formats, orphan temporaries and forecast -> commit -> disk -> new instance/reload -> exact retry.
- Final combined regression suite is CI verified at a15266e3 (147/147). Actual macOS file-operation tests additionally pass in the public API continuation. No process-kill/universal power-loss/Windows certification, real art or playable game is claimed.

## Completion handoff — public Godot API and combined proof (complete / CI verified)
- Initial handoff read in full: 461 lines, 11,983 bytes via Library. Initial base 211154d0f8b6a74ae885acb1b0c0bea63d72ee37; integrated base 30bee9ea77cf28d8f6c4f0b925e9b42de0250063, followed by concurrent numeric/backup checkpoint 51f26128befa90e3815e20bcc9255e3b1bc38a0a. Output code: a15266e3fbe6453f2489f8043fb7d5a1b0d7576d on claude/great-euler-hq83x3; this status-only checkpoint records the verified run. Isolated checkout; no unrelated changes lost.
- StableBreedingQueries exposes GetHorse/GetKnownGenetics/PreviewBreeding/CompareHypotheticalPairing/CheckBreedingEligibility/GetPregnancy/GetOffspring/ResolveVisualDescriptor. Public summaries contain no genomes; actual calculations read only revealed calls and public ancestry. Save/load/commit bind to the existing AtomicSaveStore. Shared interface and complete usage example: docs/genetics/godot_domain_api.md.
- Actual final local verification: 147/147 Release xUnit tests on macOS using two threads; no skips. Validator OK. Full real-file interruption/backup/lock/stale-writer suite passed on this Mac as well as preceding Linux CI.
- End-to-end public API proof: knowledge-limited exact palomino forecast -> eligible locked conception -> additional SaveSnapshot -> new store/reload -> identical phased foal, appearance/marking descriptor and both parents -> byte-identical duplicate command, one ledger fee and one breeding-stream advance. Conflicting reuse rejected; unreleased descriptors remain Unsupported. Actual forecasts explicitly reject uninstalled historical algorithm metadata rather than substituting current rules.
- Adversarial checksum-valid payload mutation fuzz and later-client-build preservation passed. Large-stable measurement: 3,000 horses, 6,287,455 bytes, encode/decode plus all-generation closure 0.721 seconds on this Mac in the final recorded local TRX (test budget 45 seconds; not a gameplay frame-rate claim).
- No frozen handoff, accepted engine decision, matrix release flags, visual branch, mesh, material or viewer edits. No force push/main merge, paid service or added runtime dependency.
- Actual final Actions log and artifact inspected: run 38094410593, validator OK, 147/147 tests, no skips; TRX 11685351742. Local TRX: tests/Cloverbrook.Domain.Tests/TestResults/final-integration.trx; downloaded CI TRX is under TestResults/CI-a15266e.
- Remaining: playable/visual P1-10 proof remains with Claude's art track; no headless implementation blocker. Parent-thread checkpoint messaging was unavailable (thread not found); repository checkpoints preserve progress.

## Completion handoff — P1-01 CC0 reference integration (CI verified)
- Input: owner-committed reference package at e35bdc4; isolated checkout on the visual pilot. Original source package is unchanged.
- Output: reproducible Blender importer; authored adult and juvenile GLBs meeting Body/Eyes/Mane/Tail, +X/metre/ground, COLOR_0 and TEXCOORD_1 contracts; editable Blender cages and rigs; normal-detail binding, matte solid-clump hair, calibrated head cameras and honest missing-animation UI/capture handling.
- Face-fix check: Claude's fresh 236f36d CI evidence still misplaced facial white. The legacy SDF mesh had reversed winding/inward normals; corrected geometry was locally re-rendered with forehead stars. New authored-asset captures independently show forehead/strip/snip placement and matching adult/foal marking keys.
- Validation actually run: JSON validator OK; C# build 0 warnings/errors; imported adult+foal contracts passed; main smoke OK; 131 real Metal captures, 0 failures. Visual CI run 38099550595 rebuilt both assets and passed 131 llvmpipe captures, 0 failures; its mesh manifests match local geometry/contract data except build time. Domain CI run 38099550598 passed 147/147. Existing domain implementation/tests were not changed or rerun locally for this asset-only package.
- Final reference proof: six calibrated foal views share one mesh/rest pose and scale; Blender reopened the foal and exercised head, foreleg and tail FK without invalid geometry. This does not establish gait quality. Fresh CI captures show corrected facial placement; independent peer review found no material correction. CI logs contain optional MeshOptimizer and absent audio-device diagnostics despite successful export/smoke/capture.
- Remaining: matrix approval stays with the owner. Both adult family IDs share one cob build per age, only height is applied, and rigs have no animation clips. Motion, breed shapes, live-stable integration and platform exports remain later work. No purchase, force push, main merge or live-catalog approval.

## Completion handoff — P1-02 paused preservation checkpoint
- Owner stopped animation work after rejecting the knees and surface deformation, then explicitly requested saving and pushing the experiments. No animation implementation resumed.
- The experimental Blender/GLB files, source motion samples, extraction/validation tools, playback capture and CI changes are preserved on `archive/horse-animation-experiments-2026-10-10` at `674e7908d706ae0309b5b93f0beb3ab73846fa23`. Rejected/source comparison images remain in this branch under `docs/visual/evidence/animation_paused_2026-10-10/`.
- The active game branch has the static asset implementation restored from `74f5094`. New local validation passed: 11 exact static file matches, clean C# build, both Godot imports, and 16 Metal captures with zero failures; earlier full static CI evidence is recorded above. No animation quality approval, final runtime, or visual acceptance is claimed.
- Animation work stays paused until a new owner request. No handoff/domain/matrix edit or merge into another branch.

## Completion handoff — P1-01 static restoration
- Restored the accepted static adult and foal plus their export/runtime/capture/CI path from 74f5094. Same game catalog paths now resolve the static GLBs; no motion clips are supplied.
- Archived all rejected motion work at 674e790 on archive/horse-animation-experiments-2026-10-10, already saved locally and remotely. No merge or force push.
- Verified 11 exact file matches, warnings-as-errors build, both real Godot imports, and 16 real Metal captures with zero failures. Parent independently reviewed the worker diff and inspected restored renders.
- Animation development remains paused. No domain, handoff or coat-matrix changes.

## Completion handoff — P2-02 stable calendar and daily care slice
- Owner request: continue game systems outside horse visuals. D-004 records the Godot path mapping and the tightly scoped extension to the one authoritative save system.
- Implemented a pure deterministic preview, required basic care, optional comfort care, 28-day seasons, prototype upkeep, and assistance for ordinary-care shortfalls. One locked EndDay command saves the day/care/debit/receipt together and replays exact prior requests before stale checks.
- Added persisted daily state and explicit wire/schema v1→v2 migration. Genuine fixtures preserve original identity/genome, breeding/capacity evidence, and all four RNG streams. Future formats and malformed histories remain refused; generic snapshot writes cannot bypass EndDay.
- Added the sandbox Manage stable screen, review/confirm, saved reload, same-request retry, and explicit verified-backup restoration. A new CI check renders three screen states and checks saved day, wallet, care, receipt, and ledger.
- Validation: core full Release 165/165; parent focused Stable 34/34; final strengthened guard 1/1; game build 0 warnings/errors; import, frozen-spec validator and whitespace check passed. Three actual Metal screenshots and real pointer cancel/double-click/reload/menu-navigation checks passed. Native Sol persistence and independent UI reviews found no remaining implementation blocker after corrections.
- Limits: care has four modest prototype scores, with no training/readiness/race effects. Daily purchased care is consumed without an inventory. Birth and growth boundaries block rather than skip their future transactions. No live-catalog release, horse visual change, animation work, frozen handoff edit, purchase, force push, or other-branch merge.
- GitHub domain and game workflow status will be recorded after this checkpoint is pushed.
