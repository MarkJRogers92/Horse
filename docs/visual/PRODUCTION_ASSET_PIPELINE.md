# Horse production asset pipeline

This document describes the editable Meshy-derived adult horse candidates.
They are candidate rigged masters for appearance and deformation review;
anatomy, markings, and animation have not been accepted as final.

## Rebuild

From the repository root, run:

```sh
/Applications/Blender.app/Contents/MacOS/Blender \
  --background --factory-startup --disable-autoexec \
  --python tools/horse/build_meshy_production.py
```

Use `-- --family light` or `-- --family stock` for a single-family rebuild.
The builder reads the immutable `art/candidates/meshy/light_01/source.glb` and
`smart-rig.glb` inputs. It writes editable scenes to
`art/derived/production/horse_light.blend` and `horse_stock.blend`, exports to
`game/assets/horse/production/horse_light.glb` and `horse_stock.glb`, and writes
one `.manifest.json` beside each GLB. Preview renders live in
`art/derived/production/evidence/meshy/`.

The light candidate maps to `horse.standard`; stock maps to `horse.compact`.
Both use identical decimated face/index topology before family deformation.
The current body build target is 30,000 triangles; the total GLB count also
includes Eyes, Mane, and Tail and is recorded per manifest. The light build in
the current checkpoint exports 32,944 triangles.

## Source, names, and transforms

The adult base is a private owner-generated Meshy horse from a paid Pro account;
the source was created from a neutral text prompt without an uploaded third-
party image. The owner-reported ownership basis and the Meshy policy pages/date
are recorded in each manifest. This provenance is not an independent legal
opinion. A separate CC0 reference fallback is retained under
`art/derived/production/fallback/` and
`game/assets/horse/production/fallback/`; the original reference assets remain
unchanged.

Each exported scene contains `Body`, `Eyes`, `Mane`, `Tail`, and `Skeleton`.
The source is oriented and scaled into mesh vertices: +X forward, +Y left, +Z
up, ground at z=0, with identity object transforms. The visible sculpted mane
and tail are partitions of the source shell, not overlays. The source-specific
face-ID map is guarded by source, topology, and normalized-position hashes; it
assigns 3,752 Mane faces (including 407 Forelock faces) and 4,542 Tail faces.
Face sets are classified before hoof correction or family warp. Shared seam
edges and face conservation are checked. Of the 239 extracted Forelock vertices,
56 seam-root vertices retain exactly the matching Body skin weights; 183 interior
vertices follow Head (0.75) and Head_Base (0.25). All 222 Body/hair boundary pairs
have matching positions, skin weights, and four morph deltas. Eyes are separate
locally authored UV spheres recessed into the visible socket surfaces.

UV0 is a provisional Smart UV Project atlas because the Meshy source had no
UVs. Projection runs on the intact family shell before sole correction and hair
partition, preserving the historical atlas. The lossless source-ID/UV corpus is
`art/derived/production/evidence/horse_light_uv0_provenance.json` with SHA-256
`f1c6d2521a69f99256ddd165b66843d7db9e4acbea95e6eaacd11f83247d7f54`.
Independent native checks reproduce all 25,222 historical Body UV footprints and
their multiplicities exactly. Current checks match 21,253 retained Body faces;
453 recovered skin faces have historical hair provenance and require an explicit
neutral-only detail path. The explicit v5 binding and local game integration are
accepted within this scope; this does not prove whole-Body UV or geometry identity.
The canonical anatomy mask remains on UV1/TEXCOORD_1 and the existing
importer COLOR_0 channels retain the shader inputs. The adult head axis uses a
geometry-projected upper-skull poll and centered muzzle-tip vertices cached on the
intact shell before hair removal, validated against ear-root locations; it does
not assume a short Smart Rig bone spans the face. A builder-local per-corner pass
reevaluates head/leg mask data from each
corner's geometry and normal inside majority-classified faces. The mixed-
category GLB guard remains enabled. UV0 may be replaced by the user's incoming
mesh workflow; preserve UV1 and COLOR_0 contracts unless the runtime owner
changes them deliberately.

Both adult builds apply a bounded, topology-preserving sole correction after UV
projection. It moves at most 8 mm on the lower support band; each hoof's lower
fifth-percentile vertices end at 1 mm above the floor (hind minima remain within
approximately 0.3 mm of the floor). The callback also deliberately changes three
hind-hoof rest endpoints. Independent Light contact review found 92 changed
lower-foot weight records; see `evidence/contact-qa-2a9c820a/README.md`.

## Morphs and rig

All four named mesh nodes expose `body_length`, `chest_width`, `neck_length`,
and `muscle` relative shape keys. Neutral is 0; each slider range is [-1, 1],
corresponding to descriptor values -2000..2000 divided by 2000. The per-node
maximum displacement is listed in the manifests. The shapes make bounded,
localized changes; they are not intended to be extreme anatomy controls.

