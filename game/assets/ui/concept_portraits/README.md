# Generated concept portrait originals

These original Higgsfield Soul 2.0 downloads were saved for the owner's Horse project on 2026-10-18. Open **Extras → Concept portraits** from the main menu to view the three horse studies. The stable-courtyard image supplies that gallery's softly tinted backdrop.

## Scope and quality

- These are illustrations for review, not portraits assigned to saved horses and not a genetic or foal prediction. The existing `HorsePortraitStudio` continues rendering each real horse from its descriptor.
- Original PNG bytes are preserved. `catalog.json` records SHA-256 hashes, prompts, dimensions, settings and observed defects. No alpha removal, repainting, image generation or paid service was used for integration.
- The palomino has baked white rounded corners; the dark horse has dark corners and an unrequested forehead star; the foal has a baked light frame and a frontal pose. The gallery displays those originals without cropping or distorting them.
- The courtyard original includes an unwanted fake menu at its bottom and distant horses. An `AtlasTexture` displays only `[x=0, y=0, width=1696, height=730]`, with filtering clipped to that region. Fake text is never displayed as real game controls. This is a runtime presentation crop, not an edited source image.
- Generation/account evidence does not establish commercial rights. Service terms, the generating account's entitlement and any relevant content rights must be checked before a commercial release. `commercial_release_cleared` remains false. No CC0 or other license grant is asserted.
- The bay reference is not a runtime portrait and is intentionally not included here.

## Verification

Run `python3 -m unittest discover -s tools/portraits -v` for original hashes, dimensions, provenance and integration boundaries. With the pinned Godot 4.7.2 .NET project built and imported, run:

`Godot --headless --path game --script "$PWD/tools/portraits/check_gallery.gd"`

For rendered wide/compact screenshots use the same probe without `--headless`, adding `-- --out=/absolute/output/folder`. The visual CI workflow runs it under Xvfb/Vulkan and includes its output in `visual-evidence`.
