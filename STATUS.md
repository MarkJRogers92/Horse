# Project status

**Last updated:** 2026-10-12 by Claude (income D-008, Details panel, stable guide, practice gallop D-009).
**Working branch:** `claude/relaxed-ride-9c5tto`. It contains every other active branch: `codex/horse-production-library`, which itself contains `claude/godot-horse-visual-pilot` and `claude/great-euler-hq83x3`, plus a merge of `rea/horse-coat-preview`. The animation archive (`archive/horse-animation-experiments-2026-10-10`) is intentionally not merged.
**History:** the full checkpoint log up to 2026-10-12 is in [`docs/history/STATUS_LOG_to_2026-10-12.md`](docs/history/STATUS_LOG_to_2026-10-12.md). Search it with `grep -n '^#'`. Keep this page short: replace sections instead of stacking new ones on top.

## What the game does today (Godot 4.7.2 .NET, `game/`)
- **Manage stable:** a saved stable with daily care, running costs and **50 coins/day of club support income** (D-004, D-008).
  - The player can breed their own horses for a 50-coin fee (D-006); the foal is then born, named and grows up (D-005). One atomic save, with exact-retry safety.
  - Each horse card has **Details**: visible coat, family, recorded DNA tests and care, from the domain `HorseProfiles`.
  - A skippable five-step **stable guide** leads a new player from meeting the horses to the first foal.
  - **Practice gallop** is a free, deterministic 800 m race driven by developed traits, replayed in a panel (D-009).
- **Foaling demo:** a separate sandbox save that shows a committed pregnancy through to birth and growth.
- **Horse viewer:** Light and Stock adults and the foal with coats and markings, plus a neutral Walk and Idle (D-007, Meshy-based library). A macOS export exists; it still has the earlier animation.
- **Domain (`src/`, pure C#):** phased coat genetics, pedigree, eligibility, forecasts (including the gray base-coat class), conception, the save codec, the atomic store, horse profiles and the practice gallop.

## Task board position
Live per-task status is in [`docs/TASK_STATUS.json`](docs/TASK_STATUS.json); the frozen board still says "proposed".

| Done (scoped slices) | Partly done | Not started |
| --- | --- | --- |
| P0-03, P1-04–P1-07, P1-09, P2-02, P2-07 | P0-02, P1-01/02/03/08 (D-007 art), P1-10, P2-01 (Details), P2-03 (income), P2-05 (practice gallop), P2-06 (owned parents), P2-08 (guide) | P2-04 estate, P2-09, P3+ |

## Next steps
1. **`main` branch:** create it from this branch once visual CI is green. Owner-approved on 2026-10-12. The owner must make it the default branch in GitHub settings.
2. Rest of P2-03: NPC offers to buy horses, with valuation from traits, coat and pedigree; optional player-run visitor activities.
3. P2-05 settlement: entry fees and prizes need a persisted event record (save schema v4) through its own decision record. The practice kernel's parameters can be reused.
4. P2-01: adoption, and inspection in 3D through the horse presenter.
5. **Owner decision:** keep large evidence out of git. The `.git` pack is about 280 MB, mostly `docs/visual/evidence`. Options are Git LFS (has storage quotas) or keeping only CI artifact links. Shrinking existing history would rewrite it.

## Rules that still hold
- D-001 to D-009 are accepted.
- No frozen `handoff/` edits, no real coat-matrix promotion, and no purchases or deployments without owner instruction.
- One canonical horse record, RNG, resolver and save.
- Before pushing, run `python3 tools/validate_handoff.py` and `dotnet test Cloverbrook.sln -c Release`. For UI changes, also run the game build and the three stable captures.

## Running checks in a cloud session
- Microsoft's download host is blocked, but Ubuntu's `dotnet-sdk-8.0` apt package works.
- Godot 4.7.2 .NET downloads from GitHub releases (the same URL as `.github/workflows/visual.yml`).
- `apt-get install mesa-vulkan-drivers` provides the llvmpipe Vulkan driver CI uses. Run each stable capture with `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run ... --rendering-driver vulkan`.
- The breeding capture refuses an output folder that already exists.

## Latest verification
| Commit | What | Result |
| --- | --- | --- |
| `b278537` | REA gray forecast merge | Domain [38309981689](https://github.com/MarkJRogers92/Horse/actions/runs/38309981689) and visual [38309981500](https://github.com/MarkJRogers92/Horse/actions/runs/38309981500) passed |
| `7347f07`–`2903dc8` | Income, task overlay, StableDay split, Details panel, guide | Domain CI passed on each push; visual CI was still running when this was written |
| this commit | Practice gallop (D-009) | Local: 208/208 Release tests with zero warnings, game `-warnaserror` build, validator OK. All three stable captures passed on local Godot Vulkan: stable-day 6 screenshots, coins 514; lifecycle coins 570; breeding coins 554. |

## Completion handoff — 2026-10-12 session (Claude)
- **Income (D-008):** policy `stable.dailycare.v2` credits a 50-coin `stable-income` ledger entry each End Day. Saved v1 days stay income-free. No save-format change.
- **Details panel (P2-01 slice):** `HorseProfiles.For` returns only observable data. The UI is in `StableDay.Inspect.cs`.
- **Stable guide (P2-08 slice):** steps complete from save progress or real UI actions. Skipping is stored in `guide.cfg` beside the save slot. Code is in `StableDay.Guide.cs`.
- **Practice gallop (P2-05 slice, D-009):** kernel in `src/Cloverbrook.Domain/Activities/`, replay panel in `StableDay.Practice.cs`. The CI capture asserts the save hash and wallet are unchanged.
- **Housekeeping:** `StableDay.cs` split into the `DayFlow`, `Captures`, `Inspect`, `Guide` and `Practice` partials. Added `docs/TASK_STATUS.json`. Rewrote STATUS and README.
- **Not done:** NPC market and valuation; activity prizes and settlement; barrels; tutorial narration and adoption steps; evidence storage decision.
