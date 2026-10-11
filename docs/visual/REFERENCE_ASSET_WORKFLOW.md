# CC0 horse: editable source and procedural coats

**Current state:** the static adult and foal files below are restored byte-for-byte from the known-good `74f5094bc05681479c15887ca08591793034eeb9` checkpoint. Rejected animation experiments are preserved separately on `archive/horse-animation-experiments-2026-10-10` (`674e7908d706ae0309b5b93f0beb3ab73846fa23`); they are not part of the active game pipeline. Fresh build, both Godot imports and 16 Metal clay/pair captures passed; restoration evidence is linked from VISUAL_STATUS.md. See `VISUAL_STATUS.md`.

The pilot uses the authored horse from `art/reference/Horse_Reference/` for both ages. The original package stays intact. The anatomy and source topology are by Lyndon Daniels, from [Realtime Rancher's 3D Model Pack](https://opengameart.org/content/realtime-ranchers-3d-model-pack), downloaded through ChadM's [Rigged Horse](https://opengameart.org/content/rigged-horse) submission. Both listings identify CC0. Our reference adaptation adds materials, hooves, a basic rig and inspection views; the game adaptation adds the required masks and canonical marking coordinates.

## Files and regeneration

- `art/derived/Horse_Reference/horse_adult.blend`: editable adult control cage, separate hooves, eyes, mane, tail and 27-bone rig.
- `art/derived/Horse_Reference/horse_foal.blend`: juvenile proportions derived from the same source, with a shorter, shallower barrel, proportionally long legs, shorter muzzle, small hooves and short mane/tail.
- `game/assets/horse/reference/horse_{adult,foal}.glb`: game exports, 36,162 triangles each. Hooves are joined into `Body` only for the game contract; they remain separate in Blender.
- The matching `.manifest.json` files record source hash, anatomical mapping, mesh checks, rest bones and calibrated head-camera targets.

From the repository root, with the pinned Python/bpy requirements installed:

```sh
python tools/horse/import_reference.py                 # both ages
python tools/horse/import_reference.py --profile foal  # foal only
dotnet build game/Cloverbrook.Game.csproj -warnaserror
godot --headless --import --path game
```

Blender source stays outside `game/`, so Godot does not try to launch Blender to import it in CI. The importer opens the source with scripts disabled, works on a fresh scene per age, and checks the exported GLB before reporting success. CLI Blender can also run the importer with `--background --factory-startup --disable-autoexec --python tools/horse/import_reference.py`.

The game exports use metres, Y up, +X forward and a ground-level root. Body `COLOR_0` supplies points, soft skin, dorsal/forehead and underside masks. `TEXCOORD_1` carries poll-to-muzzle and four leg coordinates; UV0 remains the surface-detail layout. Do not replace UV0 with the marking coordinates or bake chestnut/blaze/socks into the game albedo. After changing the head, regenerate masks and update the catalog's `headViewTarget` from the manifest.

## The coat look and available variation

The source package's fine fur normal map is embedded in the game GLBs and connected to the coat shader by `HorsePresenter`. This preserves surface detail while the existing descriptor recipes supply colour and markings. The original chestnut diffuse texture remains available in the reference package for art study; the game exports have neutral albedo and no fixed markings.

The current pilot draws 13 fixture classes: chestnut, bay, black, palomino, buckskin, smoky black, cremello, perlino, smoky cream, red dun, bay dun, grullo and nd1-marked bay. Existing shaders also provide dapples, sooty shading, countershade, sheen, dun dorsal stripes/leg bars, brown or blue eyes, pink or pigmented skin, hair colours and deterministic facial/leg white markings. A shared marking key gives the adult and foal the same marking recipe in their own anatomical coordinates.

These are renderer capabilities, not genetic or art approval. Grey progression, roan, tobiano/sabino, leopard/PATN1, pearl, silver, champagne and shoulder bars still refuse unsupported descriptors. No coat-matrix row is promoted by the mesh replacement.

## Adding more surface materials

Use the existing neutral-normal slot first: replace the normal image used by `create_neutral_material` in `import_reference.py`, preserve UV0, then regenerate and inspect clay, detail and all coat sheets. Set the normal image to Non-Color in Blender. Check normal direction, seams, visible tiling and muzzle/hoof detail in the actual game. A diffuse photo containing a blaze, socks, dapples or coloured patches cannot be used directly as a generic coat; those features would conflict with the descriptor.

Two free candidates were checked at their primary listings on 2026-10-10:

| Source | License / content | Workflow suitability |
| --- | --- | --- |
| [Hair Texture, kleenstudios](https://opengameart.org/content/hair-texture-0) | CC0; `HairFur.png`, described as a simple texture | Candidate detail source only; short-horse-fur appearance and UV suitability remain unverified. |
| [Seamless Cartoon Textures, OzMaister](https://opengameart.org/content/seamless-cartoon-textures-furjpg) | CC0; 256×256 light-gray, tintable `fur.jpg` | Possible stylized detail mask; not a verified realistic coat replacement. |

The [ShareTextures animal pack](https://opengameart.org/content/free-high-quality-textures-by-sharetextures) lists CC0 4K PBR maps, but its animal skins include other species. Its maps need visual inspection before use. No candidate texture above has been silently installed into the horse, and nothing was purchased. Keep the preferred horse's existing detail as the default.

## Body shapes, rig and editing limits

Adult and foal are genuinely different meshes. Standard and compact family IDs currently share the same cob build within each age; the viewer reports this limitation for compact descriptors. Only the descriptor's height is applied as a uniform scale. Body length, chest width, neck length, muscle and hair-density morphs remain explicitly unapplied; there are no body-shape blend shapes yet.

Both authored assets have a basic FK rig but no motion clips. The viewer disables unavailable clips and capture reports skip them. Placeholder gait clips cannot be copied straight onto this different skeleton without retargeting and a motion review.

When improving an existing horse mesh, compare the withers, shoulder, ribcage, pelvis, hocks, fetlocks and muzzle against the reference, then make changes on the control cage. Preserve the anatomical loops around shoulders/hips and limb joints for deformation. The foal should retain its high belly, comparatively long legs and short hair as the cage is refined. Neither clean export checks nor still images certify gait deformation, a broad range of breed shapes or a final art release.
