# D-010 — Selling bred horses to the club buyer

**Decision ID:** D-010
**Date:** 2026-10-12
**Owner:** Claude, implementing the owner's request (2026-10-12) for NPC buyers, so breeding pays and a full stable can make room.

**Question:** How can a player sell a horse without a second horse record, a hidden marketplace or a save-format change?

**Chosen result:** `HorseMarket` (`market.club-buyer.v1`) quotes one deterministic offer from the Willowbrook Riding Club. `AtomicSaveStore.CommitSale` commits it as one atomic change:

- the horse's owner becomes the club's fixed `OwnerId`;
- its revision increases by one;
- one `horse-sale` ledger credit is added, with an ID derived from the horse's ID.

**Rules:**

- **What can be sold:** only adult horses bred at the player's stable. Starter horses stay; this follows the workflow §8 rule on no nominal-fee resale and attachment first. Mares in foal or recovering after foaling cannot be sold.
- **Price:** 380 coins for a healthy adult, plus or minus up to 150–250 coins for developed running traits, plus up to 250 for gallop training (D-011). Clamped to 150–1200. The offer lists each reason in plain words.
- **After the sale:** the horse keeps its canonical record, pedigree, history and care receipts. It leaves care, practice and breeding, and frees its home place. The breeding planner and capacity count only horses still at home.
- **Integrity:** the snapshot requires each sold horse to have exactly its matching credit, and every sale credit to match a sold horse. Amounts must be within bounds. An exact repeat of a committed sale returns the saved state; different terms are rejected.

**Alternatives considered:**

- Deleting the sold horse breaks pedigree and receipt history.
- A persisted sale record needs schema v4.
- Weekly demand caps and multiple buyers are later work (P3-01).

**Migration or compatibility action:** None. Ownership and ledger kinds are already encoded. Older saves have no sales.

**Acceptance evidence:**

- Local Release suite: 213/213, including sale/replay/reload, rejection of forged or missing credits, the freed home place, and breeding the remaining pair afterwards.
- The Godot breeding capture sells the grown foal through Details, then the offer, then Sell, and verifies the wallet and ownership after a reload.
