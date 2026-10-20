# D-047 — Cohesive generated illustrations

Owner request (2026-10-20): analyze all generated pictures and place them cohesively in the game. This implementation starts at playable main e0c7351, not the repository's older default domain-only branch.

## Scope

- Preserve source bytes. Ship only 45 visually accepted illustrations, including the five corrected derivatives; the complete 75-file audit (69 outputs, five corrections, one recovery duplicate) lives in docs/art. Twenty-two correction-needed images and duplicate/superseded versions are not shipped in the new collection.
- Reuse Extras → Concept portraits with four categories and six items per page, so the collection never eagerly loads 45 full-size textures. All horses are contained in their original square. Source frames are retained where removing them would risk anatomy.
- Three existing dialog headers receive a full-square scene beside native text: sale → marketplace, practice → covered arena, daily review → courtyard. Their original heights are retained.
- Existing dialog chrome gets a contextual thumbnail: breeding planning, nursery, tack room/shop, customization/dress up, and turnout/pasture. These do not increase dialog height or replace the actual horse previews mounted in breeding/birth headers.
- Keep CozyTheme's established cream/rose/mint palette, font and buttons. No second UI style or baked image text is introduced.
- All saved horse portraits, genetic data, breeding outcomes, care actions, 3D gameplay, save schema and domain code remain unchanged. Library breed names describe artistic intent, not registry conformation or genetic proof. Four unsuitable adult breed studies remain held out.

## Provenance / release

Catalog includes SHA-256, dimensions, source vs corrected derivative and visual review notes. No new generations or paid credits. Commercial-release rights remain unverified; do not infer a permissive license or ship a commercial release from this review branch. All excluded sources remain in the user's original Library archive, not the runtime payload.

## Verification

Focused tests failed before implementation (catalog/helper absent), then passed after. Existing original-integrity tests and frozen-spec validation pass. Build/runtime outcomes are summarized in the review PR. Raw local validation logs are not part of the published artwork change.
