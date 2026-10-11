# Appearance studio fixture generator

Run from the Horse repository root:

```sh
dotnet run --project tools/horse/production-fixtures/ProductionFixtures.csproj
```

The tool writes `game/data/fixtures/appearance-studio.json`. It constructs eight
deterministic art-test horses and resolves every descriptor through the existing
`GameTraitResolver` and `PhenotypeResolver`. The four authored body profiles are
Light athletic, Light long-stride, Stock muscular, and Stock cob-influenced; each
contains two phase-one supported coat examples. They use fictional game-marker
inputs and zero developmental factors, plus distinct fixed appearance seeds so
individual markings differ. They are not player saves, breed records, or offspring.

The tool builds a temporary in-memory matrix that marks phase-one rows Released
only for this demonstration, matching the existing domain reference-fixture
practice. It never writes to the frozen matrix or changes live release status.
The resulting document is explicitly `notForLiveStock` and is only intended for
the Appearance studio and its capture checks.
