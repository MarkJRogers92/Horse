# Cloverbrook Stables

A cozy 3D horse breeding and stable management game for PC and Mac (Unity 6 LTS, URP, C#, Blender). It has believable horses, honest coat genetics and attachment-first stable life.

> **Status:** design and production spec only, with no game code yet. See [`STATUS.md`](STATUS.md).

## Where things are
| Path | What |
| --- | --- |
| [`STATUS.md`](STATUS.md) | Live checkpoint: what's done, what's blocked, the exact next step |
| [`AGENTS.md`](AGENTS.md) | Working rules for any AI model (Claude, GPT/Codex, others) |
| [`docs/REVIEW_AND_PROPOSALS.md`](docs/REVIEW_AND_PROPOSALS.md) | Review of the plan, proposed changes, owner decisions, fastest path |
| [`docs/decisions/`](docs/decisions/) | Decision records (these override the baseline) |
| [`docs/contracts/COAT_SUPPORT_MATRIX.json`](docs/contracts/COAT_SUPPORT_MATRIX.json) | Which coats exist, with genetics and visual status |
| [`handoff/`](handoff/) | Frozen v1 spec: workflow, 40-task board, contracts, fixtures, templates |
| [`tools/validate_handoff.py`](tools/validate_handoff.py) | Spec validator (`python3 tools/validate_handoff.py`) |

## Quick start for a new contributor or model
1. Read `STATUS.md`, then `AGENTS.md`.
2. Run `python3 tools/validate_handoff.py`. It should print `OK`.
3. Pick up the "Next step" in `STATUS.md`. Commit and push after each sub-step.
