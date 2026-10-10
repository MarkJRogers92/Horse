# Project status / checkpoint

**Last updated:** 2026-10-10 by GPT (domain implementation continuation).
**Phase:** P0-02a validated; narrowed P0-03 next.
**Branch:** `claude/great-euler-hq83x3`

## Task state
| Task | State |
| --- | --- |
| P0-02a | Complete: validator and both skeleton tests passed in Actions run 38089219015 |
| P0-03 (narrowed) | Next: Phase-1 contracts and purpose-labeled RNG |
| P1-04 | Not started |
| P1-05 | Not started |
| P1-06 | Not started |

## Coordination and preserved baseline
- Mark is integration lead. Claude and GPT share implementation and review; fetch the latest branch before editing and push each sub-step. GPT is taking the domain-contract/genetics sequence in this session.
- The review is complete. Do not re-review the spec. `handoff/` remains the frozen, unmodified baseline.
- D-001 is accepted; the existing coat matrix is unchanged. No coat has approved visuals or is released.
- Domain stays pure C# 9 / nullable / netstandard2.1, with xUnit on net8.0. No engine assembly references.
- No Unity/Godot project, purchase, deployment, external model transfer or main-branch merge has been made in this continuation.

## Owner decisions (answered 2026-10-10; see docs/decisions/)
- D-001 parallel genetics and visuals + coat matrix: accepted.
- D-002 licensed rigged base horse: accepted as a trial; owner chooses budget and purchases after a shortlist.
- D-003: Godot 4 .NET/C#, replacing Unity; pin the exact version at P0-02b.
- Owner has no Mac or PC. Builds, renders and tests use approved GitHub Actions.
- CI is approved. `.github/workflows/domain.yml` runs the validator and `dotnet test` on push and uploads TRX evidence.
- Mark leads integration; Claude and GPT coordinate through this file.

## Next step — exact resume point
1. Read `AGENTS.md`, then this file. Fetch the current branch; do not overwrite concurrent work.
2. Implement narrowed P0-03: HorseRecord, VariantCatalogRecord, Phenotype, HorseVisualDescriptor, BreedingCommit, SaveHeader, typed identity and purpose-labeled RNG. Use defensive snapshots, not aliased read-only collections.
3. Run the validator and `dotnet test Cloverbrook.sln --configuration Release`, locally when possible or through the approved domain Actions workflow. Inspect the actual logs; fix failures before claiming completion.
4. Update this file, commit and push after each sub-step.
5. Continue P1-04, P1-05 and P1-06 in order. Exercise every frozen genetics fixture plus review A6 slice tests without modifying `handoff/`.
6. Preserve D-001's explicit Unsupported result and release gate. Passing genetics tests does not approve art or release coats.

## Completion handoff — P0-02a validation checkpoint
- **Status:** complete for the domain scaffold (not a genetics implementation).
- **Code:** original skeleton commit `4a8d2a8865e57703bf21d59063eba2812da8c60c`, retained by Claude's subsequent commits.
- **Verified revision:** `ec178ee6cf7f190d14def20bee2d8257bb59f1da`.
- **Evidence inspected:** https://github.com/MarkJRogers92/Horse/actions/runs/38089219015 ; job `114322012360`, completed successfully.
- **Spec validator:** `python3 tools/validate_handoff.py` printed 40 tasks, 15 coat classes / 15 layers, and `OK`.
- **Test command:** `dotnet test Cloverbrook.sln --configuration Release --logger "trx;LogFileName=results.trx" --logger "console;verbosity=normal"`.
- **Actual result:** restore and Release compilation succeeded; 2 tests passed, 0 failed (frozen fixture loading and engine-dependency boundary). TRX artifact `11683782133` uploaded by CI.
- **Local environment:** no .NET SDK; git clone still fails DNS. Authenticated GitHub reads/writes and approved Actions provide repository access and execution evidence. No local C# pass is claimed.
- **History:** prior source-only checkpoint `d69e5dc` correctly reported zero executed tests; that blocker is now resolved through CI.
- **Player-visible result:** none yet; infrastructure only. Next is narrowed P0-03, followed by genetics.
