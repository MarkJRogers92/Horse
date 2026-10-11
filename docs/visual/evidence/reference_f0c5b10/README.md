# Authored horse integration evidence — f0c5b10

Code commit: `f0c5b101765bb764d3108d379031c7474e44326e`, branch `claude/godot-horse-visual-pilot`.

[Visual CI 38099550595](https://github.com/MarkJRogers92/Horse/actions/runs/38099550595) passed the descriptor check, asset rebuild, C# build (0 warnings/errors), Godot import, actual-renderer main-scene smoke and **131 captures with 0 capture failures**. [Domain CI 38099550598](https://github.com/MarkJRogers92/Horse/actions/runs/38099550598) passed **147/147 tests**. Downloaded logs and actual images were inspected; independent read-only review found no material correction.

## Inspect the replacement

- [Clay comparison](comparisons/Clay_Comparison.png): previous placeholder versus authored adult and juvenile adaptation, same whole-body camera settings.
- [Coat comparison](comparisons/Coat_Comparison.png): bay, palomino, cremello and grullo, both ages; existing recipes and stage lighting remain the same.
- [Facial markings](comparisons/Markings_Comparison.png): fresh Claude face-fix artifact versus the new mask mapping. Head-camera targets changed to fit the new anatomy, so this is a placement comparison.
- [Complete coat sheet](sheets/coat_contact_sheet.jpg), [clay sheet](sheets/clay_sheet.jpg), [markings sheet](sheets/markings_sheet.jpg) and [unsupported probes](sheets/probes_sheet.jpg).
- `highlights/` preserves selected original PNGs. `capture_report.json`, `verification.json` and both manifests preserve machine-readable provenance and checks.

The baseline is [run 38095905278](https://github.com/MarkJRogers92/Horse/actions/runs/38095905278), `1124b73`; the attempted facial fix is [run 38096698518](https://github.com/MarkJRogers92/Horse/actions/runs/38096698518), `236f36d`. All three CI artifacts report the same llvmpipe/LLVM 20.1.2 renderer. Their complete raw PNG sets remain in their `visual-evidence` Actions artifacts, subject to 30-day retention. This repository checkpoint stores selected PNGs and JPEG contact sheets rather than duplicating every raw capture. `SUMMARY.md` preserves the artifact's original relative PNG paths.

## Findings and limits

Both runtime ages use the selected CC0 anatomy. The foal has a shallower, shorter barrel, proportionally long legs, shorter muzzle, smaller hooves and short hair. The authored adult shows defined head, shoulder, limb and hoof forms compared with the placeholder. The new face captures show forehead stars, a central strip and muzzle snip; the earlier cheek-placement error is absent in inspected examples. Matching adult/foal marking keys remain deterministic.

The 65 coat-sheet images cover 13 fixture classes at both ages and stage lighting presets. Three unsupported probes show no substitute horse. The report records four sampled animations as unavailable; these are static rigs, not evidence of gait quality. Standard and compact IDs share one cob build within each age, and only descriptor height is applied. No coat-matrix rows were approved and no live domain catalog was enabled.

CI's bpy build logs a missing optional MeshOptimizer library. Godot reports absent audio devices and uses dummy audio. Exports, import, smoke and capture completed successfully; this is not a claim of error-free logs. Geometry/contract data in the CI reference manifests match the local manifests except build time.

The matching foal source and same-scale orthographic turnaround are in `art/derived/Horse_Reference/`. `Foal_Editable_Validation.json` records a Blender reopen and small head/foreleg/tail FK exercise; it does not certify animation or deformation under every pose.
