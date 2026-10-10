# Handoff: visual track → GPT

**Repo:** github.com/MarkJRogers92/Horse
**Branch:** `claude/godot-horse-visual-pilot`. It includes your backend through `1538833` via a normal merge commit; none of your domain code was changed.
**Read first:** `AGENTS.md`, then `docs/visual/VISUAL_STATUS.md`. Skip the design review; it is done.

## What exists
- **Godot 4.7.2 .NET project in `game/`** (`Cloverbrook.Game.csproj` references `src/Cloverbrook.Domain`). Scenes: `main` (menu), `viewer`, `stable`, `capture` (CI renders).
- **The presenter renders the domain's descriptor and nothing else.** `game/scripts/Visual/HorsePresenter.cs` → `Present(VisualDescriptor)`. Descriptor recipe strings are mapped to colours in `game/data/visual_catalog.json`. Anything it can't draw comes back as an explicit Unsupported result with no substitute. `DomainBridge.FromPresentation()` adapts your `StableBreedingQueries.ResolveVisualDescriptor`.
- **Shaders** in `game/shaders/`: coat (pigment, points, countershade, dun stripe/bars, nose skin, white markings, hooves), eye, hair, grass.
- **Markings** are deterministic from `markingRealizationKey` (`MarkingRealizer.cs`); the adult and foal match.
- **Placeholder horse:** `tools/horse/anatomy.py` + `build_horse.py`, a numpy SDF mesh plus Blender `bpy` that gives a rigged, skinned glb with `idle/breathe/look/walk/trot`. It is TEMPORARY. Output is in `game/assets/horse/placeholder/`.
- **CI** (`.github/workflows/visual.yml`):
  1. domain descriptor export plus drift check
  2. horse rebuild
  3. C# build
  4. Godot import
  5. smoke test
  6. renders under Xvfb + lavapipe
  7. contact sheets (`tools/visual/contact_sheets.py`)
  8. artifact `visual-evidence`

  Run 1 passed through the smoke test; rendering was in progress at handoff.
- **Evidence snapshots** are in `docs/visual/evidence/` (sheets and highlights).
- **Pinned versions** are in `docs/visual/TOOLCHAIN.md`: Godot 4.7.2, bpy 5.2.2, Python 3.13, .NET 8.

## To plug in YOUR horse model (main task)
Export a glb that meets this contract, then point `meshFamilies` in `game/data/visual_catalog.json` at it (`horse.standard.adult.v1` / `horse.standard.foal.v1`, `placeholder: false`). Nothing else changes.

1. Units are metres, +Y up, the **horse faces +X**, and the root is at the ground between the hooves.
2. Mesh nodes are named `Body`, `Eyes`, `Mane`, `Tail`. Animations are named `idle`, `breathe`, `look`, `walk`, `trot`; any you don't have are skipped.
3. `Body` vertex data:
   - `COLOR_0` (linear):
     - r = points (lower legs, ear tips)
     - g = soft-nose / eye skin
     - b = midline (dorsal stripe on trunk and neck; face-front on head)
     - a = countershade (belly and flank)
   - `TEXCOORD_1` (canonical marking coordinates, in Blender UV space before glTF export):
     - head = (s 0 poll → 1 muzzle tip, t lateral −1..1)
     - legs = (10 + leg index [FL0 FR1 HL2 HR3], height 0 ground → 1 knee/hock; hoof below 0.165)
     - everything else = (−10, 50)
4. `Eyes`: UV.x = outwardness (0 centre plane → 1 outer pole), UV.y = vertical position.
5. `Mane` and `Tail` cards: UV.x across the card, UV.y root → tip.

`anatomy.py → Horse.vertex_data()` shows one way to compute these. For an imported mesh, derive them from bone proximity and the head axis. Debug view: set `debug_view = 2` in `horse_coat.gdshader` to see the coordinates (head = red s / green t / blue front, legs = green height).

## Known issues (fix or verify)
1. **The face marking fix is unverified.** The forehead was tagged as neck. The fix (head-region reclaim in `anatomy.py`) is committed and the horses are rebuilt, but nothing was re-rendered. Check `markings_sheet` in the next CI artifact. The committed evidence images still show the bug.
2. Only the `height` morph is applied (as a whole-horse scale). The other morphs are reported unapplied; your mesh's blend shapes could take these over.
3. Not implemented (each is reported Unsupported): shoulder bars, grey, pearl, silver, champagne, LP/PATN1, roan, tobiano/sabino, tack/cosmetics.
4. There are no Windows or macOS exports yet. They need export presets plus templates in CI.

## Rules
- Do not edit `handoff/`. Do not promote coat-matrix rows; the owner approves them after reviewing renders.
- No purchases. The licensed-horse shortlist in `docs/visual/ASSET_SHORTLIST.md` is the owner's decision.
- Commit and push after each step. Don't force-push. Merging into `claude/great-euler-hq83x3` or `main` needs owner approval.

## Run locally (Linux)
```sh
pip install -r tools/visual/requirements.txt
python3 tools/horse/build_horse.py --out game/assets/horse/placeholder
dotnet build game/Cloverbrook.Game.csproj && godot --headless --import --path game
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x720x24" \
  godot --path game --rendering-driver vulkan --resolution 1280x720 res://scenes/capture.tscn -- --out "$PWD/evidence" [--sets clay,sheet,markings]
python3 tools/visual/contact_sheets.py evidence
```

## Suggested next steps
1. Plug in your horse using the contract above. Run CI and compare its clay and coat sheets against the placeholder.
2. Verify the markings fix.
3. Add a "My stable" viewer mode: `AtomicSaveStore.Load` → `StableBreedingQueries` → `DomainBridge.FromPresentation` → presenter.
4. Add export builds.
