using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class HorsePastureTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-pasture-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private (AtomicSaveStore Store, StableSnapshot State) Starter()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(SavePath, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", "old_management_initial_v3.clvr")));
            var store = new AtomicSaveStore(SavePath); var initial = store.Load().State;
            return (store, store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision));
        }

        private static HorseRecord AtHome(HorseRecord h, HomeKind home) => new HorseRecord(h.HorseId, h.Name, h.Parentage,
            h.Genome, h.ResolverVersion, h.AppearanceSeed, h.OwnerId, h.LifeState, h.ReproductionState,
            h.DevelopmentalFactors, h.InheritedGamePotential, h.TrainingState, h.PlayerKnowledge, h.CosmeticState,
            home, h.OriginRecord, h.OriginCommitId, h.BirthDay, h.Revision);

        private static SaveHeader NextRevision(SaveHeader h) => new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion,
            h.GameBuildVersion, checked(h.SaveRevision + 1), h.CalendarDay, h.CalendarProfileVersion, h.CareMode,
            h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion,
            h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, "unwritten");

        private static HorseRecord ExtraAdult(StableSnapshot s, HomeKind home = HomeKind.ActiveStable)
        {
            var h = s.Horses[0];
            return new HorseRecord(new HorseId(Guid.NewGuid()), "Pasture friend", new Parentage(), h.Genome, h.ResolverVersion,
                h.AppearanceSeed, s.PlayerOwnerId, h.LifeState, new ReproductionState(ReproductiveRole.NonBreeding),
                h.DevelopmentalFactors, h.InheritedGamePotential, homeAssignment: home, originRecord: "pasture-test");
        }

        private static StableSnapshot EndDay(AtomicSaveStore store, StableSnapshot state)
        {
            var plan = new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable
                && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, true, true)));
            var preview = new StableDayService().Preview(state, plan);
            Assert.True(preview.Allowed, preview.BlockReason);
            return store.CommitEndDay(preview.Receipt!.Request, new StableDayService()).State;
        }

        [Fact]
        public void PastureFreesSpaceForBreedingWithoutInvalidatingTheActivePair()
        {
            var (_, state) = Starter();
            state = state.WithHorses(state.Horses.Concat(new[] { ExtraAdult(state), ExtraAdult(state, HomeKind.CooperativePasture) }));
            var mare = state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Mare);
            var sire = state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Stallion);
            var quote = new StableBreedingPlanner().Preview(state, mare.HorseId, sire.HorseId, new BreedingCommitId(Guid.NewGuid()));
            Assert.True(quote.IsEligible, string.Join("; ", quote.Rejections.Select(r => r.Message)));
            Assert.Equal(3, quote.CurrentCapacityUsed);
            Assert.Equal(4, quote.FutureCapacityUsed);
        }

        [Fact]
        public void PastureFreesSpaceForTheFair()
        {
            var (_, state) = Starter();
            state = state.WithHorses(state.Horses.Concat(new[] { ExtraAdult(state), ExtraAdult(state, HomeKind.CooperativePasture) }));
            Assert.Null(HorseFair.BlockReason(state, HorseFair.Offers(state).OrderBy(o => o.PriceCoins).First()));
        }

        [Fact]
        public void PasturedHorsesCannotShowOrReceiveHandsOnCare()
        {
            var (_, state) = Starter(); var horse = state.Horses[0];
            state = state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(horse.HorseId) ? AtHome(h, HomeKind.CooperativePasture) : h));
            horse = state.Horses.Single(h => h.HorseId.Equals(horse.HorseId));
            Assert.False(HorseShow.IsEligibleHorse(state, horse));
            Assert.Contains("return", HorseShow.BlockReason(state, horse.HorseId));
            Assert.Contains("return", HorseBond.BlockReason(state, horse.HorseId, "pat"));
            Assert.False(PracticeGallop.Eligibility(state).Single(e => e.HorseId.Equals(horse.HorseId)).Eligible);
            Assert.NotNull(ClubRace.BlockReason(state, horse.HorseId));
        }

        [Fact]
        public void PasturedHorsesAreNotGivenDailyCareTasks()
        {
            var (_, state) = Starter();
            state = state.WithHorses(state.Horses.Select(h => AtHome(h, HomeKind.CooperativePasture)));
            Assert.Empty(DailyTasks.For(state));
        }

        private static StableSnapshot Move(AtomicSaveStore store, StableSnapshot state, HorseId horseId, HomeKind home) =>
            store.CommitPastureTransfer(state.PlayerOwnerId, horseId, state.Header.SaveRevision,
                state.Horses.Single(h => h.HorseId.Equals(horseId)).Revision, home);

        private AtomicSaveStore StoreSnapshot(StableSnapshot state)
        {
            var store = new AtomicSaveStore(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".clover"));
            store.Initialize(state); return store;
        }

        [Fact]
        public void StarterTransferPreservesEveryOtherFieldAndRootAndSurvivesReload()
        {
            var (store, state) = Starter(); var before = state.Horses[0];
            state = store.CommitInteraction(state.PlayerOwnerId, before.HorseId, "pat", state.Header.SaveRevision);
            state = store.CommitTack(state.PlayerOwnerId, before.HorseId, Tack.PadSlot, "pad.rose", state.Header.SaveRevision);
            state = store.CommitDnaTest(state.PlayerOwnerId, before.HorseId, state.Header.SaveRevision, DnaTest.Price);
            state = store.RecordPractice(state.PlayerOwnerId, state.Header.SaveRevision, new[] { before.HorseId });
            before = state.Horses.Single(h => h.HorseId.Equals(before.HorseId));
            var after = Move(store, state, before.HorseId, HomeKind.CooperativePasture);
            var horse = after.Horses.Single(h => h.HorseId.Equals(before.HorseId));
            Assert.Equal(HomeKind.CooperativePasture, horse.HomeAssignment);
            Assert.Equal(before.OwnerId, horse.OwnerId); Assert.Equal(before.Name, horse.Name);
            Assert.Equal(before.Genome.GenomeHash, horse.Genome.GenomeHash);
            Assert.Equal(before.AppearanceSeed, horse.AppearanceSeed);
            Assert.Equal(before.ResolverVersion, horse.ResolverVersion);
            Assert.Equal(before.DevelopmentalFactors.Hash, horse.DevelopmentalFactors.Hash);
            Assert.Equal(before.InheritedGamePotential.Hash, horse.InheritedGamePotential.Hash);
            Assert.Equal(before.TrainingState.Hash, horse.TrainingState.Hash);
            Assert.Equal(before.PlayerKnowledge.KnowledgeHash, horse.PlayerKnowledge.KnowledgeHash);
            Assert.Equal(before.CosmeticState, horse.CosmeticState);
            Assert.Equal(before.Parentage.ParentAId, horse.Parentage.ParentAId);
            Assert.Equal(before.Parentage.ParentBId, horse.Parentage.ParentBId);
            Assert.Equal(before.Parentage.FounderBackground?.AssumptionId, horse.Parentage.FounderBackground?.AssumptionId);
            Assert.Equal(before.Parentage.FounderBackground?.InbreedingCoefficient, horse.Parentage.FounderBackground?.InbreedingCoefficient);
            Assert.Equal(before.LifeState.Stage, horse.LifeState.Stage);
            Assert.Equal(before.LifeState.VisualAge, horse.LifeState.VisualAge);
            Assert.Equal(before.ReproductionState.Role, horse.ReproductionState.Role);
            Assert.Equal(before.ReproductionState.PregnancyId, horse.ReproductionState.PregnancyId);
            Assert.Equal(before.ReproductionState.RecoveryUntilDay, horse.ReproductionState.RecoveryUntilDay);
            Assert.Equal(before.LifeState.DaysSinceConception, horse.LifeState.DaysSinceConception);
            Assert.Equal(before.OriginRecord, horse.OriginRecord); Assert.Equal(before.BirthDay, horse.BirthDay);
            Assert.Equal(before.Revision + 1, horse.Revision);
            Assert.Equal(state.Header.SaveRevision + 1, after.Header.SaveRevision);
            Assert.Equal(state.CapacityRevision + 1, after.CapacityRevision);
            Assert.Equal(state.Coins, after.Coins);
            Assert.Equal(state.Ledger.Select(l => (l.Id, l.Kind, l.DeltaCoins, l.BreedingId)),
                after.Ledger.Select(l => (l.Id, l.Kind, l.DeltaCoins, l.BreedingId)));
            Assert.Equal(state.Header.RngStreams.Breeding.EventCounter, after.Header.RngStreams.Breeding.EventCounter);
            Assert.Equal(state.Conceptions.Select(c => c.ReceiptHash), after.Conceptions.Select(c => c.ReceiptHash));
            Assert.Equal(state.DailyState.Receipts.Select(c => c.ReceiptHash), after.DailyState.Receipts.Select(c => c.ReceiptHash));
            Assert.Equal(state.PendingCapacityPlans.Select(p => p.ReservationId), after.PendingCapacityPlans.Select(p => p.ReservationId));
            Assert.Equal(state.LegacyLifecycleBaselines.Count, after.LegacyLifecycleBaselines.Count);
            Assert.Equal(StableSnapshot.SchemaVersion, after.Header.SchemaVersion);
            Assert.Equal(HomeKind.CooperativePasture, new AtomicSaveStore(store.SavePath).Load().State.Horses.Single(h => h.HorseId.Equals(before.HorseId)).HomeAssignment);
            var returned = Move(store, after, before.HorseId, HomeKind.ActiveStable);
            Assert.Equal(HomeKind.ActiveStable, returned.Horses.Single(h => h.HorseId.Equals(before.HorseId)).HomeAssignment);
        }

        [Fact]
        public void GenericSavesCannotBypassTransfersInEitherDirection()
        {
            var (store, state) = Starter(); var horseId = state.Horses[0].HorseId;
            foreach (var target in new[] { HomeKind.CooperativePasture, HomeKind.ActiveStable })
            {
                var bytes = File.ReadAllBytes(store.SavePath);
                var replacement = state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(horseId) ? AtHome(h, target) : h))
                    .WithHeader(NextRevision(state.Header));
                Assert.Contains("homes", Assert.Throws<SaveIntegrityException>(() => store.SaveSnapshot(replacement, state.Header.SaveRevision)).Message);
                Assert.Equal(bytes, File.ReadAllBytes(store.SavePath));
                state = Move(store, state, horseId, target);
            }
        }

        [Fact]
        public void PastureRetainsNamingDnaAndCosmeticsAndReturnRestoresActiveActivities()
        {
            var (store, state) = Starter();
            while (state.Header.CalendarDay < Festivals.FirstDayOfSeason) state = EndDay(store, state);
            var mareId = state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Mare).HorseId;
            var sireId = state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Stallion).HorseId;
            state = Move(store, state, mareId, HomeKind.CooperativePasture);
            var bytes = File.ReadAllBytes(store.SavePath);
            Assert.Throws<StableCommandRejectedException>(() => store.RecordPractice(state.PlayerOwnerId, state.Header.SaveRevision, new[] { mareId }));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitInteraction(state.PlayerOwnerId, mareId, "pat", state.Header.SaveRevision));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitFestivalTreat(state.PlayerOwnerId, mareId, state.Header.SaveRevision));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitClubRace(state.PlayerOwnerId, mareId, state.Header.SaveRevision));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitShowEntry(state.PlayerOwnerId, mareId, state.Header.SaveRevision, HorseShow.EntryFee));
            var pairing = new StableBreedingPlanner().Preview(state, mareId, sireId, new BreedingCommitId(Guid.NewGuid()));
            Assert.Contains(pairing.Rejections, r => r.Code == "home");
            Assert.Throws<ConceptionRejectedException>(() => store.CommitStableBreeding(pairing.Request, new StableBreedingPlanner()));
            Assert.Contains("return", Festivals.BlockReason(state, mareId));
            Assert.DoesNotContain(DailyTasks.For(state), t => t.Id.Contains(mareId.ToString()));
            Assert.False(new StableDayService().Preview(state, new CarePlan(new[] { new HorseCareAssignment(mareId, true, true, true, true) })).Allowed);
            Assert.Equal(bytes, File.ReadAllBytes(store.SavePath));

            var mare = state.Horses.Single(h => h.HorseId.Equals(mareId));
            state = store.RenameHorse(state.PlayerOwnerId, mareId, state.Header.SaveRevision, mare.Revision, "Pasture favourite");
            state = store.CommitTack(state.PlayerOwnerId, mareId, Tack.PadSlot, "pad.rose", state.Header.SaveRevision);
            state = store.CommitDnaTest(state.PlayerOwnerId, mareId, state.Header.SaveRevision, DnaTest.Price);
            state = Move(store, state, mareId, HomeKind.ActiveStable);
            mare = store.Load().State.Horses.Single(h => h.HorseId.Equals(mareId));
            Assert.Equal("Pasture favourite", mare.Name);
            Assert.Equal("pad.rose", Tack.Worn(mare, Tack.PadSlot)!.Id);
            Assert.NotEmpty(mare.PlayerKnowledge.RevealedCalls);
            Assert.Null(HorseBond.BlockReason(state, mareId, "pat"));
            Assert.Null(Festivals.BlockReason(state, mareId));
            Assert.Null(HorseShow.BlockReason(state, mareId));
            Assert.Null(ClubRace.BlockReason(state, mareId));
            Assert.True(PracticeGallop.Eligibility(state).Single(e => e.HorseId.Equals(mareId)).Eligible);
            Assert.True(new StableBreedingPlanner().Preview(state, mareId, sireId, new BreedingCommitId(Guid.NewGuid())).IsEligible);
        }

        [Fact]
        public void ExactRetryNeverWritesAgainAndStaleRequestCannotUndoARoundTrip()
        {
            var (store, state) = Starter(); var horse = state.Horses[0];
            var boarded = Move(store, state, horse.HorseId, HomeKind.CooperativePasture);
            var bytes = File.ReadAllBytes(store.SavePath);
            var retry = store.CommitPastureTransfer(state.PlayerOwnerId, horse.HorseId, state.Header.SaveRevision, horse.Revision, HomeKind.CooperativePasture);
            Assert.Equal(boarded.Header.SaveRevision, retry.Header.SaveRevision); Assert.Equal(bytes, File.ReadAllBytes(store.SavePath));
            var tomorrow = EndDay(store, boarded);
            store.CommitPastureTransfer(state.PlayerOwnerId, horse.HorseId, state.Header.SaveRevision, horse.Revision, HomeKind.CooperativePasture);
            Assert.Equal(tomorrow.Header.SaveRevision, store.Load().State.Header.SaveRevision);
            var returned = Move(store, tomorrow, horse.HorseId, HomeKind.ActiveStable);
            Assert.Equal("stale_save", Assert.Throws<StableCommandRejectedException>(() =>
                store.CommitPastureTransfer(state.PlayerOwnerId, horse.HorseId, state.Header.SaveRevision, horse.Revision, HomeKind.CooperativePasture)).Code);
            Assert.Equal(HomeKind.ActiveStable, store.Load().State.Horses.Single(h => h.HorseId.Equals(horse.HorseId)).HomeAssignment);
            Assert.Equal(returned.Header.SaveRevision, store.Load().State.Header.SaveRevision);
        }

        [Fact]
        public void SixProtectedHorsesCanMakeRoomWithoutSellingAnyone()
        {
            var (_, initial) = Starter();
            var state = new StableSnapshot(initial.Header, initial.PlayerOwnerId, initial.Horses, 10000, initial.CapacityRevision,
                initial.Ledger, initial.PendingCapacityPlans, initial.Conceptions, initial.DailyState, initial.LegacyLifecycleBaselines);
            var store = StoreSnapshot(state);
            state = store.CommitStall(state.PlayerOwnerId, state.Header.SaveRevision, 450);
            state = store.CommitStall(state.PlayerOwnerId, state.Header.SaveRevision, 800);
            foreach (var offer in HorseFair.Offers(state))
                state = store.CommitPurchase(state.PlayerOwnerId, offer.HorseId, state.Header.SaveRevision, offer.PriceCoins);
            while (state.Header.CalendarDay < 7) state = EndDay(store, state);
            var last = HorseFair.Offers(state)[0];
            state = store.CommitPurchase(state.PlayerOwnerId, last.HorseId, state.Header.SaveRevision, last.PriceCoins);
            Assert.Equal(6, HorsePasture.ActivePlacesUsed(state));
            Assert.All(state.Horses, h => Assert.False(HorseMarket.Quote(state, h.HorseId).Allowed));
            var offerToBuy = HorseFair.Offers(state).First(o => !o.AlreadyBought);
            Assert.Contains("stall", HorseFair.BlockReason(state, offerToBuy));
            var boarded = Move(store, state, last.HorseId, HomeKind.CooperativePasture);
            Assert.Equal(5, HorsePasture.ActivePlacesUsed(boarded)); Assert.Equal(6, boarded.Horses.Count);
            Assert.Null(HorseFair.BlockReason(boarded, HorseFair.Offers(boarded).Single(o => o.HorseId.Equals(offerToBuy.HorseId))));
            var bought = store.CommitPurchase(boarded.PlayerOwnerId, offerToBuy.HorseId, boarded.Header.SaveRevision, offerToBuy.PriceCoins);
            Assert.Equal(7, bought.Horses.Count); Assert.Equal(6, HorsePasture.ActivePlacesUsed(bought));
            Assert.Contains("stall", HorsePasture.BlockReason(bought, last.HorseId, HomeKind.ActiveStable));
            Assert.Throws<StableCommandRejectedException>(() => Move(store, bought, last.HorseId, HomeKind.ActiveStable));
            var first = bought.Horses.First(h => h.OriginRecord == "stable-day-starter");
            var anotherOut = Move(store, bought, first.HorseId, HomeKind.CooperativePasture);
            var back = Move(store, anotherOut, last.HorseId, HomeKind.ActiveStable);
            Assert.Equal(6, HorsePasture.ActivePlacesUsed(back));
            Assert.Equal(back.PlayerOwnerId, back.Horses.Single(h => h.HorseId.Equals(last.HorseId)).OwnerId);
        }

        [Fact]
        public void BoardingKeepsCareBondAndTrainingNeutralWhileAgeAdvancesAndUpkeepFalls()
        {
            var (store, state) = Starter(); state = EndDay(store, state); var horse = state.Horses[0];
            state = Move(store, state, horse.HorseId, HomeKind.CooperativePasture);
            var care = state.DailyState.For(horse.HorseId); var coins = state.Coins;
            var tomorrow = EndDay(store, state); var after = tomorrow.Horses.Single(h => h.HorseId.Equals(horse.HorseId));
            var receipt = tomorrow.DailyState.Receipts.Last();
            Assert.Single(receipt.HorseOutcomes); Assert.Equal(6, receipt.CareCostCoins); Assert.Equal(24, receipt.OperationsCostCoins);
            Assert.Equal(coins + 50 - 6 - 24, tomorrow.Coins);
            var kept = tomorrow.DailyState.For(horse.HorseId);
            Assert.Equal(care.Nutrition, kept.Nutrition); Assert.Equal(care.Bedding, kept.Bedding);
            Assert.Equal(care.Turnout, kept.Turnout); Assert.Equal(care.Grooming, kept.Grooming);
            Assert.Equal(horse.LifeState.DaysSinceConception + 1, after.LifeState.DaysSinceConception);
            Assert.Equal(horse.TrainingState.Hash, after.TrainingState.Hash);
            Assert.Contains(tomorrow.Horses, h => h.HorseId.Equals(horse.HorseId));
            var active = tomorrow.Horses.Single(h => h.HomeAssignment == HomeKind.ActiveStable);
            var allOut = Move(store, tomorrow, active.HorseId, HomeKind.CooperativePasture);
            var emptyDay = EndDay(store, allOut);
            var emptyReceipt = emptyDay.DailyState.Receipts.Last();
            Assert.Empty(emptyReceipt.HorseOutcomes); Assert.Equal(0, emptyReceipt.CareCostCoins);
            Assert.Equal(0, emptyReceipt.OperationsCostCoins); Assert.Equal(0, emptyReceipt.IncomeCoins);
            Assert.Equal(allOut.Coins, emptyDay.Coins);
        }

        [Fact]
        public void PregnancyRecoveryAndDependentFoalsStayTogetherUntilAdulthood()
        {
            var (store, state) = Starter(); var planner = new StableBreedingPlanner();
            var mare = state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Mare).HorseId;
            var sire = state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Stallion).HorseId;
            var conception = store.CommitStableBreeding(planner.Preview(state, mare, sire, new BreedingCommitId(Guid.NewGuid())).Request, planner);
            state = conception.State; var child = conception.Receipt.Commit.OffspringHorseId;
            Assert.Contains("foal", HorsePasture.BlockReason(state, mare, HomeKind.CooperativePasture));
            Assert.Contains("adult", HorsePasture.BlockReason(state, child, HomeKind.CooperativePasture));
            Assert.Throws<StableCommandRejectedException>(() => Move(store, state, mare, HomeKind.CooperativePasture));
            void AssertUnsafeBoarding(HorseId id) => Assert.Throws<SaveIntegrityException>(() =>
                state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(id) ? AtHome(h, HomeKind.CooperativePasture) : h)));
            AssertUnsafeBoarding(mare); AssertUnsafeBoarding(child);
            // A sire is not the dependent foal's caregiver and can board safely.
            state = Move(store, state, sire, HomeKind.CooperativePasture);
            Assert.Equal(2, HorsePasture.ActivePlacesUsed(state)); // mare + unborn foal
            while (state.Header.CalendarDay < 4) state = EndDay(store, state);
            Assert.Contains("recover", HorsePasture.BlockReason(state, mare, HomeKind.CooperativePasture));
            AssertUnsafeBoarding(mare); AssertUnsafeBoarding(child);
            while (state.Header.CalendarDay < 6) state = EndDay(store, state);
            Assert.Contains("young", HorsePasture.BlockReason(state, mare, HomeKind.CooperativePasture));
            AssertUnsafeBoarding(mare); AssertUnsafeBoarding(child);
            while (state.Header.CalendarDay < 7) state = EndDay(store, state);
            Assert.Contains("adult", HorsePasture.BlockReason(state, child, HomeKind.CooperativePasture));
            Assert.Contains("young", HorsePasture.BlockReason(state, mare, HomeKind.CooperativePasture));
            AssertUnsafeBoarding(mare); AssertUnsafeBoarding(child);
            while (state.Header.CalendarDay < 10) state = EndDay(store, state);
            Assert.Null(HorsePasture.BlockReason(state, mare, HomeKind.CooperativePasture));
            state = Move(store, state, child, HomeKind.CooperativePasture);
            state = Move(store, state, mare, HomeKind.CooperativePasture);
            var reloaded = store.Load().State;
            Assert.Equal(conception.Receipt.ReceiptHash, reloaded.Conceptions.Single().ReceiptHash);
            Assert.Equal(mare, reloaded.Horses.Single(h => h.HorseId.Equals(child)).Parentage.ParentAId);
            Assert.Equal(0, HorsePasture.ActivePlacesUsed(reloaded));
            var pasturedAdult = reloaded.Horses.Single(h => h.HorseId.Equals(child));
            reloaded = EndDay(store, reloaded);
            Assert.Equal(pasturedAdult.LifeState.DaysSinceConception + 1,
                reloaded.Horses.Single(h => h.HorseId.Equals(child)).LifeState.DaysSinceConception);
        }

        [Fact]
        public void ReturnCountsPendingCapacityHolds()
        {
            var (store, state) = Starter(); var horse = state.Horses[0];
            state = Move(store, state, horse.HorseId, HomeKind.CooperativePasture);
            var holds = Enumerable.Range(0, 3).Select(_ => new BreedingCapacityPlan(new BreedingCommitId(Guid.NewGuid()),
                new CapacityReservationId(Guid.NewGuid()), state.CapacityRevision, Array.Empty<ReservedCapacityWindow>())).ToArray();
            state = new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance, state.CapacityRevision,
                state.Ledger, holds, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
            store = StoreSnapshot(state);
            Assert.Equal(4, HorsePasture.ActivePlacesUsed(state));
            Assert.Contains("stall", HorsePasture.BlockReason(state, horse.HorseId, HomeKind.ActiveStable));
            Assert.Throws<StableCommandRejectedException>(() => Move(store, state, horse.HorseId, HomeKind.ActiveStable));
        }

        [Fact]
        public void WrongOwnerMissingHorseUnsupportedHomesAndStaleRevisionsCannotTransfer()
        {
            var (store, state) = Starter(); var horse = state.Horses[0]; var bytes = File.ReadAllBytes(store.SavePath);
            Assert.Throws<StableCommandRejectedException>(() => store.CommitPastureTransfer(new OwnerId(Guid.NewGuid()), horse.HorseId, state.Header.SaveRevision, horse.Revision, HomeKind.CooperativePasture));
            Assert.Throws<StableCommandRejectedException>(() => Move(store, state, horse.HorseId, HomeKind.InstitutionalLease));
            Assert.Throws<StableCommandRejectedException>(() => Move(store, state, horse.HorseId, (HomeKind)99));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitPastureTransfer(state.PlayerOwnerId, new HorseId(Guid.NewGuid()), state.Header.SaveRevision, horse.Revision, HomeKind.CooperativePasture));
            Assert.Equal("stale_save", Assert.Throws<StableCommandRejectedException>(() => store.CommitPastureTransfer(state.PlayerOwnerId, horse.HorseId, state.Header.SaveRevision + 1, horse.Revision, HomeKind.CooperativePasture)).Code);
            Assert.Equal("stale_horse", Assert.Throws<StableCommandRejectedException>(() => store.CommitPastureTransfer(state.PlayerOwnerId, horse.HorseId, state.Header.SaveRevision, horse.Revision + 1, HomeKind.CooperativePasture)).Code);
            Assert.Equal(bytes, File.ReadAllBytes(store.SavePath));
        }

        [Theory]
        [InlineData(SaveWriteStage.TemporaryPartWritten, false)]
        [InlineData(SaveWriteStage.TemporaryWritten, false)]
        [InlineData(SaveWriteStage.TemporaryFlushed, false)]
        [InlineData(SaveWriteStage.BeforeReplace, false)]
        [InlineData(SaveWriteStage.AfterReplace, false)]
        [InlineData(SaveWriteStage.TemporaryPartWritten, true)]
        [InlineData(SaveWriteStage.TemporaryWritten, true)]
        [InlineData(SaveWriteStage.TemporaryFlushed, true)]
        [InlineData(SaveWriteStage.BeforeReplace, true)]
        [InlineData(SaveWriteStage.AfterReplace, true)]
        public void InterruptedTransferAndExactRetryNeverLoseOrDuplicateTheHorse(SaveWriteStage stage, bool returning)
        {
            var (store, state) = Starter(); var horse = state.Horses[0];
            if (returning) state = Move(store, state, horse.HorseId, HomeKind.CooperativePasture);
            var destination = returning ? HomeKind.ActiveStable : HomeKind.CooperativePasture;
            var bytes = File.ReadAllBytes(store.SavePath);
            var broken = new AtomicSaveStore(store.SavePath, faultInjector: s => { if (s == stage) throw new IOException("test interruption"); });
            Assert.Throws<IOException>(() => Move(broken, state, horse.HorseId, destination));
            if (stage != SaveWriteStage.AfterReplace) Assert.Equal(bytes, File.ReadAllBytes(store.SavePath));
            var recovered = Move(store, state, horse.HorseId, destination);
            Assert.Equal(state.Header.SaveRevision + 1, recovered.Header.SaveRevision);
            Assert.Equal(state.CapacityRevision + 1, recovered.CapacityRevision);
            Assert.Equal(state.Horses.Count, recovered.Horses.Count);
            Assert.Equal(state.Coins, recovered.Coins);
            Assert.Equal(destination, recovered.Horses.Single(h => h.HorseId.Equals(horse.HorseId)).HomeAssignment);
        }
    }
}
