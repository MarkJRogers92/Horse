# D-029 — The weekly Cloverbrook Coat & Turnout Show

Owner instruction 2026-10-13: standing permission to refine and add features. The Cup rewards speed; this show rewards looks and care.

- **Rules (domain, `HorseShow`, version `horse-show.v1`).** Once per fair week (`HorseFair.Week`, like the Cup) the player may enter one adult horse
  (owned, not sold, not unborn, not a foal) against five visiting rivals. Entry 15 coins; prizes 120 / 70 / 35 for 1st / 2nd / 3rd.
- **Judge's card (total 100).** Coat 0-40 from the resolved phenotype (base 12; cream +6/+9, dun +6, primitive markings +3 without dun,
  tobiano +10, roan +9, leopard complex +12 and +4 with PATN1, grey +8 and +4 once GrayAmount is 6000 or more); Grooming 0-25 from today's
  grooming care; Bond 0-15 (3 per heart); Turnout and tack 0-15 (turnout care scaled to 9, +3 pad, +3 bow); Judge 0-5, a deterministic
  hash of save, week and horse. Ties go to the higher judge score, then horse id.
- **Rivals.** Built with `HorseFair.Generate` from `Canonical.Hash("horse-show.v1", save, week, slot)`, so they can carry patterns. They have
  no daily state: grooming `3200 + 200*week` (cap 7500) and turnout `2500 + 200*week` (cap 7000), each plus up to 1800 of per-rival
  variation, 2-4 bond hearts, a pad half the time and a bow a third of the time. A fully groomed five-heart common coat scores about 61
  and still places in the early weeks. Rivals never join the save.
- **Save.** `AtomicSaveStore.CommitShowEntry(owner, horse, expectedRevision, quotedEntry)` publishes the entry debit (`horse-show-entry`) and any
  prize credit (`horse-show-prize`) in one save. Ledger ids derive from save and week, so a week holds one entry and a retry returns the
  current save with no outcome. `StableSnapshot` rejects an entry that is not exactly -15 in a week up to today, and a prize that is not
  one of the three amounts or has no matching entry for its week. As with the Cup, the ledger does not record which horse entered, so a
  prize cannot be re-derived on load; the commit path derives it. Existing saves load unchanged (no schema change).
- **UI.** A "Horse show" footer button opens a dialog: horse picker with portraits, the judge's card as bars with tips, the five rivals
  (portrait, coat, total), and "Enter · 15 coins". After entering it shows a ribbon banner ("Blue ribbon! First place!") and the results table.
  Ribbons: Best in Show, Leopard Lover, Silver Lining, Pattern Collector. Pip suggests the show once a week.
