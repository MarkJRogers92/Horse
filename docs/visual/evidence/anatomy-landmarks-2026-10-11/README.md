# Adult horse anatomy landmarks

These anchors were ray-cast through the pixels of the clean front and side renders onto the frontmost evaluated geometry in the frozen Light adult snapshot. The snapshot SHA-256 is `30507e2e0ba5a8232930743af970843e871699d30c93a1d9e6f11b7120a1f6a5`; its manifest SHA-256 is `4232e2a533f802286faf0e5278f9b5ed80a4aab18ca79bf84e5ebf212947d5d4`. Coordinates are meters in baked Blender space (+X forward, +Y horse-left, +Z up, floor z=0).

`horse_light_anatomy_landmarks.json` records each source pixel, hit object/face, surface normal, coordinate, and confidence. The numbered annotated front and side renders correspond to its `legend_id` values; clean renders show the unmarked geometry used for selection. These are visible surface anchors, not hidden anatomical joint centers. The jaw/neck and mane points are visual transitions; hair boundaries remain owned by the hair-segmentation map. “Mane root behind ears (poll end)” and “Mane end near withers” are landmarks, not a claim that the mane starts at the withers.

The package was reviewed and accepted as a geometry-grounded adult landmark reference. It does not establish animation or skinning acceptance.
