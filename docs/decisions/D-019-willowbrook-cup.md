# D-019 — The weekly Willowbrook Cup

Owner instruction 2026-10-13: "keep going" (standing permission to refine and add features).

Gallop training and breeding had no payoff. The cup gives a weekly goal: a prize race against visiting horses.

- **Rules (domain, `ClubRace`).** Once per calendar week, the player may enter one adult horse that can
  gallop today (`PracticeGallop` eligibility rules). It races five visiting rivals over 800 m, using the
  practice-gallop physics; the simulation is shared through `PracticeGallop.Simulate`.
  - **Entry fee:** 20 coins.
  - **Prizes:** 160 coins for 1st, 90 for 2nd, 45 for 3rd.
  - **Training:** entering counts as the horse's gallop training for the day.
- **Rivals.**
  - Each rival is derived only from `Canonical.Hash("club-race.v1", SaveId, week, slot)`. It is a full
    record with a released phase-1 coat (shared generator `HorseFair.Generate`), a name and a home stable.
  - Rivals never join the save.
  - The field improves each week, in both training (`FieldSkill`: 0, then +420 a week, up to 6,800) and
    breeding (`FieldBreeding`: the chance of each favourable trait marker, 3%, then +4% a week, up to 55%).
  - Balance check over 15 saves × 12 weeks:
    - An untrained starter sometimes places in the first weeks.
    - A starter trained to 30% places often for about six weeks.
    - Later weeks reward well-bred or fair-bought horses that have trained a lot.
- **Save.**
  - `AtomicSaveStore.CommitClubRace` runs the race inside the save lock. It then publishes three things
    together: the entry debit, any prize credit and the training.
  - The entry and prize ledger IDs are derived from the save and the week. A week therefore holds at most
    one cup, and a retry returns the current save without an outcome.
  - `StableSnapshot` rejects a save if any of these is true:
    - an entry debit does not belong to a week up to today;
    - an entry debit's amount is not the fee;
    - a prize is not one of the three amounts;
    - a prize has no matching entry for its week.
- **UI.** The gallop dialog has a "Practice / Willowbrook Cup" switch.
  - **Before the race:** the cup tab shows the prizes, the fee, the days left, and the visiting field
    with portraits and home stables. You pick one of your horses; its training is shown.
  - **When you enter:** the race is saved first, then replays in the 3D race view and the lanes.
  - **After the race:** a podium, your place and your prize. Winning plays the fanfare and cheers your
    horse in the yard.
  - **Elsewhere:** a "Cup Champion" ribbon, and a Pip tip when the cup closes within three days.
- **Yard landmarks.** Meshy made two props for 60 credits; the balance is 734 and textures are resized
  to 1024:
  - a striped fair tent at the right of the yard, labelled "Horse Fair", which you can click to open
    the fair;
  - a trophy stand by the bench that appears after your first cup win ("Cup champion", or "Cup wins ×N")
    and opens the cup tab.

  Both light up the pointer cursor on hover.
