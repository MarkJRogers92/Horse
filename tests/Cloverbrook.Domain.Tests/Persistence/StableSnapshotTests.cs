using System;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    internal static class SaveTestData
    {
        internal static StableSnapshot Initial(long coins = 100) => new StableSnapshot(ConceptionTestData.Header(), ForecastTestData.Owner,
            ConceptionTestData.Parents(), coins, 0, pendingCapacityPlans: new[] { ConceptionTestData.Capacity() });
        internal static ConceptionStateChange Commit(StableSnapshot? state = null) => ConceptionTransaction.Apply(state ?? Initial(), ConceptionTestData.Request(), ConceptionTestData.Service());
    }
    public sealed class StableSnapshotTests
    {
        [Fact]
        public void AllConceptionEffectsAppearInOneReplacementSnapshot()
        {
            var before = SaveTestData.Initial(); var change = SaveTestData.Commit(before); var after = change.State;
            Assert.False(change.Replayed); Assert.Equal(2, before.Horses.Count); Assert.Equal(100, before.Coins); Assert.Empty(before.Conceptions);
            Assert.Equal(3, after.Horses.Count); Assert.Equal(50, after.Coins); Assert.Single(after.Conceptions); Assert.Single(after.Ledger); Assert.Empty(after.PendingCapacityPlans);
            Assert.Equal(1UL, after.Header.SaveRevision); Assert.Equal(1UL, after.CapacityRevision); Assert.Equal(1UL, after.Header.RngStreams.Breeding.EventCounter);
            Assert.Equal(0UL, after.Header.RngStreams.World.EventCounter); Assert.Equal(0UL, after.Header.RngStreams.Cosmetic.EventCounter); Assert.Equal(0UL, after.Header.RngStreams.Competition.EventCounter);
            Assert.Equal(change.Receipt.Commit.BreedingCommitId, after.Horses.Single(h => h.HorseId.Equals(ForecastTestData.Id(1))).ReproductionState.PregnancyId);
        }
        [Fact]
        public void IdenticalRetryReturnsTheSameReceiptBeforeStaleChecks()
        {
            var first = SaveTestData.Commit(); var retry = ConceptionTransaction.Apply(first.State, ConceptionTestData.Request(), ConceptionTestData.Service(ForecastTestData.Unreleased()));
            Assert.True(retry.Replayed); Assert.Same(first.State, retry.State); Assert.Same(first.Receipt, retry.Receipt); Assert.Equal(50, retry.State.Coins);
        }
        [Fact]
        public void ReusingACommandIdForDifferentInputsIsNotAReplay()
        {
            var first = SaveTestData.Commit();
            var exception = Assert.Throws<ConceptionRejectedException>(() => ConceptionTransaction.Apply(first.State,
                ConceptionTestData.Request(expectedSaveRevision: 1), ConceptionTestData.Service()));
            Assert.Equal("idempotency_conflict", exception.Code); Assert.Equal(50, first.State.Coins);
        }
        [Fact]
        public void RejectionLeavesTheOriginalSnapshotUntouched()
        {
            var before = SaveTestData.Initial(49);
            Assert.Throws<ConceptionRejectedException>(() => SaveTestData.Commit(before));
            Assert.Equal(49, before.Coins); Assert.Empty(before.Conceptions); Assert.Single(before.PendingCapacityPlans); Assert.Equal(0UL, before.Header.RngStreams.Breeding.EventCounter);
        }
        [Fact]
        public void AReceiptCannotExistWithoutItsDebitPregnancyOrUnbornHorse()
        {
            var state = SaveTestData.Commit().State;
            Assert.Throws<SaveIntegrityException>(() => new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance,
                state.CapacityRevision, Array.Empty<CoinLedgerEntry>(), state.PendingCapacityPlans, state.Conceptions));
            Assert.Throws<SaveIntegrityException>(() => state.WithHorses(ConceptionTestData.Parents()));
            Assert.Throws<SaveIntegrityException>(() => state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(ForecastTestData.Id(1)) ? ConceptionTestData.Parents()[0] : h)));
        }
        [Fact]
        public void DuplicateTransactionsAndOrphanPregnanciesAreRejected()
        {
            var state = SaveTestData.Commit().State;
            Assert.Throws<SaveIntegrityException>(() => new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance,
                state.CapacityRevision, state.Ledger.Concat(state.Ledger), state.PendingCapacityPlans, state.Conceptions));
            var initial = SaveTestData.Initial();
            Assert.Throws<SaveIntegrityException>(() => initial.WithHorses(initial.Horses.Concat(initial.Horses)));
        }
        [Fact]
        public void CurrentNamesCanChangeWithoutRewritingHistoricalParentSnapshots()
        {
            var state = SaveTestData.Commit().State; var original = state.Conceptions[0].ParentAAtCommit;
            var renamed = state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(original.HorseId) ? h.Rename("Clover") : h));
            Assert.Equal("Clover", renamed.Horses.Single(h => h.HorseId.Equals(original.HorseId)).Name);
            Assert.Equal(original.Name, renamed.Conceptions[0].ParentAAtCommit.Name); Assert.Equal(0UL, renamed.Conceptions[0].ParentAAtCommit.Revision);
            Assert.Equal(state.Conceptions[0].Commit.CommitHash, renamed.Conceptions[0].Commit.CommitHash);
        }
    }
}
