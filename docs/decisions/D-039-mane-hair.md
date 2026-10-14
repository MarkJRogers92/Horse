# D-039 — Procedural mane and tail locks

Owner request 2026-10-14: implement the supplied mane-hair handoff on the dot cloud computer, with visual review before any push or merge. Work is isolated on `gpt/mane-rework` from `40912902b4de3327de3807071f6264c065f6b319`.

## Problem and measured source geometry

The existing shader treats UV coordinates as root-to-tip hair-card coordinates. The source geometry does not consistently satisfy that contract. Adult manes use a planar projection (across the horse and along its length); this is not a root-to-tip coordinate. High-contrast silver locks consequently read as broad patches. A shader adjustment alone cannot break up the smooth silhouette.

The source families differ: adult manes and tails are sculpted volumes, while the two foal families share a narrow crest strip, separate forelock and small tail. The handoff's claims of universal auto-unwraps and an unmanaged-bone forelock were not assumed correct. Inspection finds the foal forelock predominantly weighted to Head, and the extra crest bones inherit neck movement; adding another rotation could double-transform them.

## Implementation boundaries

- Add runtime-generated, deterministically seeded, skinned card layers while preserving the original opaque hair cores and their vertices for coverage and tack landmarks.
- Give generated cards a deliberate across-card/root-to-tip parameterization and tangent frame. Preserve all hair recipe and pattern parameters.
- Preserve source assets, coat maps, all body-shape channels, save format, domain code, genetics and shared contracts.
- Support both four- and eight-influence surfaces and source blend shapes. Generated cards receive the source mesh's morph values after presentation applies them.
- Keep generated mesh caching bounded and release cached references with presenter caches.
- Keep opaque underlayer shading quiet rather than interpreting its projected UVs as individual locks.
- Exclude generated hair from muzzle measurement, retain hair materials in clay review, and leave tack placement based on the original crest.

## Verification and review

The refined source is locally verified and preserved at the reviewed appearance. Fresh validation passes: 319 domain tests, validator OK, warnings-as-errors build (0 warnings/errors), 1,465,366 hair checks, 3,708 transition checks and 95,442 yard checks. Rendered smoke and stable day/lifecycle/breeding captures also completed on the final source. Source hashes match the render snapshot; a later rebuild has its own binary hash.

The initial motion-only test was insufficient: adding edge-length measurements found up to 21.93x stretching in full grazing. Lock-wide root-coherent skinning and morph transfer removed that failure in the tested cases. The failing and passing measurements are retained. These checks establish tested deformation bounds, not precise attachment, self-intersection freedom, silhouette quality or physical hair simulation.

A paired eight-horse 1280x720 llvmpipe software-renderer sample measured median frame time 291.96 to 370.81 ms (+27.0%), p95 +32.3%, draw calls 70 to 102 and primitives 306,230 to 417,974. The stable has six visible slots; this eight-horse sample is a stress comparison. It is not a target-Mac measurement or a cleared performance gate. No distance LOD exists on the new cards. A distant-card cutoff/fade retaining the original core is the smallest recommended follow-on optimization, subject to close-up/transition review and target-hardware measurement.

Known visual limits: the opposite side still shows the smooth original core; extreme foal grazing fans locks into visible steps. The main-side result approved for further polish is preserved. Review: `docs/visual/evidence/mane-hair-2026-10-14/REVIEW.md`.

The attachment reference images are preserved in `docs/visual/evidence/mane-hair-2026-10-14/`. New evidence is separated from the supplied reference images. The final capture sets cover all four families, Idle/Walk/grazing, body-shape changes, pale and dark hair, and dress-up placement. The owner must see the visual results before publication.

Meshy is optional with an owner-approved maximum of 150 credits for this task; no Meshy spending has been needed.
