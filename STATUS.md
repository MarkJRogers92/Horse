# Project status

**Last updated:** 2026-10-12 by Claude (club support income, D-008).
**Working branch:** `claude/relaxed-ride-9c5tto`. It contains every other active branch: `codex/horse-production-library`, which itself contains `claude/godot-horse-visual-pilot` and `claude/great-euler-hq83x3`, plus a merge of `rea/horse-coat-preview`. The animation archive (`archive/horse-animation-experiments-2026-10-10`) is intentionally not merged.
**History:** the full checkpoint log up to 2026-10-12 is in [`docs/history/STATUS_LOG_to_2026-10-12.md`](docs/history/STATUS_LOG_to_2026-10-12.md). Search it with `grep -n '^#'`. Keep this page short: replace sections instead of stacking new ones on top.

## What the game does today (Godot 4.7.2 .NET, `game/`)
- **Manage stable** is a saved stable with daily care, running costs and **50 coins/day of club support income** (D-004, D-008). The player can breed their own horses for a 50-coin fee (D-006), then the foal is born, named and grows up (D-005). One atomic save, with exact-retry safety.
- **Foaling demo** is a separate sandbox save that shows a committed pregnancy through to birth and growth.
- **Horse viewer** shows Light and Stock adults and the foal with coats and markings, plus a neutral Walk and Idle (D-007, Meshy-based library). A macOS export exists; it still has the earlier animation.
- **Domain (`src/`, pure C#)** covers phased coat genetics, pedigree, eligibility, forecasts (including the gray base-coat class), conception, the save codec and the atomic store.

## Task board position (live per-task status: [`docs/TASK_STATUS.json`](docs/TASK_STATUS.json); the frozen board still says "proposed")
| Done (scoped slices) | Partly done | Not started |
| --- | --- | --- |
| P0-02a, P0-03, P1-04–P1-07, P1-09, P2-02, P2-06 (owned parents only), P2-07 | P1-01/02/03/08 (horse art, rig, coats, foal: D-007), P1-10, **P2-03 (income only)** | P2-01 roster, P2-04 estate, P2-05 activities, P2-08 tutorial, P2-09, P3+ |

## Next steps (owner's chosen order, 2026-10-12)
1. **Done:** club support income (D-008).
2. **`main` branch:** create `main` from this branch once CI is green, then branch new work from it. Owner-approved on 2026-10-12. The repository's default-branch setting on GitHub must be changed by the owner.
3. Rest of P2-03: NPC offers to buy horses, with valuation from traits, coat and pedigree. Optional player-run visitor activities as a second income source.
4. P2-01 roster and inspection screen; P2-05 a first simple activity; P2-08 tutorial.
5. Housekeeping: keep large evidence out of git (the `.git` pack is about 280 MB, mostly `docs/visual/evidence`). Split `game/scripts/Stable/StableDay.cs` before adding more panels.

## Rules that still hold
D-001 to D-008 are accepted. No frozen `handoff/` edits, no real coat-matrix promotion, no purchases or deployments without owner instruction. One canonical horse record, RNG, resolver and save. Run `python3 tools/validate_handoff.py` and `dotnet test Cloverbrook.sln -c Release` before pushing. Ubuntu's `dotnet-sdk-8.0` apt package works in cloud sessions where Microsoft's download host is blocked.

## Latest verification
| Commit | What | Result |
| --- | --- | --- |
| `b278537` | Merge of REA gray forecast into the production line | Domain CI [38309981689](https://github.com/MarkJRogers92/Horse/actions/runs/38309981689) passed |
| this commit | D-008 club support income | Local Ubuntu .NET 8.0.131: 201/201 Release tests, zero warnings; game `-warnaserror` build OK; validator OK. Domain and visual CI pending on push. |

## Completion handoff — P2-03 income slice (D-008)
- Policy `stable.dailycare.v2` credits 50 coins per completed day for a nonempty active stable, inside the same End Day transaction. Today's income pays today's upkeep before assistance.
- The ledger kind is `stable-income`, with a deterministic ID per End Day operation. Snapshot validation rejects a missing, altered or orphan credit. Saved v1 receipts are unchanged and carry no income. No save-format change.
- The review screen shows "Club support: +50 coins". In-game capture checks were updated: starter day 500 → 514; breeding capture after ten days 54 → 554.
- Not done: NPC offers, valuation, visitor activities, cash forecast beyond one day.
