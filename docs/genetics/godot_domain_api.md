# Domain integration boundary for Godot

The accepted engine remains Godot 4 .NET. These engine-independent C# 9 APIs require no scene, shader, mesh, Godot singleton, or second genetics implementation.

`StableBreedingQueries` is an immutable read/preview facade over a loaded `StableSnapshot` and the existing `ConceptionService`. Construct a fresh facade after a successful durable command or reload. `GetHorse` and `GetOffspring` return `PublicHorseSummary`, which contains no genome or diagnostic phenotype. `GetKnownGenetics` returns only revealed calls. `GetPregnancy` returns a permanent offspring ID and recorded dates without exposing hidden calls.

`PreviewBreeding` projects only saved player knowledge into `BreedingForecastService`. It never derives an observation, trait prior, or allele call from hidden genotype. `GetKnownPedigree` exposes a parent edge only when that horse's knowledge records the ancestor ID; undisclosed founder-background assumptions remain unknown. Advanced publicly disclosed pedigree scenarios can use the forecast service's public-pedigree argument directly. `CompareHypotheticalPairing` accepts caller-entered phased assumptions and labels the result Hypothetical; no forecast authorizes conception.

Version metadata is checked before forecasting actual horses. Stored unsupported historical implementations may be retained without execution; an actual forecast then throws an explicit compatibility failure rather than interpreting those horses with current rules. Availability checks do not read hidden allele calls.

`CheckBreedingEligibility` is an advisory refreshable projection of saved authority, fees, revisions, eligibility, capacity and release closure. It uses the existing eligibility and closure services without drawing a foal. The durable command must revalidate the latest saved state under the store's writer lock. An identical previously committed command is recognized before old preconditions; a different request under the same ID is rejected.

Only `ResolveVisualDescriptor` crosses into the renderer. Its `PresentationResolution` contains either a `HorseVisualDescriptor` or an unsupported reason, never the diagnostic `Phenotype` or a genome. The real support matrix remains unreleased. Tests use clearly named TEST-ONLY matrix states; these are not production art approval.

The shared persistence path is `StableSaveCodec` plus `AtomicSaveStore`. Godot supplies its platform-specific absolute save path and displays explicit load/recovery failures. `LoadGame` maps to `Load`, `CommitBreeding` maps to `CommitConception`, and trusted non-breeding `SaveGame` updates map to `SaveSnapshot(replacement, expectedRevision)` with a one-step save revision. UI code must not read authoritative snapshot horse genomes. The read facade does not create another save or command implementation.

```csharp
// hostSavePath, trustedInitialState, service, and disclosedScope come from the
// integration adapter/game configuration. Initialize is for a new slot only.
var store = new AtomicSaveStore(hostSavePath);
var loaded = store.Load();
if (loaded.NeedsRecoveryAcknowledgment)
    throw new SaveRecoveryRequiredException(loaded); // show recovery offer first
var queries = new StableBreedingQueries(loaded.State, service, artVersion, maskId);
var first = queries.GetHorse(mareId);
var second = queries.GetHorse(stallionId);
var forecast = queries.PreviewBreeding(first.HorseId, second.HorseId, disclosedScope);
// Build request from actual saved revisions and the trusted confirmed hold.
var readiness = queries.CheckBreedingEligibility(request);
var committed = store.CommitConception(request, service); // locked revalidation + disk commit
queries = new StableBreedingQueries(committed.State, service, artVersion, maskId);
var offspring = queries.GetOffspring(request.EventId);
var presentation = queries.ResolveVisualDescriptor(offspring.HorseId);
// Give presentation.Descriptor to the renderer only when Supported.
// A restart constructs a new store, calls Load, and recreates these queries.
// Retrying the exact request returns the same receipt without another charge.
```

`SaveIntegrationTests.PublicApiForecastCommitSaveRestartAndDuplicateProtectionUseOneDomainPath` demonstrates this sequence with actual filesystem writes, a manual non-breeding save, full pedigree/marking/appearance identity, and byte-identical retry protection. `AtomicSaveStoreTests` covers pre/post-publication interruptions, explicit backup acknowledgment, locking, and corruption. In-memory TEST-ONLY release fixtures are used solely for headless proof. Actual Godot scenes and real art approval remain the visual track's responsibility.
