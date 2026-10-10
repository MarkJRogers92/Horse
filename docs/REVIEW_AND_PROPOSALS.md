# Review of the Cloverbrook Stables handoff (v1) and proposed changes

Reviewed on 2026-10-10. Scope: every file in `handoff/` (the workflow, the task board with 40 tasks, the contract catalog with 9 contracts, the fixtures, and the templates). The original files are committed unmodified as the baseline. Nothing in them has been edited. This document lists the proposed changes. Each one should become a decision record in `docs/decisions/` once the owner accepts it.

## Verdict

The design is strong. It is unusually careful about genetics honesty, save integrity, RNG separation and attachment, and the fixtures use exact rational probabilities. The weak points are execution, not design:

1. Nothing yet ties the size of the genetics catalog to what the renderer can draw. **D-001 fixes this.**
2. Nobody is assigned to *source the hero horse asset*, even though that asset is the real critical path.
3. The toolchain plan doesn't account for AI models being unable to run the Unity editor. Work that needs the editor has to be separated from work that doesn't.
4. Phase 0 is fully serial, so no code can start until five tasks are done.
5. v1 scope is very large for a small team (2 manual sports, full market, 10 coat families, building).

## A. Proposals (highest value first)

### A1. Accept D-001: parallel tracks gated by the coat support matrix
See `docs/decisions/D-001-parallel-genetics-and-visuals.md`. It adds `docs/contracts/COAT_SUPPORT_MATRIX.json`, a reachability test, an `Unsupported` result from the resolver, and the contact-sheet review.

### A2. Make an owner decision on sourcing the hero horse now (the biggest blocker)
AI coding models cannot produce a production-quality, rigged, morph-compatible horse mesh. P1-01 has a role ("horse_artist") but nobody filling it. Options:

| Option | Speed | Cost | Risk |
| --- | --- | --- | --- |
| **License a rigged horse base mesh** (Unity Asset Store, CGTrader, etc.), then customise the masks and UVs in Blender | Fastest (days) | A purchase | The license has to allow modification and commercial use, and the topology has to support morphs. Check both before buying. |
| Commission a freelance 3D horse artist | Weeks | Highest | Needs a clear brief. The asset contract in workflow §10 is that brief. |
| The owner builds it in Blender | Slowest unless the owner is experienced | Time only | Quality risk |

**Recommendation:** license a base mesh for the Phase 1 proof (it can be replaced later), as long as the license is suitable. The handoff forbids purchases without explicit instruction from the owner, so **this needs your call.**

### A3. Split the toolchain so AI models can do most of the work
- **Domain (genetics, pedigree, commit, save, economy rules):** a pure C# library that **targets `netstandard2.1`** with an **xUnit test project on .NET 8**. Any model or CI runner can run `dotnet test` with no Unity license and no GUI. This covers most of Phases 1–3.
- Unity uses the same source through an `asmdef` with `noEngineReferences: true`, placed under `unity/Packages/com.cloverbrook.domain/` (or the domain is built as a DLL into `Plugins/`).
- **Unity compatibility constraints, to fix in P0-03:** C# 9 maximum. No file-scoped namespaces and no `required` members. `record` needs an `IsExternalInit` shim. `System.Collections.Immutable` is **not** in Unity by default, and the contract catalog uses `ImmutableArray` and `ImmutableDictionary`. Either vendor that DLL or use `IReadOnlyList`/`IReadOnlyDictionary` with defensive copies. Recommendation: the read-only interfaces, which avoid the extra dependency.
- **Unity editor work** (meshes, shaders, scenes, PC/Mac builds) happens on the owner's machine, or in CI using GameCI with a Unity license secret. **A Mac build needs Mac hardware or a macOS CI runner.** P0-05 needs an owner decision on which.

### A4. Restructure Phase 0 so coding starts on day 1
| Change | Why |
| --- | --- |
| Split **P0-02** into **P0-02a: .NET domain solution plus CI** (no dependency, start now) and **P0-02b: pin Unity and Blender** | Genetics no longer waits on Unity installation |
| Narrow **P0-03** to the Phase-1 contracts (HorseRecord, VariantCatalogRecord, Phenotype, HorseVisualDescriptor, BreedingCommit, SaveHeader). Move RaceEntrySnapshot to P2-05 and InstitutionalLease to P3-01. | Avoids designing systems ahead of when they are built |
| Add **P0-06: Coat support matrix phase-1 lock** (schema_owner + technical_art_lead) | Required by D-001 |
| **P1-03** (coat shader) can start on a placeholder mesh that uses the agreed mask regions. Keep P1-01 as a dependency for *acceptance* only, not for starting. | Removes a serial wait on the critical path |
| Merge **P0-01**'s ownership decisions into a single "Owner decisions" checklist (below) | The owner answers once instead of piecemeal |

