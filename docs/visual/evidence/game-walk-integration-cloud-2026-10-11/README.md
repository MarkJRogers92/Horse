# Checked Stock and foal Walk in the game

The owner authorized integration after reviewing the polished candidates. Work
and testing ran exclusively on the cloud Linux computer, using official Godot
4.7.2 stable .NET (ed1daf0bf) and .NET SDK 8.0.425. No Mac was operated.

## Implemented and verified

- Stock maps to compact adult; the single juvenile master serves standard and
  compact foal. The viewer and stable presentation expose Light/Stock plus Foal.
- Walk uses neutral morphs at unit scale. Idle restores the selected descriptor,
  its original shape and height. Nonzero shape/height and unsupported probes are
  refused by direct Walk presentation. Untested gait extremes remain unaccepted.
- Native family UVs, normals, all four component morphs and coat masks are exact
  at every exported corner. No Light atlas is assigned to Stock or foal.
- The accepted Light catalog entry, assets, imports, shaders, atlas and evidence
  are unchanged. preservation.json records every protected file checked.
- The actual project importer uses 60 fps, named Skin binds and no key optimizer
  for the new assets. Runtime evidence checks every referenced weight, all four
  components, contact, loop transforms and fixed hair/skin attachment witnesses.
- One profile-speed actor route, camera follow, four-cycle wrap, paused pose,
  resume, repeated Walk/Idle transitions, coat changes and both foal mappings
  passed through the normal main entry and actual viewer controls.
- Main smoke checks three moving coats per family, signed-detail policy, star and
  leg-white shader bindings, and stale/mismatched paths, hashes, fps, duration,
  speed, duty factor, family age and borrowed-atlas refusal.
- 24 real Compatibility/OpenGL captures were inspected: bay/chestnut/cremello,
  stars, leg whites and all four viewer mappings. Stills alone are not gait proof;
  the independent imported-Skin and moving application tests establish movement.

## Final imported mechanical evidence

Exact reports are in the respective game/assets/horse/production/evidence/
{stock,foal}-walk-polish/game-import-report.json files and are SHA-bound in code
and the catalog. Run tools/horse/check_imported_walk.gd with --imported to
reproduce against the normal project's actual imported scenes.

Stock: all 429 sole witnesses register, maximum planted drift 1.092 mm,
Body stretch 1.461977x, and all 222 closed Mane/Tail joins stay exactly closed.
Foal: all 176 soles register, maximum planted drift 0.491 mm, Body stretch
1.646699x, and 395 imported attachment witnesses stay within 0.059 mm gap drift.
Every component has four morphs and normalized, nonzero skin weights. All
components remain below the unchanged 2x stretch bound. Both source clips are
in-place; no second root-motion translation is applied.

## Tests

- Warnings-as-errors game build: passed, zero warnings/errors
- Domain suite: 190 passed, zero failed/skipped
- Focused Python evidence tests: 14 passed
- Frozen-spec validation and whitespace checks: passed
- Actual main-entry headless and rendered integration smoke: passed
- Additional appearance/foal Idle/adult Idle/viewer/Light detail/Stock/probe
  regression: 50 entries, zero failures
- Exported raw-evidence/imported-scene checks: passed from an empty project

The broader pass exposed a pre-existing fragile adult Idle assertion: it compared
only the initial head angle against the half-cycle, where periodic movement can
return near its starting pose. It also used single-precision acos for tiny
rotations. The test now takes the maximum over the advancing interval and uses
relative-quaternion atan2, retaining the 0.0001-radian movement gate. Unchanged
Light/Stock clips show approximately 0.009 radians of movement. The original
failure log is retained; the corrected regression has zero failures.

The cloud test display was temporary and terminated after rendering. Later
regression rendering used a temporary authenticated X server; no lasting access
or user-computer session was created.

## Mac boundary

A separate unsigned universal build 20261011.2 was cross-exported successfully.
Its PCK contains the checked raw evidence and imported resources. Native Apple
signing correctly refuses on Linux because Xcode command-line tools are absent.
The cloud candidate is unsigned, unnotarized and **not Mac launch-verified**.
The existing signed, M2-tested download remains the verified Mac package.
Do not present the new candidate as launch-ready or as a replacement verification.

No release-matrix promotion, main merge, public release or paid generation occurred.
