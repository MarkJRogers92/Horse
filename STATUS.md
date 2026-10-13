# Project status

**Last updated:** 2026-10-13 by Claude (D-012 scene-first hub: Meshy tool, first scenery, stable preview descriptors).
**Working branch:** `claude/eloquent-davinci-o1hhr6` (D-012, from `main` a0fc8ad). Previous: `claude/relaxed-ride-9c5tto`. It contains every other active branch: `codex/horse-production-library`, which itself contains `claude/godot-horse-visual-pilot` and `claude/great-euler-hq83x3`, plus a merge of `rea/horse-coat-preview`. The animation archive (`archive/horse-animation-experiments-2026-10-10`) is intentionally not merged.
**History:** the full checkpoint log up to 2026-10-12 is in [`docs/history/STATUS_LOG_to_2026-10-12.md`](docs/history/STATUS_LOG_to_2026-10-12.md). Search it with `grep -n '^#'`. Keep this page short: replace sections instead of stacking new ones on top.

## What the game does today (Godot 4.7.2 .NET, `game/`)
- **Manage stable:** a saved stable with daily care, running costs and **50 coins/day of club support income** (D-004, D-008).
  - The player can breed their own horses for a 50-coin fee (D-006); the foal is then born, named and grows up (D-005). One atomic save, with exact-retry safety.
  - Each horse card has **Details**: visible coat, family, recorded DNA tests and care, from the domain `HorseProfiles`.
  - **Pip the rabbit** (drawn in code, talks with a typewriter bubble) runs a skippable six-step guide from meeting the horses to the first foal, and greets players on the main menu.
  - **Practice gallop** is a free, deterministic 800 m race driven by developed traits (D-009). Each horse's first gallop of the day saves gallop training (D-011).
  - **Selling:** adult horses you bred can be sold to the Willowbrook Riding Club from Details (D-010). Starter horses stay.
  - **UI:** every screen uses the shared `CozyTheme`: Nunito font (OFL, `game/assets/fonts/`), puffy pill buttons, hand-drawn toggles and checkboxes, ribbon-titled dialogs with Pip peeking in.
