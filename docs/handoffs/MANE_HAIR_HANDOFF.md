# Handoff: rework the mane and tail hair (Cloverbrook Stables)

You are picking up one visual task in an existing Godot 4 / C# game. Read this whole file before changing anything.

- **Repo:** github.com/MarkJRogers92/Horse
- **Start from:** branch `main` (or `claude/eloquent-davinci-o1hhr6` if main is behind it).
- **Work on:** a new branch, for example `gpt/mane-rework`. Do not push to `main`; the owner merges after review.
- **Read first:** `AGENTS.md` (repo rules), then the top of `STATUS.md`.

## The problem

The owner says the horses' manes and tails "don't look great". Light manes are the worst: flaxen chestnuts, silver dapples and greys show cream and tan patches instead of flowing hair. Dark manes look like a smooth sculpted lump rather than hair.

Reference images in this bundle:

- `current_mane_contact_sheet.jpg` shows the D-038 renders. Look at "silver black v0 side" and "silver bay v0 head": the mane is blotchy.
- `current_rarer_coats.jpg` shows the D-037 renders, where the silver manes look like white blobs on the neck.

## Root cause (verified in the code)

1. **The mane is not hair cards.** On all four mesh families the `Mane` and `Tail` are *sculpted solid partitions of the body shell*, from a Meshy-generated model. See `docs/visual/PRODUCTION_ASSET_PIPELINE.md`, around line 78: "The visible sculpted mane and tail are partitions of the source shell, not overlays." Every family in `game/data/visual_catalog.json` sets `"hairGeometry": "solid_clumps"`.
2. **The hair shader assumes cards.** `game/shaders/horse_hair.gdshader` (39 lines) assumes UV.x runs across a card and UV.y from root to tip. It derives strand lanes, lengths and dark or white locks from `UV.x` and `UV.y`. On the sculpted mesh the UVs are an unrelated auto-unwrap, so the strands follow UV islands. That produces the patches. The ragged tips and alpha are switched off for `solid_clumps`, so the silhouette stays a smooth lump.

The fix therefore has to give the hair a real flow direction and some real geometry. Tuning colours will not work.

## How the hair is wired today

- **`game/scripts/Visual/HorsePresenter.cs`**, around lines 225–260, builds one `ShaderMaterial` from `horse_hair.gdshader` and puts it on the meshes named `Mane` and `Tail`. It sets these uniforms:
  - `hair_color` and `tip_color`;
  - `seed`;
  - `solid_clumps`;
  - `grey_amount` (progressive grey);
  - `white_fraction` (white locks for tobiano, leopard, sabino and splash);
  - `dark_color` and `dark_fraction` (dark strands in silver manes).

  **All of these must keep working.** The colours come from `game/data/visual_catalog.json` under `hair`.
- **Rigging:** the mane is skinned to the neck and head bones. The foal mane uses bones that `HorseIdleLife` does not drive, so a foal's forelock tuft can float during grazing. That is a known issue you may fix.
- **Who reads the mane mesh:**
  - `game/scripts/Visual/HorseIdleLife.cs` skips the `Mane` mesh in one loop (line ~126).
  - `game/scripts/Visual/HorseTack.Extras.cs` reads the mane vertices to place braids and the festival crown or halter. See `AddBraids`, which finds the crest from `c.Verts` plus `c.ManeVerts`.
  - If you change the mane geometry, braids must still sit on the crest.
- **Body map (useful):** `game/scripts/Visual/BodyMap.cs` (decision D-025) gives every Body vertex a continuous coordinate in `CUSTOM0 = (u, h, s)`:
  - u runs along the body (0 = buttock, 1 = elbow line; the neck and head are above 1);
  - h is height (0 = belly, 1 = topline);
  - s is the side (−1 to 1).

  You can reuse the same idea for hair: a per-vertex "distance from the crest" and "along the neck" coordinate makes a good strand direction.

## Recommended approach (two phases, ship phase 1 first)

### Phase 1: procedural hair cards generated at load time (no new art files)

Add `game/scripts/Visual/HairCards.cs`, modelled on `BodyMap.cs`: run once per imported mesh resource, cache the result, and make no change to the asset files or saves.

- **Mane:**
  1. Find the crest line from the existing `Mane` mesh: the top-most vertices along the neck, from poll to withers. `AddBraids` in `HorseTack.Extras.cs` already does a version of this.
  2. Generate 2–3 layers of overlapping card strips hanging from the crest to one side. Real manes fall to one side, usually the right (off) side. Card length is about the existing mane depth. Use a little per-card randomness in width, length, droop and twist, seeded deterministically from `seed` / `Markings.EdgeSeed` so a horse always looks the same.
  3. Add a forelock between the ears falling over the forehead.
  4. Card UVs must follow the existing contract: UV.x across the card and UV.y from root (0) to tip (1), before glTF's V flip. The existing shader then works as designed.
