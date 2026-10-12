using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class StableSaveV1MigrationTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-v1-migration-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", name));
        private static CarePlan Plan(StableSnapshot state) => new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId)
            && h.HomeAssignment == HomeKind.ActiveStable && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
        private static void AssertOriginalRngStreams(RngStreams actual, ulong breedingCounter)
        {
            AssertStream(new RngStreamState(new Seed128(11, 22), "breeding", breedingCounter), actual.Breeding);
            AssertStream(new RngStreamState(new Seed128(11, 22), "cosmetic"), actual.Cosmetic);
            AssertStream(new RngStreamState(new Seed128(11, 22), "world"), actual.World);
            AssertStream(new RngStreamState(new Seed128(11, 22), "competition"), actual.Competition);
        }
        private static void AssertStream(RngStreamState expected, RngStreamState actual)
        {
            Assert.Equal(expected.Seed, actual.Seed); Assert.Equal(expected.Purpose, actual.Purpose);
            Assert.Equal(expected.EventCounter, actual.EventCounter); Assert.Equal(expected.AlgorithmVersion, actual.AlgorithmVersion);
        }
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Theory]
        [InlineData("old_static_initial_v1.clvr", 2, 0)]
        [InlineData("old_conception_v1.clvr", 3, 1)]
        public void GenuineV1FixturesMigrateCareRootWithoutChangingExistingHorseOrConceptionData(string filename, int horseCount, int conceptionCount)
        {
            var bytes = Fixture(filename); var original = (byte[])bytes.Clone(); var codec = new StableSaveCodec();
            var migrated = codec.Decode(bytes);
            Assert.Equal(original, bytes); Assert.Equal(StableSnapshot.SchemaVersion, migrated.Header.SchemaVersion);
            Assert.Contains("stable.daily-care-root.v1-to-v2", migrated.Header.MigrationHistory);
            Assert.Equal(horseCount, migrated.Horses.Count); Assert.Equal(conceptionCount, migrated.Conceptions.Count);
            Assert.Equal(horseCount, migrated.DailyState.HorseCare.Count); Assert.Empty(migrated.DailyState.Receipts);
            Assert.All(migrated.Horses, h => Assert.Equal(HorseCareState.Initial.Nutrition, migrated.DailyState.For(h.HorseId).Nutrition));
            Assert.Equal(10, migrated.Header.CalendarDay);
            Assert.Equal(conceptionCount == 0 ? 100 : 50, migrated.Coins); Assert.Equal(ForecastTestData.Owner, migrated.PlayerOwnerId);
            AssertOriginalRngStreams(migrated.Header.RngStreams, (ulong)conceptionCount);
            var expectedIds = new[] { ForecastTestData.Id(1), ForecastTestData.Id(2) }.Concat(conceptionCount == 0
                ? Array.Empty<HorseId>() : new[] { migrated.Conceptions[0].Commit.OffspringHorseId }).OrderBy(id => id.ToString(), StringComparer.Ordinal);
            Assert.Equal(expectedIds, migrated.Horses.Select(h => h.HorseId));
            Assert.Equal(ForecastTestData.Genome(("slc45a2", "N", "Cr")).GenomeHash,
                migrated.Horses.Single(h => h.HorseId.Equals(ForecastTestData.Id(1))).Genome.GenomeHash);
            if (conceptionCount == 0)
            {
                Assert.Equal(0UL, migrated.CapacityRevision); Assert.Single(migrated.PendingCapacityPlans);
                Assert.Equal(ConceptionTestData.Reservation, migrated.PendingCapacityPlans.Single().ReservationId);
                Assert.Empty(migrated.Ledger);
            }
            else
            {
                var conception = migrated.Conceptions.Single(); Assert.Equal(ConceptionTestData.Event, conception.Commit.BreedingCommitId);
                Assert.Equal(ForecastTestData.Id(1), conception.Commit.ParentAId); Assert.Equal(ForecastTestData.Id(2), conception.Commit.ParentBId);
                Assert.Equal(10, conception.Commit.ConceptionDay); Assert.Equal(14, conception.Commit.PlannedBirthDay);
                Assert.Equal(50, conception.Terms.CostCoins); Assert.Equal(1UL, migrated.CapacityRevision);
                Assert.Empty(migrated.PendingCapacityPlans); Assert.Single(migrated.Ledger);
                Assert.Equal(-50, migrated.Ledger.Single().DeltaCoins); Assert.Equal("breeding", migrated.Ledger.Single().Kind);
                Assert.Equal(ConceptionTestData.Reservation, conception.Capacity.ReservationId);
                Assert.Equal(LifeStage.Unborn, migrated.Horses.Single(h => h.HorseId.Equals(conception.Commit.OffspringHorseId)).LifeState.Stage);
            }
            var currentBytes = codec.Encode(migrated); var restarted = codec.Decode(currentBytes);
            Assert.Equal(currentBytes, codec.Encode(restarted));
            if (conceptionCount == 1)
            {
                Assert.Equal(migrated.Conceptions[0].Commit.CommitHash, restarted.Conceptions[0].Commit.CommitHash);
                Assert.Equal(migrated.Conceptions[0].UnbornAtCommit.Genome.GenomeHash, restarted.Conceptions[0].UnbornAtCommit.Genome.GenomeHash);
                Assert.Equal(migrated.Conceptions[0].UnbornAtCommit.AppearanceSeed, restarted.Conceptions[0].UnbornAtCommit.AppearanceSeed);
                Assert.Equal(migrated.Conceptions[0].Commit.BreedingSeed, restarted.Conceptions[0].Commit.BreedingSeed);
            }
        }

        [Fact]
        public void MigratedConceptionSaveCanCommitAndRestartANewDayWithoutLosingItsHistory()
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_conception_v1.clvr"));
            var store = new AtomicSaveStore(SavePath); var loaded = store.Load().State; var originalCommitHash = loaded.Conceptions.Single().Commit.CommitHash;
            var request = new EndDayRequest(new TransactionId(Guid.Parse("d1000000-0000-0000-0000-000000000001")), loaded.PlayerOwnerId,
                loaded.Header.SaveRevision, loaded.Header.CalendarDay, Plan(loaded), StableDailyPolicy.Version);
            var ended = store.CommitEndDay(request, new StableDayService()); var savedBytes = File.ReadAllBytes(SavePath);
            var restarted = new AtomicSaveStore(SavePath).Load().State;
            Assert.Equal(11, restarted.Header.CalendarDay); Assert.Single(restarted.DailyState.Receipts);
            Assert.Equal(new[] { "stable.daily-care-root.v1-to-v2", "stable.lifecycle-events.v2-to-v3" }, restarted.Header.MigrationHistory);
            Assert.Equal(originalCommitHash, restarted.Conceptions.Single().Commit.CommitHash);
            Assert.Equal(ended.Receipt.ReceiptHash, restarted.DailyState.Receipts.Single().ReceiptHash);
            Assert.Equal(savedBytes, new StableSaveCodec().Encode(restarted));
        }

        [Theory]
        [InlineData("old_care_day_v2.clvr", 11, 64, 1)]
        [InlineData("old_pregnancy_care_v2.clvr", 13, 0, 3)]
        [InlineData("old_born_foal_v2.clvr", 14, 50, 0)]
        public void GenuineV2FixturesMigrateWithoutInventingLifecycleEvents(string filename, int day, long coins, int receipts)
        {
            var bytes = Fixture(filename); var original = (byte[])bytes.Clone(); var migrated = new StableSaveCodec().Decode(bytes);
            Assert.Equal(original, bytes); Assert.Equal(StableSnapshot.SchemaVersion, migrated.Header.SchemaVersion);
            Assert.Contains("stable.lifecycle-events.v2-to-v3", migrated.Header.MigrationHistory);
            Assert.Equal(day, migrated.Header.CalendarDay); Assert.Equal(coins, migrated.Coins);
            Assert.Equal(receipts, migrated.DailyState.Receipts.Count);
            Assert.All(migrated.DailyState.Receipts, receipt => Assert.Empty(receipt.LifecycleEvents));
            AssertOriginalRngStreams(migrated.Header.RngStreams, filename == "old_care_day_v2.clvr" ? 0UL : 1UL);
            var currentBytes = new StableSaveCodec().Encode(migrated); var restarted = new StableSaveCodec().Decode(currentBytes);
            Assert.Equal(currentBytes, new StableSaveCodec().Encode(restarted));

            if (filename == "old_pregnancy_care_v2.clvr")
            {
                Assert.All(migrated.DailyState.Receipts, receipt => Assert.Equal(StableDailyPolicy.LegacyVersion, receipt.Request.PolicyVersion));
                Assert.Equal(LifeStage.Unborn, migrated.Horses.Single(h => h.OriginCommitId != null).LifeState.Stage);
                Assert.Equal(14, migrated.Conceptions.Single().Commit.PlannedBirthDay);
            }
            if (filename == "old_born_foal_v2.clvr")
            {
                var foal = migrated.Horses.Single(h => h.OriginCommitId != null);
                Assert.Equal(LifeStage.Foal, foal.LifeState.Stage); Assert.Equal(14, foal.BirthDay);
                Assert.Equal(4, foal.LifeState.DaysSinceConception); Assert.Empty(migrated.DailyState.Receipts);
                Assert.Single(migrated.LegacyLifecycleBaselines);
                var mareId = migrated.Conceptions.Single().ParentAAtCommit.ReproductionState.Role == ReproductiveRole.Mare
                    ? migrated.Conceptions.Single().Commit.ParentAId : migrated.Conceptions.Single().Commit.ParentBId;
                Assert.Equal(16, migrated.Horses.Single(h => h.HorseId.Equals(mareId)).ReproductionState.RecoveryUntilDay);
                Assert.False(migrated.LifecycleCapacity.Obligations.Single().CountsAsAdultHorse);
                Assert.True(migrated.LifecycleCapacity.Obligations.Single().UsesConfirmedReservation);
            }
        }

        [Fact]
        public void MigratedBornFoalKeepsLegacyBaselineAndPublishesGrowthWithoutInventingBirth()
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_born_foal_v2.clvr"));
            var store = new AtomicSaveStore(SavePath); var state = store.Load().State; var foalId = state.Horses.Single(h => h.OriginCommitId != null).HorseId;
            DailyCareReceipt? growthReceipt = null;
            while (state.Header.CalendarDay < 17)
            {
                var plan = Plan(state); var request = new EndDayRequest(new TransactionId(Guid.NewGuid()), state.PlayerOwnerId,
                    state.Header.SaveRevision, state.Header.CalendarDay, plan, StableDailyPolicy.Version);
                var result = store.CommitEndDay(request, new StableDayService());
                if (state.Header.CalendarDay == 16) growthReceipt = result.Receipt;
                state = result.State;
            }
            var grown = state.Horses.Single(h => h.HorseId.Equals(foalId));
            Assert.Equal(LifeStage.Youngster, grown.LifeState.Stage); Assert.Equal(17, state.Header.CalendarDay);
            Assert.DoesNotContain(state.DailyState.Receipts.SelectMany(r => r.LifecycleEvents), e => e.Kind == LifecycleEventKind.Birth);
            Assert.Equal(LifecycleEventKind.FoalToYoungster, Assert.Single(growthReceipt!.LifecycleEvents).Kind);
            var withoutGrowthEvent = new DailyCareReceipt(growthReceipt.Request, growthReceipt.FromDay, growthReceipt.ToDay,
                growthReceipt.CareCostCoins, growthReceipt.OperationsCostCoins, growthReceipt.WalletDebitCoins, growthReceipt.AssistanceCoins,
                growthReceipt.CostTransactionId, growthReceipt.HorseOutcomes);
            var receiptsWithoutGrowth = state.DailyState.Receipts.Select(r => r.Request.OperationId.Equals(withoutGrowthEvent.Request.OperationId)
                ? withoutGrowthEvent : r).ToArray();
            var h = state.Header;
            var tamperedHeader = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, h.SaveRevision, h.CalendarDay,
                h.CalendarProfileVersion, h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion,
                h.DefaultOffspringResolverVersion, h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex,
                h.MigrationHistory, "unwritten");
            Assert.Throws<SaveIntegrityException>(() => { _ = new StableSnapshot(tamperedHeader, state.PlayerOwnerId, state.Horses,
                state.OpeningCoinBalance, state.CapacityRevision, state.Ledger, state.PendingCapacityPlans, state.Conceptions,
                new StableDailyState(state.DailyState.HorseCare, receiptsWithoutGrowth), state.LegacyLifecycleBaselines); });
            var reloaded = new AtomicSaveStore(SavePath).Load().State;
            Assert.Equal(LifeStage.Youngster, reloaded.Horses.Single(h => h.HorseId.Equals(foalId)).LifeState.Stage);
            Assert.Equal(state.DailyState.Receipts.Select(r => r.ReceiptHash), reloaded.DailyState.Receipts.Select(r => r.ReceiptHash));
        }

        [Fact]
        public void PreviewAndCommitDeriveOffspringAgeFromTheConceptionAnchor()
        {
            Directory.CreateDirectory(directory);
            var loaded = new StableSaveCodec().Decode(Fixture("old_pregnancy_care_v2.clvr"));
            var child = loaded.Horses.Single(h => h.OriginCommitId != null);
            var staleChild = new HorseRecord(child.HorseId, child.Name, child.Parentage, child.Genome, child.ResolverVersion, child.AppearanceSeed,
                child.OwnerId, new LifeState(LifeStage.Unborn, 0, child.LifeState.VisualAge), child.ReproductionState, child.DevelopmentalFactors,
                child.InheritedGamePotential, child.TrainingState, child.PlayerKnowledge, child.CosmeticState, child.HomeAssignment,
                child.OriginRecord, child.OriginCommitId, child.BirthDay, child.Revision);
            var state = loaded.WithHorses(loaded.Horses.Select(h => h.HorseId.Equals(child.HorseId) ? staleChild : h));
            var plan = Plan(state); var preview = new StableDayService().Preview(state, plan);
            Assert.True(preview.Allowed, preview.BlockReason); Assert.Equal(4, preview.ToDay - state.Conceptions.Single().Commit.ConceptionDay);
            File.WriteAllBytes(SavePath, new StableSaveCodec().Encode(state));
            var request = new EndDayRequest(new TransactionId(Guid.NewGuid()), state.PlayerOwnerId, state.Header.SaveRevision,
                state.Header.CalendarDay, plan, StableDailyPolicy.Version);
            var result = new AtomicSaveStore(SavePath).CommitEndDay(request, new StableDayService());
            var born = result.State.Horses.Single(h => h.HorseId.Equals(child.HorseId));
            Assert.Equal(4, born.LifeState.DaysSinceConception); Assert.Equal(14, born.BirthDay);
        }

        [Fact]
        public void MigratedLegacyFoalCannotRewriteItsCapturedRoleStageOrAge()
        {
            var state = new StableSaveCodec().Decode(Fixture("old_born_foal_v2.clvr"));
            var foal = state.Horses.Single(h => h.OriginCommitId != null);
            HorseRecord Copy(LifeState life, ReproductiveRole role) => new HorseRecord(foal.HorseId, foal.Name, foal.Parentage, foal.Genome,
                foal.ResolverVersion, foal.AppearanceSeed, foal.OwnerId, life,
                new ReproductionState(role, foal.ReproductionState.PregnancyId, foal.ReproductionState.RecoveryUntilDay),
                foal.DevelopmentalFactors, foal.InheritedGamePotential, foal.TrainingState, foal.PlayerKnowledge, foal.CosmeticState,
                foal.HomeAssignment, foal.OriginRecord, foal.OriginCommitId, foal.BirthDay, foal.Revision);
            Assert.Throws<SaveIntegrityException>(() => state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(foal.HorseId)
                ? Copy(new LifeState(LifeStage.Youngster, 4, VisualAge.Foal), foal.ReproductionState.Role) : h)));
            Assert.Throws<SaveIntegrityException>(() => state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(foal.HorseId)
                ? Copy(new LifeState(LifeStage.Foal, 0, VisualAge.Foal), foal.ReproductionState.Role) : h)));
            var newRole = foal.ReproductionState.Role == ReproductiveRole.Mare ? ReproductiveRole.Stallion : ReproductiveRole.Mare;
            Assert.Throws<SaveIntegrityException>(() => state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(foal.HorseId)
                ? Copy(foal.LifeState, newRole) : h)));
            var conception = state.Conceptions.Single(); var mareId = conception.ParentAAtCommit.ReproductionState.Role == ReproductiveRole.Mare
                ? conception.Commit.ParentAId : conception.Commit.ParentBId;
            var mare = state.Horses.Single(h => h.HorseId.Equals(mareId));
            var recoveredTooSoon = new HorseRecord(mare.HorseId, mare.Name, mare.Parentage, mare.Genome, mare.ResolverVersion, mare.AppearanceSeed,
                mare.OwnerId, mare.LifeState, new ReproductionState(mare.ReproductionState.Role, null, foal.BirthDay!.Value + 1),
                mare.DevelopmentalFactors, mare.InheritedGamePotential, mare.TrainingState, mare.PlayerKnowledge, mare.CosmeticState,
                mare.HomeAssignment, mare.OriginRecord, mare.OriginCommitId, mare.BirthDay, mare.Revision);
            Assert.Throws<SaveIntegrityException>(() => state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(mareId) ? recoveredTooSoon : h)));
        }

        [Fact]
        public void GenericSameDaySaveCannotRewriteLegacyLifecycleProvenance()
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_born_foal_v2.clvr"));
            var store = new AtomicSaveStore(SavePath); var state = store.Load().State; var baseline = Assert.Single(state.LegacyLifecycleBaselines);
            var altered = new LegacyLifecycleBaseline(baseline.HorseId, baseline.Stage, baseline.AgeDays - 1,
                baseline.RecordedDay - 1, baseline.BirthDay, baseline.Role, baseline.RecoveryUntilDay);
            var h = state.Header;
            var nextHeader = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, checked(h.SaveRevision + 1),
                h.CalendarDay, h.CalendarProfileVersion, h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion,
                h.DefaultOffspringResolverVersion, h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex,
                h.MigrationHistory, "unwritten");
            var replacement = new StableSnapshot(nextHeader, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance, state.CapacityRevision,
                state.Ledger, state.PendingCapacityPlans, state.Conceptions, state.DailyState, new[] { altered });
            Assert.Throws<SaveIntegrityException>(() => store.SaveSnapshot(replacement, h.SaveRevision));
        }

        [Fact]
        public void LegacyBornAgeUsesItsCapturedCounterInsteadOfInventingConceptionElapsedDays()
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_born_foal_unaligned_v2.clvr"));
            var store = new AtomicSaveStore(SavePath); var state = store.Load().State; var oldFoal = state.Horses.Single(h => h.OriginCommitId != null);
            Assert.Equal(19, oldFoal.LifeState.DaysSinceConception); Assert.Single(state.LegacyLifecycleBaselines);
            while (state.Header.CalendarDay < 17)
            {
                var request = new EndDayRequest(new TransactionId(Guid.NewGuid()), state.PlayerOwnerId, state.Header.SaveRevision,
                    state.Header.CalendarDay, Plan(state), StableDailyPolicy.Version);
                var preview = new StableDayService().Preview(state, request.Plan); Assert.True(preview.Allowed, preview.BlockReason);
                state = store.CommitEndDay(request, new StableDayService()).State;
            }
            var grown = state.Horses.Single(h => h.HorseId.Equals(oldFoal.HorseId));
            Assert.Equal(22, grown.LifeState.DaysSinceConception); Assert.Equal(LifeStage.Youngster, grown.LifeState.Stage);
        }

        [Fact]
        public void LegacyBornMareCanConceiveAndFoalAgainWhileKeepingBothLifecycleHistories()
        {
            Directory.CreateDirectory(directory);
            StableSnapshot? conceptionState = null;
            HorseRecord? firstFoal = null;
            var extraStallion = ForecastTestData.Horse(3, ForecastTestData.Genome(), ReproductiveRole.Stallion);
            for (ulong seed = 1; seed < 128 && conceptionState == null; seed++)
            {
                var initial = SaveTestData.Initial(1000); var header = initial.Header;
                var seededHeader = new SaveHeader(header.SaveId, header.SaveMode, header.SchemaVersion, header.GameBuildVersion,
                    header.SaveRevision, header.CalendarDay, header.CalendarProfileVersion, header.CareMode, header.ActiveVariantCatalogVersion,
                    header.ActiveInheritanceModelVersion, header.DefaultOffspringResolverVersion, header.RetainedResolverVersions,
                    RngStreams.FromSeed(new Seed128(seed, seed + 1)), header.LastCommittedBreedingEventIndex, header.MigrationHistory, "unwritten");
                var existingHold = initial.PendingCapacityPlans.Single();
                var enlargedHold = new BreedingCapacityPlan(existingHold.BreedingId, existingHold.ReservationId, existingHold.CapacityRevision,
                    existingHold.Windows.Select(w => new ReservedCapacityWindow(w.Stage, w.StartDay, w.EndDayExclusive, 5, 3, 0, true)));
                var seeded = new StableSnapshot(seededHeader, initial.PlayerOwnerId, initial.Horses.Concat(new[] { extraStallion }),
                    initial.OpeningCoinBalance, initial.CapacityRevision, initial.Ledger, new[] { enlargedHold }, initial.Conceptions, initial.DailyState);
                var result = ConceptionTransaction.Apply(seeded, ConceptionTestData.Request(), ConceptionTestData.Service()).State;
                var child = result.Horses.Single(h => h.OriginCommitId != null);
                if (child.ReproductionState.Role != ReproductiveRole.Mare) continue;
                conceptionState = result; firstFoal = child;
            }
            Assert.NotNull(conceptionState); Assert.NotNull(firstFoal);
            var conceived = conceptionState!; var foal = firstFoal!;
            var bornFoal = new HorseRecord(foal.HorseId, foal.Name, foal.Parentage, foal.Genome, foal.ResolverVersion, foal.AppearanceSeed,
                foal.OwnerId, new LifeState(LifeStage.Foal, 4, VisualAge.Foal), new ReproductionState(ReproductiveRole.Mare),
                foal.DevelopmentalFactors, foal.InheritedGamePotential, foal.TrainingState, foal.PlayerKnowledge, foal.CosmeticState,
                foal.HomeAssignment, foal.OriginRecord, foal.OriginCommitId, 14, checked(foal.Revision + 1));
            var mareId = conceived.Conceptions.Single().ParentAAtCommit.ReproductionState.Role == ReproductiveRole.Mare
                ? conceived.Conceptions.Single().Commit.ParentAId : conceived.Conceptions.Single().Commit.ParentBId;
            var pregnantMare = conceived.Horses.Single(h => h.HorseId.Equals(mareId));
            var recoveredMare = new HorseRecord(pregnantMare.HorseId, pregnantMare.Name, pregnantMare.Parentage, pregnantMare.Genome,
                pregnantMare.ResolverVersion, pregnantMare.AppearanceSeed, pregnantMare.OwnerId, pregnantMare.LifeState,
                new ReproductionState(ReproductiveRole.Mare, null, 16), pregnantMare.DevelopmentalFactors, pregnantMare.InheritedGamePotential,
                pregnantMare.TrainingState, pregnantMare.PlayerKnowledge, pregnantMare.CosmeticState, pregnantMare.HomeAssignment,
                pregnantMare.OriginRecord, pregnantMare.OriginCommitId, pregnantMare.BirthDay, checked(pregnantMare.Revision + 1));
            var day14Header = new SaveHeader(conceived.Header.SaveId, conceived.Header.SaveMode, conceived.Header.SchemaVersion,
                conceived.Header.GameBuildVersion, conceived.Header.SaveRevision, 14, conceived.Header.CalendarProfileVersion,
                conceived.Header.CareMode, conceived.Header.ActiveVariantCatalogVersion, conceived.Header.ActiveInheritanceModelVersion,
                conceived.Header.DefaultOffspringResolverVersion, conceived.Header.RetainedResolverVersions, conceived.Header.RngStreams,
                conceived.Header.LastCommittedBreedingEventIndex, conceived.Header.MigrationHistory, "unwritten");
            var bornRecords = conceived.Horses.Select(h => h.HorseId.Equals(foal.HorseId) ? bornFoal
                : h.HorseId.Equals(mareId) ? recoveredMare : h).ToArray();
            var baseline = new LegacyLifecycleBaseline(foal.HorseId, LifeStage.Foal, 4, 14, 14, ReproductiveRole.Mare, 0);
            var legacyBorn = new StableSnapshot(day14Header, conceived.PlayerOwnerId, bornRecords, conceived.OpeningCoinBalance,
                conceived.CapacityRevision, conceived.Ledger, conceived.PendingCapacityPlans, conceived.Conceptions,
                conceived.DailyState, new[] { baseline });
            File.WriteAllBytes(SavePath, new StableSaveCodec().Encode(legacyBorn));
            var store = new AtomicSaveStore(SavePath); var state = store.Load().State;
            while (state.Header.CalendarDay < 22)
            {
                var request = new EndDayRequest(new TransactionId(Guid.NewGuid()), state.PlayerOwnerId, state.Header.SaveRevision,
                    state.Header.CalendarDay, Plan(state), StableDailyPolicy.Version);
                var preview = new StableDayService().Preview(state, request.Plan); Assert.True(preview.Allowed, preview.BlockReason);
                state = store.CommitEndDay(request, new StableDayService()).State;
            }
            var grownMare = state.Horses.Single(h => h.HorseId.Equals(foal.HorseId));
            Assert.Equal(LifeStage.Adult, grownMare.LifeState.Stage); Assert.Equal(12, grownMare.LifeState.DaysSinceConception);
            var secondEvent = new BreedingCommitId(Guid.Parse("bc000000-0000-0000-0000-00000000000a"));
            var secondReservation = new CapacityReservationId(Guid.Parse("bc000000-0000-0000-0000-00000000000b"));
            var nextHold = new BreedingCapacityPlan(secondEvent, secondReservation, state.CapacityRevision, new[] {
                new ReservedCapacityWindow(CapacityStage.Pregnancy, 22, 26, 5, 4, 0, true),
                new ReservedCapacityWindow(CapacityStage.Foal, 26, 29, 5, 4, 0, true),
                new ReservedCapacityWindow(CapacityStage.Youngster, 29, 32, 5, 4, 0, true),
                new ReservedCapacityWindow(CapacityStage.Adult, 32, null, 5, 4, 0, true) });
            var holdHeader = new SaveHeader(state.Header.SaveId, state.Header.SaveMode, state.Header.SchemaVersion, state.Header.GameBuildVersion,
                checked(state.Header.SaveRevision + 1), state.Header.CalendarDay, state.Header.CalendarProfileVersion, state.Header.CareMode,
                state.Header.ActiveVariantCatalogVersion, state.Header.ActiveInheritanceModelVersion, state.Header.DefaultOffspringResolverVersion,
                state.Header.RetainedResolverVersions, state.Header.RngStreams, state.Header.LastCommittedBreedingEventIndex,
                state.Header.MigrationHistory, "unwritten");
            var holdReplacement = new StableSnapshot(holdHeader, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance, state.CapacityRevision,
                state.Ledger, state.PendingCapacityPlans.Concat(new[] { nextHold }), state.Conceptions, state.DailyState,
                state.LegacyLifecycleBaselines);
            state = store.SaveSnapshot(holdReplacement, state.Header.SaveRevision);
            grownMare = state.Horses.Single(h => h.HorseId.Equals(foal.HorseId));
            var request2 = new ConceptionRequest(secondEvent, state.PlayerOwnerId, foal.HorseId, extraStallion.HorseId,
                state.Header.SaveRevision, grownMare.Revision, state.Horses.Single(h => h.HorseId.Equals(extraStallion.HorseId)).Revision,
                state.CapacityRevision, secondReservation, ConceptionTestData.Terms.Version);
            state = new AtomicSaveStore(SavePath).CommitConception(request2, ConceptionTestData.Service()).State;
            Assert.Equal(secondEvent, state.Horses.Single(h => h.HorseId.Equals(foal.HorseId)).ReproductionState.PregnancyId);
            while (state.Header.CalendarDay < 26)
            {
                var request = new EndDayRequest(new TransactionId(Guid.NewGuid()), state.PlayerOwnerId, state.Header.SaveRevision,
                    state.Header.CalendarDay, Plan(state), StableDailyPolicy.Version);
                var result = store.CommitEndDay(request, new StableDayService()); state = result.State;
            }
            var foalingMare = state.Horses.Single(h => h.HorseId.Equals(foal.HorseId));
            Assert.Null(foalingMare.ReproductionState.PregnancyId); Assert.Equal(28, foalingMare.ReproductionState.RecoveryUntilDay);
            Assert.Equal(LifeStage.Foal, state.Horses.Single(h => h.OriginCommitId != null && !h.HorseId.Equals(foal.HorseId)).LifeState.Stage);
            Assert.Equal(2, state.Conceptions.Count); Assert.Single(state.LegacyLifecycleBaselines);
            Assert.Equal(1, state.DailyState.Receipts.SelectMany(r => r.LifecycleEvents).Count(e => e.Kind == LifecycleEventKind.Birth));
            Assert.Equal(StableSnapshot.SchemaVersion, store.Load().State.Header.SchemaVersion);
        }

        [Fact]
        public void UnsupportedHistoricalWireVersionStillRequiresAnExplicitMigration()
        {
            var v1 = Fixture("old_static_initial_v1.clvr"); var unknown = (byte[])v1.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(unknown.AsSpan(8, 4), 0);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                sha.ComputeHash(unknown.AsSpan(StableSaveCodec.FrameHeaderBytes).ToArray()).CopyTo(unknown, 16);
            Assert.Throws<SaveCompatibilityException>(() => new StableSaveCodec().Decode(unknown));
        }
    }
}
