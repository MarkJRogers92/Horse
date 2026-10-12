# D-011 — Gallop training from practice

**Decision ID:** D-011
**Date:** 2026-10-12
**Owner:** Claude, implementing the owner's request (2026-10-12) that practice should improve horses.

**Question:** How should practice build lasting skill without changing inherited traits, adding a save field, or letting players grind?

**Chosen result:**

- **Storage:** learned gallop skill lives in the horse's existing `TrainingState` vector, under key `gallop` (0–10000). Key `gallop_day` holds the last trained calendar day + 1, modulo 10000.
- **Recording:** `AtomicSaveStore.RecordPractice` saves one day of training for each eligible entrant through the existing trusted generic save path. That path already permits training changes and forbids wallet, calendar and identity changes.
- **Gain:** at most once per horse per calendar day, with diminishing returns: `600 × (10000 − skill) / 10000`, minimum 1.
- **Race effect:** gallop kernel v2 (`activity.practice-gallop.v2`) adds up to +0.8 m/s top speed and less late fade from skill.
- **Price effect:** the club buyer adds up to +250 coins for skill.
- **Inheritance:** foals never inherit training.

**Alternatives considered:**

- Training as a daily care toggle changes the care-plan hash and receipt encoding, which needs a schema bump.
- A separate training record needs schema v4.
- Writing to inherited potential would break the genetics contract.

**Migration or compatibility action:** None. An absent key means skill 0. Gallop v1 results are not stored anywhere, so the version bump only changes future practice seeds.

**Acceptance evidence:**

- Local Release suite: 213/213. Coverage includes once per day, diminishing gain, stale-revision rejection, a higher sale price, and a trained horse winning 15–20 of 20 races against an identical untrained one.
- The Godot stable-day capture asserts one saved day of training for both starter horses and an unchanged wallet.
