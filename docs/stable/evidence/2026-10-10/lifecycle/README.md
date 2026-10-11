# Saved birth and growth acceptance — 2026-10-10

The owner requested births and foal growth on `claude/godot-horse-visual-pilot`, keeping horse visuals and animations frozen.

## Locally verified

- Final full Release domain run: **178 passed, zero failures or skips**, .NET 8.0.425 with two test threads. Includes genuine v1/v2 migration, legacy age continuation, receipt hashes, both generations of lineage, daily atomic faults/retry, naming, future-format refusal and existing regressions. Actual result: `lifecycle-final.trx`.
- Final game Debug build: zero warnings/errors with warnings treated as errors. Godot 4.7.2 .NET uses that Debug assembly for these local captures.
- Actual Metal flow used a fresh explicit temporary save and the real Review/Confirm/name/reload handlers. Seven screenshots and `capture.log` show conception at day 10, birth at day 14, name Clover, youngster at day 17, adult at day 20, and reload through birth +8 at day 22. The runner checks one daily receipt/debit, one event per stage change, mare recovery at birth +2, naming, and assistance without negative funds.
- The existing two-horse stable-day capture passed again: three screenshots, one saved day, 464 coins, with care/receipt/ledger assertions. See `stable-day-regression/`.
- Real mouse playtest used a separate fresh file: three ordinary days; birth-day review cancel (day 13/342 coins unchanged); double-click confirmation (one birth/day to 14/306 coins); name Willow; reload with same name/parents/birth; return to menu and Quit. Godot exited zero. Independent saved-state inspection: `pointer-verification.json`.
- Frozen-spec validator and whitespace checks passed. All **144** tracked handoff/art/visual asset/shader/catalog/tool files in the starting boundary retain their original Git blob hashes. `frozen-boundary.json` records the baseline.
- Native Sol reviewed the actual persistence/lifecycle diff and final corrections; an independent UI reviewer examined the screen/embedded-fixture/CI diff. No remaining blocker. Inspection is distinct from parent-run tests and renders above.

Saved calendar days are zero-based; screenshots display day +1. This is a text-based foaling sandbox, seeded from a TEST-ONLY committed conception in its own slot. It does not enable a breeding catalog or approve real coat-matrix rows. Facility-wide capacity management remains later work. Platform exports, process-kill/universal power-loss guarantees and animation quality are not claimed.

An earlier suite attempt had one obsolete future-wire test target (3 had become current). It was corrected to current version +1; the successful final TRX supersedes that attempt. The runtime evidence here uses the final repaired implementation.

Implementation `80076b0` passed [domain run 38109572283](https://github.com/MarkJRogers92/Horse/actions/runs/38109572283) (downloaded TRX: 178/178, no skips). The entire [game/visual run 38109572317](https://github.com/MarkJRogers92/Horse/actions/runs/38109572317) passed, including build/import/startup, ordinary stable-day and birth/name/growth/reload checks. Downloaded CI lifecycle images/log show day 22; the existing render batch completed with zero failures. Exact metadata and TRX are in `ci-80076b0/`. No horse visual or matrix approval is implied.
