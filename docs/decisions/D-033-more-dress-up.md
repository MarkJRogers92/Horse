# D-033 — More dress-up (blanket, halter, braids, tail bow)

Builds on D-020. Free, cosmetic, presentation only.

- **Domain (`Tack`).** Four new slots join "pad" and "bow": "blanket", "halter", "braids" and "tail".
  - Blanket: navy, forest, cranberry, lavender, cream, plus a **Snowflake rug** (white with pale blue) that is unlocked while the
    stable is in winter (`StableCalendar.SeasonIndex(CalendarDay) == 3`). It is derived from the save like the gold and daisy
    items and never stored. A rug already worn stays worn when winter ends; only choosing it is gated.
  - Halter: pink, sky, mint, red, purple. Braids: "Button braids" (no ribbon) and "Ribbon braids" in pink, white or blue. Tail bow:
    the same six pastels as the mane bow.
  - Existing ids (`pad.*`, `bow.*`) are unchanged. `CommitTack` accepts the new slots through the same checks (owner, revision,
    slot, unlock). Choices live in `CosmeticState`, so there is no save schema change.
  - `Tack.Layer` already sorts "slot=item" pairs ordinally, so a horse that wears only a pad and/or a bow gets exactly the same layer
    string (and portrait cache keys) as before. New slots add pairs ("blanket=…;bow=…;braids=…;halter=…;pad=…;tail=…").
- **Game (`HorseTack`, `HorseTack.Extras`).** Placed from the bind pose and the body's own vertices, attached to bones, on all rigs.
  - Blanket: a surface built from per-slice body half-widths at eight depths below the topline, so it hugs the back and ribs and drapes
    to about two-thirds down the barrel, with a trim band at the hem and ends. It grows with the chest-width and muscle morphs. A worn
    blanket hides the saddle pad (rendering only; both stay stored).
  - Halter: tubes swept around the muzzle (noseband), behind the ears (crown) and along the cheeks, with small brass rings. The head
    axis runs from the Head bone to the muzzle tip read from the body (the "Head_Nose" bone sits mid-head).
  - Braids: eight knobs on the mane crest (the mane's own vertices are used so they sit on top of it), in the mane's colour 22 percent
    darker, attached to the neck bones; ribbon braids add a ribbon band and a small bow on each.
  - Tail bow: the mane bow's shape at the Tail_01 bone, oriented along the tail.
  - The foal rig's "Chest" bone sits behind its "Pelvis", so the frame now faces the way the head is.
- **UI.** The Dress up dialog (860 px wide) keeps the portrait on the left and a scrolling list of six swatch rows on the right.
  The pad row notes when it is hidden under a rug.
- **Journal.** "Fashion Show": dress a horse in four different pieces at once.
- **Review.** `TackPreview` renders all six pieces on every rig; it now takes `--family`, `--shapes`, `--anims`, `--views`, `--layer`
  and `--coat`. Evidence is in `docs/visual/evidence/tack-2026-10-14/`.
