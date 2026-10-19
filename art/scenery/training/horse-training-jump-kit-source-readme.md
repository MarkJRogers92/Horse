# Horse training jump kit

Original Blender-authored modular training obstacle in the existing Horse cream and muted-teal palette. No Meshy calls or credits, image-generation credits, external textures or third-party model inputs were used.

## Included
- `models/training-jump-kit.glb`: assembled scene, 11 independent mesh objects (2 standards, 4 cups, 3 poles, 2 cavaletti blocks), 7,340 triangles.
- `models/training-jump-kit.blend`: editable mesh source and studio preview setup. Studio lights, floor and camera are not in the GLBs.
- `models/jump-standard.glb`: left winged standard, 1,516 triangles. Rotate 180 degrees around vertical for the opposite side.
- `models/pole-cup.glb`: open-top metal cradle, 240 triangles. Rotate 180 degrees around vertical to face the opposite side.
- `models/striped-pole.glb`: 3 m × 0.10 m reusable cream/teal pole, 828 triangles.
- `models/cavaletti-block.glb`: crossed wood block, 432 triangles.
- `previews/training-jump-kit.png`: rendered overview, visually inspected.
- `qa/geometry.json`: component dimensions, triangle counts and scene pivot coordinates.
- `qa/godot-import.log`: Godot 4.6.3 import and material/mesh checks for all five GLBs.
- `build_jump_kit.py`: reproducible Blender build script; change R to your destination before running.

## Scale and placement
Meters; Blender Z-up, standard glTF export converts to Godot Y-up. Individual exports have ground-plane placement pivots (standards centered on the post), unit scale, and are ready to instance. Assembly root sits on the ground; its component nodes preserve their independent bottom pivots.

Standards are 1.63 m tall, 3.24 m apart at post centers. The two jump poles have center heights of 0.70 m and 1.10 m. The low cavaletti pole center is 0.342 m. Poles run along local X. To adjust jump height, move the matching cups and pole together; front socket markers are decorative.

Three exported solid PBR materials: warm cream painted wood, muted teal painted wood and dark metal cups/hardware. No texture dependencies. Pole paint bands are separate closed mesh sections joined into one object, with intentionally coincident internal end faces; this is a visual prop, not a watertight fabrication model.

## Integration status
Import-ready prototype assets, not installed in either game. No collision shapes, physics bodies, LODs, navigation, jumping logic, animation, breakaway logic or gameplay tuning are included. Add simple primitive collisions in your game; use separate rigid bodies for poles only if needed. Test gameplay visibility and jumping clearance in the target scene before release. The source includes deliberate intersections where structural wood pieces meet.

## Validation
Blender 4.3.2 builds and renders the scene; all 5 GLBs import and instantiate in Godot 4.6.3 with non-null meshes and materials. Standalone exports rest at the ground plane. Geometry is procedural and does not depend on paid generation services.
