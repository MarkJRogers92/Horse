# D-046: Curated practice-equipment nook

Owner approved integrating the prepared training props and jump kit into Horse's
practice area on 2026-10-19. This is a presentation-only continuation of D-045,
not the deferred jumping/barrel-racing gameplay systems.

Replace the old show-jump decoration behind the paddock with the authored jump
assembly, two neatly stored ground poles, a separate low cavaletti, three course
cones and one barrel. Shift the fair tent 0.5m right to preserve clearance; its
label and picking continue to use the same holder. Existing purchased decoration
spots, barn porch props, gates, horse slots and wandering remain unchanged.
The equipment sits outside the elliptical paddock, clear of the barn path and
roaming horses. Mow the small equipment footprint so low poles remain visible.

Use native metre scale and bottom pivots on the flat grass plane (Y=0). In
particular, do not normalize the modular jump using its conservative AABB:
rotated parts make that box extend below the actual feet. Keep the separate
components and editable Blender sources under art/scenery/training.

Like existing yard scenery, these are decorative meshes without colliders,
navigation regions, interaction areas, saves or rewards. Horses never approach
this nook; adding physical blockers would not affect the current transform-based
horse movement. The existing practice gallop track/replay is untouched.

The props/jump are original procedural geometry. Barrel reference-provider
commercial rights remain unverified; see the source README. No new generation
spend, purchases, external assets or changes to genetics/domain contracts.

## Verification

Offline tests validate GLB payloads, materials and exact triangle budgets. The
Godot probe instantiates the real yard and checks precise transformed vertices,
unit dimensions, ground contact, per-component separation, outside-fence
clearance, decoration-slot clearance and unchanged porch props. The dedicated
CI workflow renders both the normal yard camera and a focused equipment view.
See STATUS.md and docs/visual/evidence/training-props-2026-10-19 for final results.
