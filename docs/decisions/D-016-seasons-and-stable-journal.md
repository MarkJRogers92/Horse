# D-016 — Seasons in the yard and a Stable Journal

Owner instruction 2026-10-13: "continue refining/adding features, visuals, systems, UI … use your own judgement".

- **Seasons (presentation only).** The calendar's 28-day season index cycles Spring, Summer,
  Autumn, Winter. `StableYard.SetSeason` recolours grass, hills, paddock rim and grass tufts,
  hides flowers outside spring/summer, swaps petals for falling leaves or snow, tints the Meshy
  oaks with a translucent overlay and warms/cools the sun. The HUD shows "Spring · Day 3" with
  the year; the day review and morning card use the same names. No domain or save change.
- **Stable Journal.** Twelve ribbons are derived purely from the saved `StableSnapshot` (foals
  born and named, breeding, adulthood, sales, gallop training, care, herd size, savings, time).
  No new save data: nothing can be lost, duplicated or desynchronised. Which ribbons the player
  has already been shown is a display preference in `journal.cfg` beside the save; a stable
  opened for the first time accepts what it has already earned without a flood of pop-ups.
