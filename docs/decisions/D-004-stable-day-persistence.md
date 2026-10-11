# D-004 — Player-controlled stable days through the shared save store

**Decision ID:** D-004
**Date:** 2026-10-10
**Owner:** GPT integration lead, implementing the owner's request to continue game systems outside horse visuals.

**Question:** How should the first calendar/care slice advance time and charge ordinary upkeep without creating a second save or bypassing committed breeding?

**Chosen result:** P2-02 uses one explicit End Day command, one deterministic daily preview, and the existing `StableSnapshot` / `AtomicSaveStore` transaction boundary. New daily state holds care records keyed by canonical horse IDs and immutable completion receipts. Learned skill, inherited traits, appearance and all RNG streams remain separate from ordinary care. The Godot management screen reads saved horse names and care state; it does not use the horse renderer or approve visual catalog rows.

**Reason and alternatives considered:** A frame timer or real-world clock would violate player-advanced days and introduce offline punishment. A UI-local wallet or separate JSON save would conflict with the authoritative ledger and atomic store. Reusing training values as temporary care would mix learned ability and readiness. A root daily extension preserves the existing historical conception/horse encodings while adding a distinct, reviewed care state.

**Affected contracts/assets/tasks/saves:** P2-02 calendar, care, cost and schedule paths, mapped to `src/Cloverbrook.Domain/Stable/`, `game/scripts/Stable/`, and the Stable Day scene under accepted Godot decision D-003. The tightly scoped persistence extension remains owned by one implementation worker and independently reviewed by the native Sol consultant. Frozen `handoff/`, horse assets, shaders, visual recipes and matrix release state are unchanged.

**Migration or compatibility action:** A new positional save extension requires a versioned wire/schema upgrade. The previous verified version is decoded explicitly and migrated with neutral care defaults and no daily receipts; existing identity, genealogy, wallet and conception evidence are preserved. Unknown versions and malformed extensions refuse to load. End Day recalculates from locked current state, persists care/day/debit/receipt together and recognizes an exact retry before stale preconditions. Generic saves may not advance the calendar or rewrite daily completion history.

**Scoped lifecycle policy:** This slice advances one day at a time. It blocks at birth and juvenile growth boundaries until the future lifecycle implementation can activate the already committed horse and reconcile capacity. It does not silently skip those transitions, create a new foal or consume a breeding hold again. Missing reliable juvenile birth anchors are a visible unsupported condition.

**Prototype costs and welfare:** Purchased feed is consumed within the same day, with no separate inventory system in this slice. The workflow's illustrative rates are six coins per active owned born horse and 24 shared operating coins for a nonempty active stable. These are versioned game-tuning values. Cooperative and leased horses do not incur local upkeep. Assistance covers an ordinary-care shortfall without granting spendable coins or creating debt. The game never deletes or sells a horse to pay these costs.

**Acceptance evidence:** Local implementation and migration are verified: full Release suite 165/165, parent focused Stable suite 34/34, final strengthened daily-root guard 1/1, clean game build, real Metal screen captures and actual pointer cancel/double-click/reload/navigation checks. Native Sol reviewed the persistence corrections and an independent UI peer checked the screen and CI flow. Evidence and subsequent GitHub results are recorded in `STATUS.md` and `docs/stable/evidence/2026-10-10/`. Full pregnancy/growth, income activities, feed storage, capacity management, adoption and racing remain separate tasks; this decision does not claim full Phase 2 acceptance.
