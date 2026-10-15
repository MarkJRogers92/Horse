# Mane and tail review, 14 October 2026

Local review only. No push, merge, publication, asset regeneration or Meshy spending.

## Result

The final main-side adult locks and shorter foal mane/forelock are retained from the refined, reviewed source. Source hashes were checked against the final capture snapshot before resuming. No further hair code changes were needed during this verification pass.

## Verification

- Fresh game build: 0 warnings, 0 errors, warnings treated as errors.
- Fresh domain tests: 319 passed, 0 failed, 0 skipped. Spec validator: OK.
- Fresh hair geometry/skin/morph/cache/palette checks: 1,465,366 passed.
- Fresh transition regression: 3,708 checks, no failures.
- Fresh yard-walk regression: 95,442 checks, 240 simulated seconds.
- Earlier final-source rendered smoke and stable day/lifecycle/breeding captures completed successfully. Those logs are retained; they were not unnecessarily repeated in this resume.
- Final render collection: 53 baseline images and 338 refined images, including the paired performance screenshots. The ZIP carries a selected review set, not all 391 images.

## Performance cost, not a cleared performance gate

Eight horses, all four families, idle and walk, eight distinct seeds; 60 warmup frames and 120 measured frames per checkout. Both runs used the same 1280 x 720 viewport, Godot 4.7.2 .NET and Vulkan Forward+ on Mesa llvmpipe software rendering. They ran sequentially after earlier CPU-heavy probes completed. This is one paired sample, not a statistical hardware certification.

| Metric | Baseline | Refined | Change |
|---|---:|---:|---:|
| Mean frame time | 303.50 ms | 383.21 ms | +26.3% |
| Median frame time | 291.96 ms | 370.81 ms | +27.0% |
| p95 frame time | 385.60 ms | 510.17 ms | +32.3% |
| Draw calls | 70 | 102 | +32 |
| Rendered primitives | 306,230 | 417,974 | +111,744 |
| Initial eight-horse presentation | 737 ms | 1,894 ms | +1,157 ms |

The stable currently has at most six visible yard horses (six slots; four base stalls plus two upgrades). Eight is a stress comparison. This was not a six-horse yard FPS test, and software-renderer timing does not predict the user's Mac GPU performance. No performance-neutral or Mac-ready claim is made.

Generated cards currently have no distance LOD or visibility cutoff and cast shadows. The smallest recommended follow-on optimization is distance-based hiding/fading of the added cards while retaining the original opaque hair core. Preserve close-up appearance, check transition popping, then measure on the target Mac before choosing a threshold. This optimization was not implemented in this pass.

## Visible limitations

- The main hanging side has directional locks. The opposite side still exposes the smoother original opaque core, including a relatively squared end.
- Deep grazing fans the foal's root-anchored locks into visible steps. This is a visual limitation despite the tested deformation bounds.
- Strand fringes can show aliasing. These are skinned ribbons, not a physical hair simulation.
- Structural tests do not prove precise root attachment, self-intersection freedom, or silhouette quality. Screenshots must still be reviewed.
- The existing smoke shutdown resource-leak diagnostics occur in the baseline and changed runs. They are preserved rather than described as a clean shutdown.

## Review images

- [Adult, final main side](after-720p/horse.standard.adult.v1/silver_black__v0__head.png)
- [Foal, final main side](after-720p/plain/silver_black/horse.standard.foal.v1__neutral__idle_head.png)
- [Adult, matched baseline](before-720p/horse.standard.adult.v1/silver_black__v0__head.png)
- [Foal, matched baseline](before-720p/plain/silver_black/horse.standard.foal.v1__neutral__idle_head.png)
- [Opposite-side limit](after-720p/contracts-adults/horse.standard.adult.v1__silver_black__walk__mane_left.png)
- [Deep-graze limit](after-720p/graze/horse.standard.foal.v1__graze_q3.png)

All listed images are direct Godot captures. No generative image edits were used.
