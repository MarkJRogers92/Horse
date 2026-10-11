# Eight-weight Godot compatibility renderer probe

This isolated synthetic probe checks whether Godot 4.7.2's macOS `gl_compatibility` renderer uses influences from both `JOINTS_0/WEIGHTS_0` and `JOINTS_1/WEIGHTS_1`. It does not test horse anatomy, production weighting, locomotion, hoof contact, or performance.

The source `eight_weight_probe.gltf` is one flat quad with eight separate joints. Each vertex has eight float32 weights of 0.125. The imported Godot mesh exposes eight bone slots per vertex; each imported weight is 0.1249866486 (sum 0.9998931885), and the scene has eight named skeleton bones. At runtime the test translates only joints 5–8 by +0.8 m in X. If all eight equal weights affect the vertex, the expected translation is +0.4 m.

Four 640x480 captures share the same orthographic camera and unshaded material: imported rest, imported posed, an independent unskinned +0.4 m reference, and an unskinned zero-shift first-four-only reference. The imported posed silhouette matches the +0.4 m reference exactly (IoU 1.0; zero differing pixels) and shifts 74 px from rest. It differs clearly from the first-four-only result (IoU 0.4264; 74 px center separation).

Godot reported OpenGL API 4.1 Metal on Apple M2 using the compatibility renderer. The actual capture commands are recorded in the per-mode logs and exit-code files. `renderer_report.json` contains hashes and checks. Captures and logs are retained beside this note.
