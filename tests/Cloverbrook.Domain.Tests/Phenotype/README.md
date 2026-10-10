# Genetics and renderer-reference tests

Run `dotnet test Cloverbrook.sln --configuration Release` from the repository root, or inspect the approved `domain` Actions workflow. The tests load the actual frozen `handoff/GENETICS_FIXTURES.json` and the real coat support matrix; they do not replace the original expected probabilities.

`FrozenGeneticsTests` executes every concrete genetics fixture. `PedigreeTests` executes every pedigree coefficient and invalid/unknown-history case. The remaining classes provide additional invariants and review A6 regressions. Unhandled fixture kinds/validation cases throw rather than being silently skipped.

## Art-track handoff

The passing run uploads a second artifact, **visual-descriptor-fixtures**, containing `visual-descriptor-fixtures.json`: 13 phase-1 matrix class references at adult and foal ages (26 descriptors). It includes explicit genotype overrides, visible labels, versioned material recipes, logical mesh family IDs, bounded morph weights and identical age-independent marking keys. The `non_dun1_marked` row is a bay overlay reference, not a replacement base pigment.

These are deterministic data fixtures, NOT renders or production meshes. Their metadata says `headless_reference_only`, `not_rendered` and `notForLiveStock`. The capability-version suffix is `.test-only`. No real matrix status is changed. Do not import the fixture release assumptions into a live game.

The Godot/art track should map the logical asset/mask IDs to actual approved assets, compose the declared material layers, render the adult/foal pairs in neutral and stable lighting, and reuse the presenter for portraits. Verify anatomy, all age/LOD masks and intended label/color consistency. Only then may the integration owner approve the real matrix rows. Full founder/NPC reachability remains a separate P1-07 gate; individually drawable phase-1 parents can still produce an unreleased cream+dun combination.

The fixture export is written under the test assembly's `TestResults/` directory, never into `handoff/`. CI only publishes the descriptor artifact after the complete test job succeeds. TRX evidence is still uploaded on failure for debugging.
