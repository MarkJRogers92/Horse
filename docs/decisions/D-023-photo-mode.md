# D-023 — Photo mode

Owner instruction 2026-10-13: "keep going while CI runs". Photo mode was on the polish list.

- **What it does.** A camera button in the top bar hides the whole interface. The yard camera then
  orbits the focused horse (or the first horse), and "Next subject" cycles through the horses and then the
  whole yard.
- **Controls:**
  - drag to turn (pitch is limited to 3–70°);
  - scroll to zoom (2.4–18 m);
  - Esc or Done to leave.
- **Snap.** It hides the bar for one frame, then saves a full-resolution PNG to
  `Pictures/Cloverbrook Stables/Cloverbrook-YYYYMMDD-HHMMSS.png`. A white flash, a synthesized shutter
  sound (`tools/audio/synth.py shutter`) and a "Saved to Pictures" note follow.
- **Presentation only.** Nothing is written to the save. Horses keep wandering, and weather, seasons and
  tack all show in the photos.
- **Also:**
  - caring for a horse now makes it give a small happy hop with its hearts;
  - the Meshy camera icon cost 3 credits (balance 581).
