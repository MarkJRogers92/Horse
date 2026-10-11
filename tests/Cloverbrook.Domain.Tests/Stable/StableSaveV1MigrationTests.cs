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
            Assert.Equal("stable.daily-care-root.v1-to-v2", restarted.Header.MigrationHistory.Single());
            Assert.Equal(originalCommitHash, restarted.Conceptions.Single().Commit.CommitHash);
            Assert.Equal(ended.Receipt.ReceiptHash, restarted.DailyState.Receipts.Single().ReceiptHash);
            Assert.Equal(savedBytes, new StableSaveCodec().Encode(restarted));
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
