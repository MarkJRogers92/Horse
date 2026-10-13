# D-022 — Stable shop: stalls and decorations

Owner request 2026-10-13: "lets do 2 and 4". Item 4 was spending coins to decorate the yard and to buy
extra stalls.

- **Stalls.** A stable starts with 4 stalls. Two more can be built, for 450 and 800 coins.
  - `StableShop.Stalls(state)` replaces the fixed capacity of 4. The breeding planner (capacity windows
    and checks) and the Horse Fair stall check both use it.
  - Capacity windows already store their own totals, so existing conceptions stay valid.
  - The first extra stall raises a second stable block (annex) in the yard.
- **Decorations.** Eleven items, each owned once, for 40 to 180 coins:
  - six reuse earlier Meshy scenery (planter, hay, bench, wheelbarrow, saddle rack, trough);
  - five are new Meshy models (flower cart, lantern post, birdhouse, picnic table, rose arch).
- **Saving.** Every purchase is one ledger debit whose ID is derived from the save plus the stall number
  or item, so ownership comes from the wallet history and the save format is unchanged.
  - `StableShop.LedgerProblem` (called from `StableSnapshot` validation) rejects a save if any of these
    is true:
    - a debit is not a known item;
    - a debit's amount is not the item's price;
    - a stall was bought before the one it extends.
  - `CommitStall` and `CommitDecor` publish atomically. Buying an item already owned returns the current
    save.
- **Placement (presentation only).**
  - Owned decorations stand at eight fixed spots outside the paddock, chosen to be visible from the yard
    camera. They pop in when placed.
  - "Move" swaps an item to the next spot. Which spot holds which item is kept in `decor.cfg` beside the
    save.
  - Items beyond eight are "in storage".
  - Losing the file only resets positions; the save itself is unaffected.
- **UI.** A "Shop" button in the top bar opens the Stable Shop:
  - stalls, with the count in use and a Build button;
  - a decoration grid using each model's thumbnail, with Buy, or Owned plus Move.
