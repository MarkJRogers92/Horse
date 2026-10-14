# D-036 — Polish pass: grazing reach, fuller rugs, fold-away task card, horse sounds

Presentation only (game side). No domain, save or contract change.

## Decisions

1. **Grazing reaches the grass.** `HorseIdleLife` now stores pitch per bone (degrees at full graze) instead of one total split by
   shares. The lower neck takes most of the bend, and the head bones extend back (negative) so the face points down and forward
   instead of tucking under the chest. The neck alone cannot reach (neck + head is shorter than the withers height), so the
   shoulders also dip (`Spine_Mid/Spine_Upper/Chest`, or `Spine_Base/Chest` on the foal) and the forelegs stand one forward, one
   back, counter-rotated so they stay roughly upright. Tuned per family (standard adult, compact adult, foal) with the new
   `GrazePreview` tool (`scenes/tools/GrazePreview.tscn -- --out DIR [--family text] [--bones 1]`), which holds the graze at full weight,
   renders side and three-quarter stills and prints the muzzle and front-hoof height. Measured muzzle height above the ground:
   standard adult 0.04 m, foals 0.05 m, compact adult 0.13 m (a shorter neck; a deeper dip tucked the head). Front hooves sink up to
   about 4 to 8 cm into the turf at full graze (hidden by grass). Offsets are zero outside a graze, during Walk and across swaps
   (YardWalkProbe unchanged and passing).
2. **Fuller turnout rugs.** `AddBlanket` now runs from the withers to the croup (stops at the tail head), drapes to about the belly
   line, has hem binding piping, a chest strap with a buckle that follows the chest surface, and a belly surcingle. Still measured
   from body vertices, so it follows width morphs. Reviewed on all four families at neutral and extreme shapes.
3. **Fold-away task card.** A chevron on the card header folds it to one row: "Pip's tasks · 1 of 3 done" plus " · Claim!" when
   something can be collected. The choice is a display preference in `tasks.cfg` beside the save (not written in the foaling demo). A
   task that becomes claimable opens the card again. The stable-day capture presses the toggle and saves `03b_tasks_collapsed.png`.
4. **Horse sounds.** `tools/audio/synth.py horse` synthesizes `horse_nicker`, `horse_whinny`, `horse_stamp` and `horse_chew` (formant-filtered
   pulse-train voice, filtered noise; no samples). Played quietly through `GameAudio` (SFX bus, mute respected): every 20 to 60 s one
   resting yard horse nickers or stamps; a graze sometimes comes with a chew; a click on a resting horse sometimes gets a nicker; a newborn
   whinnies after the birth fanfare (`StableDay.Birth.cs`, outside Care.cs).

## Not done / notes

- The sounds were checked numerically (pitch contour, level), not by ear. The owner should listen.
- The chest strap reads as a short rod from the side on some views.
- The foal's mane tuft is skinned to rig bones the idle life does not drive, so it floats during a graze (pre-existing).
