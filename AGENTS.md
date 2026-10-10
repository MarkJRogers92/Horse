# Instructions for any AI model working on this repository

This file is canonical for Claude, GPT/Codex and any other agent. `CLAUDE.md` points here.

## Read first, in this order (stop when you have enough context)
1. `STATUS.md` — where the project is right now and the exact next step. **Always read this file.**
2. `docs/REVIEW_AND_PROPOSALS.md` — accepted and pending changes to the original plan.
3. `docs/decisions/` — decision records. These override `handoff/` wherever they conflict.
4. `handoff/START_HERE.txt` → `handoff/DECISIONS_AND_CONSTRAINTS.txt` — the original spec.
5. Only when your task needs them: `handoff/CLOVERBROOK_WORKFLOW.txt` (full design, about 90 KB), `handoff/TASK_BOARD.json`, `handoff/CONTRACT_CATALOG.json`, `handoff/GENETICS_FIXTURES.json`, `docs/contracts/COAT_SUPPORT_MATRIX.json`.

Save tokens: read sections with `grep -n '^#' handoff/CLOVERBROOK_WORKFLOW.txt` and line ranges, not the whole file.

## Rules
- **Checkpoint constantly.** After every completed sub-step: update `STATUS.md`, commit, and push. Work may move to a different model at any moment, and uncommitted work is lost.
- Do not edit files in `handoff/`. That is the frozen v1 baseline. Propose changes through a new `docs/decisions/D-NNN-*.md` record (format in `handoff/DECISIONS_AND_CONSTRAINTS.txt`).
- One task from `handoff/TASK_BOARD.json` at a time. Stay inside its `allowed_paths`. Finish with `handoff/COMPLETION_HANDOFF_TEMPLATE.txt` content appended to `STATUS.md` (short form is fine).
- Shared contracts (`docs/contracts/`, domain types) have one owner. Do not invent a second horse record, RNG or resolver.
- The domain code is pure C# (`netstandard2.1`, C# 9 max, no UnityEngine references), so it is testable with `dotnet test`.
- Never claim a test passed unless you ran it. Never send project content to outside services, buy anything, publish or deploy without explicit owner instruction.
- Run `python3 tools/validate_handoff.py` before committing changes to JSON specs.

## Commit message style
Imperative summary line, then a short body naming the task ID (for example `P1-04`).
