# Training scenery sources

Original authored metre-scale assets integrated by D-046. Runtime GLBs live in
`game/assets/scenery/{training_jump,ground_pole,low_cavaletti,course_cone,training_barrel}`.
The matching editable Blender files and reusable jump pieces are retained here.
Blender 4.3.2 sources export Y-up GLBs with ground-plane pivots. Import at unit
scale: the assembled jump contains rotated parts, so its conservative mesh AABB
extends below its actual feet and must not be used to normalize the asset.

The props and jump kit use original procedural geometry and solid PBR materials,
without third-party geometry or textures. The barrel was authored after viewing
a saved Higgsfield reference; no image pixels are used as textures. Reference-provider
commercial rights remain unverified. No commercial-clearance claim is made.
No paid generation, purchases or new external services were used for integration.

Source archive READMEs are retained for provenance; their original “not integrated”
statements describe the source archive before D-046, not this repository state.
Models are static decoration, with no physics, navigation, interaction or sport rules.