- **Tail:** the same idea, cards radiating around the tail dock and falling down. Keep the existing tail mesh as the dock.
- **Skinning:** copy bone indices and weights from the nearest `Mane` or `Tail` vertex for each card vertex, so the cards move with walk, idle and graze animations. Keep 8-bone-weight surfaces working; `BodyMap.Build` shows how.
- **Coverage:**
  - Keep the original sculpted mane underneath as an underlayer, shrunk slightly or darkened (e.g. a `hair_underlayer` uniform that darkens it towards `dark_color`), so no gaps show the neck.
  - Alternatively, hide it and rely on denser cards. Either way, the neck must never show through the mane in side view.
- **Material:** switch the cards to `solid_clumps = false`, the card path with alpha-scissored ragged tips and anisotropic sheen. Tune `strand_count` so strands do not alias into veins at 1280×720. The current comment in the shader explains that limit.
- **Wiring:** `HorsePresenter` should add the cards as child `MeshInstance3D` nodes named `ManeCards` and `TailCards`, skinned to the same `Skeleton3D`. Then give them the hair material.
  - Update the `"Mane" or "Tail"` switch.
  - Update the clay/override code near line 477 that checks `mesh.Name == "Mane" || mesh.Name == "Tail"`.
  - Update `HorseIdleLife` and `HorseTack.Extras` if they need to see the cards.
- **Performance:** about 150–400 cards per horse is plenty; the yard shows up to about 8 horses. Check the frame time with the stable scene.

### Phase 2 (only if phase 1 looks good): shine and texture

- Add a Kajiya-Kay style dual specular highlight along the strand tangent in `horse_hair.gdshader`, plus slight colour variation per card.
- Optionally add a very subtle coat hair-direction normal in `game/shaders/horse_coat.gdshader`, using the body map. It was deferred from decision D-038.

## Hard rules (from AGENTS.md; please follow them)

- Do **not** edit anything in `handoff/` (the frozen spec).
- The domain code in `src/` is pure C# (`netstandard2.1`, C# 9). This task should not need to touch it.
- Do **not** change the save format, the genetics, or `docs/contracts/`.
- Commit in small steps with imperative messages and a body naming `D-039` (use that number for this decision). Write `docs/decisions/D-039-mane-hair.md` in the style of `docs/decisions/D-025-body-map.md`.
- Never claim a test passed unless you ran it.
- Do not add downloaded art, paid assets or new external dependencies. Everything should be procedural, in C# or shaders.

## How to build and check

```bash
dotnet test                                    # domain tests (currently 319, all must pass)
python3 tools/validate_handoff.py              # spec validator, must print OK
dotnet build game/Cloverbrook.Game.csproj -warnaserror --no-incremental   # must be 0 warnings, 0 errors
```

The Godot version is pinned in `docs/visual/TOOLCHAIN.md`. Headless rendering on Linux uses Xvfb and lavapipe:

```bash
G=/path/to/Godot_v4.x_mono_linux.x86_64
export VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json
$G --headless --path game --import
xvfb-run -a -s "-screen 0 1280x720x24" $G --path game --rendering-driver vulkan -- --smoke-test     # must print "[smoke] OK ..."
# Coat contact sheets (read game/scripts/Tools/CoatGallery.cs for all options: --only, --family, --views, --lighting, --variants):
xvfb-run -a -s "-screen 0 1280x720x24" $G --path game --rendering-driver vulkan res://scenes/tools/CoatGallery.tscn -- --only bay,chestnut,silver_overlay --views side,head
# Tack check (braids must still sit on the crest): scenes/tools/TackPreview.tscn, read game/scripts/Tools/TackPreview.cs for options.
# Stable captures:
xvfb-run -a -s "-screen 0 1280x720x24" $G --path game --rendering-driver vulkan --resolution 1280x720 res://scenes/stable_day.tscn -- --stable-day-capture=/tmp/cap --save-path=/tmp/cap-save/stable.clvr
```

GitHub CI (`.github/workflows/visual.yml` and `domain.yml`) runs the same checks on every push.

## Acceptance criteria (the owner will judge by eye)

1. In side and head views, the mane reads as **hair**: separate locks with ragged tips falling to one side, plus a forelock. It is no longer a smooth lump.
2. Flaxen, silver and grey manes show **no patches**. The neck never shows through the mane.
3. It works on all four mesh families (`horse.standard.adult.v1`, `horse.compact.adult.v1`, and both foals) and while walking, idling and grazing. Nothing detaches or floats; fix the foal forelock tuft if you can.
4. Braids, the festival crown and the fairy halter from Dress up still sit correctly (TackPreview).
5. Every hair uniform still works: grey, white locks, dark strands, colours.
6. All the checks above are green, and before/after renders are saved in `docs/visual/evidence/mane-hair-YYYY-MM-DD/`.

When done, write a short report: what you changed, the render paths, and anything still off.
