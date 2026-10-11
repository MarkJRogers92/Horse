> Superseded by the polished Stock Walk in candidates/stock-walk-polish. A later all-component audit found eight inherited unweighted Tail vertices; the polished asset repairs them and improves 60 fps contact. Keep this earlier checkpoint for provenance.

# Stock horse Walk, checked neutral asset

Editable Blender 5.2.2 source and game-ready GLB derived from the existing Stock adult. The original Stock geometry, all four morph targets, normals, UV0/Canonical UVs and coat-mask colors are preserved exactly. The checked Light Walk supplies the motion/skin; rest pivots are fitted to Stock's own proportions. The Light-only coat atlas is not reused.

## Playback
- Open horse_stock_walk.blend in Blender 5.2.2 or later and play frames 0–29 at 30 fps.
- Import horse_stock_walk.glb into Godot. Select Walk and explicitly set loop_mode to LOOP_LINEAR; glTF does not encode the application's looping flag.
- Use neutral morph weights (all zero) and unit scale. Move the actor forward along +X at 0.749997329711914 m/s, once only. The skeletal clip is in place. Loop length is 29/30 seconds.
- Body, Eyes, Mane and Tail retain their usual interfaces. Keep the Stock procedural coat materials/recipe using original UV/color data. The supplied renders use a plain inspection material, not a new coat texture.

## Checked
- Source: 61 phases; maximum Body edge stretch 1.470871; 222 exact Body/hair boundary pairs remain closed; exact first/last-frame geometry; all 429 source sole witnesses preserved.
- Actual Godot 4.6.3: Walk advances; four components and four morph targets per component; all 429 sole witnesses resolve through imported Skin binds; 61-phase contact checks pass. Worst per-axis stance motion 3.837 mm, worst ground penetration 1.30 mm, central swing clearance about 60 mm. Endpoint bone error below 0.001 mm.
- Every exported corner matches the original Stock GLB exactly for geometry, normals, both body UV channels, coat-mask colors and all morph position/normal data. See reports.
- Actual side and quarter frames were rendered and inspected. Rig/skin data intentionally changes; exact imported weight equality is not claimed.

## Scope
Neutral shape walking only. Extreme morph walking, .NET game integration, application coat rendering and an updated Mac build were not tested here. The existing Light and source Stock/foal assets and the game's Stock/foal Walk guards remain unchanged. No main merge or public release.

## Recheck
From a minimal Godot project, run:

godot --headless --path . --script evidence/check_walk.gd -- horse_stock_walk.glb new-runtime-report.json walk-motion-profile.json

For reproduction in the project checkout, run Blender in background with tools/horse/derive_stock_walk.py. It reads the frozen Stock and Light Walk sources and writes separate candidates; explicit named Color export is required to preserve coat masks.

The motion donor ultimately uses Mesh2Motion's CC0 horse rig/base timing, adapted and checked for the existing Meshy Stock geometry. See the enclosed donor license and provenance.
