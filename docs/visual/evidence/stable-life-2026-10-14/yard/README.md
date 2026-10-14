# Yard personalities and atmosphere review — 2026-10-14

Local review only. No commit, push, merge, asset generation or paid service was used for this slice.

## Start here

- Matching overview: `render/atmosphere/01_spring_dry.png` and `02_spring_rain.png`
- Puddle/ripple movement: `render/atmosphere/07_puddles_close.png` and `08_puddles_motion.png`
- Rooted grass/flower movement: `render/atmosphere/09_wind_a.png` and `10_wind_b.png`
- Horse responds to selection: `render/social/cuddly_rest.png` and `cuddly_response.png`
- Proud head lift: `render/social/brave_rest.png` and `brave_response.png`

The final renders use identical camera placement for each pair and explicit fixed wind/ripple time. Capture helpers wait for all incremental sky-radiance layers to settle. Pre-existing rain/petal emitters are hidden only in atmosphere comparison captures; live gameplay retains them.

## Verified

- Source contracts: 3/3 (`source-contracts.log`).
- Game build with warnings as errors: 0 warnings/errors (`render/review-run.log`).
- Weather/material integration: 52 headless checks; 62 checks with ten rendered images. Seasons preserve vegetation visibility, winter resets liquid wetness, all four materials remain per-yard, and vegetation stays at 15,600 instances with zero added draw surfaces or particle systems.
- Social simulation: 166,500 headless checks over 180 seconds; 166,524 with twelve rendered images. It recorded 12 greetings, 6 companion arrivals, 3 deliberately blocked-route yields, 6,478 attention frames and zero clearance loss. All six personalities produced real, distinct head poses; measured forehoof displacement stayed zero.
- Locomotion regression: 71,379 checks over approximately 390 seconds, including the rain phase. 144 walk starts, 286 bridge swaps, 14,317 grazing frames. Maximum pace error 0.00000867 m/s; foal mean/max mare separation 1.30/2.69 m.
- Fixed a reproduced collision regression: reading temporary rig scale let a neutral bridge enlarge a stationary horse's protected body space. Collision bounds now reserve the larger intended/neutral footprint throughout the bridge. The original failing log is retained as `social-failing-regression.log`.

## Reproduce

Build `game/Cloverbrook.Game.csproj` with `dotnet build -warnaserror`. Import `game/` with Godot 4.7.2 .NET. Run `YardAtmosphereProbe.tscn`, `YardSocialProbe.tscn`, and `YardWalkProbe.tscn` under `game/scenes/tools/`, passing `-- --out=OUTPUT_DIRECTORY`. Headless runs exercise state/geometry checks. Add `--capture` to the first two under a real renderer to save images.

Final review uses Godot 4.7.2 .NET, Forward+ Vulkan and Mesa llvmpipe at 1280×720. It is Linux software-renderer evidence. Native Mac/OpenGL rendering and hardware GPU performance have not been measured. Earlier `build-initial.log` records unfinished cross-feature integration and is superseded by the successful final build log.
