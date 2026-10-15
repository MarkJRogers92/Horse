# D-041 — Visible yard personalities and gentle social life

Date: 2026-10-14. Status: owner-approved scope; local implementation pending review.

## Decision

Make the existing six derived personalities visible with short, restrained head reactions. Cuddly horses glance warmly, Cheeky horses nod, Shy horses peek away, Greedy horses lower their heads in a hopeful sniff, Brave horses lift their heads, and Dreamy horses respond more slowly. Pointer or roster hover and selection count as approaching a horse in the scene-first yard.

Nearby resting horses occasionally exchange glances. The first nearby yardmate they greet becomes a session-local familiar companion; on some later walks, they choose a clear place alongside that horse and settle to graze. There are no relationship scores, rewards, save changes or paired animation dependencies. Mother/foal following and rain shelter retain target priority.

Before moving or turning, check horizontal body capsules, with a small steering preference for passing and a brief neutral-bridge stop when blocked. Reserve the larger intended/neutral-bridge height for the entire transition, so a stationary rig swap cannot expand into a neighbour. Travel and Walk playback continue to share the exact same pace. Existing slot overlap may unwind but a step cannot deepen it. This is simple yard avoidance, not a general navigation system.

## Boundaries

- All new state is runtime presentation state, bounded by the six visible horses.
- Head cues exist only on Idle, blend out, and clear before neutral model bridges.
- Existing grazing, ear/tail animation, weather, mother following and shaped Walk support remain.
- No hair geometry, source assets, genetics, domain rules or save-format changes.
- No paid assets, push, merge or publication authorized by this implementation.

## Verification

`YardSocialProbe.tscn` uses a fixed clock, all six personalities, segment geometry cases, body clearance, greetings, companion arrival, yielding and Idle/Walk safety assertions. `YardWalkProbe.tscn` remains the longer locomotion, grazing, mother/foal and rain-shelter regression. Actual results are recorded in the parent handoff after execution.

### Local verification, 2026-10-14

- Game `dotnet build -warnaserror`: 0 warnings/errors.
- `YardSocialProbe`: 166,500 checks over 180 simulated seconds; 12 greetings, 6 companion arrivals, 3 blocked-route yields, 6,478 attention frames, 3,732 walking frames, zero clearance loss. The blocked-route fixture is deliberate: a successful avoidance run need not yield naturally.
- Fixed a reproduced bridge-clearance regression at frame 2,786: a stationary horse's model scale changed from 0.9960 to 0.9964 during a bridge, deepening overlap. Collision bounds now use a stable intended/neutral envelope instead of temporary model scale. The same all-six-personality simulation passes.
- `YardWalkProbe`: 71,379 checks across the sunny and rain phases (390 simulated seconds), 144 walk starts, 286 bridge swaps, 14,317 grazing frames. Maximum ground/clip pace error 0.00000867 m/s; foal mean/max mare separation 1.30/2.69 m. Rain moves the adults toward shelter.
- Evidence: `docs/visual/evidence/stable-life-2026-10-14/yard/`. Optional `--capture` on `YardSocialProbe` renders fixed-clock rest/selection responses through the real skeleton modifier. The final 1280×720 Vulkan/llvmpipe capture passes 166,524 checks, including all six real skeleton responses and twelve rest/response images. Cuddly and Brave interaction pairs were inspected; all six muzzle measurements show bounded head movement with zero measured hoof displacement. Final captures wait for incremental sky radiance to settle. No native Mac rendering or GPU timing is claimed.