The rig is based on the local Smart Rig candidate. Its source hierarchy is
transformed with the family geometry, extended with the canonical root, and
four-influence weights are transferred to each production mesh. Each manifest
records the rig source hash, bone mapping, transfer statistics, and rest pose.
The builder exports the subtle `Idle` clip. Its bounded contact, loop and actual
game-pose evidence is accepted; Walk and Graze remain gated and rejected reference
clips were not copied. This does not establish full gait or owner art approval.

The reviewed Light geometric neck scaffold uses 222 exact opposing-winding
Body/Mane/Tail seam pairs, with no production weld or new faces. Four nested
cranial sections retain the complete original skull and ear membership while
excluding the declared limb and caudal torso domains. The lowest section's
ventral extension is a Chest/cervical junction. Protected indexed geometry,
morphs, UVs, masks, object transforms and rest rig match the frozen Light source.
The analysis input's earlier weight changes are recorded explicitly; a new field
must start from frozen production weights. See `evidence/light-neck-scaffold-9c2975ba`.
This scaffold accepts geometry mapping, not shoulder weights, walking or grazing.

A subsequent anatomical Light field and immutable boundary preflight were
rejected; no candidate weights or clips were promoted. Immutable permissions
removed silent donor fallback but exposed attached Mane/root support, caudal
Pelvis/tail-root ownership and incompatible hard Chest boundaries. Native Sol
requires one local constructor/attachment correction before another solve. See
`evidence/rejected-light-field-77f44226` and
`evidence/rejected-junction-preflight-75b3c383` for the original rejected evidence.
The unchanged Walk and mild-neck shape gates remain. Deep GrazeStress is a
reported diagnostic; an actual head-lowering clip is allowed by the brief and
still requires deformation, hoof-contact and visual acceptance.

An isolated eight-weight FLOAT export probe retains eight-wide Body/Mane buffers
in Godot 4.7.2, matching 58 joint names, finite active weights, valid bone indices
and nonempty generated LOD index buffers. Native Sol accepted this format scope.
The source field remains anatomically rejected. The headless inspector did not
render the scene or establish posed accuracy; its aggregate sum tolerance is not
an individual-weight or deformation bound. Maximum imported sum drift is about
0.0122%. Original export/import exits are reported without retained raw logs;
the corrected inspector rerun has archived stdout/stderr, exit 0 and passing JSON.
See `evidence/eight-weight-format-5e007c40`. This does not promote candidate motion.

A separate synthetic renderer probe uses eight independent joints with equal
FLOAT32 weights on one quad. Moving only joints 5–8 by +0.8 m produces a 74-pixel
shift and exactly matches the independently constructed unskinned +0.4 m
reference at 640×480. Actual Godot 4.7.2 OpenGL compatibility logs, four captures,
source and exit codes are retained; parent source/evidence review accepted this
bounded feature check. Imported weights are quantized and the pixel comparison
does not establish subpixel or production deformation accuracy. See
`evidence/eight-weight-renderer-75984e70`; no horse motion is accepted by this probe.

## LOD and acceptance limits

The Light Body detail texture is
`game/assets/horse/production/horse_light_body_detail_v5.png`, SHA-256
`56514e8a81871779ded06be790c3d1b9ba021ffb237cfa03af5b31973cdc5cfa`.
The accepted handoff, explicit native UV binding and recovered-skin neutral mask
live in `game/assets/horse/production/evidence/light-v5-detail/`. Catalog validation
checks source/export, texture and evidence hashes, RGB8 data bounds and the neutral
mask; the presenter also verifies Godot's imported lossless pixels. Invalid or
missing data disables the optional detail while preserving procedural appearance.

Use normal PNG orientation, primary UV0 and a linear data sampler without
`source_color`. Decode `1 + ((sample.r * 255 - 128) / 127) * 0.12` and apply with
runtime strength 1; the recipe strength 0.30 is already baked into the codes.
Accepted codes 90..166 produce multipliers about 0.9641..1.0359. This layer modulates
the Body base coat before semantic pigment points, skin, white markings and hooves.
Eyes/Mane/Tail, Stock and foal do not bind this Light atlas. The 12-image actual
Godot activation and native Sol/parent review are preserved under
`evidence/light-v5-detail-runtime-26a4870d`. Exported-binary raw-file availability
for provenance checks is still unverified; any future source hash or UV change
requires a fresh accepted binding.

The GLBs contain one source mesh level per node; no manually decimated LOD
meshes are exported. Actual Godot 4.7.2 checks on the current Light and Stock
exports found nonempty imported LOD index buffers: Body 4, Eyes 9, Mane 4,
Tail 7. These are engine-generated levels, not authored reductions.

Current source identities:

| Family | Editable Blend SHA-256 | GLB SHA-256 |
| --- | --- | --- |
| Light | `9c2975ba2cd2cf4311b45e96c827bae0474c9259b92fe8b81bafa100f12bc295` | `26a4870d8e93c488a513171fce52de3e76a0837692ed8c1a9a59e40d4a1c6cbc` |
| Stock | `77b3f3d188a525f255b3d46adf0645f472ec81fd3b47dd750a52e216dd0f4627` | `0b4f422d238e38ad32d2684ab0f0e55edb479aadb505e9c3542f3e1637066439` |

