# macOS builds with checked family Walk

The current source offers checked Light and Stock adult Walk plus both foal
family mappings. Walk uses neutral shape values at unit scale; Idle restores the
selected descriptor. Stock/foal keep their own UVs, masks and procedural coats;
only Light uses its accepted v5 signed-detail atlas. Unsupported probes, untested
shape extremes and Graze remain outside this acceptance.

The new cloud cross-export (build 20261011.2) is an **unsigned, unnotarized
candidate**, not Mac launch-verified. Linux cannot run native Apple codesign.
Its raw evidence and imported resources passed independent PCK checks, and the
game passed Godot 4.7.2 .NET Linux runtime tests. The older signed M2-tested
download documented below remains the verified Mac package. No Mac was accessed
for this integration.

## Build and verify

Use the official Godot **4.7.2 stable .NET** editor and matching .NET export
templates. The game targets .NET 8; the checked host built with SDK 10.0.401.
The Mac app includes its runtime and needs no separately installed SDK.

From the repository root, set `GODOT_EDITOR` to that editor's executable:

```sh
TASK_MAC_BUILD=$(mktemp -d /private/tmp/cloverbrook-macos.XXXXXX)
"$GODOT_EDITOR" --headless --path game --export-release macOS "$TASK_MAC_BUILD/Cloverbrook Stables.app"
/usr/bin/codesign --verify --deep --strict --verbose=2 "$TASK_MAC_BUILD/Cloverbrook Stables.app"
"$TASK_MAC_BUILD/Cloverbrook Stables.app/Contents/MacOS/Cloverbrook Stables" -- --smoke-test
```

The preset uses universal native binaries because the official template contains
universal binaries. Intel execution has not been tested. The Apple Silicon
minimum is macOS 13. The Mac renderer override uses the checked Compatibility
renderer and disables its unsupported screen-space AA setting.

Use a local temporary build folder outside iCloud-managed Documents. On the
checked host, iCloud attached Finder metadata to `.app` folders in Documents and
made strict bundle verification fail. Native Apple signing creates valid DER
entitlements; the engine's built-in ad hoc signer was rejected by the host OS.
Neither workaround changes OS security settings or removes quarantine metadata.

The enabled `horse_asset_evidence` export plugin adds the original Light
GLB, all three Walk GLBs, detail PNG and neutral-skin mask bytes alongside their
normal Godot imports. Exact Stock/foal proof JSON is also exported explicitly;
other JSON evidence is included by the preset. Do not remove runtime
provenance checks to accommodate an export. The smoke test requires the checked
Walk metadata and actual lossless shader detail to remain active in the app.

## Package

Put the app and launch instructions in a folder, then preserve executable modes
while excluding unnecessary filesystem metadata:

```sh
/usr/bin/ditto -c -k --norsrc --noextattr --noacl --keepParent "$TASK_MAC_BUILD" Cloverbrook-Stables-macOS.zip
```

Extract the ZIP into a fresh local folder, compare its app file hashes, rerun
strict signature verification and launch the extracted app with `--smoke-test`.
Also launch it normally and select Horse viewer → Walk, change coats, toggle
Foal and return to Idle. A build exit code alone does not establish these checks.

The app is ad hoc signed and **not Apple notarized**. A downloaded copy may
require the owner's approval through macOS. Distribution with a Developer ID
and notarization is a separate package and needs the owner's Apple setup.
Official guidance: [Godot macOS export](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html).

## Existing checked downloadable package (Light-only Walk)

`Cloverbrook-Stables-macOS-2026-10-11.zip` contains the app, launch instructions,
build manifest and license notices. Game source is commit
`70a7232feeb99d0be4251508a6bbec40d84d881a`; later documentation checkpoints do
not change its game code. The 144,201,711-byte archive has SHA-256
`2cf47cb414760e50430aae67b9efc91d7b14d015962d9f7ccc78b4e3ccbb13eb`.

The exported and freshly extracted apps passed actual runtime smoke checks on
Apple M2. All 381 extracted app files matched the original hashes, sizes and
executable modes, and strict native bundle signature verification passed.
Normal exported-app interaction also checked coats, foal fallback, the walking
head camera and switching back to Idle. Supporting logs, package receipt,
preservation checks and review notes are saved in
[`evidence/game-macos-70a7232`](evidence/game-macos-70a7232/README.md).
