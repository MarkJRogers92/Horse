using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class PersistenceBoundaryTests
    {
        private static StableSnapshot WithFounderFraction(Rational coefficient)
        {
            var original = ForecastTestData.Horse(1, ForecastTestData.Genome());
            var horse = new HorseRecord(original.HorseId, original.Name, new Parentage(founderBackground: new FounderBackground("fraction-boundary-fixture", coefficient)),
                original.Genome, original.ResolverVersion, original.AppearanceSeed, original.OwnerId, original.LifeState, original.ReproductionState);
            return new StableSnapshot(ConceptionTestData.Header(), ForecastTestData.Owner, new[] { horse }, 100, 0);
        }
        [Fact]
        public void TheLargestSupportedRationalComponentRoundTripsExactly()
        {
            var coefficient = new Rational(BigInteger.One, BigInteger.Pow(10, StableSaveCodec.MaxRationalTextBytes - 1));
            var codec = new StableSaveCodec(); var bytes = codec.Encode(WithFounderFraction(coefficient)); var loaded = codec.Decode(bytes);
            Assert.Equal(coefficient, loaded.Horses.Single().Parentage.FounderBackground!.InbreedingCoefficient);
            Assert.Equal(bytes, codec.Encode(loaded));
        }
        [Fact]
        public void TheWriterRejectsRationalsThatItsReaderCannotRepresent()
        {
            var coefficient = new Rational(BigInteger.One, BigInteger.Pow(10, StableSaveCodec.MaxRationalTextBytes));
            Assert.Throws<SaveIntegrityException>(() => new StableSaveCodec().Encode(WithFounderFraction(coefficient)));
        }
        [Fact]
        public void ChecksumValidZeroDenominatorsRemainRecoverableIntegrityErrors()
        {
            var codec = new StableSaveCodec(); var bytes = codec.Encode(WithFounderFraction(Rational.Half));
            var malformed = SaveCodecTests.Reframe(bytes, alterPayload: payload => {
                var marker = Encoding.UTF8.GetBytes("fraction-boundary-fixture");
                var at = Enumerable.Range(0, payload.Length - marker.Length + 1).Single(i => payload.AsSpan(i, marker.Length).SequenceEqual(marker));
                var numeratorLengthOffset = at + marker.Length;
                var numeratorLength = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(numeratorLengthOffset, 4));
                var denominatorLengthOffset = numeratorLengthOffset + 4 + numeratorLength;
                Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(denominatorLengthOffset, 4)));
                payload[denominatorLengthOffset + 4] = (byte)'0';
            });
            Assert.Throws<SaveIntegrityException>(() => codec.Decode(malformed));
        }
        private static SaveHeader AtRevision(SaveHeader h, ulong revision) => new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion,
            revision, h.CalendarDay, h.CalendarProfileVersion, h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion,
            h.DefaultOffspringResolverVersion, h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, "unwritten");
        [Fact]
        public void LaterUpdatesReplaceTheBackupWithTheLatestGoodState()
        {
            var directory = Path.Combine(Path.GetTempPath(), "cloverbrook-rolling-backup-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new AtomicSaveStore(Path.Combine(directory, "stable.clover")); store.Initialize(SaveTestData.Initial());
                var committed = store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service());
                var revisionOneBytes = File.ReadAllBytes(store.SavePath);
                var renamed = committed.State.WithHorses(committed.State.Horses.Select(h => h.Rename("Clover " + h.Name)))
                    .WithHeader(AtRevision(committed.State.Header, 2));
                var saved = store.SaveSnapshot(renamed, 1);
                Assert.Equal(2UL, saved.Header.SaveRevision); Assert.Equal(revisionOneBytes, File.ReadAllBytes(store.BackupPath));
                Assert.Equal(committed.Receipt.Commit.CommitHash, saved.Conceptions.Single().Commit.CommitHash);
                Assert.Equal(50, saved.Coins);
                var damaged = File.ReadAllBytes(store.SavePath); damaged[100] ^= 1; File.WriteAllBytes(store.SavePath, damaged);
                var candidate = store.Load(); Assert.True(candidate.NeedsRecoveryAcknowledgment); Assert.Equal(1UL, candidate.State.Header.SaveRevision);
                var recovered = store.RecoverFromBackup(candidate.State.Header.SaveId, 1, candidate.State.Header.PayloadHash);
                Assert.Equal(50, recovered.State.Coins); Assert.Equal(3, recovered.State.Horses.Count);
                Assert.Equal(committed.Receipt.Commit.CommitHash, recovered.State.Conceptions.Single().Commit.CommitHash);
                var retry = store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service());
                Assert.True(retry.Replayed); Assert.Equal(revisionOneBytes, File.ReadAllBytes(store.SavePath));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