Light runtime evidence includes eight focused views; Stock has separate focused
head/front, advancing Idle, bone/morph/LOD evidence. A refreshed studio capture at
`aaf9796` uses both current adult masters and the current material code. It records
eight distinct test-only coats across four presets, a 2×4 overview, signed morph
boundary views and generated LOD records: 23 images with zero harness failures.
Its 120-frame static-scene sample on Apple M2 averaged 8.353 ms, with p95 11.182 ms;
this is not moving-herd or gait performance. The report does not freshly measure
signed-detail activation; the unchanged Light v5 binding has separate accepted
proof. Independent review accepted these limits. See
`evidence/appearance-studio-aaf9796`, `evidence/adult-runtime-26a4870d` and
`evidence/detail-stock-runtime-0b4f422d`. Current Light anatomy guides and exact
hair seam checks are in `evidence/final-light-anatomy-26a4870d`.

Parent visual acceptance includes lowered socket eyes, separate ears/hair/skin,
and facial markings on bare skin with natural partial forelock occlusion. It does
not establish a fully unobstructed forehead star or new owner art approval. The
angular mane edge and clipped shoulder endpoint remain visual limitations.
Walk and Graze are still rejected/excluded. Candidate repairs and ground-reaching
pose fitting remain outside the production masters until independent deformation,
support, loop, morph-endpoint and actual game playback checks pass.

The distinct genuine juvenile mesh has a separate 64-joint adapter and the same
four component morph names. Rebuild with `tools/horse/build_foal_production.py`
using background factory Blender. Its first actual 15-image preview passed harness
contracts but exposed a close-up camera error. Local repairs now fit the forelock
on the scalp rather than the ears, normalize orphaned skin group names, inherit
Body weights at hair roots, parent Head to Head_Base and a measured cervical
auxiliary chain to Neck. Rest endpoints and retained geometry remain unchanged.
Native Sol accepted the `995b9223` hierarchy repair and reviewed small target poses
for bounded Idle proof. This is not full animation or owner anatomy approval.

One Godot editor import refreshed the stale foal cache; raw and import-sidecar
digests then matched for all three masters. Actual foal inventory proves 64 bones,
the repaired Head ancestry, components, morphs and generated LODs. The viewer age
toggle preserves test marking keys; unsupported probes remain unsupported. These
checks and nine actual-presenter optional-detail fallback cases passed independent
game-diff review. See `evidence/foal-imported-rig-995b9223`,
`evidence/viewer-age-toggle-7d622aa6` and `evidence/detail-fallback-26a4870d`.
Final foal GLB `f7552842` preserves that repaired geometry/rig and adds a restrained
four-second, 24 fps Idle. Its 97-frame Blender audit has finite geometry, exact
loop endpoints, zero root motion, and greatest front-sole penetration 1.215 mm
within the declared 2 mm tolerance. Native Sol accepted the bounded mechanical
proof; this does not establish walking, grazing or new owner art approval.

Godot initially optimized the tiny rotations to one constant key per imported
track. Only this foal's `AnimationPlayer` disables the import optimizer through
its `.glb.import` subresource settings, preserving 123 sampled keys in the Head
and Neck tracks. Actual runtime pose changes, fixed Root, 15 refreshed preview
images, morph/LOD checks and real viewer age/Idle controls pass. Light and Stock
already retain changing Head/ear tracks and their import settings are unchanged;
their Head pose changes were also measured. See `evidence/foal-runtime-f7552842`.
The source GLB bytes remain frozen. Historical pre-repair, rejected pose, and
collapsed-animation import evidence remain preserved. The anatomy companion and
calibrated front/near/far/three-quarter overlays identify bilateral Body neck-region
ray hits and separately label skeleton projections and the unmeasured Jaw_Tip
muzzle alias. The crest's poll-side endpoint is 17–30 mm forward of the sampled
ear roots on this asset; its legacy names are explained rather than treated as
anatomical truth. Native Sol reviewed these corrections. Eight individual morph
endpoints at Idle frame 49 pass finite geometry, the same sole tolerance and
accessory-root drift checks; this is not all morph combinations across the loop.

Frozen foal Blend SHA256 is `4322de7c3b97e6aa0e71111a6ab0de325f72f4f905424b4ff6e8d22df0ca041b`;
GLB is `f7552842a1ae8a24fb32df60c6d5de8200dfe19055c9787330cf1ac5ff72c72a`;
metadata manifest is `c77a3eb46cd93739ea0a414c6607dcb9f6d26b253c5b48e1846de7c77088acfe`;
anatomy companion is `77b5ffc2ece595e5e9c925665e6491d91a950692a248e3c7e1c462973a7221d5`.
