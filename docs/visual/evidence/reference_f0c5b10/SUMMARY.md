# Cloverbrook visual evidence

- Godot: `4.7.2-stable (official)`
- Renderer: `llvmpipe (LLVM 20.1.2, 256 bits) / 1.4.318`
- Visual catalog: `reference-assets.v1`
- Images: 131 · capture failures: **0**

> Stills prove materials, markings and composition only - not animation quality, not art approval.
> No coat-matrix row is approved by these renders.

## Mesh sources
- `horse.standard.adult.v1`: `res://assets/horse/reference/horse_adult.glb` (placeholder: False)
- `horse.compact.adult.v1`: `res://assets/horse/reference/horse_adult.glb` (placeholder: False)
- `horse.standard.foal.v1`: `res://assets/horse/reference/horse_foal.glb` (placeholder: False)
- `horse.compact.foal.v1`: `res://assets/horse/reference/horse_foal.glb` (placeholder: False)

## Unavailable animations (skipped)
- `walk`
- `trot`
- `look`
- `idle`

## Sheets
- `sheets/coat_contact_sheet.png`
- `sheets/markings_sheet.png`
- `sheets/clay_sheet.png`
- `sheets/turntable_sheet.png`
- `sheets/detail_sheet.png`
- `sheets/pair_sheet.png`
- `sheets/stable_sheet.png`
- `sheets/probes_sheet.png`

## Unsupported probes (expected refusals)
- `probe.grey` → UNSUPPORTED: coat layer 'grey_age' has no renderer implementation; grey progression is not implemented
- `probe.unknown-recipe` → UNSUPPORTED: coat recipe 'eumelanin_points_pheomelanin_body:champagne' is not in the visual catalog
- `probe.unknown-mesh` → UNSUPPORTED: mesh family 'horse.draft.adult.v1' has no asset

## Coat classes rendered
- bay: Bay with primitive markings — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- bay: Bay — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- bay_dun: Bay dun — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- black: Black — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- buckskin: Buckskin — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- chestnut: Chestnut — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- cremello: Cremello — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- grullo: Grullo — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- palomino: Palomino — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- perlino: Perlino — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- red_dun: Red dun — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- smoky_black: Smoky black — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
- smoky_cream: Smoky cream — Supported with limitations: morphs not applied: body_length,chest_width,mane_density,muscle,neck_length,tail_density; test-only descriptor; animation 'idle' unavailable; static pose
