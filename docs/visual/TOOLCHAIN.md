# Visual toolchain (pinned)

| Tool | Version | Where it is pinned |
| --- | --- | --- |
| Godot (.NET / mono build) | **4.7.2-stable** | `.github/workflows/visual.yml` (`GODOT_VERSION`), `game/Cloverbrook.Game.csproj` (`Godot.NET.Sdk/4.7.2`) |
| .NET SDK | 8.0.x (game targets `net8.0`; domain stays `netstandard2.1` / C# 9) | workflows |
| Blender (as Python module `bpy`) | **5.2.2 LTS** | `tools/visual/requirements.txt` |
| Python | 3.13 (the `bpy` wheel requires it) | `visual.yml` |
| numpy / scikit-image / Pillow | 2.5.3 / 0.26.0 / 12.3.0 | `tools/visual/requirements.txt` |
| Renderer in CI | Godot Forward+ on Vulkan via **Mesa lavapipe** (software) under **Xvfb** | `visual.yml` |

## Conventions
- Units are metres. glTF is +Y up. **The horse faces +X**, and its right (mane) side is +Z in Godot.
- The horse root is at ground level between the hooves.
- Horse glTF node names are a contract for the presenter: `Body`, `Eyes`, `Mane`, `Tail`, `Skeleton`. Animations are named `idle`, `breathe`, `look`, `walk`, `trot`.
- `Body` vertex data contract:
  - `COLOR_0` holds the coat masks.
  - `TEXCOORD_1` holds the canonical marking coordinates.

  Exact meanings are in `game/assets/horse/placeholder/*.manifest.json`. A licensed replacement must provide the same masks and coordinates, either as vertex data or mask textures.

## Reproduce locally (Linux)
```sh
pip install -r tools/visual/requirements.txt
python3 tools/horse/build_horse.py --out game/assets/horse/placeholder     # ~3 min
dotnet build game/Cloverbrook.Game.csproj
godot --headless --import --path game
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x720x24" \
  godot --path game --rendering-driver vulkan --resolution 1280x720 res://scenes/capture.tscn -- --out "$PWD/evidence"
python3 tools/visual/contact_sheets.py evidence
```
Software rendering is slow: a full capture takes about 10–15 minutes on 4 cores. Real GPUs are far faster.
