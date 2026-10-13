# D-013 — Release the phase-1 coats; Walk with every body shape

Accepted by owner instruction on 2026-10-13 ("all 4 … you have my permission for all").

## 1. Phase-1 coat release
The 13 phase-1 classes in `docs/contracts/COAT_SUPPORT_MATRIX.json` (chestnut, bay, black,
palomino, buckskin, smoky black, cremello, perlino, smoky cream, red dun, bay dun, grullo,
non-dun1 marked) move to `released` for genetics, visuals and release:
- Genetics: implemented with exact fixtures and the full domain suite.
- Visuals: every class renders on the Light/Stock adults and the foal with markings, in
  neutral and stable lighting (visual CI contact sheets), and the owner reviewed the game.
Phase-2 classes (dunalino family, tobiano overlay) stay unreleased and are still refused.

Consequences:
- The game reads a byte-identical copy at `game/data/coat_support_matrix.json`
  (`tools/validate_handoff.py` fails if the copies differ) through `DomainBridge.Matrix`.
- Stable portraits and yard horses use the normal `ResolveForPresentation` path. The D-012
  `ResolveForStablePreview` workaround is removed.
- Breeding itself is unchanged: the stable planner still uses its text-only sandbox matrix.

## 2. Walk with the horse's own body shape
The checked Walk variants already carry the same four body channels (body_length,
chest_width, neck_length, muscle) as the Idle masters. A deformed review rendered every
family (standard/compact adult and foal) walking at neutral, each channel at ±1, and all
channels at +1 and −1, at four stride phases from side and three-quarter views:
`docs/visual/evidence/walk-shapes-2026-10-13/` (tool: `scenes/tools/WalkShapeAudit.tscn`).
No tearing, intersections or detached hair were found beyond what the neutral shape shows.

So `HorsePresenter` now plays Walk for any shape inside the declared channel range, and
scales the ground speed with height so hooves do not slide. The foal Idle↔Walk bridge in
the viewer still interpolates through the neutral shape and keeps its neutral requirement.
Probes and families without a checked Walk are still refused.
