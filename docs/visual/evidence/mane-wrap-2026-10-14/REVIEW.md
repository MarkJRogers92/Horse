# D-039 crest-wrap correction — 2026-10-14

The owner approved retaining the long mane mainly on one side, adding short crest/opposite-side strands and softening the harsh gray shine before another Mac build.

## Small runtime change

- Append 24 adult / 20 foal root-anchored strips to the existing mane surface. The long locks and forelock retain their geometry and seeded random sequence.
- Sample the opposite side of the original hair/body surface and inherit the crest root's skin weights and morphs. Body data is read-only; original hair cores, assets, rig, body UVs, saves and domain behavior are unchanged.
- Generated mane vertices rise from 5,220 to 5,940 for adults (+13.8%) and 3,300 to 3,900 for foals (+18.2%). There is still one generated mane surface and one generated tail surface per horse; no extra material or draw surface is introduced.
- Raise generated-card roughness from 0.56–0.61 to 0.78–0.82 and reduce specular from 0.30 to 0.22. This softens the direct-light panel highlight while retaining strand shading and the existing palette contract.

## Evidence

The three JPEG contact sheets retain representative final-source screenshots. The rear before/after pair uses the same stable lighting, camera (yaw -115°, pitch 22°), adult/foal scale and animation sample. Long-side and opposite-side views show the asymmetric design. The full 116-image neutral gallery also covers all four family mappings, dark bay and pale palomino hair, Idle, Walk, front, rear, both sides, both mane close-ups, extreme morphs and grazing.

All new images use Godot 4.7.2 **GL Compatibility / OpenGL3**, the rendering method used by the Mac export, on Linux Mesa llvmpipe. This is renderer-compatibility evidence, not execution on a physical Mac or a target-GPU performance measurement. Source and loaded game binary hashes are in `source-sha256.txt`.

## Validation

- Game warnings-as-errors build: 0 warnings, 0 errors.
- Release domain tests: 342 passed, 0 failed, 0 skipped.
- Handoff validator: OK.
- A new opposite-side coverage assertion fails on the preceding generator; `red-probe.log` retains that failure.
- Final headless hair probe: 1,633,918 checks passed. Final rendered hair probe: 1,634,086 checks passed. The report includes far-side crest coverage, short-lock bounds, the 20% mane-geometry budget, single-surface count, unchanged imported data, deterministic generation/cache, normalized skin weights, all source morphs, Walk/Idle and full-graze edge-stretch bounds.
- Four-family model-transition probe: 3,708 checks passed.
- Yard walk/graze regression: 71,379 checks over 390 simulated seconds; 144 walks, 286 rest-bridge swaps, maximum pace error 0.00000867 m/s.
- Main smoke: all four family mappings, coats, Walk/Idle, camera and viewer controls pass.
- Stable-day: pass, 7 screenshots, day 1 / 514 coins. Lifecycle: pass, 7 screenshots, day 22 / 570 coins. Breeding: pass, 9 screenshots, day 10 / 886 coins. Overall wrapper exits 0.
- The GL driver reports unsupported V-Sync / screen-space AA as expected for this backend. Unsupported-fixture warnings are deliberate smoke/transition checks. Lifecycle and breeding still report 8 ObjectDB instances / 4 resources at shutdown, matching their previous D-039 logs; functional assertions and exit codes pass. No new runtime error was observed.

## Review limits

The owner reviewed the final matching stable views and said “looks better.” Independent read-only code and image review found no new blocker. The far-side short coverage is deliberately asymmetric. Close-up pale foals retain tiny crest seams, and deep foal grazing still exposes the previously documented stepped/fanned locks. This correction is not a new hair simulation or a full groom. Existing target-Mac performance limitations remain; no new performance claim is made.

Full-resolution images and complete runtime logs are retained in the task's `hair-wrap-review/final` evidence folder. The compact checked-in set avoids duplicating the full gallery in repository history.
