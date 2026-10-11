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
neutral-only detail path. This does not approve a final texture or prove geometry
identity. The canonical anatomy mask remains on UV1/TEXCOORD_1 and the existing
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
The builder exports only the subtle `Idle` candidate clip; Walk and Graze remain
gated and rejected reference clips were not copied. Idle is not animation
acceptance; rig and pose acceptance remains with the motion review.

## LOD and acceptance limits

The GLBs contain one source mesh level per node; no manually decimated LOD
meshes are exported. Actual Godot 4.7.2 checks on the current Light and Stock
exports found nonempty imported LOD index buffers: Body 4, Eyes 9, Mane 4,
Tail 7. These are engine-generated levels, not authored reductions.

Current source identities:

| Family | Editable Blend SHA-256 | GLB SHA-256 |
| --- | --- | --- |
| Light | `9c2975ba2cd2cf4311b45e96c827bae0474c9259b92fe8b81bafa100f12bc295` | `26a4870d8e93c488a513171fce52de3e76a0837692ed8c1a9a59e40d4a1c6cbc` |
| Stock | `77b3f3d188a525f255b3d46adf0645f472ec81fd3b47dd750a52e216dd0f4627` | `0b4f422d238e38ad32d2684ab0f0e55edb479aadb505e9c3542f3e1637066439` |

Light runtime evidence covers eight focused views and 23 appearance-studio
images; the studio Stock captures predate its latest rebuild. Current Stock has
separate focused head/front, advancing Idle, bone/morph/LOD evidence. Both runs
had zero capture-harness failures. See `evidence/adult-runtime-26a4870d` and
`evidence/detail-stock-runtime-0b4f422d`. Current Light anatomy guides and exact
hair seam checks are in `evidence/final-light-anatomy-26a4870d`.

Parent visual acceptance includes lowered socket eyes, separate ears/hair/skin,
and facial markings on bare skin with natural partial forelock occlusion. It does
not establish a fully unobstructed forehead star or new owner art approval. The
angular mane edge and clipped shoulder endpoint remain visual limitations.
Walk and Graze are still rejected/excluded; final texture integration, cleaned
foal validation, and full motion acceptance remain outstanding.
