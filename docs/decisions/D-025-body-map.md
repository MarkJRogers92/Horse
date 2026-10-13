# D-025 — Body map: continuous coat coordinates over the whole horse

Owner request 2026-10-13: "yes start the body map" (roadmap item 1 in D-024).

## Problem

There was no usable coordinate across the trunk:

- UV0 is a Smart UV Project, so trunk patterns changed scale and broke at island seams.
- UV2 only covers the head and legs; the trunk carries the sentinel (−10, 50).
- The dorsal mask (COLOR.b) is a hairline on the very top of the back, so the dun stripe could not be seen from the side.

Because of this, dapples showed seams, sooty shading was a hard band, and pinto, roan, appaloosa and grey patterns could not be drawn.

## Decision

`game/scripts/Visual/BodyMap.cs` adds a rest-pose body coordinate to every Body vertex, in `CUSTOM0`. It runs at
presentation time, once per imported mesh resource, and the result is cached. No asset, GLB, Blender script or
save change is needed, and it works for all four mesh families, the walk variants and the foal.

`CUSTOM0 = (u, h, s, 1)`:

- **u** runs along the body: 0 at the buttock, 1 at the elbow line (the mean of the front-leg tops). The chest, neck
  and head continue past 1.
- **h** is height: 0 at the belly, 1 at the topline of the back, both measured over the barrel only. The legs go
  below 0 and the neck rises above 1.
- **s** is the side: −1..1 across the widest part of the barrel.

The landmarks come from the canonical UV2 regions (head, the four leg indices, trunk), so the same numbers mean the
same place on adults and foals. The forward direction is taken from the hind-leg tops towards the front-leg tops.

How it is applied:

- The coordinates come from the mesh's own vertex positions before skinning and morphs. Patterns therefore stay
  glued to the skin while the horse walks or changes shape.
- The rebuilt mesh keeps the vertex data, skin weights (including 8-weight surfaces), blend shapes and their mode,
  and the materials.
- The shader receives `use_body_map` and `body_aspect`. `body_aspect` gives body-length units per axis, so spots
  come out round.
- If a mesh lacks the canonical regions, it keeps its original mesh and the shader falls back to the old UV0
  path.

## Shader uses (`horse_coat.gdshader`)

- **Dapples:** 3D cellular noise on the body map. They are round, lighter spots of varying size with slightly darker
  hair between them. They are richest over the barrel and hindquarters, fade down the legs, along the neck and on
  the head, and never meet a seam.
- **Sooty:** darkens from the topline downwards (h), mottled by 3D noise, over the trunk and lightly on the head,
  never on the legs. It is stronger than before (to ×0.42), because real sooty horses can be very dark along the
  top.
- **Dun dorsal stripe:** about 5 cm wide (|s| < 0.1 of the barrel half-width), running over the back and croup from
  the withers to the tail head. It now reads from the side.
- **Dun shoulder stripe:** a faint bar across the withers, only on strongly marked duns.
- **Leg bars:** moved to the forearm above the knee and around the hock (canonical leg height 0.46–0.84; height 1 is
  the elbow/stifle line, not the knee as D-024 assumed). They are broken up by noise so they are uneven rather than
  rings.
- **Debug views:** 3 shows the body-map grid; 4 shows the primitive-marking masks (R dorsal, G bars, B shoulder).

## Tools

`scenes/tools/CoatGallery.tscn` gains these options:

- `--only class,class` limits the classes;
- `--family <meshFamilyId>` swaps the mesh family (for example a foal);
- `--debug N` sets the debug view;
- `--set dapple=1,sooty=0.6` overrides float uniforms.

## What this unlocks next

The body map is the base for the phase-2 and phase-5 patterns. These need domain or genetics work first and are not
implemented in play:

- roan: a per-hair white mix that spares the head and legs;
- tobiano and overo pinto: large 3D-noise white fields, placed by u, h and s;
- appaloosa: a blanket over the hips, with leopard spots;
- progressive greying: dapple grey, then flea-bitten.

Each needs only a coat recipe flag and a shader branch, with no asset work.

## Verification

- Body-map grids were rendered on the adult and the foal, with no seams.
- Bay, chestnut and dun renders were checked with dapple, sooty and primitive-marking overrides.
- Smoke test, three stable captures, `-warnaserror` build and 229 domain tests (see STATUS for this checkpoint).

Evidence: `docs/visual/evidence/bodymap-2026-10-13/`.
