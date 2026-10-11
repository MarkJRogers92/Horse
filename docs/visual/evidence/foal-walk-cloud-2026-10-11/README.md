# Foal neutral Walk preview

This is an editable, checked walking preview of the existing genuine juvenile model. It preserves its original geometry, normals, primary and Canonical UVs, coat-mask colors, and all four morph targets. The source foal and existing game behavior are unchanged. This is a neutral-shape preview with softer shoulder/knee compression than the Stock adult, not a claim of finished production animation or owner art approval.

## Playback and import
- Open horse_foal_walk.blend with Blender 5.2.2 or later. Play frames 0–58 at 60 fps.
- For Godot, set animation/fps to 60 before importing or reimporting the GLB. The usual 30 fps bake exceeded the unchanged 5 mm planted-foot slip limit. The checked import uses 60 fps.
- Explicitly set Walk to LOOP_LINEAR. glTF does not encode the application's loop flag.
- Keep all four morph weights at zero and scale at one. Move the actor once along +X at 0.3448275862068966 m/s. Duration is 58/60 seconds. The skeleton stays horizontally in place and has a small vertical body bob.
- Continue using the existing foal coat material/recipe and its own masks/UVs. No Light atlas was copied. Inspection renders use a plain material.

## Evidence
- Four-beat walk with a 20 cm stance excursion and 66 mm peak swing lift, sized to the foal. The original separate limb chains receive solved poses in their own rest space. Smooth local limb membership and medial-chest attenuation repair the original shoulder weight discontinuities; all geometry and coat data stay untouched.
- Source: 61-phase contact/loop checks and 581-phase dense deformation check. Maximum Body edge stretch is 1.990931 under the unchanged 2.0 limit. This margin is narrow; morph extremes are not accepted. Weight sums differ from one by less than 4.7e-8, with at most five influences.
- Hair attachment: 235 fixed Body-triangle witnesses over 61 phases; maximum attachment-gap drift is below 0.030 mm. The original tiny surface offsets remain unchanged.
- Actual Godot 4.6.3 at 60 fps: all 176 sole witnesses resolve through Skin binds; 61-phase stance, ground, swing and loop gates pass. Worst stance motion is 2.942 mm, floor penetration about 0.021 mm, central swing clearance exceeds 41.36 mm. Full imported-skin Body edge stretch is 1.98921. Four components and four morphs each are retained; Walk visibly advances and closes.
- Exact original-GLB comparison: zero error at every corner for positions, normals, UVs, coat-mask colors and all morph positions/normals.
- Final-hash side/quarter renders and six chest closeups were inspected. Some soft knee/shoulder compression remains; this is suitable for reviewing the initial neutral walk, not animation polish acceptance.

## Scope and recheck
No .NET game integration, application coat render or new Mac build is included. Original assets and default Stock/foal Walk guards remain intact. Graze and extreme-morph walking are untested. No new generation, payment, public release or main merge.

From a minimal Godot project:

godot --headless --path . --script evidence/check_walk.gd -- horse_foal_walk.glb new-runtime-report.json walk-motion-profile.json

The harness honors the profile's 60 fps bake. Run the project's tools/horse/derive_foal_walk.py in Blender background mode to reproduce the candidate; tools/horse/audit_walk_candidate.py and audit_foal_attachments.py reproduce the dense source and attachment checks. These read the original project sources and write only separate candidates/evidence.
