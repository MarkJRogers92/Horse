using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class AtomicSaveStoreTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-save-test-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        private AtomicSaveStore Store(Action<SaveWriteStage>? fault = null) => new AtomicSaveStore(SavePath, faultInjector: fault);
        private static void FailAt(SaveWriteStage actual, SaveWriteStage target) { if (actual == target) throw new IOException("Injected interrupted save"); }
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Fact]
        public void DurableConceptionSurvivesRestartAndRetryWithoutAnotherChargeOrFoal()
        {
            var store = Store(); var initial = store.Initialize(SaveTestData.Initial()); var originalBytes = File.ReadAllBytes(SavePath);
            var committed = store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service());
            Assert.True(committed.IsDurablyCommitted); Assert.False(committed.Replayed); Assert.Equal(50, committed.State.Coins);
            Assert.Equal(originalBytes, File.ReadAllBytes(store.BackupPath));
            var committedBytes = File.ReadAllBytes(SavePath); var restarted = Store(); var loaded = restarted.Load();
            Assert.False(loaded.NeedsRecoveryAcknowledgment); Assert.Equal(3, loaded.State.Horses.Count); Assert.Equal(1UL, loaded.State.Header.SaveRevision);
            var retry = restarted.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service());
            Assert.True(retry.Replayed); Assert.Equal(committed.Receipt.Commit.CommitHash, retry.Receipt.Commit.CommitHash); Assert.Equal(committedBytes, File.ReadAllBytes(SavePath));
            Assert.Equal(originalBytes, File.ReadAllBytes(store.BackupPath)); Assert.Equal(initial.Header.SaveId, retry.State.Header.SaveId);
        }
        [Theory]
        [InlineData(SaveWriteStage.TemporaryPartWritten)] [InlineData(SaveWriteStage.TemporaryWritten)]
        [InlineData(SaveWriteStage.TemporaryFlushed)] [InlineData(SaveWriteStage.BeforeReplace)]
        public void InterruptionBeforePublicationLeavesTheOriginalSaveUntouched(SaveWriteStage stage)
        {
            Store().Initialize(SaveTestData.Initial()); var original = File.ReadAllBytes(SavePath);
            Assert.Throws<IOException>(() => Store(s => FailAt(s, stage)).CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service()));
            Assert.Equal(original, File.ReadAllBytes(SavePath)); Assert.Equal(100, Store().Load().State.Coins); Assert.Empty(Store().Load().State.Conceptions);
            var result = Store().CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service());
            Assert.Equal(50, result.State.Coins); Assert.Equal(3, result.State.Horses.Count); Assert.Equal(1UL, result.State.Header.RngStreams.Breeding.EventCounter);
        }
        [Fact]
        public void InterruptionAfterPublicationIsAnIdempotentSuccessOnRetry()
        {
            Store().Initialize(SaveTestData.Initial());
            Assert.Throws<IOException>(() => Store(s => FailAt(s, SaveWriteStage.AfterReplace)).CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service()));
            var bytes = File.ReadAllBytes(SavePath); var loaded = Store().Load().State; Assert.Equal(50, loaded.Coins); Assert.Single(loaded.Conceptions);
            var retry = Store().CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service());
            Assert.True(retry.Replayed); Assert.Equal(bytes, File.ReadAllBytes(SavePath)); Assert.Single(retry.State.Ledger);
        }
        [Fact]
        public void FirstWriteInterruptionDoesNotCreateAPartialPrimaryOrEraseAnything()
        {
            Assert.Throws<IOException>(() => Store(s => FailAt(s, SaveWriteStage.TemporaryPartWritten)).Initialize(SaveTestData.Initial()));
            Assert.False(File.Exists(SavePath)); Assert.False(File.Exists(Store().BackupPath));
            Store().Initialize(SaveTestData.Initial()); Assert.Equal(100, Store().Load().State.Coins);
        }
        [Fact]
        public void CorruptPrimaryRequiresAcknowledgmentAndKeepsItsBackupAndEvidence()
        {
            var store = Store(); store.Initialize(SaveTestData.Initial()); store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service());
            var backupBytes = File.ReadAllBytes(store.BackupPath); var corrupt = File.ReadAllBytes(SavePath); corrupt[100] ^= 1; File.WriteAllBytes(SavePath, corrupt);
            var offered = store.Load(); Assert.True(offered.NeedsRecoveryAcknowledgment); Assert.Equal(0UL, offered.State.Header.SaveRevision);
            Assert.Throws<SaveRecoveryRequiredException>(() => store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service()));
            Assert.Equal(corrupt, File.ReadAllBytes(SavePath));
            Assert.Throws<SaveIntegrityException>(() => store.RecoverFromBackup(offered.State.Header.SaveId, 999, offered.State.Header.PayloadHash));
            var restored = store.RecoverFromBackup(offered.State.Header.SaveId, offered.State.Header.SaveRevision, offered.State.Header.PayloadHash);
            Assert.False(restored.NeedsRecoveryAcknowledgment); Assert.NotNull(restored.QuarantinedPath); Assert.Equal(corrupt, File.ReadAllBytes(restored.QuarantinedPath!));
            Assert.Equal(backupBytes, File.ReadAllBytes(store.BackupPath)); Assert.Equal(backupBytes, File.ReadAllBytes(SavePath));
            Assert.Equal(100, restored.State.Coins); Assert.Empty(restored.State.Conceptions);
        }
        [Fact]
        public void AHealthyPrimaryCannotBeRolledBackToAnOlderBackup()
        {
            var store = Store(); var original = store.Initialize(SaveTestData.Initial()); store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service());
            var current = File.ReadAllBytes(SavePath);
            Assert.Throws<InvalidOperationException>(() => store.RecoverFromBackup(original.Header.SaveId, original.Header.SaveRevision, original.Header.PayloadHash));
            Assert.Equal(current, File.ReadAllBytes(SavePath));
        }
        [Fact]
        public void FuturePrimaryFormatsAreNotMisdiagnosedAsCorruptionOrRolledBack()
        {
            var store = Store(); var original = store.Initialize(SaveTestData.Initial()); store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service());
            var future = SaveCodecTests.Reframe(File.ReadAllBytes(SavePath), 99); File.WriteAllBytes(SavePath, future);
            Assert.Throws<SaveCompatibilityException>(() => store.Load());
            Assert.Throws<SaveCompatibilityException>(() => store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service()));
            Assert.Throws<SaveCompatibilityException>(() => store.RecoverFromBackup(original.Header.SaveId, original.Header.SaveRevision, original.Header.PayloadHash));
            Assert.Equal(future, File.ReadAllBytes(SavePath));
        }
        [Fact]
        public void NeitherCorruptCopyCreatesAFreshStableAndMissingPrimaryUsesAnExplicitOffer()
        {
            var store = Store(); store.Initialize(SaveTestData.Initial()); store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service());
            File.Delete(SavePath); Assert.True(store.Load().NeedsRecoveryAcknowledgment);
            File.WriteAllBytes(SavePath, new byte[] { 1, 2 }); File.WriteAllBytes(store.BackupPath, new byte[] { 3, 4 });
            Assert.Throws<SaveIntegrityException>(() => store.Load()); Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(SavePath));
            Assert.Throws<InvalidOperationException>(() => store.Initialize(SaveTestData.Initial()));
        }
        [Fact]
        public void ExclusiveWriterLockAndOptimisticRevisionRejectLostUpdates()
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial());
            using (var held = new FileStream(store.LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<SaveBusyException>(() => Store().CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service()));
            var candidate = before.WithHorses(before.Horses.Select(h => h.Rename("Clover"))).WithHeader(ConceptionTestData.Header(1));
            store.SaveSnapshot(candidate, 0); var updated = File.ReadAllBytes(SavePath);
            Assert.Equal("stale_save", Assert.Throws<ConceptionRejectedException>(() => Store().SaveSnapshot(candidate, 0)).Code);
            Assert.Equal(updated, File.ReadAllBytes(SavePath)); Assert.True(File.Exists(store.LockPath));
        }
        [Fact]
        public void GenericSavingCannotBypassConceptionOrChangeFounderInheritance()
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial());
            Assert.Throws<SaveIntegrityException>(() => store.SaveSnapshot(SaveTestData.Commit(before).State, 0));
            var altered = before.WithHorses(new[] { ForecastTestData.Horse(1, ForecastTestData.Genome()), before.Horses[1] }).WithHeader(ConceptionTestData.Header(1));
            Assert.Throws<SaveIntegrityException>(() => store.SaveSnapshot(altered, 0)); Assert.Equal(0UL, store.Load().State.Header.SaveRevision);
        }
        [Fact]
        public void RejectedConceptionAndConflictingRetryDoNotTouchDiskOrBackup()
        {
            var store = Store(); store.Initialize(SaveTestData.Initial()); var before = File.ReadAllBytes(SavePath);
            Assert.Throws<ConceptionRejectedException>(() => store.CommitConception(ConceptionTestData.Request(expectedSaveRevision: 1), ConceptionTestData.Service()));
            Assert.Equal(before, File.ReadAllBytes(SavePath)); Assert.False(File.Exists(store.BackupPath));
            store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service()); var after = File.ReadAllBytes(SavePath);
            Assert.Equal("idempotency_conflict", Assert.Throws<ConceptionRejectedException>(() => store.CommitConception(ConceptionTestData.Request(expectedSaveRevision: 1), ConceptionTestData.Service())).Code);
            Assert.Equal(after, File.ReadAllBytes(SavePath)); Assert.Equal(before, File.ReadAllBytes(store.BackupPath));
        }
        [Fact]
        public void AbandonedTemporariesNeverOverrideACommittedSave()
        {
            var store = Store(); store.Initialize(SaveTestData.Initial());
            File.WriteAllBytes(SavePath + ".tmp.abandoned", new StableSaveCodec().Encode(SaveTestData.Commit().State));
            Assert.Equal(100, store.Load().State.Coins); Assert.Empty(store.Load().State.Conceptions);
        }
        [Fact]
        public void ForecastToDiskToRestartPreservesTheChosenGeneticOutcome()
        {
            var initial = SaveTestData.Initial(); var matrix = ForecastTestData.TestMatrix("chestnut", "palomino", "cremello");
            var forecasts = new CoatForecastService(matrix); var a = initial.Horses[0]; var b = initial.Horses[1];
            var forecast = forecasts.ForKnownParents(ForecastTestData.Known(1, a.Genome), ForecastTestData.Known(2, b.Genome), PublicCoatScope.AllSupported());
            var store = Store(); store.Initialize(initial); var saved = store.CommitConception(ConceptionTestData.Request(), ConceptionTestData.Service(matrix));
            var foal = saved.Receipt.UnbornAtCommit; var label = PhenotypeResolver.VisibleLabel(new PhenotypeResolver().ResolveGenetics(foal).Phenotype!);
            Assert.Contains(forecast.Outcomes, o => o.Label == label && o.ExactProbability!.Value > Rational.Zero);
            forecasts.Hypothetical(a.Genome, b.Genome); var loaded = Store().Load().State; var original = loaded.Conceptions.Single().UnbornAtCommit;
            Assert.Equal(foal.Genome.GenomeHash, original.Genome.GenomeHash); Assert.Equal(foal.AppearanceSeed, original.AppearanceSeed);
            Assert.Equal(foal.DevelopmentalFactors.Hash, original.DevelopmentalFactors.Hash); Assert.Equal(foal.Parentage.ParentAId, original.Parentage.ParentAId);
            Assert.Equal(saved.Receipt.Commit.CommitHash, loaded.Conceptions.Single().Commit.CommitHash);
        }
    }
}
