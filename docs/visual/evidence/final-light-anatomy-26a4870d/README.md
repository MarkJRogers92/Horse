# Final Light anatomy and seam validation

Read-only validation targets the final Light blend SHA-256 `9c2975ba2cd2cf4311b45e96c827bae0474c9259b92fe8b81bafa100f12bc295`, GLB SHA-256 `26a4870d8e93c488a513171fce52de3e76a0837692ed8c1a9a59e40d4a1c6cbc`, and manifest SHA-256 `dcc04138259ce99a8ccc59252fbe17f69cc26a3dc36e6d06acdb1da76b06973c`.

`horse_light_anatomy_and_poll.json` combines the current image-ray picks for both ear bases, both nostrils, nose tip, behind-jaw transition, forelock, and Mane root/end with the final poll ray proof and actual rig bone names. The current Mane-root and Mane-end pixels hit the named `Mane` mesh. The poll ray hit is `[0.914083, 0.000283, 1.893765]` on `Mane`, face 3075; it differs from the manifest's intact-shell poll `[0.91377, 0.00081, 1.89362]` by 0.63 mm. The same ray point is marked in top/front/side renders. It is a centered inter-ear skull-poll anchor; the visible hair surface occludes Body at the crown.

`shared_seam_and_morph_validation.json` independently pairs duplicate Body↔Mane (172) and Body↔Tail (50) source vertices by their exact baked position. For both partitions, base position differences and bone-weight differences are zero; skin-weight sums are within 5.3e-8. The four morph deltas (`body_length`, `chest_width`, `neck_length`, `muscle`) match exactly on every shared pair. The 70 Mane cut-boundary vertices in the head-cap box have zero measured Body clearance at rest, Idle frame 49, and isolated local-X Head/Neck +5° poses. Front, side, and q3 images cover each state; no visible through-gap was seen at the forelock seam.

All renders use temporary neutral materials and camera/light setups; no source asset was saved or changed.
