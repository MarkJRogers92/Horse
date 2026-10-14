# D-040 — Reversible cooperative pasture

**Decision ID:** D-040
**Date:** 2026-10-14
**Status:** Owner-approved local implementation; no push or merge before review.

## Purpose

Keep favourite horses, their ownership and their family history while making room at the active stable. In particular, a stable filled with the two protected starters and four protected fair purchases must not become permanently stuck at six horses.

## Smallest useful slice

Reuse `HorseRecord.HomeAssignment`: a horse moves between `ActiveStable` and `CooperativePasture`. Pasture is a roster destination, with a simple management dialog, rather than a second 3D yard. There are no boarding charges, transfer charges, new services or extra save fields.

- All owned adults, including starters and fair purchases, may board.
- Pregnant mares, recovering mares and mares with owned nonadult offspring stay at the active stable. Foals and youngsters cannot board in this slice.
- Boarding frees one active place. Active places count every owner-owned active-home horse, including an unborn foal, plus every pending capacity hold. Return requires one free active place; existing foaling holds are respected.
- Pastured horses remain owned and listed. Their identity, coat, pedigree, conception evidence, training, bond, DNA knowledge, tack, names and care history are retained.
- Age continues to advance with the existing calendar. Care conditions stay exactly as they were when the horse left: no decay, bonus or active-stable upkeep. A completely boarded stable has neither active upkeep nor the existing active-stable club-support income.
- Pastured horses cannot breed, train, race, enter shows or receive hands-on/festival care. Return makes those activities available under their normal rules. Daily care tasks only choose active horses.
- Family/history inspection, naming, DNA knowledge and cosmetic records do not disappear because of boarding. This slice does not sell or retire horses.

## Persistence and retry semantics

`AtomicSaveStore.CommitPastureTransfer` sets a requested destination under the existing save lock. It validates the requester, horse ownership and destination. If the horse is already at the requested destination, it returns the current state without writing. Otherwise both expected save and horse revisions must match and current eligibility/capacity must pass.

A real transfer increments the save, horse and capacity revisions once, preserving all other record fields and roots. The normal flushed temporary-file and atomic replacement path publishes the change. A retry after an uncertain successful write cannot move twice. A stale request after a subsequent return is rejected; it never reboards the horse. This is destination-setting idempotency, not a new transaction receipt history.

The save schema remains v3. No zero-coin wallet entries or cosmetic markers are introduced. Generic snapshot saves may no longer change a horse's home, so they cannot bypass transfer eligibility or capacity checks. Save validation rejects unsafe boarded juvenile/pregnancy/dependent-mare states; legacy valid adult pasture assignments remain readable.

## Alternatives not chosen

- Selling protected horses changes the attachment-first rule and removes ownership.
- A new pasture scene, pasture fees, aging penalties or retirement adds scope and consequences unrelated to the immediate capacity problem.
- A new receipt field or nonfinancial entries in the coin ledger adds persistence machinery without improving this desired-location command.

## Verification

Tests cover protected-horse capacity recovery, fair and breeding space, reserved/unborn capacity, blocked full returns, dependent offspring, daily care and costs, aging, activity eligibility, save/load and preserved identity/history, owner and stale-revision checks, repeated requests, round trips, and interrupted atomic writes. Final test and rendered-review evidence is recorded in the implementation handoff.