### A5. Make model handoff structural, not optional
Added in this commit: `AGENTS.md` (read automatically by GPT/Codex and most agents), `CLAUDE.md` (which points to AGENTS.md), and `STATUS.md` (the live checkpoint). The rule: **commit and push after every completed sub-step and update STATUS.md.** A new model then reads AGENTS.md, then STATUS.md, then continues. There is nothing to re-derive.

### A6. Add fixtures the Phase-1 slice needs but doesn't have
`GENETICS_FIXTURES.json` covers base epistasis, cream, cream/pearl, silver masking, PATN1/LP and linkage. Missing for the slice:
- **Dun:** `D/nd2 × D/nd2` gives 3/4 dun and 1/4 non-dun (nd2/nd2). Also `nd1` shows primitive markings without dilution.
- **Base + cream two-locus cross:** for example, buckskin `E/e A/a N/Cr` × chestnut `e/e a/a N/N`. This tests that independent loci compose correctly through the resolver.
- **Commit determinism:** the same event seed and the same parents produce a byte-identical offspring genome and appearance seed. Opening a forecast in between changes nothing.
- **Reachability:** the phase-1 founder set reaches only `released` matrix classes.

### A7. Protect v1 from scope collapse
- Treat the **Phase 2 stable-session slice as a shippable product checkpoint** (a demo or early-access candidate). If budget runs out, there is still something real.
- Gate every v1 coat family through the matrix. If a family doesn't reach `approved` visuals, it moves to post-v1. The workflow already says this. The matrix now enforces it.
- Keep the second sport (barrels) behind the Phase 4 gate, as planned. Don't start it early.

### A8. Small corrections and clarifications
- `handoff/CLOVERBROOK_WORKFLOW.txt` is Markdown. Make it canonical and treat the `.docx` as an export, so there is one editable source.
- Pacing check: 4-day pregnancy, foal days 4–7, youngster 7–10, adult day 10, breedable day 12, in a 28-day season, which allows about 2 generations per season. That is consistent with the spec. Mark it as a tuning value for playtests.
- Define the **reference hardware** early, even provisionally. The 60 fps budget means nothing without it.
- `PACKAGE_MANIFEST.json` hashes now protect the baseline. `tools/validate_handoff.py` reports any drift so spec edits stay deliberate.

## B. Owner decisions needed (answer once, unblocks everything)

1. **Accept D-001** (parallel tracks plus the coat matrix)? *Recommended: yes.*
2. **Hero horse source:** license a base mesh / commission an artist / build it yourself? Budget limit?
3. **Engine:** confirm Unity 6 LTS + URP (the default), or Godot?
4. **Your hardware:** do you have a Mac for Mac builds and Unity editor work? A Windows PC?
5. **CI:** may we use GitHub Actions (free tier) for `dotnet test` now, and GameCI later (needs a Unity license secret)?
6. **Ownership roles:** who is integration lead (you?), and which model takes which track?

## C. Fastest path to the Phase 1 proof

| Step | Who | Needs Unity? | Output |
| --- | --- | --- | --- |
| 1 | Owner | – | Answers to section B |
| 2 | Any model | No | P0-02a: `src/Cloverbrook.Domain` (netstandard2.1) + `tests/` (net8, xUnit) + GitHub Actions `dotnet test` |
| 3 | Any model | No | P0-03 (narrowed): Phase-1 contract types, seeded RNG with purpose labels, SaveHeader |
| 4 | Any model | No | P1-04/05/06: phased genome, meiosis, pedigree/kinship, resolver → descriptor. All existing plus A6 fixtures green |
| 5 | Owner + artist | Yes | P0-02b, P1-01: pinned Unity project, hero mesh with mask regions, inspection scene |
| 6 | Model + owner | Yes | P1-03: layer shader, contact sheet of the 13 phase-1 classes from hand-written descriptors |
| 7 | Any model | No | P1-07, P1-09: forecasts, commit, atomic save/load, reachability test |
| 8 | Owner + model | Yes | P1-08, P1-10: foal, then the integrated proof (mating → visible foal → save/reload) |

Steps 2–4 and 7 can start **today** and run alongside 5–6.
