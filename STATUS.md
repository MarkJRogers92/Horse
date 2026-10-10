# Project status / checkpoint

**Last updated:** 2026-10-10 by GPT (domain continuation).
**Phase:** P0-03 Phase-1 records submitted to CI.
**Branch:** `claude/great-euler-hq83x3`

## Task state
| Task | State |
| --- | --- |
| P0-02a | Complete; Actions 38089219015, 2/2 tests and validator passed |
| P0-03 (narrowed) | Foundations verified (10/10); six-record subset authored, verify this commit's CI |
| P1-04 | Next after P0-03 verification |
| P1-05 | Not started |
| P1-06 | Not started |

## Coordination / owner decisions
Mark leads integration. Claude and GPT implement/review and fetch before editing; GPT is taking the domain sequence. Push each sub-step with STATUS. D-001 parallel genetics/visuals and matrix accepted; D-002 licensed horse trial accepted (owner sets budget/buys after shortlist); D-003 selects Godot 4 .NET, not Unity. Owner has no Mac/PC; GitHub Actions builds, renders and tests are approved. `handoff/` stays frozen. Domain: C# 9, nullable, netstandard2.1, net8.0 xUnit. Path mapping: `docs/contracts/PHASE1_DOMAIN.md`. No purchase, deployment, external-model transfer or default-branch merge.

## Next step
1. Inspect this commit's domain CI. Fix build/tests before calling P0-03 complete.
2. P1-04: one phased meiosis service, explicit versioned allele/linkage validation, exact distributions and seeded sampling. Do not use independent rolls for linked markers.
3. P1-05: complete pedigree recurrence, unknown ancestry, cycles, eligibility and capacity reservations.
4. P1-06: pure resolver -> descriptor with D-001 explicit Unsupported gates. Exercise all frozen genetics/pedigree cases plus review A6 tests outside `handoff/`.
5. Update STATUS, commit, push and inspect CI after every sub-step. No genetics pass authorizes visual approval or live release.

## Evidence / completion handoffs
### P0-02a complete
- Skeleton `4a8d2a8` retained at revision `ec178ee6cf7f190d14def20bee2d8257bb59f1da`.
- Actions https://github.com/MarkJRogers92/Horse/actions/runs/38089219015 , job 114322012360: validator OK; Release build; 2/2 tests. TRX 11683782133. Checkpoint `6e96338`.
### P0-03 foundations complete
- Commit `b5481897284e805f19ff1bcafaf0b070ceb19890`.
- Actions https://github.com/MarkJRogers92/Horse/actions/runs/38089752903 , job 114323591679: validator OK; Release build; 10/10 tests; TRX 11683906664.
- Typed identity, exact fractions, defensive snapshots, canonical hashing, versioned/purpose-isolated RNG, independent golden vectors, restore/overflow/culture tests.
### P0-03 record subset (CI pending)
- Added `Contracts/Genome.cs`, `HorseRecord.cs`, `VariantCatalogRecord.cs`, `AppearanceRecords.cs`, `BreedingAndSave.cs`, nine record-contract tests and `docs/contracts/PHASE1_RECORDS.md`.
- Records are immutable defensive snapshots; one phased genome, one horse identity, separate player knowledge and training, renderer-only descriptors, structurally checked conception result, version-retaining save header.
- No transaction execution, atomic save, forecast UI or renderer is claimed. Management-only fields and race/lease contracts remain deferred.
- Local .NET is absent; no local test pass claimed. C# validation is this revision's approved CI. No JSON specification changed; frozen fixture hashes remain unchanged.
