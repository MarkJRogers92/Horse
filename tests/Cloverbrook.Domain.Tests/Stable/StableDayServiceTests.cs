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
            Assert.Equal(12, first.CareCostCoins); Assert.Equal(24, first.OperationsCostCoins); Assert.Equal(0, first.AssistanceCoins); Assert.Equal(50, first.IncomeCoins); Assert.Equal(114, first.ProjectedCoins);
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
            // Legacy v1 rules have no club support, so an empty wallet exercises the assistance path.
            var legacy = new StableDayService(new StableCalendar(), new StableDailyPolicy(StableDailyPolicy.LegacyVersion));
            var state = SaveTestData.Initial(0); var preview = legacy.Preview(state, Plan(state));
            Assert.True(preview.Allowed); Assert.Equal(36, preview.AssistanceCoins); Assert.Equal(0, preview.ProjectedCoins);
            Assert.All(preview.HorseOutcomes, h => Assert.True(h.After.Nutrition >= h.Before.Nutrition && h.After.Bedding >= h.Before.Bedding));
            Directory.CreateDirectory(directory); var store = new AtomicSaveStore(Path.Combine(directory, "stable.clover")); store.Initialize(state);
            var next = store.CommitEndDay(Request(state, version: StableDailyPolicy.LegacyVersion), legacy).State;
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
        public void LifecyclePreviewExposesDueBirthAndJuvenileGrowthTransitions()
        {
            var conceived = SaveTestData.Commit().State;
            var h = conceived.Header;
            var dueHeader = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, h.SaveRevision, 13, h.CalendarProfileVersion,
                h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion, h.RetainedResolverVersions,
                h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, h.PayloadHash);
            var due = conceived.WithHeader(dueHeader);
            var birth = Service.Preview(due, Plan(due));
            Assert.True(birth.Allowed); var birthEvent = Assert.Single(birth.LifecycleEvents);
            Assert.Equal(LifecycleEventKind.Birth, birthEvent.Kind); Assert.Equal(LifeStage.Unborn, birthEvent.FromStage);
            Assert.Equal(LifeStage.Foal, birthEvent.ToStage); Assert.Equal(14, birthEvent.EffectiveDay);
            Assert.Equal(ForecastTestData.Id(1), birthEvent.RelatedHorseId);

            var template = ForecastTestData.Horse(9, ForecastTestData.Genome());
            var foal = new HorseRecord(template.HorseId, template.Name, template.Parentage, template.Genome, template.ResolverVersion, template.AppearanceSeed,
                template.OwnerId, new LifeState(LifeStage.Foal, 2, VisualAge.Foal), template.ReproductionState, birthDay: 0);
            var foalHeader = new SaveHeader(new SaveId(Guid.NewGuid()), SaveMode.Sandbox, StableSnapshot.SchemaVersion, "test", 0, 2, "calendar.v1", CareMode.Standard,
                CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.InheritanceVersion, CoreGeneticsCatalog.ResolverVersion,
                new[] { CoreGeneticsCatalog.ResolverVersion }, RngStreams.FromSeed(new Seed128(1, 2)), 0, Array.Empty<string>(), "unwritten");
            var youngStable = new StableSnapshot(foalHeader, template.OwnerId, new[] { foal }, 100, 0);
            var growth = Service.Preview(youngStable, Plan(youngStable));
            Assert.True(growth.Allowed); var growthEvent = Assert.Single(growth.LifecycleEvents);
            Assert.Equal(LifecycleEventKind.FoalToYoungster, growthEvent.Kind); Assert.Equal(LifeStage.Youngster, growthEvent.ToStage);
            Assert.Equal(3, growthEvent.EffectiveDay);

            var noAnchor = new HorseRecord(template.HorseId, template.Name, template.Parentage, template.Genome, template.ResolverVersion, template.AppearanceSeed,
                template.OwnerId, new LifeState(LifeStage.Foal, 2, VisualAge.Foal), template.ReproductionState);
            var noAnchorStable = new StableSnapshot(foalHeader, template.OwnerId, new[] { noAnchor }, 100, 0);
            Assert.Contains("no recorded birth day", Service.Preview(noAnchorStable, Plan(noAnchorStable)).BlockReason);
        }

        [Fact]
        public void UnsupportedConceptionRulesBlockLifecycleBeforeAnyCost()
        {
            var unknownService = new ConceptionService(new BreedingPolicy("unknown.rules.v999", new Rational(1, 8)),
                ConceptionTestData.Terms, ForecastTestData.TestMatrix("chestnut", "palomino", "cremello"));
            var committed = ConceptionTransaction.Apply(SaveTestData.Initial(), ConceptionTestData.Request(), unknownService).State;
            var h = committed.Header;
            var dueHeader = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, h.SaveRevision, 13, h.CalendarProfileVersion,
                h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion,
                h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, h.PayloadHash);
            var unsupportedState = new StableSnapshot(dueHeader, committed.PlayerOwnerId, committed.Horses, committed.OpeningCoinBalance,
                committed.CapacityRevision, committed.Ledger, committed.PendingCapacityPlans, committed.Conceptions, committed.DailyState);
            var unsupportedPreview = Service.Preview(unsupportedState, Plan(unsupportedState));
            Assert.False(unsupportedPreview.Allowed); Assert.Contains("breeding rules", unsupportedPreview.BlockReason);

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
        private static StableSnapshot CommitThrough(AtomicSaveStore store, StableSnapshot state, int targetDay)
        {
            while (state.Header.CalendarDay < targetDay)
            {
                var request = Request(state); var preview = new StableDayService().Preview(state, request.Plan);
                Assert.True(preview.Allowed, preview.BlockReason);
                var committed = store.CommitEndDay(request, new StableDayService());
                Assert.Equal(preview.LifecycleEvents.Select(EventKey), committed.Receipt.LifecycleEvents.Select(EventKey));
                state = committed.State;
            }
            return state;
        }
        private static string EventKey(StableLifecycleEvent e) => string.Join("|", e.Kind, e.HorseId, e.RelatedHorseId, e.BreedingEventId,
            e.FromStage, e.ToStage, e.EffectiveDay);
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
            Assert.Equal(1UL, committed.State.Header.SaveRevision); Assert.Equal(114, committed.State.Coins); Assert.Single(committed.State.DailyState.Receipts);
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
        public void BirthAndGrowthAdvanceTheCommittedFoalWithoutRerollingOrConsumingItsHoldAgain()
        {
            var before = SaveTestData.Commit().State; var childBefore = before.Horses.Single(h => h.LifeState.Stage == LifeStage.Unborn);
            Directory.CreateDirectory(directory); var store = Store(); store.Initialize(before);
            var state = CommitThrough(store, before, 14); var foal = state.Horses.Single(h => h.HorseId.Equals(childBefore.HorseId));
            Assert.Equal("New foal", foal.Name); Assert.Equal(LifeStage.Foal, foal.LifeState.Stage); Assert.Equal(14, foal.BirthDay);
            Assert.Equal(4, foal.LifeState.DaysSinceConception); Assert.Null(foal.ReproductionState.PregnancyId);
            Assert.Equal(childBefore.Genome.GenomeHash, foal.Genome.GenomeHash); Assert.Equal(childBefore.AppearanceSeed, foal.AppearanceSeed);
            Assert.Equal(childBefore.Parentage.ParentAId, foal.Parentage.ParentAId); Assert.Equal(childBefore.Parentage.ParentBId, foal.Parentage.ParentBId);
            Assert.Equal(childBefore.DevelopmentalFactors.Hash, foal.DevelopmentalFactors.Hash); Assert.Equal(childBefore.InheritedGamePotential.Hash, foal.InheritedGamePotential.Hash);
            var mare = state.Horses.Single(h => h.HorseId.Equals(ForecastTestData.Id(1)));
            Assert.Null(mare.ReproductionState.PregnancyId); Assert.Equal(16, mare.ReproductionState.RecoveryUntilDay);
            Assert.Equal(1, state.DailyState.Receipts.SelectMany(r => r.LifecycleEvents).Count(e => e.Kind == LifecycleEventKind.Birth));
            var birthReceipt = state.DailyState.Receipts.Single(r => r.ToDay == 14);
            var noBirth = new DailyCareReceipt(birthReceipt.Request, birthReceipt.FromDay, birthReceipt.ToDay, birthReceipt.CareCostCoins,
                birthReceipt.OperationsCostCoins, birthReceipt.WalletDebitCoins, birthReceipt.AssistanceCoins, birthReceipt.CostTransactionId,
                birthReceipt.HorseOutcomes);
            var receiptWithoutBirth = state.DailyState.Receipts.Select(r => r.Request.OperationId.Equals(noBirth.Request.OperationId) ? noBirth : r).ToArray();
            Assert.Throws<SaveIntegrityException>(() => { _ = new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses,
                state.OpeningCoinBalance, state.CapacityRevision, state.Ledger, state.PendingCapacityPlans, state.Conceptions,
                new StableDailyState(state.DailyState.HorseCare, receiptWithoutBirth), state.LegacyLifecycleBaselines); });
            Assert.Equal(1, state.Ledger.Count(l => l.Kind == "breeding")); Assert.Empty(state.PendingCapacityPlans);
            Assert.Equal(before.CapacityRevision, state.CapacityRevision); AssertStreamsEqual(before.Header.RngStreams, state.Header.RngStreams);
            var reservationId = before.Conceptions.Single().Capacity.ReservationId;
            var foalObligation = Assert.Single(state.LifecycleCapacity.Obligations);
            Assert.Equal(CapacityStage.Foal, foalObligation.Stage); Assert.Equal(reservationId, foalObligation.ReservationId);
            Assert.True(foalObligation.UsesConfirmedReservation); Assert.False(foalObligation.CountsAsAdultHorse);
            var originalBirthReceiptHash = state.DailyState.Receipts.Single(r => r.ToDay == 14).ReceiptHash;
            state = CommitThrough(store, state, 17); foal = state.Horses.Single(h => h.HorseId.Equals(childBefore.HorseId));
            Assert.Equal(LifeStage.Youngster, foal.LifeState.Stage);
            Assert.Contains(state.DailyState.Receipts.Single(r => r.ToDay == 17).LifecycleEvents, e => e.Kind == LifecycleEventKind.FoalToYoungster);
            state = CommitThrough(store, state, 20); foal = state.Horses.Single(h => h.HorseId.Equals(childBefore.HorseId));
            Assert.Equal(LifeStage.Adult, foal.LifeState.Stage); Assert.Equal(20, state.Header.CalendarDay);
            var adultObligation = Assert.Single(state.LifecycleCapacity.Obligations);
            Assert.Equal(CapacityStage.Adult, adultObligation.Stage); Assert.False(adultObligation.UsesConfirmedReservation);
            Assert.True(adultObligation.CountsAsAdultHorse);
            Assert.Equal(originalBirthReceiptHash, state.DailyState.Receipts.Single(r => r.ToDay == 14).ReceiptHash);
            var partner = state.Horses.Single(h => h.HorseId.Equals(ForecastTestData.Id(2)));
            var graph = new PedigreeGraph(state.Horses.Select(h => new PedigreeNode(h.HorseId, h.Parentage)));
            var beforeReady = new BreedingEligibility().Evaluate(foal, partner, graph, ConceptionTestData.Policy, 21,
                ConceptionTestData.Event, null, foal.Revision, partner.Revision, state.CapacityRevision);
            Assert.Contains(beforeReady.Rejections, issue => issue.Code == "age");
            state = CommitThrough(store, state, 22); foal = state.Horses.Single(h => h.HorseId.Equals(childBefore.HorseId));
            var ready = new BreedingEligibility().Evaluate(foal, partner, graph, ConceptionTestData.Policy, 22,
                ConceptionTestData.Event, null, foal.Revision, partner.Revision, state.CapacityRevision);
            Assert.DoesNotContain(ready.Rejections, issue => issue.Code == "age");
            var lateRecord = new HorseRecord(foal.HorseId, foal.Name, foal.Parentage, foal.Genome, foal.ResolverVersion, foal.AppearanceSeed,
                foal.OwnerId, new LifeState(LifeStage.Adult, 12, VisualAge.Adult), foal.ReproductionState, foal.DevelopmentalFactors,
                foal.InheritedGamePotential, foal.TrainingState, foal.PlayerKnowledge, foal.CosmeticState, foal.HomeAssignment,
                foal.OriginRecord, foal.OriginCommitId, 20, foal.Revision);
            var late = new BreedingEligibility().Evaluate(lateRecord, partner, graph, ConceptionTestData.Policy, 22,
                ConceptionTestData.Event, null, lateRecord.Revision, partner.Revision, state.CapacityRevision);
            Assert.Contains(late.Rejections, issue => issue.Code == "age");
            var staleCounterAdult = new HorseRecord(foal.HorseId, foal.Name, foal.Parentage, foal.Genome, foal.ResolverVersion, foal.AppearanceSeed,
                foal.OwnerId, new LifeState(LifeStage.Adult, 0, VisualAge.Adult), foal.ReproductionState, foal.DevelopmentalFactors,
                foal.InheritedGamePotential, foal.TrainingState, foal.PlayerKnowledge, foal.CosmeticState, foal.HomeAssignment,
                foal.OriginRecord, foal.OriginCommitId, 20, foal.Revision);
            var beforeBirthReady = new BreedingEligibility().Evaluate(staleCounterAdult, partner, graph, ConceptionTestData.Policy, 27,
                ConceptionTestData.Event, null, staleCounterAdult.Revision, partner.Revision, state.CapacityRevision);
            Assert.Contains(beforeBirthReady.Rejections, issue => issue.Code == "age");
            var birthReady = new BreedingEligibility().Evaluate(staleCounterAdult, partner, graph, ConceptionTestData.Policy, 28,
                ConceptionTestData.Event, null, staleCounterAdult.Revision, partner.Revision, state.CapacityRevision);
            Assert.DoesNotContain(birthReady.Rejections, issue => issue.Code == "age");
            var restarted = Store().Load().State;
            Assert.Equal(LifeStage.Adult, restarted.Horses.Single(h => h.HorseId.Equals(childBefore.HorseId)).LifeState.Stage);
            Assert.Equal(state.DailyState.Receipts.Select(r => r.ReceiptHash), restarted.DailyState.Receipts.Select(r => r.ReceiptHash));
        }

        [Fact]
        public void RenameBornFoalUsesCanonicalRecordAndRejectsUnbornStaleOrUnauthorizedChanges()
        {
            Directory.CreateDirectory(directory); var store = Store(); var state = store.Initialize(SaveTestData.Commit().State);
            var unborn = state.Horses.Single(h => h.LifeState.Stage == LifeStage.Unborn);
            Assert.Equal("not_born", Assert.Throws<StableCommandRejectedException>(() => store.RenameHorse(state.PlayerOwnerId, unborn.HorseId,
                state.Header.SaveRevision, unborn.Revision, "Clover")).Code);
            state = CommitThrough(store, state, 14); var foal = state.Horses.Single(h => h.HorseId.Equals(unborn.HorseId));
            var receiptHashes = state.DailyState.Receipts.Select(r => r.ReceiptHash).ToArray(); var genomeHash = foal.Genome.GenomeHash;
            var renamed = store.RenameHorse(state.PlayerOwnerId, foal.HorseId, state.Header.SaveRevision, foal.Revision, "Clover");
            var namedFoal = renamed.Horses.Single(h => h.HorseId.Equals(foal.HorseId));
            Assert.Equal("Clover", namedFoal.Name); Assert.Equal(genomeHash, namedFoal.Genome.GenomeHash);
            Assert.Equal(foal.OriginCommitId, namedFoal.OriginCommitId); Assert.Equal(receiptHashes, renamed.DailyState.Receipts.Select(r => r.ReceiptHash));
            Assert.Equal("stale_save", Assert.Throws<StableCommandRejectedException>(() => store.RenameHorse(state.PlayerOwnerId, foal.HorseId,
                state.Header.SaveRevision, foal.Revision, "Clover II")).Code);
            Assert.Equal("ownership", Assert.Throws<StableCommandRejectedException>(() => store.RenameHorse(new OwnerId(Guid.NewGuid()), foal.HorseId,
                renamed.Header.SaveRevision, namedFoal.Revision, "Clover II")).Code);
            Assert.Equal("invalid_name", Assert.Throws<StableCommandRejectedException>(() => store.RenameHorse(state.PlayerOwnerId, foal.HorseId,
                renamed.Header.SaveRevision, namedFoal.Revision, new string('x', 41))).Code);
        }

        [Fact]
        public void BirthAndGrowthFaultsResolveOnlyThroughExactAtomicRetry()
        {
            Directory.CreateDirectory(directory); var store = Store(); var state = store.Initialize(SaveTestData.Commit().State);
            state = CommitThrough(store, state, 13); var childId = state.Horses.Single(h => h.LifeState.Stage == LifeStage.Unborn).HorseId;
            var birthRequest = Request(state); var preBirthBytes = File.ReadAllBytes(SavePath);
            Assert.Throws<IOException>(() => Store(s => FailAt(s, SaveWriteStage.BeforeReplace)).CommitEndDay(birthRequest, new StableDayService()));
            Assert.Equal(preBirthBytes, File.ReadAllBytes(SavePath));
            var stillPregnant = Store().Load().State;
            Assert.Equal(13, stillPregnant.Header.CalendarDay); Assert.Equal(LifeStage.Unborn, stillPregnant.Horses.Single(h => h.HorseId.Equals(childId)).LifeState.Stage);
            var born = Store().CommitEndDay(birthRequest, new StableDayService()); Assert.False(born.Replayed);
            Assert.Equal(LifecycleEventKind.Birth, Assert.Single(born.Receipt.LifecycleEvents).Kind);

            state = CommitThrough(Store(), born.State, 16); var growthRequest = Request(state);
            Assert.Throws<IOException>(() => Store(s => FailAt(s, SaveWriteStage.AfterReplace)).CommitEndDay(growthRequest, new StableDayService()));
            var published = Store().Load().State; Assert.Equal(17, published.Header.CalendarDay);
            Assert.Equal(LifeStage.Youngster, published.Horses.Single(h => h.HorseId.Equals(childId)).LifeState.Stage);
            var committedBytes = File.ReadAllBytes(SavePath); var replay = Store().CommitEndDay(growthRequest, new StableDayService());
            Assert.True(replay.Replayed); Assert.Equal(committedBytes, File.ReadAllBytes(SavePath));
            Assert.Equal(LifecycleEventKind.FoalToYoungster, Assert.Single(replay.Receipt.LifecycleEvents).Kind);
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
            var retry = Store().CommitEndDay(request, new StableDayService()); Assert.Equal(11, retry.State.Header.CalendarDay); Assert.Equal(114, retry.State.Coins);
        }

        [Fact]
        public void PostReplaceFailureIsResolvedByExactRetryWithoutChargingTwice()
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial()); var request = Request(before);
            Assert.Throws<IOException>(() => Store(s => FailAt(s, SaveWriteStage.AfterReplace)).CommitEndDay(request, new StableDayService()));
            var committedBytes = File.ReadAllBytes(SavePath); var replay = Store().CommitEndDay(request, new StableDayService());
            Assert.True(replay.Replayed); Assert.Equal(committedBytes, File.ReadAllBytes(SavePath)); Assert.Equal(114, replay.State.Coins); Assert.Single(replay.State.DailyState.Receipts);
        }
    }
}
