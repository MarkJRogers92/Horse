# Initial production assessment — 2026-10-11

- Live visual base: `bedcca5`; clean original checkout. Remote domain branch is
  separately at `1538833`; do not replace the integrated visual branch with it.
- Existing assets: CC0 authored horse reference, editable adult/foal adaptations,
  36,162-triangle GLBs, 27-bone FK skeleton, procedural coat and marking shaders.
  Both adult body-family IDs currently load the same shape. No accepted clips.
- Reuse: source topology, UV0 detail, COLOR_0 anatomical masks, TEXCOORD_1 marking
  coordinates, canonical descriptor/resolver and existing material recipes.
- Improve: distinct adult silhouettes; bounded inherited morphology; animation
  deformation and rig adequacy; LODs; a multi-horse inspection scene.
- Structure: Light and Stock adult masters sharing the same topology and morph
  interface where feasible; preserve the existing juvenile source. Do not make
  Draft/Foal quantity a prerequisite for finishing the essential adults.
- Blender MCP verified live (5.2.1 LTS, protocol 13); current live scene contains
  the default cube. Production runs separately in background Blender to preserve
  the user's open session. No paid Blender generator is enabled.
- Meshy CLI 0.4.0 initially reports no authenticated profile; device authorization
  started. Website inspected signed out. Its Image-to-3D panel offers 7.1,
  Standard, texture off, Private, and displays 20 credits. No tasks submitted.
- First-generation estimate: two untextured Standard candidates = 40 credits,
  subject to account balance and final request verification. Eligible website
  retries preferred; API retries are not presumed free.
- Current API rigging docs explicitly exclude non-humanoids. Do not pay for API
  horse rigging. Website quadruped capability needs a signed-in inspection.

Sources checked 2026-10-11:
[Meshy prices](https://docs.meshy.ai/en/api/pricing),
[rigging limitations](https://docs.meshy.ai/en/api/rigging),
[CC0 source](https://opengameart.org/content/realtime-ranchers-3d-model-pack),
[rigged source listing](https://opengameart.org/content/rigged-horse).

This is initial evidence, not completed asset acceptance. Current results and
limitations will be recorded after actual export and runtime review.
