# Checked game integration and Mac package

Game source: `70a7232feeb99d0be4251508a6bbec40d84d881a`, branch
`codex/horse-production-library`. Owner authorization covers integration,
downloadable Mac export and pushing this branch. No main merge or public hosting.

The release export, strict Apple ad hoc bundle verification, exported-app smoke
test, normal app interaction and fresh-unpack smoke test completed successfully
on Apple M2. `unpacked-verification.json` records the archive checksum and exact
381-file comparison including executable modes. The app includes .NET 8.0.31.
Intel execution is untested; the app is not Apple notarized.

Smoke coverage includes zero Walk morphs/unit scale, active signed detail at
strength 1, the playing clip, profile-speed actor/camera movement, paused-route
freeze, original Idle shape restoration, and refused foal/Stock/probe Walk.
`probe.grey` produces one intentional unsupported-feature warning in the smoke
test. No ordinary-game error was observed.

The targeted warnings-as-errors game build and frozen-spec validator passed
during implementation. Domain/save code is unchanged; no new full-domain test
result is claimed. The prior neutral Walk geometry/contact proofs are retained
in the existing Light Walk evidence; this package does not expand their scope.

`preservation.json` confirms fifteen protected files remained byte-identical,
including the pre-existing dirty motion tool. The default catalog is the sole
authorized change among the original sixteen protected paths. New editor-only
export support retains exact raw evidence beside normal imported resources;
runtime provenance checks remain enforced.
