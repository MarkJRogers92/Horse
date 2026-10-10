# D-002: Start the hero horse from a licensed, pre-rigged base mesh

| Field | Value |
| --- | --- |
| Status | **Accepted (trial)** by owner, 2026-10-10. The owner makes any purchase personally after reviewing a shortlist. |
| Question | Where does the Phase 1 hero horse (task P1-01) come from? |
| Chosen result | License an existing rigged horse base, then customise its masks, UVs and materials in Blender. Revisit this decision if no candidate passes the checklist below. |
| Alternatives considered | Commissioning a 3D artist (slower and more expensive; this is the fallback). Building it in-house (too slow). |

## Candidate checklist (all must pass before buying)
1. **License:** allows modification and commercial use in a sold game, and allows the mesh to ship inside a built game. Prefer the Unity Asset Store standard EULA.
2. **Formats:** FBX plus an editable source (`.blend` preferred). Textures at 2K or higher.
3. **Rig:** full quadruped skeleton (spine, neck, head, ears, 4 legs to the hooves, tail) with a sensible bone count. It must work as a Unity **Generic** rig.
4. **Topology:** clean quads, adult body about 25k–40k triangles, separate mane/tail meshes preferred, and UVs with no overlaps (so asymmetric markings work).
5. **Anatomy:** realistic proportions with a good head, eyes and hooves. A stylised/toy look fails.
6. **Bonus:** walk/trot/canter/idle animations, a foal variant, existing blend shapes.
7. **Budget:** owner to set. Typical range is $30 to $150.

## Next action
A model produces a shortlist of 3–5 candidates (link, price, license, how each scores against the checklist). The owner picks one and buys it. Asset files go in `art/source/horse/` only if the license allows storing them in the repo. Otherwise they stay local and the repo keeps a manifest entry only.
