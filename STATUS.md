# Project status / checkpoint

**Last updated:** 2026-10-10 by Claude (review session)
**Phase:** Pre-Phase 0. The spec has been reviewed. Waiting on owner decisions.
**Branch:** `claude/great-euler-hq83x3`

## Done
- Original handoff package committed unmodified in `handoff/` (with the .docx).
- Review and proposals: `docs/REVIEW_AND_PROPOSALS.md`.
- D-001 (parallel genetics and visuals with a coat support matrix): `docs/decisions/D-001-parallel-genetics-and-visuals.md`. **Proposed.**
- `docs/contracts/COAT_SUPPORT_MATRIX.json`: 15 render layers and 15 coat classes (13 in Phase 1).
- `tools/validate_handoff.py`: checks manifest hashes, the task DAG, JSON parsing and matrix rules. Currently **passes**.
- `AGENTS.md` / `CLAUDE.md`: shared instructions for any model.

## Blocked on owner (see section B of docs/REVIEW_AND_PROPOSALS.md)
1. Accept D-001?
2. Hero horse source (license, commission or build) and budget.
3. Confirm Unity 6 LTS + URP.
4. Available hardware (Mac? Windows?).
5. Is GitHub Actions CI allowed?
6. Role assignments.

## Next step (can start before the answers, needs no Unity)
**P0-02a:** create `src/Cloverbrook.Domain/` (netstandard2.1, C# 9, nullable on) and `tests/Cloverbrook.Domain.Tests/` (net8.0, xUnit) plus `Cloverbrook.sln`. Add one test that loads `handoff/GENETICS_FIXTURES.json`. Add `.github/workflows/domain.yml` to run `dotnet test` (only if CI is approved). Then do the narrowed P0-03 contracts.

## Checkpoint if interrupted
No uncommitted work. The validator passes.
