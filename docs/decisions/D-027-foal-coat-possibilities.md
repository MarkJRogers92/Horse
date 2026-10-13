# D-027 — Foal coat possibilities in the breeding review

Owner request 2026-10-13, while D-026 CI ran: "anything we can do while we wait?"

## Decision

The breeding review lists the coats the foal could have, most likely first, and words each chance in a cozy way:
"very likely", "good chance", "possible" or "small chance", followed by the honest percentage or range.

**Domain: `FoalCoatOdds`** (`src/Cloverbrook.Domain/Breeding/Forecasts/FoalCoatOdds.cs`)

- **No hidden genes leak.** Each parent is known only by its visible coat and any revealed calls. Every genotype the
  player cannot rule out becomes a scenario. A coat's chance is the min–max over all scenario pairs, with no invented
  prior weights; this is the same rule as `CoatForecastService.ForKnownParents`. A pairing of two duns that are
  secretly D/D still reads "Bay dun 42–100%", and non-dun foals read "up to 25%".
- **Candidates come from the visible layers, locus by locus,** and each is confirmed with the real resolver. The
  general forecast service enumerates the whole allele scope before filtering (about 30,000 genotypes per parent),
  which is over its limit.
- **Foal enumeration is per block.** Every biological block is a single locus or has zero recombination, so a parent
  passes on homolog A or homolog B. This is guarded: if a recombining biological block is ever added, the odds are
  not shown.
- **Hidden PATN1 is explored only when a parent shows LP,** because PATN1 cannot show otherwise.
- **Speed:** 0.2–1.2 s per pairing on a loaded CI-class CPU. The game computes it off the main thread.
- **Unreleased outcomes** appear as "A colour not in this version". None are reachable from live horses.

**Game**

- `StableDay.Breeding.ShowFoalCoatOdds` shows "Working out…" and then the list: the top eight, then "…and N rarer
  colours", plus a note when ranges come from hidden genes.
- The breeding capture waits for the list and asserts that the starter duns show "Bay dun".
- Details adds a short note for grey, tobiano, roan and appaloosa horses, saying how the coat changes or passes on.

## Tests

`FoalCoatOddsTests` covers:

- starter duns: dun leads, and the other coats start at zero;
- bay tobiano × bay dun: tobiano is at least 50%, and plain coats start at zero;
- grey × leopard, and buckskin × dun;
- the friendly text format;
- a speed bound.
