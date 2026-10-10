# Project status / checkpoint

**Last updated:** 2026-10-10 by GPT (persistence continuation).
**Phase:** P1-09 bounded codec integrated over concurrent unified-forecast work; inspect CI.
**Branch:** `claude/great-euler-hq83x3`

## Task state
| Task | State |
| --- | --- |
| P0-02a / P0-03 / P1-04 / P1-05 / P1-06 / P1-07 core | Complete scoped headless slices |
| P1-07 unified output | fa67666b preserved; other continuation records 105/105 local tests; inspect combined CI |
| P1-09 stable state | Verified: 100/100 tests and validator in run 38093208018 |
| P1-09 codec | Implemented in this checkpoint; CI pending |
| P1-09 atomic filesystem adapter | In progress in the persistence continuation; do not create a second store/codec |

## Coordination / decisions
Mark leads integration. GPT owns the domain continuation; two concurrent continuations were observed, so preserve each other's commits and use expected-head non-forced pushes. This persistence workstream owns src/Cloverbrook.Domain/Persistence and persistence tests while completing the atomic store. The unified-forecast continuation owns its forecast additions/review. Shared records/RNG remain single-source.

D-001 accepted; D-002 licensed horse trial accepted (owner chooses/purchases); D-003 Godot 4 .NET. Actions execution approved. The forecast continuation reports an isolated local SDK; this persistence environment still has no SDK/network and uses actual Actions logs. C# 9/netstandard2.1, net8.0 tests. No outside AI transfer, purchase, deployment, main merge, frozen handoff edit or matrix promotion.

## Next step
1. Inspect this combined revision's Actions log and fix failures. Unpublished codec commit 32693d8a was not forced over concurrent fa67666b; its three new files were reapplied onto fa67666b with this reconciled STATUS.
2. Persistence continuation: finish a real same-directory atomic filesystem adapter, validated backup, exclusive writer lock, optimistic revision checks and durable conception command.
3. Test interruptions before/after publication, retry after restart, corrupt/future files, explicit acknowledged backup recovery and conflicting/stale requests.
4. No live catalog or actual horse visual is enabled. Descriptor exports are references, not renders. External stud authorization, full capacity/calendar management and game UI remain later integrations.

## Verified CI evidence
All listed checkpoints passed validator and Release dotnet test. Older complete evidence is retained in previous STATUS commits.
| Checkpoint | Commit | Run / job | Passed | TRX |
| --- | --- | --- | --- | --- |
| P1-06 export | e54b26c7 | 38091748382 / 114329407287 | 79/79 | 11684063483 |
| P1-07 forecasts | fb328c7e | 38092285736 / 114331006946 | 87/87 | 11684382756 |
| P1-07 closure/conception | 211154d0 | 38092969914 / 114333004779 | 93/93 | 11684154261 |
| P1-09 stable state | c8d4eb93 | 38093208018 / 114333697222 | 100/100 | 11684792047 |
P1-07 compile collision in 3090d47a was fixed before these checkpoints. Latest verified descriptor artifact: 11685106073.

## Completion handoff — preserved unified forecasts
fa67666b adds BreedingForecastService, inherited-call ranges, body/athletic/temperament ranges, exact hypothetical linked-trait marginals and public-pedigree relatedness. Actual inputs remain public knowledge; hypothetical results never authorize conception. docs/genetics/forecast_contract.md and its new sources/tests are preserved unchanged. The other continuation recorded 105 integrated local tests; combined Actions evidence must still be inspected here.

## Completion handoff — P1-09 codec (CI pending)
- One explicit bounded binary codec for existing canonical state: horse data, phased alleles, knowledge, appearance/development, historical parents/unborn, pregnancy/ledger/holds and all stream counters.
- Frame/payload and inner-record checksums plus domain invariants reject corruption. Loading samples no RNG and never replaces a resolver or invents object types.
- Explicit forward migration registry. Default rejects unknown older/future formats. Synthetic TEST-ONLY envelope tests routing/history without claiming nonexistent legacy-release compatibility.
- Round-trip/idempotence, phase/Unicode/knowledge/training, historical-parent, culture, malformed-length, corruption/truncation/future-version and migration tests added.
- Filesystem publication, process interruption and recovery tests remain next; the codec itself is not durability.
