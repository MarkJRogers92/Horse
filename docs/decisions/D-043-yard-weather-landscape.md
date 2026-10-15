# D-043 — Gentle weather and landscape motion

Owner-approved bounded visual polish, 2026-10-14. Implemented locally on the dot cloud computer; no paid assets, Meshy calls, commits, push or merge. This scope is presentation only and does not change domain records, saved weather, seasons, or the existing day lighting.

## Design

- Keep the existing 14,000 grass and 1,600 flower instances in their two MultiMeshes. One wind shader, with two per-yard materials, bends grass from its fixed base and lightly sways whole flower heads. Existing seasonal visibility and grass tints still apply.
- Drive wind and ground wetness from the existing `SetWeather`/`SetSeason` flow. Rain increases wind gradually; sand and the barn path become damp over several seconds and dry more slowly when the rain stops.
- Draw sparse shallow puddles and small staggered rain rings directly inside the existing paddock shader. They fade before the grassy rim and only affect the top face. The path darkens but does not gain puddles. There are no transparent planes, mirror reflections, added particle emitters, or additional draw surfaces.
- A winter rain day remains snow: liquid wetness is reset to zero and rain rings are disabled immediately. Existing snow, blossom, autumn leaves, sky, lighting, and rain emitters remain in control of their original code.
- Materials are owned by each yard, never global shader state, so menu and game viewports cannot leak weather into one another.

## Implementation and expected cost

`StableYard.Atmosphere.cs` owns state, frame-rate-independent wet/dry easing, wind strength, and the explicit shader clock. `StableYard.cs` contains only three hooks: initialization with the existing path, per-frame uniform updates, and weather updates.

Added rendering cost: zero draw surfaces, zero particles, zero mesh instances, and zero per-blade CPU updates. Four existing surfaces use four atmosphere materials. Seven scalar uniform updates per frame are constant in the number of plants. Wind adds two sine waves per existing vegetation vertex. Wet paddock pixels add two low-frequency noise samples and one ring calculation; a uniform branch skips that work while dry. These are workload expectations, not measured GPU timings. Rendering measurements and visual review must be reported separately for the actual test backend.

## Reproducible review

- Fast shader interface checks: `python3 -m unittest discover -s tools/visual -p 'test_yard_atmosphere_contract.py'`. The three contracts failed against the prior implementation and passed after the feature was added.
- Runtime checks: run `res://scenes/tools/YardAtmosphereProbe.tscn -- --out=DIR` with the pinned Godot .NET build. Checks wetting/drying, ripple rain gating, winter, seasonal vegetation, material reuse and isolation, and node/instance budgets.
- Add `--capture` under a real renderer for fixed-time dry/rain/winter, puddle, and foliage comparisons. The focused probe hides the pre-existing stochastic precipitation emitters only in its screenshots; live gameplay retains them. `SetAtmospherePreviewTime(seconds)` supplies deterministic wind and ripple time, and passing null resumes normal animation.

Local verification on 2026-10-14: source contracts 3/3, game build 0 warnings/errors, and the headless `YardAtmosphereProbe` passes all 52 runtime checks. Materials stay isolated and reuse their four instances across all seasons, winter has zero liquid wetness, and vegetation remains at 15,600 instances with no new nodes or particles. Evidence is under `docs/visual/evidence/stable-life-2026-10-14/yard/`. The final 1280×720 Vulkan/llvmpipe run passes 62 checks including all ten captures; dry/rain, all four seasons, puddle rings and fixed-time vegetation movement were inspected. Capture helpers now wait for all incremental sky-radiance roughness layers before comparing frames. Final screenshots and render log are under `yard/render/atmosphere/` and `yard/render/review-run.log`. This is Linux software-renderer evidence, not native Mac validation or GPU timing measurements.
