using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Cloverbrook.Domain.Tests
{
    public sealed class SaveIntegrationTests
    {
        private readonly ITestOutputHelper output;
        public SaveIntegrationTests(ITestOutputHelper output) { this.output = output; }
        [Fact]
        public void LaterClientBuildPreservesPermanentVisualIdentityAndFullPedigree()
        {
            var original = SaveTestData.Commit().State; var h = original.Header;
            var nextBuild = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, "later-client-build", h.SaveRevision, h.CalendarDay, h.CalendarProfileVersion,
                h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion,
                h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, h.PayloadHash);
            var codec = new StableSaveCodec(); var loaded = codec.Decode(codec.Encode(original.WithHeader(nextBuild)));
            var before = original.Conceptions.Single().UnbornAtCommit; var after = loaded.Conceptions.Single().UnbornAtCommit;
            var resolver = new PhenotypeResolver(); var matrix = ForecastTestData.TestMatrix("chestnut", "palomino", "cremello");
            Assert.Equal(resolver.ResolveForPresentation(before, matrix, "test-art", "test-mask").Descriptor!.CacheKey,
                resolver.ResolveForPresentation(after, matrix, "test-art", "test-mask").Descriptor!.CacheKey);
            Assert.Equal("later-client-build", loaded.Header.GameBuildVersion);
            Assert.Equal(new PedigreeGraph(original.Horses.Select(x => new PedigreeNode(x.HorseId, x.Parentage))).PedigreeHash,
                new PedigreeGraph(loaded.Horses.Select(x => new PedigreeNode(x.HorseId, x.Parentage))).PedigreeHash);
            Assert.Equal(original.Conceptions[0].Commit.CommitHash, loaded.Conceptions[0].Commit.CommitHash);
        }
        [Fact]
        public void ThreeThousandHorsesRoundTripAndReachabilityWithinTheDeclaredBudget()
        {
            var watch = Stopwatch.StartNew(); var genome = ForecastTestData.Genome();
            var horses = Enumerable.Range(1, 3000).Select(i => ForecastTestData.Horse(i, genome)).ToArray();
            var state = new StableSnapshot(ConceptionTestData.Header(), ForecastTestData.Owner, horses, 100, 0);
            var codec = new StableSaveCodec(); var bytes = codec.Encode(state); var loaded = codec.Decode(bytes);
            var closure = LiveCatalogCoatGuard.Check(loaded.Horses, ForecastTestData.TestMatrix("chestnut"));
            watch.Stop(); output.WriteLine("3000 horses: {0} bytes, codec + all-generation closure {1:F3} seconds", bytes.Length, watch.Elapsed.TotalSeconds);
            Assert.Equal(3000, loaded.Horses.Count); Assert.Equal(horses.Select(h => h.Genome.GenomeHash), loaded.Horses.Select(h => h.Genome.GenomeHash));
            Assert.True(closure.IsSupported); Assert.Equal(1, closure.CombinationCount); Assert.True(bytes.Length <= StableSaveCodec.MaxPayloadBytes + StableSaveCodec.FrameHeaderBytes);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(45), "3000-horse persistence/closure exceeded the declared 45-second headless regression budget.");
        }
        [Fact]
        public void ChecksumValidMutationFuzzHasOnlyExplicitOutcomes()
        {
            var codec = new StableSaveCodec(); var frame = codec.Encode(SaveTestData.Commit().State);
            var rng = new CounterRng(new Seed128(23, 71), "test-only/save-fuzz");
            for (var i = 0; i < 100; i++)
            {
                var at = (int)rng.NextBelow(frame.Length - StableSaveCodec.FrameHeaderBytes);
                var changed = SaveCodecTests.Reframe(frame, alterPayload: p => p[at] ^= 0x80);
                try
                {
                    var loaded = codec.Decode(changed); // harmless mutable text/seed fields may remain structurally valid
                    Assert.Equal(loaded.Conceptions[0].Commit.CommitHash, codec.Decode(codec.Encode(loaded)).Conceptions[0].Commit.CommitHash);
                }
                catch (SaveIntegrityException) { }
                catch (SaveCompatibilityException) { }
            }
        }
        [Fact]
        public void PublicApiForecastCommitSaveRestartAndDuplicateProtectionUseOneDomainPath()
        {
            var directory = Path.Combine(Path.GetTempPath(), "cloverbrook-public-api-proof-" + Guid.NewGuid().ToString("N"));
            try
            {
                var initial = SaveTestData.Initial();
                initial = initial.WithHorses(initial.Horses.Select(h => new HorseRecord(h.HorseId, h.Name, h.Parentage, h.Genome, h.ResolverVersion, h.AppearanceSeed,
                    h.OwnerId, h.LifeState, h.ReproductionState, playerKnowledge: ForecastTestData.Known(h.HorseId.Equals(ForecastTestData.Id(1)) ? 1 : 2, h.Genome, "asip").Knowledge)));
                var path = Path.Combine(directory, "stable.clover"); var store = new AtomicSaveStore(path); var service = ConceptionTestData.Service();
                var queries = new StableBreedingQueries(store.Initialize(initial), service, "TEST-ONLY-art", "TEST-ONLY-mask");
                var request = ConceptionTestData.Request();
                var forecast = queries.PreviewBreeding(request.ParentAId, request.ParentBId, PublicCoatScope.AllSupported());
                Assert.Equal(ForecastAuthority.PlayerKnowledge, forecast.Authority); Assert.Equal(ForecastMethod.Exact, forecast.Coat.Method);
                Assert.Equal(Rational.Half, forecast.Coat.Outcomes.Single(o => o.Label == "Palomino").ExactProbability);
                Assert.DoesNotContain("asip", queries.GetKnownGenetics(request.ParentAId).RevealedCalls.Keys);
                Assert.True(queries.CheckBreedingEligibility(request).IsEligible);
                var committed = store.CommitConception(request, service); Assert.True(committed.IsDurablyCommitted);
                var foal = committed.Receipt.UnbornAtCommit;
                queries = new StableBreedingQueries(committed.State, service, "TEST-ONLY-art", "TEST-ONLY-mask");
                var visual = queries.ResolveVisualDescriptor(foal.HorseId).Descriptor!;
                queries.PreviewBreeding(request.ParentAId, request.ParentBId, PublicCoatScope.AllSupported());
                Assert.False(queries.CompareHypotheticalPairing(initial.Horses[0].Genome, initial.Horses[1].Genome).AuthorizesConception);
                var manuallySaved = store.SaveSnapshot(committed.State.WithHeader(SaveCodecTestsHeader(committed.State.Header, committed.State.Header.SaveRevision + 1)), committed.State.Header.SaveRevision);
                var restarted = new AtomicSaveStore(path); var loaded = restarted.Load(); Assert.False(loaded.NeedsRecoveryAcknowledgment);
                queries = new StableBreedingQueries(loaded.State, service, "TEST-ONLY-art", "TEST-ONLY-mask");
                Assert.Equal(foal.HorseId, queries.GetOffspring(request.EventId).HorseId);
                Assert.Equal(foal.HorseId, queries.GetPregnancy(request.ParentAId)!.OffspringHorseId);
                Assert.Equal(visual.CacheKey, queries.ResolveVisualDescriptor(foal.HorseId).Descriptor!.CacheKey);
                var restoredFoal = loaded.State.Horses.Single(h => h.HorseId.Equals(foal.HorseId));
                Assert.Equal(foal.Genome.GenomeHash, restoredFoal.Genome.GenomeHash); Assert.Equal(foal.AppearanceSeed, restoredFoal.AppearanceSeed);
                Assert.Equal(foal.Parentage.ParentAId, restoredFoal.Parentage.ParentAId); Assert.Equal(foal.Parentage.ParentBId, restoredFoal.Parentage.ParentBId);
                var beforeRetry = File.ReadAllBytes(path); var replay = restarted.CommitConception(request, service);
                Assert.True(replay.Replayed); Assert.Equal(beforeRetry, File.ReadAllBytes(path)); Assert.Equal(50, replay.State.Coins);
                Assert.Single(replay.State.Conceptions); Assert.Single(replay.State.Ledger); Assert.Equal(3, replay.State.Horses.Count);
                Assert.Equal(1UL, replay.State.Header.RngStreams.Breeding.EventCounter); Assert.Equal(manuallySaved.Header.SaveRevision, replay.State.Header.SaveRevision);
                Assert.Equal("idempotency_conflict", Assert.Throws<ConceptionRejectedException>(() => restarted.CommitConception(ConceptionTestData.Request(expectedSaveRevision: 1), service)).Code);
                Assert.Equal(beforeRetry, File.ReadAllBytes(path));
                Assert.Equal(ResolutionStatus.Unsupported, new StableBreedingQueries(loaded.State, ConceptionTestData.Service(ForecastTestData.Unreleased())).ResolveVisualDescriptor(foal.HorseId).Status);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        private static SaveHeader SaveCodecTestsHeader(SaveHeader h, ulong revision) => new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, revision,
            h.CalendarDay, h.CalendarProfileVersion, h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion,
            h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, h.PayloadHash);
    }
}
