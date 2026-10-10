# Project status / checkpoint

**Last updated:** 2026-10-10 by Claude (recorded owner decisions, added CI). The previous update was by GPT (domain scaffold).
**Phase:** P0-02a scaffold pushed; .NET validation blocked.
**Branch:** `claude/great-euler-hq83x3`

## Task state
| Task | State |
| --- | --- |
| P0-02a | Source scaffold authored and pushed in `4a8d2a8865e57703bf21d59063eba2812da8c60c`; restore/build/test NOT verified |
| P0-03 (narrowed) | Not started; resume after the scaffold passes `dotnet test` |
| P1-04 | Not started |
| P1-05 | Not started |
| P1-06 | Not started |

## Preserved baseline
- Original handoff package remains unchanged in `handoff/`.
- The review is finished; do not re-review it.
- Existing D-001 proposal and coat support matrix are unchanged. No class was promoted or released.
- `AGENTS.md` / `CLAUDE.md` and the existing validator are unchanged.
- No Unity project, CI workflow, purchase, deployment, external model transfer or merge was made.

## Owner decisions (all answered 2026-10-10; see docs/decisions/)
- **Integration lead:** the owner (Mark). Claude and GPT share the implementation and review each other's work. Coordinate through this file. Pull before you start and push after every sub-step, because both models push to this branch.
- D-001 parallel genetics and visuals + coat matrix: **accepted**.
- D-002 licensed rigged base horse: **accepted (trial)**. Owner buys after seeing a shortlist and sets the budget.
- D-003 engine: **Godot 4 (.NET)**, revised from Unity because the owner has no Mac or PC. See `docs/decisions/D-003-engine-godot.md`.
- Hardware: the owner has **no Mac or PC**. All builds, renders and tests run in **GitHub Actions** (approved).
- CI: **GitHub Actions approved.** `.github/workflows/domain.yml` runs `dotnet test` on every push.

## Next step — exact resume point
1. Use an environment with the .NET 8 SDK, **or** push and read the `domain` GitHub Actions run (that run is the accepted evidence).
2. Read `AGENTS.md`, then this file. Keep working on this branch; do not re-review the spec.
3. Run `dotnet test Cloverbrook.sln --configuration Release` from the repository root.
   Fix any restore/build/test failures. Run `python3 tools/validate_handoff.py`.
4. Record the actual output, update this file, commit and push the P0-02a validation checkpoint.
5. Implement narrowed P0-03 (HorseRecord, VariantCatalogRecord, Phenotype,
   HorseVisualDescriptor, BreedingCommit, SaveHeader, typed identity and purpose-labeled RNG).
   Use C# 9 / netstandard2.1 and defensive snapshots, not aliased read-only collections.
6. Then implement P1-04, P1-05 and P1-06 in order. Run the frozen fixtures through xUnit,
   adding the slice cases identified in review A6 without modifying `handoff/`.
7. Update this file, commit and push after every sub-step. Do not call a task complete
   or promote matrix genetics status until its actual tests pass.

CI is approved: the domain workflow runs `dotnet test` on push. Use its result as the validation evidence when the local environment has no .NET.
The remaining work needs implementation and execution, not another design review.

## Completion handoff — P0-02a source checkpoint
- **Status:** partial; source authored, execution blocked before restore/compilation.
- **Base:** `806334250a7df14cb17bc7c68af930b5fde5e961`.
- **Source commit:** `4a8d2a8865e57703bf21d59063eba2812da8c60c`, pushed to this branch.
- **Files:** `Cloverbrook.sln`, `.gitignore`,
  `src/Cloverbrook.Domain/Cloverbrook.Domain.csproj`, `DomainAssembly.cs`,
  `tests/Cloverbrook.Domain.Tests/Cloverbrook.Domain.Tests.csproj`,
  `FixtureFiles.cs`, `FixtureLoadingTests.cs`, `docs/setup/DOMAIN.md`, `STATUS.md`.
- **Result:** pure nullable netstandard2.1/C# 9 library and a net8.0 xUnit harness.
  MSBuild copies the actual fixture JSON and coat matrix into the test output;
  tests load them relative to AppContext.BaseDirectory rather than the working directory.
  Two tests are authored: fixture-file loading/structure and engine-dependency exclusion.
  They are NOT the future genetics acceptance suite.
- **Checks actually run:** both project XML files parsed successfully in Python.
  Local copies of TASK_BOARD.json, CONTRACT_CATALOG.json, GENETICS_FIXTURES.json and
  CLOVERBROOK_WORKFLOW.txt have Git blob hashes identical to the inspected remote tree.
- **Command attempted:** `dotnet test Cloverbrook.sln --configuration Release`.
- **Observed result:** exit 127, `dotnet: command not found`; zero tests executed.
  No SDK was found. Local Git cloning failed with DNS resolution errors, and an
  official dotnet installer download also failed. This is not a C# test failure or pass.
- **Not run:** C# compilation, NuGet restore, xUnit, or the repository Python validator.
  No JSON specification was changed in this checkpoint.
- **Player-visible result:** none yet; this is source infrastructure only.
- **Checkpoint:** source and this status are committed via the authenticated GitHub API.
  No background build, remote worker or pending process is running. No downstream
  contracts or genetics implementation exists yet. Resume with step 1 above.
