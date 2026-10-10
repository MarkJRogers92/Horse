# D-003: Engine is Godot 4 (.NET / C#)

| Field | Value |
| --- | --- |
| Status | **Accepted**. The owner delegated the choice ("whichever is easier"), 2026-10-10. Revised the same day after the owner said they have no Mac or PC. |
| Chosen result | Godot 4 stable, .NET build (pin the exact version in P0-02b). C#, Blender for art, glTF for asset import. |

## Reason
The owner has no Mac or Windows machine, so **every build, render and screenshot must come from GitHub Actions** and be produced by AI models. Under that constraint, Godot is much easier than Unity:

| Need | Godot 4 | Unity 6 |
| --- | --- | --- |
| Runs headless in CI | Small download, no account | Large editor download, plus licence activation secrets (GameCI) |
| Licence | MIT, free | Account/seat licence must be activated in CI |
| Files AI models can edit | Scenes and resources are plain text (`.tscn`/`.tres`) | Scene YAML is fragile, and much work needs the editor GUI |
| Windows and macOS export from a Linux runner | Supported with export templates (macOS unsigned until the owner gets an Apple developer account) | Mac builds effectively need a macOS runner (10× CI minutes) |
| Language | C# (the domain library is shared unchanged) | C# |

The domain library (`netstandard2.1`, pure C#) is unaffected. It does not depend on any engine.

## Consequences
- The workflow text that says "Unity" now means Godot equivalents: Generic rig becomes Skeleton3D, URP becomes the Forward+ renderer, LODGroup becomes Godot mesh LOD/visibility ranges, and the Unity +Y up / +Z forward rule becomes Godot +Y up / −Z forward (glTF).
- D-002 (licensed base horse): prefer candidates that ship as `.blend`/glTF or clean FBX. Unity-only packages (`.unitypackage` with Unity-specific shaders) score lower.
- **Visual review without a computer:** CI renders turntable screenshots and the coat contact sheet headlessly and uploads them as Actions artifacts or commits them under `docs/evidence/`. The owner reviews them on a phone or tablet.
- **Playing builds:** the owner needs some way to run a build eventually. Options: a borrowed or cheap PC, a cloud gaming VM, or a web export for review. Verify that the current Godot release supports C# web export before relying on it. This doesn't block anything until Phase 2.
- Risk: Godot has fewer ready-made realistic horse assets. That is mitigated by preferring glTF/Blender assets in D-002.

## Alternative considered
Unity 6 LTS + URP, the original default. It has a richer asset store, but licence activation and editor-only workflows conflict with the no-computer, all-CI constraint. Reconsider only if the owner gets a capable PC/Mac and Godot fails the hero horse visual gate.
