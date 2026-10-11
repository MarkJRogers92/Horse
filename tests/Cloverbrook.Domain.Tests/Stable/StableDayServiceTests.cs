using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class StableDayServiceTests : IDisposable
    {
        private static readonly StableDayService Service = new StableDayService();
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-day-preview-" + Guid.NewGuid().ToString("N"));
        private static CarePlan Plan(StableSnapshot state, bool feed = true, bool bedding = true, bool turnout = false, bool grooming = false) =>
            new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable && h.LifeState.Stage != LifeStage.Unborn)
                .Select(h => new HorseCareAssignment(h.HorseId, feed, bedding, turnout, grooming)));
        private static EndDayRequest Request(StableSnapshot state, CarePlan? plan = null, TransactionId? operation = null, string? version = null) =>
            new EndDayRequest(operation ?? new TransactionId(Guid.NewGuid()), state.PlayerOwnerId, state.Header.SaveRevision, state.Header.CalendarDay,
                plan ?? Plan(state), version ?? StableDailyPolicy.Version);
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Fact]
        public void PreviewIsPureDeterministicAndComputesOneDayCostAndCare()
        {
            var state = SaveTestData.Initial(); var plan = Plan(state, turnout: true, grooming: true);
            var beforeBytes = new StableSaveCodec().Encode(state);
            var first = Service.Preview(state, plan); var second = Service.Preview(state, plan);
            Assert.True(first.Allowed); Assert.Equal(10, first.FromDay); Assert.Equal(11, first.ToDay);
            Assert.Equal(12, first.CareCostCoins); Assert.Equal(24, first.OperationsCostCoins); Assert.Equal(0, first.AssistanceCoins); Assert.Equal(64, first.ProjectedCoins);
            Assert.Equal(2, first.HorseOutcomes.Count); Assert.Equal(first.Receipt!.ReceiptHash, second.Receipt!.ReceiptHash);
            Assert.All(first.HorseOutcomes, o => { Assert.Equal(7900, o.After.Nutrition); Assert.Equal(7900, o.After.Bedding); Assert.Equal(6200, o.After.Turnout); Assert.Equal(6000, o.After.Grooming); });
            Assert.Equal(beforeBytes, new StableSaveCodec().Encode(state));
        }

        [Fact]
        public void MissingFeedOrBeddingIsBlockedAndUnknownProfilesAreNotGuessed()
        {
            var state = SaveTestData.Initial();
            var missing = Service.Preview(state, Plan(state, feed: false));
            Assert.False(missing.Allowed); Assert.Contains("Feeding and clean bedding", missing.BlockReason);
            var h = state.Header;
            var unknownHeader = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, h.SaveRevision, h.CalendarDay, "calendar.v999", h.CareMode,
                h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion, h.RetainedResolverVersions, h.RngStreams,
                h.LastCommittedBreedingEventIndex, h.MigrationHistory, h.PayloadHash);
            var unknown = new StableSnapshot(unknownHeader, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance, state.CapacityRevision, state.Ledger,
                state.PendingCapacityPlans, state.Conceptions, state.DailyState);
            Assert.False(Service.Preview(unknown, Plan(unknown)).Allowed);
        }

        [Fact]
        public void UnreceiptedNonDefaultCareCannotBeLoadedOrPreviewedAsACommitCandidate()
        {
            var state = SaveTestData.Initial();
            var unsupported = new StableDailyState(state.Horses.Select(h =>
                new KeyValuePair<HorseId, HorseCareState>(h.HorseId, new HorseCareState(8000, 7000, 5000, 5000))));
            Assert.Throws<SaveIntegrityException>(() => state.WithDailyState(unsupported));
        }

        [Fact]
        public void AssistanceCoversTheShortfallWithoutNegativeWalletOrMissingBasicCare()
        {
            var state = SaveTestData.Initial(0); var preview = Service.Preview(state, Plan(state));
            Assert.True(preview.Allowed); Assert.Equal(36, preview.AssistanceCoins); Assert.Equal(0, preview.ProjectedCoins);
            Assert.All(preview.HorseOutcomes, h => Assert.True(h.After.Nutrition >= h.Before.Nutrition && h.After.Bedding >= h.Before.Bedding));
            Directory.CreateDirectory(directory); var store = new AtomicSaveStore(Path.Combine(directory, "stable.clover")); store.Initialize(state);
            var next = store.CommitEndDay(Request(state), Service).State;
            Assert.Equal(0, next.Coins); Assert.Empty(next.Ledger); Assert.Equal(36, next.DailyState.Receipts.Single().AssistanceCoins);
        }

        [Fact]
        public void ReceiptAmountOverflowIsRejectedInsteadOfWrapping()
        {
            var state = SaveTestData.Initial(); var request = Request(state);
            Assert.Throws<ArgumentException>(() => new DailyCareReceipt(request, request.FromDay, request.FromDay + 1,
                long.MaxValue, 1, 0, 0, new TransactionId(Guid.NewGuid()), Array.Empty<HorseCareOutcome>()));
        }

        [Fact]
        public void LifecycleGuardsStopUnbornBirthAndJuvenileStageTransitions()
        {
            var conceived = SaveTestData.Commit().State;
            var h = conceived.Header;
            var dueHeader = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, h.SaveRevision, 13, h.CalendarProfileVersion,
                h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion, h.RetainedResolverVersions,
                h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, h.PayloadHash);
            var due = conceived.WithHeader(dueHeader);
            var blockedBirth = Service.Preview(due, Plan(due));
            Assert.False(blockedBirth.Allowed); Assert.Contains("due to be born", blockedBirth.BlockReason);

            var template = ForecastTestData.Horse(9, ForecastTestData.Genome());
            var foal = new HorseRecord(template.HorseId, template.Name, template.Parentage, template.Genome, template.ResolverVersion, template.AppearanceSeed,
                template.OwnerId, new LifeState(LifeStage.Foal, 2, VisualAge.Foal), template.ReproductionState, birthDay: 0);
            var foalHeader = new SaveHeader(new SaveId(Guid.NewGuid()), SaveMode.Sandbox, StableSnapshot.SchemaVersion, "test", 0, 2, "calendar.v1", CareMode.Standard,
                CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.InheritanceVersion, CoreGeneticsCatalog.ResolverVersion,
                new[] { CoreGeneticsCatalog.ResolverVersion }, RngStreams.FromSeed(new Seed128(1, 2)), 0, Array.Empty<string>(), "unwritten");
            var youngStable = new StableSnapshot(foalHeader, template.OwnerId, new[] { foal }, 100, 0);
            var blockedGrowth = Service.Preview(youngStable, Plan(youngStable));
            Assert.False(blockedGrowth.Allowed); Assert.Contains("change life stage", blockedGrowth.BlockReason);

            var noAnchor = new HorseRecord(template.HorseId, template.Name, template.Parentage, template.Genome, template.ResolverVersion, template.AppearanceSeed,
                template.OwnerId, new LifeState(LifeStage.Foal, 2, VisualAge.Foal), template.ReproductionState);
            var noAnchorStable = new StableSnapshot(foalHeader, template.OwnerId, new[] { noAnchor }, 100, 0);
            Assert.Contains("no recorded birth day", Service.Preview(noAnchorStable, Plan(noAnchorStable)).BlockReason);
        }
    }

    public sealed class StableDayPersistenceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-day-test-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        private AtomicSaveStore Store(Action<SaveWriteStage>? fault = null) => new AtomicSaveStore(SavePath, faultInjector: fault);
        private static void FailAt(SaveWriteStage actual, SaveWriteStage target) { if (actual == target) throw new IOException("Injected interrupted EndDay save"); }
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private static CarePlan Plan(StableSnapshot state) => new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId)
            && h.HomeAssignment == HomeKind.ActiveStable && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, true, false)));
        private static EndDayRequest Request(StableSnapshot state, Guid? id = null, CarePlan? plan = null) => new EndDayRequest(
            new TransactionId(id ?? Guid.NewGuid()), state.PlayerOwnerId, state.Header.SaveRevision, state.Header.CalendarDay, plan ?? Plan(state), StableDailyPolicy.Version);
        private static void AssertStreamsEqual(RngStreams expected, RngStreams actual)
        {
            AssertStreamEqual(expected.Breeding, actual.Breeding); AssertStreamEqual(expected.Cosmetic, actual.Cosmetic);
            AssertStreamEqual(expected.World, actual.World); AssertStreamEqual(expected.Competition, actual.Competition);
        }
        private static void AssertStreamEqual(RngStreamState expected, RngStreamState actual)
        {
            Assert.Equal(expected.Seed, actual.Seed); Assert.Equal(expected.Purpose, actual.Purpose);
            Assert.Equal(expected.EventCounter, actual.EventCounter); Assert.Equal(expected.AlgorithmVersion, actual.AlgorithmVersion);
        }

        [Fact]
        public void EndDayPublishesCalendarCareWalletAndReceiptAsOneRestartableIdempotentChange()
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial()); var operationId = Guid.Parse("de000000-0000-0000-0000-000000000002"); var request = Request(before, operationId);
            var committed = store.CommitEndDay(request, new StableDayService());
            Assert.True(committed.IsDurablyCommitted); Assert.False(committed.Replayed); Assert.Equal(11, committed.State.Header.CalendarDay);
            Assert.Equal(1UL, committed.State.Header.SaveRevision); Assert.Equal(64, committed.State.Coins); Assert.Single(committed.State.DailyState.Receipts);
            Assert.Equal(2, committed.State.DailyState.HorseCare.Count); Assert.Equal(before.Header.RngStreams.World.EventCounter, committed.State.Header.RngStreams.World.EventCounter);
            var bytes = File.ReadAllBytes(SavePath); var restarted = Store().Load().State;
            var replay = Store().CommitEndDay(request, new StableDayService());
            Assert.True(replay.Replayed); Assert.Equal(bytes, File.ReadAllBytes(SavePath)); Assert.Equal(committed.Receipt.ReceiptHash, replay.Receipt.ReceiptHash);
            Assert.Equal(7900, restarted.DailyState.For(restarted.Horses[0].HorseId).Nutrition);
            var conflictingPlan = new CarePlan(Plan(restarted).Assignments.Select(a => new HorseCareAssignment(a.HorseId, true, true, false, false)));
            var conflicting = Request(restarted, operationId, conflictingPlan);
            Assert.Equal("idempotency_conflict", Assert.Throws<EndDayRejectedException>(() => Store().CommitEndDay(conflicting, new StableDayService())).Code);
        }

        [Fact]
        public void SafeEndDayPreservesIdentityInheritancePregnancyAndUnbornStageWhileAgingByOne()
        {
            var before = SaveTestData.Commit().State; Directory.CreateDirectory(directory);
            var store = Store(); store.Initialize(before); var childBefore = before.Horses.Single(h => h.LifeState.Stage == LifeStage.Unborn);
            var result = store.CommitEndDay(Request(before), new StableDayService()).State;
            var childAfter = result.Horses.Single(h => h.HorseId.Equals(childBefore.HorseId));
            Assert.Equal(LifeStage.Unborn, childAfter.LifeState.Stage); Assert.Equal(childBefore.LifeState.DaysSinceConception + 1, childAfter.LifeState.DaysSinceConception);
            Assert.Equal(childBefore.Genome.GenomeHash, childAfter.Genome.GenomeHash); Assert.Equal(childBefore.AppearanceSeed, childAfter.AppearanceSeed);
            Assert.Equal(childBefore.Parentage.ParentAId, childAfter.Parentage.ParentAId); Assert.Equal(childBefore.InheritedGamePotential.Hash, childAfter.InheritedGamePotential.Hash);
            Assert.Equal(childBefore.OwnerId, childAfter.OwnerId); Assert.Equal(childBefore.OriginCommitId, childAfter.OriginCommitId);
            Assert.Equal(ConceptionTestData.Event, result.Horses.Single(h => h.ReproductionState.PregnancyId != null).ReproductionState.PregnancyId);
            AssertStreamsEqual(before.Header.RngStreams, result.Header.RngStreams);
        }

        [Fact]
        public void GenericSnapshotWritesCannotAdvanceCalendarOrRewriteCareRoot()
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial()); var h = before.Header;
            var advancedHeader = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, 1, h.CalendarDay + 1, h.CalendarProfileVersion,
                h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion, h.RetainedResolverVersions,
                h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, "unwritten");
            Assert.Throws<SaveIntegrityException>(() => store.SaveSnapshot(before.WithHeader(advancedHeader), h.SaveRevision));
            var seededCare = new StableDailyState(before.Horses.Select(horse =>
                new KeyValuePair<HorseId, HorseCareState>(horse.HorseId, HorseCareState.Initial)));
            var careHeader = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, 1, h.CalendarDay, h.CalendarProfileVersion,
                h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion, h.RetainedResolverVersions,
                h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, "unwritten");
            var changedCare = new StableSnapshot(careHeader, before.PlayerOwnerId, before.Horses, before.OpeningCoinBalance, before.CapacityRevision,
                before.Ledger, before.PendingCapacityPlans, before.Conceptions, seededCare);
            Assert.Throws<SaveIntegrityException>(() => store.SaveSnapshot(changedCare, h.SaveRevision));

            var committed = store.CommitEndDay(Request(before), new StableDayService()).State;
            var activeHorse = committed.Horses[0];
            var moved = committed.Horses.Select(horse => horse.HorseId.Equals(activeHorse.HorseId)
                ? new HorseRecord(horse.HorseId, horse.Name, horse.Parentage, horse.Genome, horse.ResolverVersion, horse.AppearanceSeed, horse.OwnerId,
                    horse.LifeState, horse.ReproductionState, horse.DevelopmentalFactors, horse.InheritedGamePotential, horse.TrainingState,
                    horse.PlayerKnowledge, horse.CosmeticState, HomeKind.CooperativePasture, horse.OriginRecord, horse.OriginCommitId, horse.BirthDay,
                    checked(horse.Revision + 1)) : horse).ToArray();
            var movedHeader = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, committed.Header.SaveRevision + 1,
                committed.Header.CalendarDay, h.CalendarProfileVersion, h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion,
                h.DefaultOffspringResolverVersion, h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex,
                h.MigrationHistory, "unwritten");
            var movedSnapshot = new StableSnapshot(movedHeader, committed.PlayerOwnerId, moved, committed.OpeningCoinBalance, committed.CapacityRevision,
                committed.Ledger, committed.PendingCapacityPlans, committed.Conceptions, committed.DailyState);
            store.SaveSnapshot(movedSnapshot, committed.Header.SaveRevision);
            Assert.Equal(HomeKind.CooperativePasture, store.Load().State.Horses.Single(horse => horse.HorseId.Equals(activeHorse.HorseId)).HomeAssignment);
        }

        [Theory]
        [InlineData(SaveWriteStage.TemporaryPartWritten)] [InlineData(SaveWriteStage.TemporaryWritten)]
        [InlineData(SaveWriteStage.TemporaryFlushed)] [InlineData(SaveWriteStage.BeforeReplace)]
        public void PreReplaceFailureLeavesDayCareWalletAndCalendarUntouchedThenRetryWorks(SaveWriteStage stage)
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial()); var original = File.ReadAllBytes(SavePath); var request = Request(before);
            Assert.Throws<IOException>(() => Store(s => FailAt(s, stage)).CommitEndDay(request, new StableDayService()));
            Assert.Equal(original, File.ReadAllBytes(SavePath)); var unchanged = Store().Load().State;
            Assert.Equal(before.Header.CalendarDay, unchanged.Header.CalendarDay); Assert.Empty(unchanged.DailyState.Receipts); Assert.Equal(100, unchanged.Coins);
            var retry = Store().CommitEndDay(request, new StableDayService()); Assert.Equal(11, retry.State.Header.CalendarDay); Assert.Equal(64, retry.State.Coins);
        }

        [Fact]
        public void PostReplaceFailureIsResolvedByExactRetryWithoutChargingTwice()
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial()); var request = Request(before);
            Assert.Throws<IOException>(() => Store(s => FailAt(s, SaveWriteStage.AfterReplace)).CommitEndDay(request, new StableDayService()));
            var committedBytes = File.ReadAllBytes(SavePath); var replay = Store().CommitEndDay(request, new StableDayService());
            Assert.True(replay.Replayed); Assert.Equal(committedBytes, File.ReadAllBytes(SavePath)); Assert.Equal(64, replay.State.Coins); Assert.Single(replay.State.DailyState.Receipts);
        }
    }
}
