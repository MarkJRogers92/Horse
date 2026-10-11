# Foal Smart Rig retry — isolated limb deformation check

Task ID: `horse-production-motion-20261011`
Rig source SHA-256: `6b5961323edb18dc795f5d54a0b65bfdd9e47bc1661677fc2989f22e8a670d0a`
Source: `art/candidates/meshy/foal_01/smart-rig-retry.glb`

This is a bounded source-rig adapter check. It imports the retry Smart Rig,
keeps its transformed rest joint positions, canonicalizes labels, reparents the
four limb chains, and projects the source skin weights onto a target made from
the same Smart Rig surface. The target is a fixture (23,416 vertices, scaled to
1.1 m); its nearest-surface projection distance is therefore exactly zero.

The neutral and four one-limb poses are shown from side and three-quarter views.
Each pose rotates one shoulder/hip root by +30 degrees about the horse's lateral
axis. The corresponding hoof rises while the other three limbs stay in stance.
This demonstrates that the retry has controllable bilateral hind limbs and that
the hierarchy correction separates the forelegs from neck motion.

The source rig has 60 joints; the adapter adds one non-deforming `Root` for 61
bones total. Surface weights use at most four influences, have a maximum
normalization error below `6e-8`, and show 6 clear-lateral vertices (of 23,416)
with more than 8% weight assigned to the opposite side. Those six sit close to
the medial seam between paired legs. See `report.json` for the canonical names,
reparent changes, and numeric checks.

**Scope limit:** this check does not validate transfer onto the builder's final
family-warped/decimated foal mesh. It does not establish shoulder or haunch
crease quality, ground contact under loaded poses, gait, or animation readiness.
Walk and graze clips remain excluded.
