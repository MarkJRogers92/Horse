# Project status / checkpoint

**Last updated:** 2026-10-10 by GPT.
**Phase:** P0-02a/P0-03/P1-04/P1-05/P1-06 verified; continuing P1-07, then P1-09.
**Branch:** `claude/great-euler-hq83x3`

## Task state
| Task | State |
| --- | --- |
| P0-02a | Complete; solution and CI execution verified |
| P0-03 (narrowed) | Complete; immutable Phase-1 contract subset |
| P1-04 | Complete; exact phased inheritance and seeded provenance |
| P1-05 | Complete; pedigree and eligibility/capacity validation |
| P1-06 | Complete headless slice; 79/79 tests, validator and descriptor export verified |
| P1-07 | In progress: knowledge-limited forecasts, closure guard and conception API |
| P1-09 | Next: production atomic persistence and replay-safe transactions |

## Coordination and accepted decisions
Mark is integration lead. Claude and GPT implement/review; fetch current head before editing and use non-forced expected-head pushes after every sub-step. GPT owns this domain continuation. D-001 parallel genetics/art + matrix accepted; D-002 licensed horse trial accepted (owner chooses/buys after budget/shortlist); D-003 chooses Godot 4 .NET, not Unity. Owner has no desktop; approved GitHub Actions provides testing/builds/renders. `handoff/` stays frozen. C# 9, netstandard2.1, no engine assemblies; net8.0 xUnit. No purchases, deployments, external AI transfers or main-branch merges.

## Next step
1. Implement P1-07 forecasts from the production meiosis/resolver, with a knowledge-only boundary, explicit actual-vs-hypothetical authority and bounded exact/sampled metadata. No hidden genotype may leak through a forecast.
2. Complete the multi-generation founder/NPC catalog closure guard; fail closed if exhaustive validation is unavailable. Do NOT enable a live catalog or promote visual/release statuses.
3. Add the minimal conception command against the existing records and services. P1-09 owns durable atomic save/load and idempotent transaction persistence; later P2 tasks own marketplace, calendar and complete capacity management.
4. Commit/push each tested sub-step and inspect that revision's Actions result. Continue P1-09 only against the resulting shared contracts.
5. Art proceeds separately using the exported descriptors and `tests/Cloverbrook.Domain.Tests/Phenotype/README.md`. No real mesh or game scene exists yet.

## Verified CI checkpoints
Each run executed `python3 tools/validate_handoff.py` (OK) and Release `dotnet test` with TRX output.
| Task checkpoint | Commit | Run / job | Actual passed | TRX artifact |
| --- | --- | --- | --- | --- |
| P0-02a | `ec178ee6` / `6e96338` | 38089219015 / 114322012360 | 2/2 | 11683782133 |
| P0-03 values/RNG | `b5481897` | 38089752903 / 114323591679 | 10/10 | 11683906664 |
| P0-03 records | `1f88e2a1` | 38090145782 / 114324735041 | 19/19 | 11683772363 |
| P1-04 meiosis | `e406fbb6` | 38090747015 / 114326483624 | 30/30 | 11684130596 |
| P1-05 pedigree/eligibility | `f81b8862` | 38091011084 / 114327270107 | 49/49 | 11683398802 |
| P1-06 resolver/fixtures | `b125167a` | 38091474449 / 114328620063 | 65/65 | 11684750668 |
| P1-06 descriptor export | `e54b26c7` | 38091748382 / 114329407287 | 79/79 | 11684063483 |
Latest verified source run: https://github.com/MarkJRogers92/Horse/actions/runs/38091748382
Descriptor artifact: `visual-descriptor-fixtures` / 11683549483 (26 adult/foal specimens). Both artifact uploads and all tests were verified in the actual job log.

## Completion handoff — implemented domain slice
- One typed immutable horse/genome/phenotype/visual/commit/header contract family with defensive snapshots and versioned, purpose-isolated random streams. No independent duplicate horse/RNG implementation.
- One phase-aware meiosis service: exact rational distributions, seeded samples, parental homolog/crossover provenance, shared cream/pearl state, explicit unsupported haplotypes and 32 fictional markers.
- Every concrete frozen fixture is executed: six genetics cases, six pedigree coefficients and three pedigree validation cases. Extra tests cover dun/nd1, combined base+cream, a 128-generation pedigree, 10,000 sampled meioses, preview isolation and bounds.
- One genotype-to-phenotype-to-descriptor pipeline. Permanent marking identity persists across age/cosmetics/asset versions. Missing visual support returns Unsupported; no silent color substitution.
- `ConceptionDraft` is NOT a persisted/debited pregnancy. Capacity checks validate supplied confirmed snapshots, not an implemented capacity manager. Atomic saving, marketplace, knowledge-projected forecasts and cross-system proposal strings remain later work.
- The 13 adult/foal descriptor reference tests and generated JSON export cover 26 specimens. Metadata forbids live-stock use and states no render has been produced. No baseline JSON or matrix status changed.
- Local SDK/network access is unavailable in this continuation. Approved GitHub Actions is the execution authority; source-only work is not reported as passing tests.
- No production horse mesh, renderer, playable scene, live catalog or save migration has been built.
