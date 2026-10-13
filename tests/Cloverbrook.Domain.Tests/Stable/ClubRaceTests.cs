using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-019 Willowbrook Cup.
    public sealed class ClubRaceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-cup-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private (AtomicSaveStore Store, StableSnapshot State) Starter()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(SavePath, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", "old_management_initial_v3.clvr")));
            var store = new AtomicSaveStore(SavePath); var initial = store.Load().State;
            return (store, store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision));
        }

        private static StableSnapshot EndDay(AtomicSaveStore store, StableSnapshot state)
        {
            var plan = new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable
                && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
            var preview = new StableDayService().Preview(state, plan);
            Assert.True(preview.Allowed, preview.BlockReason);
            return store.CommitEndDay(preview.Receipt!.Request, new StableDayService()).State;
        }

        [Fact]
        public void RivalsAreStableReleasedCoatsAndTheRaceIsDeterministic()
        {
            var (_, state) = Starter();
            var rivals = ClubRace.RivalsThisWeek(state);
            Assert.Equal(ClubRace.Rivals, rivals.Count);
            Assert.Equal(rivals.Select(r => r.Horse.HorseId), ClubRace.RivalsThisWeek(state).Select(r => r.Horse.HorseId));
            Assert.Equal(rivals.Count, rivals.Select(r => r.Horse.Name).Distinct().Count());
            var matrix = PhenotypeTests.Matrix();
            Assert.All(rivals, r => Assert.Equal(ResolutionStatus.Supported, new PhenotypeResolver().ResolveForPresentation(r.Horse, matrix, "a", "b").Status));
            var horse = state.Horses.First().HorseId;
            var a = ClubRace.Preview(state, horse); var b = ClubRace.Preview(state, horse);
            Assert.Equal(a.Race.Seed, b.Race.Seed); Assert.Equal(a.Place, b.Place); Assert.Equal(ClubRace.Rivals + 1, a.Race.Entrants.Count);
            Assert.Equal(ClubRace.PrizeFor(a.Place), a.PrizeCoins);
        }

        [Fact]
        public void EnteringPaysTheFeeAndPrizeOnceAndTrains()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First(h => ClubRace.BlockReason(state, h.HorseId) == null).HorseId;
            var expected = ClubRace.Preview(state, horse);
            var (raced, outcome) = store.CommitClubRace(state.PlayerOwnerId, horse, state.Header.SaveRevision);
            Assert.NotNull(outcome);
            Assert.Equal(expected.Place, outcome!.Place);
            Assert.Equal(state.Coins - ClubRace.EntryFee + outcome.PrizeCoins, raced.Coins);
            Assert.True(ClubRace.EnteredThisWeek(raced)); Assert.Equal(outcome.PrizeCoins, ClubRace.PrizeThisWeek(raced));
            Assert.True(PracticeTraining.Skill(raced.Horses.Single(h => h.HorseId.Equals(horse))) > 0);
            var bytes = File.ReadAllBytes(SavePath);
            var (again, none) = new AtomicSaveStore(SavePath).CommitClubRace(state.PlayerOwnerId, horse, state.Header.SaveRevision);
            Assert.Null(none); Assert.Equal(raced.Coins, again.Coins); Assert.Equal(bytes, File.ReadAllBytes(SavePath));
            Assert.Contains("already", ClubRace.BlockReason(raced, horse));
            var reloaded = new AtomicSaveStore(SavePath).Load().State;
            Assert.Equal(raced.Coins, reloaded.Coins);
            // A new week opens a new cup.
            var later = reloaded;
            while (HorseFair.Week(later.Header.CalendarDay) == HorseFair.Week(reloaded.Header.CalendarDay)) later = EndDay(store, later);
            Assert.False(ClubRace.EnteredThisWeek(later));
            Assert.NotEqual(ClubRace.RivalsThisWeek(reloaded)[0].Horse.HorseId, ClubRace.RivalsThisWeek(later)[0].Horse.HorseId);
        }

        [Fact]
        public void StaleOrIneligibleEntriesAreRefused()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First().HorseId;
            Assert.Throws<StableCommandRejectedException>(() => store.CommitClubRace(state.PlayerOwnerId, horse, state.Header.SaveRevision + 1));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitClubRace(state.PlayerOwnerId, new HorseId(Guid.NewGuid()), state.Header.SaveRevision));
            var broke = new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, 0, state.CapacityRevision, state.Ledger,
                state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
            Assert.Contains("fee", ClubRace.BlockReason(broke, horse));
        }

        [Fact]
        public void ForgedCupLedgerEntriesAreRejected()
        {
            var (store, state) = Starter();
            StableSnapshot With(params CoinLedgerEntry[] extra) => new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance,
                state.CapacityRevision, state.Ledger.Concat(extra), state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
            var save = state.Header.SaveId;
            Assert.Throws<SaveIntegrityException>(() => With(new CoinLedgerEntry(ClubRace.PrizeId(save, 0), 160, ClubRace.PrizeLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(new CoinLedgerEntry(ClubRace.EntryId(save, 0), -20, ClubRace.EntryLedgerKind),
                new CoinLedgerEntry(ClubRace.PrizeId(save, 0), 999, ClubRace.PrizeLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(new CoinLedgerEntry(ClubRace.EntryId(save, 40), -20, ClubRace.EntryLedgerKind)));
            var ok = With(new CoinLedgerEntry(ClubRace.EntryId(save, 0), -20, ClubRace.EntryLedgerKind), new CoinLedgerEntry(ClubRace.PrizeId(save, 0), 90, ClubRace.PrizeLedgerKind));
            Assert.Equal(state.Coins + 70, ok.Coins);
            Assert.NotNull(store);
        }
    }
}
