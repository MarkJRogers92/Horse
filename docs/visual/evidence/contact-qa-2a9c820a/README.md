# Scoped intermediate hoof contact review

Independent cloud QA reviewed immutable Light Blend `9533fbd22cbceffae6d807e704f30300ccbb62bbe959ebbff373217f507f110e` and GLB `2a9c820a519c848f6fb9d4b061823fa779123e5c5ea37ca8fe0595500332a76a`. The parent inspected the accompanying identical-camera clay comparison. This is a practical contact pass for this intermediate, not head, UV, coat, rig-identity or final runtime acceptance.

Front-left/front-right/hind-left/hind-right minima were +1.000/+1.000/+0.062/−0.252 mm. Projected support within 2 mm was 2612/3079/11786/11593 mm², versus 0/0/660/643 before correction. All 97 Idle frames remained stable; source/export agreement was within 0.002104 mm. The tiny right-hind penetration was unchanged. The correction lowered 377 matched local hoof vertices by no more than 8.00005 mm, without meaningful XY resizing.

The change also affects the rig and skin transfer. The builder confirmed its sole-support callback intentionally moves `Hind_R_Hoof_02.tail` −4.461 mm Z and `Hind_L_Hoof.tail`/`Hind_L_Hoof_02.head` −5.404 mm Z. These are support-warped rest endpoints, not independently fitted ground anchors. Cloud QA found 92 of 1267 matched lower-foot weight records changed, maximum delta 0.027451. These changes must remain in final pose/runtime validation; this fix must not be described as geometry-only or an unchanged rig.

The parent received the scoped measurements from the independent review chat and inspected its locally materialized `contact-comparison.png`. Current Light `26a4870d` subsequently fixes UV projection order, canonical head anchors and hair seam skin continuity; its final engine checks are separate.
