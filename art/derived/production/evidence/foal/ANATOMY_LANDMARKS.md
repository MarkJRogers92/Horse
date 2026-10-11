# Foal anatomy landmark guide

This guide records measured geometry and skeleton references for the juvenile candidate. Coordinates are metres in Blender object-local XYZ; Godot uses `(x, z, -y)`. `horse_foal_anatomy_landmarks.json` includes exact points, bilateral ray-hit face IDs, normals, ray origins/directions, and screen projections for the labeled front, near-side, far-side, and three-quarter views.

The ears and poll are sourced from the mesh. The poll is a centered crown ray hit between the ears. The muzzle tip is the forward-most nose surface; nostril points are nearest Body references to approximate nostril probes. Eye centers are fitted Eyes-component centers, not socket-depression centers.

The legacy `manePollEnd_m` field is the poll-side Mane endpoint. On this particular foal, its X coordinate is 17–30 mm forward of the sampled ear-root points, so it is not behind the ears. The other legacy field, `maneStart_m`, is the withers-side Mane endpoint. Both exact locations remain available under those legacy names in the manifest.

Three bilateral neck-region stations are measured by lateral rays that hit the evaluated Body directly: approximate middle neck at x=0.285,z=1.11 m; neck/shoulder transition at x=0.20,z=0.90 m; and head/jaw/neck transition at x=0.46,z=1.14 m. Their Y coordinates, source face IDs, normals, and ray setup are recorded. The names describe approximate probe stations, not a claim that the mesh marks anatomical boundaries independently.

`Neck_Base` is a skeleton joint. Its nearest Body projection lies behind the withers and must not be read as a lower-neck surface point. Other bone-to-surface nearest projections are labeled as projection-only. The non-deforming `Jaw_Tip` helper aliases the muzzle point and is not an independent jaw or neck observation.

The foal remains a candidate. Landmark measurements support inspection and procedural setup, not anatomy acceptance. The Idle report also checks all four descriptor keys at both normalized endpoints during the frame-49 Idle midpoint, with finite meshes, persistent support-surface IDs, and accessory-root comparisons; it is not a full 97-frame sweep for every morph combination. Idle verification covers the 97-frame loop and reports full hoof support, attachment drift, and endpoint checks. Rest views are reused from the previous Idle candidate only because exact comparison found geometry, UVs, color attributes, morphs, weights, and rest hierarchy unchanged.
