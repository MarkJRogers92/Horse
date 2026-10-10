# Project status / checkpoint

**Last updated:** 2026-10-10 by GPT.
**Phase:** P1-04 verified; P1-05 pedigree/eligibility submitted to CI.
**Branch:** `claude/great-euler-hq83x3`

## Task state
| Task | State |
| --- | --- |
| P0-02a | Complete; 2/2 initial tests |
| P0-03 (narrowed) | Complete; 19/19 tests |
| P1-04 | Complete domain inheritance; 30/30 tests and validator passed |
| P1-05 | Implemented; inspect this commit's CI |
| P1-06 | Next: resolver, descriptor gates and all coat-label fixtures |

## Coordination / decisions
Mark leads integration; Claude/GPT implement and review. GPT is taking this domain sequence. Fetch before edits; expected-head, non-forced pushes after each sub-step. D-001 parallel genetics/art accepted (no released visuals); D-002 licensed horse trial accepted, owner chooses/purchases; D-003 Godot 4 .NET, not Unity. Owner has no desktop; GitHub Actions builds/renders/tests approved. Frozen `handoff/` unchanged. Domain C# 9/netstandard2.1, no engine refs; xUnit net8.0. No purchases, deployment, outside model transfer or main merge.

## Next step
1. Inspect the domain CI logs for this commit. Fix failures before marking P1-05 complete.
2. Implement P1-06 in `Appearance/`, `Traits/`, phenotype tests and `docs/genetics/phenotype_mapping.md`: one resolver, versioned stable descriptors, bounded body/trait values, explicit Unsupported result and live coat gate.
3. Exercise all frozen genetics fixtures (including phenotype assertions), all pedigree cases, plus dun, base+cream composition, determinism and reachability guard tests. No fixture edits.
4. Update STATUS, commit/push and inspect actual CI after each sub-step. No genetics pass approves visuals or releases coats. Actual transactions/saving/knowledge-limited forecasting stay later tasks.

## Verified checkpoints
- P0-02a `ec178ee6` / `6e96338`: Actions 38089219015, job 114322012360, validator OK, 2/2, TRX 11683782133.
- P0-03 foundation `b5481897`: Actions 38089752903, job 114323591679, validator OK, 10/10, TRX 11683906664.
- P0-03 records `1f88e2a1`: Actions 38090145782, job 114324735041, validator OK, 19/19, TRX 11683772363. Scope documented in `docs/contracts/PHASE1_RECORDS.md`.
- P1-04 `e406fbb6d866a8a0af4a3059077caa6c69bd64c5`: https://github.com/MarkJRogers92/Horse/actions/runs/38090747015 , job 114326483624; validator OK, Release build, 30/30 passed, TRX 11684130596. Exact linked fixture, cream/pearl genotype outcomes, 10,000 sampled meioses, parental provenance and deterministic draft bytes verified. See `docs/genetics/inheritance.md`.

## Completion handoff — P1-05 (CI pending)
- Added `Pedigree/PedigreeGraph.cs`, `Breeding/Eligibility/BreedingEligibility.cs`, frozen pedigree tests and eligibility tests.
- Pedigree uses exact phi recurrence, full recorded ancestry, explicit founder assumptions, nonzero founder F, cycle/duplicate/self-parent rejection and unknown rather than fabricated zero. Query-local iterative memoization avoids recursive depth limits.
- Eligibility checks adulthood/breeding age, pregnancy/recovery, reproductive roles, active housing, matching authoritative pedigree, configurable game relationship policy and stale revisions.
- Capacity snapshots require confirmed event-specific holds across pregnancy, foal, youngster and the complete adult timeline, including projected lease returns. Gaps, overbooking and stale reservation revisions fail with readable reasons.
- This is pure validation of supplied snapshots, NOT the future transactional reservation/debit/save service. Relationship cutoff is an explicit game-policy input, not medical advice or a real-world safety threshold.
- Local .NET unavailable; inspect approved Actions for actual test evidence. No JSON specification changed. No visual/player scene exists yet.
