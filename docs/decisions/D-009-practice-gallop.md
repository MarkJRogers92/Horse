# D-009 — Practice gallop as the first activity

**Decision ID:** D-009
**Date:** 2026-10-12
**Owner:** Claude, implementing the owner's request (2026-10-12) to add a first simple activity so inherited traits have a visible use.

**Question:** P2-05 asks for versioned race kernels, frozen start snapshots, prize settlement persisted through saves, and a playable barrel exercise. Settlement needs a new persisted event record, which means a save-format bump and migration. What is a safe first slice that gives traits a use now without changing the save?

**Chosen result:** `PracticeGallop` (`activity.practice-gallop.v1`) is a deterministic 800 m flat-race kernel in the domain. It is shown in the stable screen as a "Practice gallop" panel that replays the race.

- **Inputs:** each entrant's developed game traits from the existing `GameTraitResolver`, via `PhenotypeResolver`:
  - `speed` sets top speed (12 to 18 m/s).
  - `acceleration` sets how fast a horse reaches it.
  - `endurance` sets how much it fades after 60% of the distance.
  - `composure` sets how much its form and stride vary.
- **Determinism:** the whole race uses integer millimetres and milliseconds in 100 ms ticks. The seed is a hash of the rules version, save ID, calendar day, a session run index and the sorted entrants. It never draws from the save's RNG streams.
- **Playback:** the panel only replays recorded frames. Playback speed and frame rate cannot change a result.
- **Eligibility:** owned adults at the active stable, excluding mares in foal or recovering after foaling. Up to six entrants.
- **Output:** placings, finish times, first and last 200 m splits, and observations taken only from the race itself ("quickest away", "strongest finish"). It never prints hidden trait values.
- **Effect on the game:** none. Practice pays nothing, costs nothing and writes nothing.

**Reason and alternatives considered:**

- A full P2-05 event with entry snapshot and settlement needs a save schema v4. That deserves its own reviewed decision.
- A Godot-side simulation would let frame rate decide outcomes, which P2-05 forbids.
- Using the save's competition RNG stream without persisting the advance would break the "one canonical RNG" rule.

A derived, unpersisted seed keeps practice honest and reproducible.

**Affected contracts/assets/tasks/saves:**

- P2-05, partial. Changed files:
  - `src/Cloverbrook.Domain/Activities/PracticeGallop.cs`
  - `game/scripts/Stable/StableDay.Practice.cs`
  - `tests/Cloverbrook.Domain.Tests/Activities/PracticeGallopTests.cs`
- The stable-day CI capture now runs a gallop and asserts that the save hash and wallet are unchanged.
- No save, frozen handoff, asset or matrix change.

**Migration or compatibility action:** None. Future rule changes must use a new version string so recorded seeds remain explainable.

**Not done (later P2-05 work):**

- Entry fees, prizes and settlement persisted through the atomic store.
- Frozen start snapshots stored with event records.
- Barrel steering.
- Use of the 3D horse presenter.
- Training effects.

**Acceptance evidence:**

- Local Ubuntu .NET 8.0.131: 208/208 Release tests, including:
  - same inputs give a byte-identical result;
  - the run index changes the seed;
  - the save is unchanged;
  - ineligible horses are rejected;
  - a horse with better running traits wins 15–20 of 20 practices.
- Godot 4.7.2 local Vulkan (llvmpipe) stable-day capture passed, with mid-race and result screenshots.
