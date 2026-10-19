# Horse training props

Three original stylized training props in matching muted teal, warm cream and aged brass. Texture-free, self-contained GLB files, editable Blender source files and rendered previews.

## Included models

| Model | Triangles | Godot X × Y × Z (metres) |
|---|---:|---|
| ground-pole | 764 | 3.000 × 0.110 × 0.110 |
| low-cavaletti | 1196 | 3.000 × 0.444 × 0.505 |
| rounded-course-cone | 1000 | 0.390 × 0.500 × 0.390 |

Each prop uses one mesh object, three material surfaces and a bottom-centre origin. One scene unit equals one metre. Blender source uses Z up; exported GLB uses Y up. These are static environment props without rigs, animations, collisions or gameplay scripts. Pole lengths are 3 m; cone height is 0.5 m.

## Import into Godot

1. Copy the three individual GLB files from models/ into your project's asset folder.
2. Let Godot import them, then instance them in your scene at unit scale.
3. Place the node at ground level using its bottom-centre pivot. Add project-specific colliders and behaviour as needed.

GLBs contain only the prop meshes and embedded material values, with no external images, buffers, camera or lighting dependencies. Blender files also contain a clearly named STUDIO_PREVIEW_ONLY collection for presentation. Do not export that collection into the game. training-props-showcase.blend is a presentation scene, not a separate game asset.

## Validation

Fresh tests passed with Blender 4.3.2 and Godot 4.6.3:

- Source topology: no boundary or non-manifold edges, inconsistent normals, degenerate faces or loose vertices; positive volume for each closed component.
- Each asset remains below 2,000 triangles.
- GLB round-trip: valid glTF 2.0 binary, matching dimensions/triangle counts and bottom-centre origin; finite vertex coordinates and no external dependencies.
- Isolated Godot headless load: GLTFDocument load, scene creation, exact vertex-buffer geometry checks and PackedScene instantiation pass for all three.
- All individual previews and the combined showcase were visually inspected.

Assembly components intentionally overlap; they are not boolean-unioned. No actual game repository integration, Godot rendered screenshot, performance benchmark, collision test or real-world equipment certification is included. Visual previews were rendered in Blender.

JSON results and reproducible scripts are in validation/. To rerun source audit without changing geometry: blender --background --python validation/recover_audit.py. Then run blender --background --python validation/verify_glb_roundtrip.py. For Godot, run godot --headless --path validation/godot --script verify_import.gd -- /absolute/path/to/horse-training-props. In a restricted environment, set HOME and the XDG data/config/cache directories to writable temporary paths first.

The Godot checker defers execution until the scene tree is ready and checks raw surface vertex buffers, avoiding coordinate snapping in Mesh.get_faces().

## Provenance and contents

Generated procedurally for this asset workflow with no downloaded geometry or textures and no paid generation service. build_training_props.py contains the editable construction recipe; running it rebuilds outputs. Existing model geometry was preserved during final recovery/QA.

Files: models/ for GLB and Blender source; previews/ for PNG images; validation/ for tests and audit evidence; manifest.json for model metadata; SHA256SUMS.txt for file-integrity checks.
