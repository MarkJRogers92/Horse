using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    internal static class ConceptionTestData
    {
        internal static readonly BreedingPolicy Policy = new BreedingPolicy("cozy.policy.v1", new Rational(1, 8));
        internal static readonly ConceptionTerms Terms = new ConceptionTerms("breeding.fee.v1", 50);
        internal static readonly BreedingCommitId Event = new BreedingCommitId(new Guid("bc000000-0000-0000-0000-000000000001"));
        internal static readonly CapacityReservationId Reservation = new CapacityReservationId(new Guid("bc000000-0000-0000-0000-000000000002"));
        internal static SaveHeader Header(ulong revision = 0) => new SaveHeader(new SaveId(new Guid("bc000000-0000-0000-0000-000000000003")), SaveMode.Sandbox,
            StableSnapshot.SchemaVersion, "headless-proof", revision, 10, "calendar.v1", CareMode.Gentle, CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.InheritanceVersion,
            CoreGeneticsCatalog.ResolverVersion, new[] { CoreGeneticsCatalog.ResolverVersion }, RngStreams.FromSeed(new Seed128(11, 22)), 0, Array.Empty<string>(), "unwritten");
        internal static HorseRecord[] Parents() => new[] { ForecastTestData.Horse(1, ForecastTestData.Genome(("slc45a2", "N", "Cr"))),
            ForecastTestData.Horse(2, ForecastTestData.Genome(("slc45a2", "N", "Cr")), ReproductiveRole.Stallion) };
        internal static BreedingCapacityPlan Capacity(BreedingCommitId? eventId = null, CapacityReservationId? reservation = null, ulong revision = 0) =>
            new BreedingCapacityPlan(eventId ?? Event, reservation ?? Reservation, revision, new[] {
                new ReservedCapacityWindow(CapacityStage.Pregnancy, 10, 14, 4, 2, 0, true),
                new ReservedCapacityWindow(CapacityStage.Foal, 14, 17, 4, 2, 0, true),
                new ReservedCapacityWindow(CapacityStage.Youngster, 17, 20, 4, 2, 0, true),
                new ReservedCapacityWindow(CapacityStage.Adult, 20, null, 4, 2, 0, true) });
        internal static ConceptionRequest Request(BreedingCommitId? eventId = null, ulong expectedSaveRevision = 0, ulong expectedParentARevision = 0) =>
            new ConceptionRequest(eventId ?? Event, ForecastTestData.Owner, ForecastTestData.Id(1), ForecastTestData.Id(2), expectedSaveRevision,
                expectedParentARevision, 0, 0, Reservation, Terms.Version);
        internal static ConceptionService Service(CoatSupportMatrix? matrix = null) => new ConceptionService(Policy, Terms,
            matrix ?? ForecastTestData.TestMatrix("chestnut", "palomino", "cremello"));
    }
    public sealed class CatalogAndConceptionTests
    {
        [Fact]
        public void ClosureIncludesSecondGenerationClassesNotPresentInFoundersOrF1()
        {
            var a = ForecastTestData.Horse(1, ForecastTestData.Genome(("mc1r", "E", "E"), ("asip", "A", "A"), ("slc45a2", "Cr", "Cr")));
            var b = ForecastTestData.Horse(2, ForecastTestData.Genome(), ReproductiveRole.Stallion);
            var incomplete = ForecastTestData.TestMatrix("bay", "chestnut", "perlino", "buckskin");
            Assert.True(LiveCoatGuard.CheckPair(a, b, incomplete).IsSupported);
            var closure = LiveCatalogCoatGuard.Check(new[] { a, b }, incomplete);
            Assert.True(closure.Complete); Assert.False(closure.IsSupported); Assert.Contains("black", closure.CoatClasses); Assert.Contains("smoky_cream", closure.CoatClasses);
            var full = ForecastTestData.TestMatrix("chestnut", "bay", "black", "palomino", "buckskin", "smoky_black", "cremello", "perlino", "smoky_cream");
            Assert.True(LiveCatalogCoatGuard.Check(new[] { a, b }, full).IsSupported);
        }
        [Fact]
        public void EmptyUnreleasedAndOverBudgetCatalogsNeverPass()
        {
            Assert.False(LiveCatalogCoatGuard.Check(Array.Empty<HorseRecord>(), ForecastTestData.Unreleased()).IsSupported);
            Assert.False(LiveCatalogCoatGuard.Check(ConceptionTestData.Parents(), ForecastTestData.Unreleased()).IsSupported);
            var result = LiveCatalogCoatGuard.Check(ConceptionTestData.Parents(), ForecastTestData.TestMatrix("chestnut", "palomino", "cremello"), maxCoatCombinations: 1);
            Assert.False(result.Complete); Assert.False(result.IsSupported);
        }
        [Fact]
        public void SyntheticRecombinationDoesNotCauseFalseExponentialCoatExplosion()
        {
            var edits = Enumerable.Range(0, 32).Select(i => ("game.marker." + i.ToString("D2"), "0", "1")).ToArray();
            var a = ForecastTestData.Horse(1, ForecastTestData.Genome(edits)); var b = ForecastTestData.Horse(2, ForecastTestData.Genome(), ReproductiveRole.Stallion);
            var result = LiveCatalogCoatGuard.Check(new[] { a, b }, ForecastTestData.TestMatrix("chestnut"));
            Assert.True(result.IsSupported); Assert.Equal(1, result.CombinationCount);
        }
        [Fact]
        public void PreparationIsDeterministicAndDoesNotMutateOrDebitTheInput()
        {
            var header = ConceptionTestData.Header(); var parents = ConceptionTestData.Parents(); var service = ConceptionTestData.Service();
            var first = service.Prepare(header, parents, 100, 0, ConceptionTestData.Request(), ConceptionTestData.Capacity());
            var second = service.Prepare(header, parents, 100, 0, ConceptionTestData.Request(), ConceptionTestData.Capacity());
            Assert.Equal(first.Commit.CommitHash, second.Commit.CommitHash); Assert.Equal(first.ReceiptHash, second.ReceiptHash);
            Assert.False(first.IsDurablyCommitted); Assert.Equal(0UL, header.RngStreams.Breeding.EventCounter); Assert.Null(parents[0].ReproductionState.PregnancyId);
            Assert.Equal(1UL, first.AfterBreedingStream.EventCounter); Assert.Equal(1UL, first.Commit.BreedingEventIndex);
            Assert.Equal(ConceptionTestData.Event, first.PregnantMare.ReproductionState.PregnancyId); Assert.Equal(1UL, first.PregnantMare.Revision);
            Assert.Equal(LifeStage.Unborn, first.UnbornAtCommit.LifeState.Stage); Assert.Equal(50, first.Terms.CostCoins);
        }
        [Fact]
        public void RejectedRequestsConsumeNoStreamAndNeverCreateAnOutcome()
        {
            var header = ConceptionTestData.Header(); var parents = ConceptionTestData.Parents(); var service = ConceptionTestData.Service();
            Assert.Equal("funds", Assert.Throws<ConceptionRejectedException>(() => service.Prepare(header, parents, 49, 0, ConceptionTestData.Request(), ConceptionTestData.Capacity())).Code);
            Assert.Equal("stale_save", Assert.Throws<ConceptionRejectedException>(() => service.Prepare(header, parents, 100, 0, ConceptionTestData.Request(expectedSaveRevision: 1), ConceptionTestData.Capacity())).Code);
            Assert.Equal("eligibility", Assert.Throws<ConceptionRejectedException>(() => service.Prepare(header, parents, 100, 0, ConceptionTestData.Request(expectedParentARevision: 1), ConceptionTestData.Capacity())).Code);
            Assert.Equal("unreleased_catalog", Assert.Throws<ConceptionRejectedException>(() => ConceptionTestData.Service(ForecastTestData.Unreleased()).Prepare(header, parents, 100, 0, ConceptionTestData.Request(), ConceptionTestData.Capacity())).Code);
            Assert.Equal(0UL, header.RngStreams.Breeding.EventCounter);
        }
        [Fact]
        public void ChangingAnUncommittedRequestIdCannotRerollTheNextFoal()
        {
            var service = ConceptionTestData.Service(); var parents = ConceptionTestData.Parents(); var header = ConceptionTestData.Header();
            var other = new BreedingCommitId(new Guid("bc000000-0000-0000-0000-000000000009"));
            var a = service.Prepare(header, parents, 100, 0, ConceptionTestData.Request(), ConceptionTestData.Capacity());
            var b = service.Prepare(header, parents, 100, 0, ConceptionTestData.Request(other), ConceptionTestData.Capacity(other));
            Assert.Equal(a.Commit.OffspringHorseId, b.Commit.OffspringHorseId); Assert.Equal(a.Commit.OffspringGenome.GenomeHash, b.Commit.OffspringGenome.GenomeHash);
            Assert.Equal(a.Commit.OffspringAppearanceSeed, b.Commit.OffspringAppearanceSeed); Assert.Equal(a.Commit.CostTransactionId, b.Commit.CostTransactionId);
            Assert.Equal(a.UnbornAtCommit.DevelopmentalFactors.Hash, b.UnbornAtCommit.DevelopmentalFactors.Hash);
        }
    }
}
