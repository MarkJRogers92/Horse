using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class SaveCodecTests
    {
        internal static byte[] Reframe(byte[] frame, int? wireVersion = null, Action<byte[]>? alterPayload = null)
        {
            var copy = (byte[])frame.Clone(); if (wireVersion.HasValue) BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(8, 4), wireVersion.Value);
            var payload = copy.AsSpan(StableSaveCodec.FrameHeaderBytes).ToArray(); alterPayload?.Invoke(payload);
            payload.CopyTo(copy, StableSaveCodec.FrameHeaderBytes); SHA256.HashData(payload).CopyTo(copy, 16); return copy;
        }
        [Fact]
        public void ACommittedFoalAndEveryTransactionEffectSurviveByteStableRoundTrip()
        {
            var state = SaveTestData.Commit().State; var codec = new StableSaveCodec(); var bytes = codec.Encode(state); var loaded = codec.Decode(bytes);
            Assert.Equal(bytes, codec.Encode(loaded)); Assert.Equal(state.Header.SaveId, loaded.Header.SaveId); Assert.Equal(50, loaded.Coins);
            Assert.Equal(1UL, loaded.Header.SaveRevision); Assert.Equal(1UL, loaded.CapacityRevision); Assert.Equal(1UL, loaded.Header.RngStreams.Breeding.EventCounter);
            var before = state.Conceptions.Single(); var after = loaded.Conceptions.Single();
            Assert.Equal(before.Commit.CommitHash, after.Commit.CommitHash); Assert.Equal(before.ReceiptHash, after.ReceiptHash);
            Assert.Equal(before.Commit.OffspringGenome.GenomeHash, after.Commit.OffspringGenome.GenomeHash);
            Assert.Equal(before.Commit.OffspringAppearanceSeed, after.Commit.OffspringAppearanceSeed);
            Assert.Equal(before.Commit.GameteA.ProvenanceHash, after.Commit.GameteA.ProvenanceHash); Assert.Equal(before.Commit.GameteB.ProvenanceHash, after.Commit.GameteB.ProvenanceHash);
            Assert.Equal(before.UnbornAtCommit.DevelopmentalFactors.Hash, after.UnbornAtCommit.DevelopmentalFactors.Hash);
            Assert.Equal(before.UnbornAtCommit.InheritedGamePotential.Hash, after.UnbornAtCommit.InheritedGamePotential.Hash);
            Assert.Equal(64, loaded.Header.PayloadHash.Length); Assert.NotEqual("unwritten", loaded.Header.PayloadHash);
            var retry = ConceptionTransaction.Apply(loaded, ConceptionTestData.Request(), ConceptionTestData.Service());
            Assert.True(retry.Replayed); Assert.Equal(bytes, codec.Encode(retry.State));
        }
        [Fact]
        public void PhaseHiddenKnowledgeCosmeticsTrainingAndUnicodeNamesRoundTrip()
        {
            var g = ForecastTestData.Genome(("slc45a2", "Cr", "Prl"), ("game.marker.00", "1", "0"), ("game.marker.01", "0", "1"));
            var original = ForecastTestData.Horse(1, g); var potential = GameTraitResolver.Resolve(g, QuantizedVector.Empty).Inherited;
            var horse = new HorseRecord(original.HorseId, "Étoile — 小马", new Parentage(founderBackground: new FounderBackground("declared", new Rational(1, 32))),
                g, original.ResolverVersion, new Seed128(123, 456), original.OwnerId, original.LifeState, original.ReproductionState,
                inheritedGamePotential: potential, trainingState: new QuantizedVector(new[] { new KeyValuePair<string, int>("turning", 1234) }),
                playerKnowledge: new PlayerKnowledgeRecord(new[] { new KeyValuePair<string, AllelePair>("mc1r", new AllelePair("e", "e")) }, new[] { ForecastTestData.Id(9) }),
                cosmeticState: new[] { new KeyValuePair<string, string>("ribbon", "lavender") }, birthDay: 0, revision: 7);
            var state = new StableSnapshot(ConceptionTestData.Header(), ForecastTestData.Owner, new[] { horse }, 123, 0);
            var codec = new StableSaveCodec(); var bytes = codec.Encode(state); var copy = codec.Decode(bytes).Horses.Single();
            Assert.Equal(bytes, codec.Encode(codec.Decode(bytes))); Assert.Equal(horse.Name, copy.Name); Assert.Equal(horse.Genome.GenomeHash, copy.Genome.GenomeHash);
            Assert.Equal(horse.Genome.Blocks.SelectMany(b => b.HomologA), copy.Genome.Blocks.SelectMany(b => b.HomologA));
            Assert.Equal(horse.PlayerKnowledge.KnowledgeHash, copy.PlayerKnowledge.KnowledgeHash); Assert.Single(copy.PlayerKnowledge.RevealedCalls);
            Assert.Equal(1234, copy.TrainingState.Get("turning")); Assert.Equal("lavender", copy.CosmeticState["ribbon"]);
            Assert.Equal(new Rational(1, 32), copy.Parentage.FounderBackground!.InbreedingCoefficient); Assert.Equal(7UL, copy.Revision);
        }
        [Fact]
        public void HistoricalParentRevisionsDoNotGetReconstructedFromCurrentNames()
        {
            var state = SaveTestData.Commit().State; state = state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(ForecastTestData.Id(1)) ? h.Rename("New name") : h));
            var codec = new StableSaveCodec(); var loaded = codec.Decode(codec.Encode(state));
            Assert.Equal("New name", loaded.Horses.Single(h => h.HorseId.Equals(ForecastTestData.Id(1))).Name);
            Assert.Equal(0UL, loaded.Conceptions.Single().ParentAAtCommit.Revision); Assert.Equal(state.Conceptions.Single().Commit.CommitHash, loaded.Conceptions.Single().Commit.CommitHash);
        }
        [Theory]
        [InlineData(0)] [InlineData(12)] [InlineData(16)] [InlineData(100)]
        public void DamagedFramesAreRejectedInsteadOfPartiallyLoaded(int offset)
        {
            var codec = new StableSaveCodec(); var bytes = codec.Encode(SaveTestData.Initial()); bytes[offset] ^= 0x7f;
            Assert.Throws<SaveIntegrityException>(() => codec.Decode(bytes));
        }
        [Fact]
        public void TruncatedAndTrailingDataNeverProduceAValidSave()
        {
            var codec = new StableSaveCodec(); var bytes = codec.Encode(SaveTestData.Initial());
            Assert.Throws<SaveIntegrityException>(() => codec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
            Assert.Throws<SaveIntegrityException>(() => codec.Decode(bytes.Concat(new byte[] { 1 }).ToArray()));
            Assert.Throws<SaveIntegrityException>(() => codec.Decode(new byte[4]));
        }
        [Fact]
        public void ChecksumValidHostileLengthsFailBeforeAllocation()
        {
            var codec = new StableSaveCodec(); var bytes = Reframe(codec.Encode(SaveTestData.Initial()), alterPayload: payload => BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), int.MaxValue));
            Assert.Throws<SaveIntegrityException>(() => codec.Decode(bytes));
        }
        [Fact]
        public void FutureWireAndPayloadSchemasRequireCompatibilityNotBackupRollback()
        {
            var codec = new StableSaveCodec(); var bytes = codec.Encode(SaveTestData.Initial());
            Assert.Throws<SaveCompatibilityException>(() => codec.Decode(Reframe(bytes, 3)));
            var futurePayload = Reframe(bytes, alterPayload: p => {
                var needle = Encoding.UTF8.GetBytes(StableSnapshot.SchemaVersion); var at = Enumerable.Range(0, p.Length - needle.Length + 1).First(i => p.AsSpan(i, needle.Length).SequenceEqual(needle));
                p[at + needle.Length - 1] = (byte)'9'; });
            Assert.Throws<SaveCompatibilityException>(() => codec.Decode(futurePayload));
        }
        private sealed class TestOnlyLegacyEnvelopeMigration : ISavePayloadMigration
        {
            public string Id => "TEST-ONLY.same-payload-envelope.0-to-1";
            public int FromWireVersion => 0; public int ToWireVersion => StableSaveCodec.WireVersion;
            public byte[] Upgrade(byte[] verifiedOldPayload) => verifiedOldPayload;
        }
        [Fact]
        public void LegacyMigrationMustBeExplicitAndRecordedWithoutRerolling()
        {
            var state = SaveTestData.Commit().State; var plain = new StableSaveCodec(); var legacy = Reframe(plain.Encode(state), 0);
            Assert.Throws<SaveCompatibilityException>(() => plain.Decode(legacy));
            var migrator = new StableSaveCodec(new[] { new TestOnlyLegacyEnvelopeMigration() }); var loaded = migrator.Decode(legacy);
            Assert.Single(loaded.Header.MigrationHistory); Assert.Contains("TEST-ONLY", loaded.Header.MigrationHistory[0]);
            Assert.Equal(state.Conceptions[0].Commit.CommitHash, loaded.Conceptions[0].Commit.CommitHash);
            Assert.Equal(state.Conceptions[0].Commit.OffspringAppearanceSeed, loaded.Conceptions[0].Commit.OffspringAppearanceSeed);
            var again = plain.Decode(migrator.Encode(loaded)); Assert.Single(again.Header.MigrationHistory); Assert.Equal(loaded.Header.PayloadHash, again.Header.PayloadHash);
        }
        [Fact]
        public void EncodingRejectsOversizedStringsRatherThanCreatingUnreadableSaves()
        {
            var state = SaveTestData.Initial(); state = state.WithHorses(state.Horses.Select(h => h.Rename(new string('x', StableSaveCodec.MaxTextBytes + 1))));
            Assert.Throws<SaveIntegrityException>(() => new StableSaveCodec().Encode(state));
        }
        [Fact]
        public void DeterministicEncodingDoesNotDependOnCurrentCulture()
        {
            var state = SaveTestData.Commit().State; var codec = new StableSaveCodec(); var expected = codec.Encode(state); var previous = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Assert.Equal(expected, codec.Encode(codec.Decode(expected))); }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
