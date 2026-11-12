# Validation — November 11, 2026

- 30 GLBs generated, downloaded, normalized, and visually reviewed from front and rear.
- 30/30 Godot 4.7.2 imports loaded as nonempty mesh scenes with finite dimensions, expected height, and ground origin within 1 mm. Zero asset-check failures.
- Gallery GDScript syntax check and headless startup passed. Interactive mouse and keyboard controls were not exercised in a visible window.
- All 90 embedded maps decoded successfully at 2048 by 2048 pixels.
- 125,318 total measured triangles; per-asset range 1,839–6,621.
- The original binary geometry/texture payload is preserved for 29 models. The log obstacle was locally repaired and re-rendered from both sides.
- All 60 Meshy tasks succeeded. Actual spend 450 credits, matching the balance change. No additional paid work.

The environment prevented Godot from writing its global preferences/user-data folder and accessing system certificates. Those messages did not prevent asset import, geometry checks, or headless gallery startup. Blender's Metal initialization crashed; previews therefore use the documented CPU base-color renderer. They are actual mesh renders, but do not show full PBR lighting. Production game integration, physics, animation, and performance in a populated gameplay scene were not tested.
