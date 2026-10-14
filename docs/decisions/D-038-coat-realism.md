# D-038 — Coat realism: bay and chestnut colour, splash edge, silver mane

Follows D-037. Three presentation-only fixes found in the D-037 renders. No domain, genetics or save change.

## Decision

1. **Bay and chestnut colour** (`game/data/visual_catalog.json`, mirrored in `visual_catalog_light_walk_preview.json`).
   - Bay body `#7f4224`, counter `#94532c`, range light `#94502b` to dark `#4a2615`, black points. Not orange.
   - Chestnut body `#a0522d`, counter `#b86a36`, range light `#b5642f` to liver `#5a2e1c` (deep chocolate-red).
   - The per-horse shade (`CoatVariation.cs`, D-024) is unchanged, so each horse still lands at its own place in the range.
   - Silver mane and tail (`silver_lightened_mane_tail`) move from grey-beige to creamy flaxen `#d4c19d`, tip `#e0d0b0`.
2. **Splash edge** (`game/shaders/horse_coat.gdshader`). The trunk edge keeps its crisp 0.02 smoothstep but gains an
   irregular ripple and scallop built from the raw body map (along, height, side). SW1/SW1 (`splash == 2`) now
   tops out near h 0.33 (below mid-barrel), so white covers the legs, belly, chest and broad face. The double's wave is damped to 60%.
3. **Silver mane** (`game/shaders/horse_hair.gdshader`). Variation comes from thin strands that run along the hair
   (two per card lane, wandering slightly across the card), not per-lane blobs. A share of the strands (`dark_fraction`)
   is darker. Six strands per lane looked like marbled veins at viewing size, so the density was reduced.

Bay and chestnut sit within a few per cent of the targets when measured from the renders (median barrel pixels, neutral
lighting): bay v0 about `#743c22`, bay v7 about `#49281b`, chestnut v0 about `#934d2b`, chestnut v7 about `#5b3020`.

## Notes

- `dark bay` and `liver chestnut` have no fixtures. The gallery reaches them through the per-horse shade: `bay` and
  `chestnut` variant 7 sit at the dark end of the range (shade 1.0).
- The splash wave is deliberately gentle. If the owner wants a more obvious break, raise the ripple amplitude (`ripple`, `scallop`) in the trunk branch.
- Hair geometry is unchanged. A hair-texture normal layer is out of scope and not in this decision.

## Verification

See STATUS. Evidence: `docs/visual/evidence/coat-realism-2026-10-14/coat_realism_contact_sheet.jpg`.
