# D-008 — Daily club support income

**Decision ID:** D-008
**Date:** 2026-10-12
**Owner:** Claude, implementing the owner's request (2026-10-12) to add income so the saved stable is not a one-way drain.

**Question:** The saved stable charges feed and operations every End Day (D-004) and a fee per pairing (D-006), but nothing pays the player. Every save slowly runs down to permanent assistance. What is the smallest income route that satisfies P2-03's "simple club/visitor income activity and clear daily cash forecast" without a save-format change?

**Chosen result:** Daily policy `stable.dailycare.v2` adds the workflow's recurring club support arrangement (§8 "One earned currency and clear books": 350 coins per seven days) as a fixed **50-coin credit per completed day** for a nonempty active stable. It is credited inside the same End Day transaction as upkeep. Today's income can pay today's upkeep before assistance is used.

- The credit is a ledger entry of kind `stable-income`, with a deterministic ID derived from the End Day operation ID (`stable.daily-care.income.v1`). Exact retries never pay twice.
- The amount is derived from the receipt's own policy version and cared-for horse count. The existing receipt hash already binds both, so the receipt and wire formats are unchanged.
- Snapshot validation rejects a missing, altered or orphan income credit, and an income credit on a v1 receipt.
- The review screen shows "Club support: +50 coins" above the costs.

**Reason and alternatives considered:**

- A new income field on the receipt would need a schema bump, migration code and new fixtures for a value that can already be derived.
- Player-run visitor activities (+100 each in the workflow example) need a new plan choice and request hash. They are a good follow-up, but the fixed arrangement alone fixes the drain.
- Selling horses or NPC offers is the larger remainder of P2-03 and needs valuation primitives first.

**Balance check (prototype values):**

| Stable | Daily cost | Daily net |
| --- | --- | --- |
| Starter pair | 12 feed + 24 operations = 36 | +14 |
| Three horses | 18 + 24 = 42 | +8 |
| Four horses | 48 | +2 |
| Five horses | 54 | −4, slowly drawn down |

The surplus shrinking as the herd grows is intended (§8: the interface must show when the arrangement no longer supports a larger roster). Assistance still protects welfare at zero coins.

**Affected contracts/assets/tasks/saves:**

- P2-03 (income slice only; NPC offers and valuation remain open). Changed files:
  - `src/Cloverbrook.Domain/Stable/Costs/StableDailyPolicy.cs`
  - `src/Cloverbrook.Domain/Stable/Calendar/StableDayService.cs`
  - `src/Cloverbrook.Domain/Stable/Care/EndDayRequest.cs`
  - `src/Cloverbrook.Domain/Persistence/StableSnapshot.cs`
  - `src/Cloverbrook.Domain/Persistence/AtomicSaveStore.cs`
  - `game/scripts/Stable/StableDay*.cs`
  - `tests/Cloverbrook.Domain.Tests/Stable/StableIncomeTests.cs`
- Frozen `handoff/`, horse assets and matrix state are unchanged.

**Migration or compatibility action:** None needed. Saved v1 receipts keep `stable.dailycare.v1`, verify unchanged and carry zero income. New days use v2, and a save may contain both. The service still refuses a request whose policy differs from its own (`policy_unknown`).

**Acceptance evidence:**

- Local Ubuntu .NET 8.0.131: full Release suite 201/201, zero warnings, including six new income tests.
- Game project build with `-warnaserror` succeeded.
- The in-game stable-day, lifecycle and breeding capture checks were updated for the credit (starter day 500 → 514; breeding capture after ten days 54 → 554). Remote Godot capture results are recorded in `STATUS.md`.
