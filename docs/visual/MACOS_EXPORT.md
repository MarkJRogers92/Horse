# macOS build with checked Light Walk

The normal Horse viewer and stable view offer the checked Light adult Walk with
the accepted v5 signed-detail atlas and procedural coats. Walking temporarily
uses neutral shape values and unit scale. Idle restores the source descriptor's
shape. Stock, foal and unsupported probes retain their existing restrictions.
This does not accept extreme-morph gait quality, Graze or moving-herd performance.

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

The enabled `horse_asset_evidence` export plugin adds only the original Light
GLB, Walk GLB, detail PNG and neutral-skin mask bytes alongside their normal
Godot imports. JSON evidence is included by the preset. Do not remove runtime
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
