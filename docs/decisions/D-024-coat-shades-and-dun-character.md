# D-024 — Individual coat shades and dun character

Owner request 2026-10-13: "take a look at our procedural UV mesh program and see where we can improve…
research actual horses and coats to make a system that better makes these coats on demand."

## How coats are drawn today (reviewed)

- **Shader:** `game/shaders/horse_coat.gdshader` draws every coat from a per-class recipe in
  `game/data/visual_catalog.json`.
- **Vertex masks** (COLOR), baked by `tools/horse/import_reference.py` `body_attributes`:
  - **R:** points (lower legs, ear rims);
  - **G:** soft-nose and eye skin;
  - **B:** face-front on the head, dorsal midline on the trunk;
  - **A:** belly countershade.
- **UV2 canonical coordinates:**
  - **head:** poll-to-muzzle s, lateral t;
  - **legs:** 10 + leg index, height relative to the knee;
  - **trunk:** a sentinel (−10, 50), which means **no coordinates**.
- **Dapples:** fbm noise on UV0. UV0 is a Smart UV Project, so dapples change scale between islands and
  break at seams.

## What research and the renders showed

Sources: UC Davis VGL dun dilution, horse-genetics.com, morgancolors.com, Wikipedia (Bay, Cream gene,
Primitive markings).

| Finding | Old render | Real horses |
| --- | --- | --- |
| One shade per class | every bay identical | light copper → blood bay → dark/seal bay; light sorrel → liver chestnut |
| Bay/chestnut tone | saturated orange in yard light | browner copper red |
| Dun head | same as body | undiluted, so the head (and lower legs) read darker than the body |
| Leg barring | rings on the forearm | zebra bars on the backs of the knees and hocks |
| Double cream | near paper-white | cream/ivory; perlino and smoky cream have peachy or coffee points |

## What changed

- **`CoatLook` (game, presentation only).** Each horse gets a deterministic place in its class's shade
  range, plus a sooty amount and a dapple amount.
  - Everything comes from the descriptor's marking realization key, so a horse always looks the same and
    portrait caches stay valid.
  - Shades cluster near the classic tone; darker horses lean sooty, and paler horses lean dappled.
  - This is the existing `shading_modifiers` artistic layer. Genetics are untouched.
- **Recipes** gain `bodyRange`, `sootyRange`, `dappleRange` and `dunHead`. Bay, chestnut and the cream
  family were recalibrated against references.
- **Shader:**
  - a dun head mask (strongest over the face);
  - a crisper, slightly wavy dorsal stripe;
  - leg barring moved to a knee/hock band with 3–4 soft bars.
- **`scenes/tools/CoatGallery.tscn`** renders every coat, with `--variants N`, `--views` and
  `--lighting stable`. Before and after sheets are in `docs/visual/evidence/coats-2026-10-13/`.

## Recommended next steps (not done yet)

1. **Trunk canonical coordinates.** Bake a rest-pose body coordinate into a vertex attribute for the trunk
   (for example UV3 = normalised barrel position, or CUSTOM0 = rest position), so that body patterns stop
   depending on UV0 islands. This one change unlocks:
   - seamless dapples;
   - realistic sooty gradients (topline → flank);
   - a dun shoulder stripe;
   - pangaré/mealy, flaxen-to-body blends;
   - phase-2 and phase-5 patterns: tobiano/sabino masks, roan (solid head and legs), grey age curve,
     appaloosa blanket.
2. **Mane and tail per-horse variation:** flaxen tint on chestnuts, frosting on duns.
3. **Hair-level detail:** a small tiling hair-flow normal map in rest-pose body space, instead of
   UV0-scaled noise.
4. **A reference-calibration pass** using sampled photo swatches under neutral light, with
   `CoatGallery --lighting neutral` as the comparison view.
