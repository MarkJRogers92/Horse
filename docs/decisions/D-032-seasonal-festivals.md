# D-032 — Seasonal festivals

Owner instruction 2026-10-13: standing permission to refine and add features. Gives each season a cozy event to look forward to.

- **Calendar.** Each 28-day season has a festival in its third week, season days 14 to 20 (calendar day `d` with `d % 28` in 14..20, which is
  also a fair week): Spring "Blossom Fair" (Flower garland), Summer "Summer Picnic" (Watermelon slice), Autumn "Harvest Festival" (Crunchy
  apple), Winter "Winter Lights" (Peppermint). Domain `Festivals.Current(day)` gives id, name, treat, year (`day / 112`) and days remaining;
  `Festivals.Next(day)` gives the coming one ("in N days"). Presentation text and visuals are derived from this, never stored.
- **Festival treat.** A fourth once-a-day care interaction, only during the festival week. It raises bond exactly like a carrot with a base gain
  of 450 (same diminishing returns, shared `HorseBond.Apply`). Stored in the horse's existing `TrainingState`: `festival_treat_day` (day marker)
  and `fest_{year}_{season}` (days treated in that festival, 0 to 7). `AtomicSaveStore.CommitFestivalTreat` is free and non-ledger, like
  `CommitInteraction`. No save schema change.
- **Rewards.** A festival's reward tack unlocks permanently once any of the player's horses has a count of 3 or more for that season in any
  year (scan of `fest_*_{season}`): Flower crown (bow slot), Sunflower tail bow (new `tail` slot), Harvest braids (new `braids` slot),
  Fairy-light halter (new `halter` slot). The existing pad and bow slots and items are unchanged. Hint: "Give festival treats on 3 days of the
  Blossom Fair".
- **Daily tasks.** During a festival Pip's second task is "Give {horse} a {treat}" (task id `treat:{horseId}`, done when the day marker matches).
  The offer depends only on the day and the horse, so finishing it never removes it. Journal ribbons: Festival Friend (a reward unlocked) and
  Year of Festivals (all four).
- **Presentation.** `StableYard.SetFestival(id)` (public, so the hub preview can force any season) shows a few dozen shared-material primitives
  in the free band inside the fence: pastel bunting and blossom pots; striped bunting, picnic blanket and parasol; pumpkins and apple baskets;
  fairy-light strings and a snowman. The care bubble gets a fourth button with the treat name; a festival pill with the "fair" icon sits next to
  the coin pill (the season pill heading shows the festival name); a ribbon toast announces the first day; Pip tips for the treat and "Next
  festival: ... in N days". Festival rewards are drawn in `HorseTack` from the body's own vertices (head section, neck crest, tail bones).
- **Verified.** `FestivalTests` (windows, one treat per horse per day, bond gain, count across days, unlock at 3 and persistence, refusal
  outside the festival, save round trip, daily task). Visual review: `--hub-preview` captures `04_festival_{season}.png` and the tack shots;
  evidence under `docs/stable/evidence/2026-10-13-hub/d032_*`.
