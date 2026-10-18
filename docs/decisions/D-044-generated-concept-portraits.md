# D-044 — Preserve generated art in a review gallery

Owner request, 2026-10-18: save the produced art locally or to GitHub and put it into the game with a hands-off workflow. This bounded integration starts from the current playable `main` at `93f569bd5d696df3aff74439f92183ca6e13c3b0`; the repository's default branch still points to the older headless domain work.

## Integration

- Add **Extras → Concept portraits**, a read-only gallery of the three generated horse studies using the existing CozyTheme. Back and Escape return to the title menu; repeated Back is guarded. Narrow layouts stack and scroll.
- Preserve the original PNG downloads in `game/assets/ui/concept_portraits/` with SHA-256, dimensions, model/settings, prompt provenance, defects and unverified commercial-release rights in `catalog.json`.
- Use the clean upper 1696×730 region of the new stable-courtyard original as the gallery backdrop, through Godot's `AtlasTexture` with clipped filtering. The bottom fake menu remains only in the preserved source PNG and is never presented as functional UI.
- Retain existing descriptor-based portraits for owned horses, breeding, care and Details. These three generic images cannot reliably represent an arbitrary horse's coat, markings, sex, life stage or genotype. No catalog, coat-support promotion, domain record, save or 3D horse is changed.
- Do not change the title screen's live stable yard, which already shows the player's own horses.

## Release boundary

This is a reviewable source branch, not a new distributed game build. Generation cost is recorded as zero paid credits for these four originals. Commercial-use clearance remains unverified and false; no permissive license is invented. Original corner/frame defects are disclosed in the gallery. A later art decision can replace or clean these studies, but that is not required to preserve and inspect the originals now.

## Verification

The focused source/original-integrity tests were observed failing before implementation and pass after it. The runtime gallery probe verifies imported image dimensions, clean background region, disclosure text, wide/compact layout, scrolling, initial keyboard focus, repeated Back, reopening and Escape. The visual workflow records wide and compact engine screenshots. Before publication, run the frozen-spec validator, full domain suite, warnings-as-errors game build and focused probe; record actual outcomes and any rendering limitations in STATUS.