- **Foaling demo:** a separate sandbox save that shows a committed pregnancy through to birth and growth.
- **Horse viewer:** Light and Stock adults and the foal with coats and markings, plus a neutral Walk and Idle (D-007, Meshy-based library). A macOS export exists; it still has the earlier animation.
- **Domain (`src/`, pure C#):** phased coat genetics, pedigree, eligibility, forecasts (including the gray base-coat class), conception, the save codec, the atomic store, horse profiles and the practice gallop.

## Task board position
Live per-task status is in [`docs/TASK_STATUS.json`](docs/TASK_STATUS.json); the frozen board still says "proposed".

| Done (scoped slices) | Partly done | Not started |
| --- | --- | --- |
| P0-03, P1-04–P1-07, P1-09, P2-02, P2-07 | P0-02, P1-01/02/03/08 (D-007 art), P1-10, P2-01 (Details), P2-03 (income), P2-05 (practice gallop), P2-06 (owned parents), P2-08 (guide) | P2-04 estate, P2-09, P3+ |

## Current work: D-012 scene-first stable hub (owner request 2026-10-13)
Goal: stop the stable looking like a text game. (1) portraits of every horse, (2) a 3D yard home screen with the player's horses, scenery from Meshy.
- [x] D-012 decision record, Meshy budget 300 credits (check in with owner at 150). Ledger: `art/scenery/meshy/ledger.json`.
- [x] `tools/meshy/meshy.py` (key is an environment network secret, never in the repo). Barn generated (30 credits).
- [x] Domain `PhenotypeResolver.ResolveForStablePreview` + test (214/214 locally). No matrix promotion.
- [x] Fence, oak tree, hay props, water trough generated (30 credits each). **Spend is 150/300: owner check-in reached; ask before any further generation** (tool refuses without `--past-checkpoint`). Textures resized to 1024 (42 MB to 6 MB).
- [x] Portraits: `HorsePortraitStudio` (private SubViewport through HorsePresenter, cached by descriptor) + `portrait_card.gdshader`. Shown on roster cards, care sections and Details.
- [x] 3D yard hub: `StableYard` (Meshy scenery with procedural fallbacks, saved horses in paddock slots, click a horse to open Details and focus the camera). New layout: top bar, roster left, care/breeding right, Pip top centre, bottom dock. Captures wait for portraits.
- Verified locally: 214/214 tests, game `-warnaserror` build, stable-day, lifecycle and breeding captures all OK. Evidence: `docs/stable/evidence/2026-10-13-hub/` (00_before vs 01_hub).
- [x] Owner raised the budget to about 500 after the check-in and merged to `main` (07e91e5). Spent 300/500: saddle stand, signpost (real "Cloverbrook Stables" text via Label3D), show jump, wheelbarrow; a flowering bush was rejected (shard foliage, files not kept).
- [x] Breeding review shows mare ♥ stallion = ? foal portraits; sale offer shows the horse. Pip sits beneath dialogs.
- [x] Yard life: horses rest, then turn in place (Walk is refused for non-neutral body shapes by the checked-walk rule, so travel only happens for neutral horses). Raked-sand shader, grass tufts and wildflowers. `--hub-preview=DIR` writes live yard screenshots (game time) plus a UI-free view.
- [ ] Next: visual practice gallop, end-of-day sunset transition, main menu in the yard, illustrated icons (Meshy text-to-image if cheap), retry a bush/planter.

## Next steps (before D-012)
1. **`main` branch and Mac install:** `main` is created from this branch once visual CI is green (owner-approved 2026-10-12). Every push to `main` runs `.github/workflows/macos-build.yml`, which uploads `Cloverbrook-Stables-macOS-<run>` (universal, ad-hoc signed, not notarized; see INSTALL.txt inside) to the run's Artifacts. It can also be started by hand from the Actions tab. The owner must make `main` the default branch in GitHub settings.
2. Rest of P2-03: more buyers with weekly demand, buying horses, visitor activities.
3. P2-05 settlement: entry fees and prizes need a persisted event record (save schema v4) through its own decision record. The practice kernel's parameters can be reused.
4. P2-01: adoption, and inspection in 3D through the horse presenter.
5. **Owner decision:** keep large evidence out of git. The `.git` pack is about 280 MB, mostly `docs/visual/evidence`. Options are Git LFS (has storage quotas) or keeping only CI artifact links. Shrinking existing history would rewrite it.

## Rules that still hold
- D-001 to D-011 are accepted.
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
| `7347f07`–`a2b5070` | Income, Details, guide, practice gallop | Domain CI passed on each push; visual CI results are checked before `main` is created |
| this commit | Sales, training, capture fail-fast, Pip and cozy UI | Local: 213/213 Release tests, game `-warnaserror` build, validator OK. Local Godot Vulkan: stable-day (practice + training), lifecycle, and breeding (sale for 335 coins, final coins 889) captures pass. Main-menu smoke test (`[smoke] OK all four family mappings`) passes. |

## Completion handoff — 2026-10-12 (Claude, second pass)
- **Domain:** `HorseMarket` (D-010) and `PracticeTraining` (D-011), plus store commands `CommitSale` and `RecordPractice`. The planner ignores sold horses for population and capacity. No save-format change.
- **UI:**
  - `CozyTheme` now styles every control type and provides `Dialog`, `Ribbon` and procedural icons.
  - `Bunny` and `BunnySpeaker` are in `game/scripts/UI/Bunny.cs`.
  - All dialogs (review, breeding, Details, practice, sale) share one style.
  - The guide is voiced by Pip. Captures check `_guideStep`.
  - The main menu is restyled.
- **Captures:** a refused setup now exits 1 immediately. Stable-day writes 6 screenshots; breeding writes 9, including the offer and after-sale views.
- **Not done:** recovery dialog still uses Godot's window chrome (rarely shown); horse viewer and appearance studio panels use the theme but were not redesigned.
