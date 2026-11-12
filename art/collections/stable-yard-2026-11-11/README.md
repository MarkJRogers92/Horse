# Cloverbrook — Stable and Yard Asset Collection

30 textured static props, created November 11, 2026.

## Start here

- Open `Preview-Collection.jpg` for the whole collection.
- Open `Browse-Assets.html` in a browser for front/rear pictures and notes.
- Import `project.godot` into Godot 4, then run the project to browse the models. Left/right arrows or the buttons change assets; drag to rotate; mouse wheel zooms.
- To use a prop in Cloverbrook, copy its GLB from `models/` into the game's assets folder and drag it into a scene. Textures are embedded in each GLB. One unit is one metre; Y is up; the origin is centered on the ground footprint.

The gallery is a separate inspection project. This pack does not change the existing game, saves, horse models, coats, animation, or production branch. Props are static: no gameplay interactions, collision shapes, wheel rigs, adjustable jump rails, or lantern light sources are included.

## Collection

Stable supplies: wheelbarrow, grooming case, lidded feed barrel, mounting block, tack trunk, boot scraper, feed scoop, pitchfork, push broom, muck tub, wash caddy, hose reel, water bucket, feed sacks, and feed wagon.

Furniture and yard details: hitching rail, bench, lantern, notice board, horse weather vane, flower planter, birdhouse, and blanket rack.

Training equipment: log jump, training jump, cavaletti, jump filler, cone, wooden arena marker, and pasture gate.

## Generation and cost

Meshy T2.5 Smart Topology geometry, followed by 2K PBR texturing. Thirty geometry tasks at 5 credits and thirty texture tasks at 10 credits used exactly **450 credits**. No paid retries, remeshes, purchases, or conversions were used.

`catalog.json` records each asset's task IDs, cost, actual triangle count, dimensions, and review notes. `reports/` records model hashes and geometry checks. These records contain no credentials or signed download links.

## Inspection and preparation

All 30 models were inspected from front and rear using shaded base-color renders. These previews use the models' actual geometry and textures; they do not render the normal, roughness, or metallic maps. Original 2K PBR maps remain embedded in the delivered GLBs.

The collection totals **125,318 triangles**, ranging from **1,839 to 6,621** per asset. Smart Topology targets are approximate; the catalog records measured counts. This is an asset count, not a whole-game performance guarantee. Texture memory still matters when placing many different props simultaneously.

Twenty-nine GLBs preserve their original binary geometry/texture payload and add only a scale/translation root. The log jump needed a local geometry repair: its log was lowered and supports moved inward beneath it. No texture or UV changes were needed. All models have finite coordinates, valid indices, UVs, embedded base colors, and measured ground placement and height. See `godot-validation.json` for the engine import results.

## Known limitations

- Several generated shapes differ from their prompts; names in this pack describe the delivered objects. The mounting block has two steps. The feed bin became a timber barrel. The jump includes its rails. The feed cart became a four-wheel wagon.
- The lantern has irregular thin side rods; use it as a background prop or clean those rods before a close-up.
- The weather vane's compass lettering is decorative and imperfect.
- Tiny stray surface fragments remain near the wheelbarrow handles.
- The arena marker is a blank timber form. Add any required dressage letter as a separate sign or decal.
- Hinges, handles, caps, straps, and other generated details are decorative. Do not assume they are separate riggable parts.
- The Godot gallery is an inspection aid, not an integrated game release. Runtime lighting may differ from the preview sheets.

## Future editing

Import any GLB into Blender to edit it. `prepare_portable.py` records the preparation and CPU preview procedure; it requires Python, NumPy, Pillow, and the original source files. Original downloads are retained in the working folder's `source/` directory but omitted from the ZIP because the normalized models already embed the same geometry and texture content, except for the documented log repair. Use task IDs for any still-available Meshy source download; do not pay to regenerate merely to obtain another format.
