# D-021 — Hands-on care and bond

Owner request 2026-10-13: "lets do 2 and 4". Item 2 was hands-on care in the yard (brush, carrot, pat,
with hearts) plus a bond level that helps a little in races.

- **Domain (`HorseBond`).** Three interactions: pat, brush and carrot.
  - **Limit:** each raises a horse's bond once per calendar day.
  - **Gains:** 250, 350 and 400 points, with diminishing returns (gain × (10000 − bond) / 10000).
  - **Free, no decay:** interactions cost nothing and bond never goes down.
  - **Display:** 0–5 hearts.
  - **Storage:** bond and the day markers live in the horse's existing `TrainingState`. That keeps bond
    learned rather than inherited and needs no save-schema change.
- **Saving.** `AtomicSaveStore.CommitInteraction` checks the owner, the save revision, the kind and the
  daily limit. It goes through `SaveSnapshotUnlocked`, so the coins, ledger and care history provably stay
  unchanged.
- **Racing (practice-gallop v3).** Bond makes a horse calmer and a little quicker:
  - up to 30% less pace wobble;
  - up to 0.3 m/s faster.

  This applies to practice gallops and the Willowbrook Cup. The rules version moved to v3 because the
  physics changed; no saved result depends on the old version.
- **UI.**
  - **The care bubble:** clicking a horse in the yard opens a bubble beside it with its name, bond hearts,
    Pat, Brush and Carrot buttons, and Details. A greyed button explains it was already done today.
  - **Each interaction:** saves at once, cheers the horse with hearts, plays a sound and says something
    warm. It also announces when a heart is gained.
  - **Elsewhere:**
    - roster cards and Details show the hearts;
    - a Pip tip suggests a pat for a horse nobody has visited today;
    - a "Best Friends" ribbon is awarded for five hearts;
    - the Journal ribbon grid now scrolls.
