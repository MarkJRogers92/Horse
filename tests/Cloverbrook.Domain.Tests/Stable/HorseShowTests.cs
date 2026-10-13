using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-029 Coat and Turnout Show.
    public sealed class HorseShowTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-show-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private (AtomicSaveStore Store, StableSnapshot State) Starter()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(SavePath, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", "old_management_initial_v3.clvr")));
            var store = new AtomicSaveStore(SavePath); var initial = store.Load().State;
            return (store, store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision));
        }

        private static StableSnapshot EndDay(AtomicSaveStore store, StableSnapshot state, params HorseId[] groomed)
        {
            var plan = new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable
                && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, groomed.Contains(h.HorseId), groomed.Contains(h.HorseId))));
            var preview = new StableDayService().Preview(state, plan);
            Assert.True(preview.Allowed, preview.BlockReason);
            return store.CommitEndDay(preview.Receipt!.Request, new StableDayService()).State;
        }

        private static StableSnapshot With(StableSnapshot s, params CoinLedgerEntry[] extra) => new StableSnapshot(s.Header, s.PlayerOwnerId, s.Horses, s.OpeningCoinBalance,
            s.CapacityRevision, s.Ledger.Concat(extra), s.PendingCapacityPlans, s.Conceptions, s.DailyState, s.LegacyLifecycleBaselines);

        private static Phenotype Resolve(int days = 12, params (string, string, string)[] calls) =>
            new PhenotypeResolver().ResolveGenetics(FrozenFixtureData.Horse(FrozenFixtureData.With(calls), 100, days: days)).Phenotype!;

        [Fact]
        public void CoatScoringTable()
        {
            Assert.Equal(12, HorseShow.CoatScore(Resolve()));
            Assert.Equal(12, HorseShow.CoatScore(null));
            Assert.Equal(18, HorseShow.CoatScore(Resolve(12, ("slc45a2", "Cr", "N"))));
            Assert.Equal(21, HorseShow.CoatScore(Resolve(12, ("slc45a2", "Cr", "Cr"))));
            Assert.Equal(18, HorseShow.CoatScore(Resolve(12, ("tbx3", "D", "nd2"))));
            Assert.Equal(15, HorseShow.CoatScore(Resolve(12, ("tbx3", "nd1", "nd2"))));
            Assert.Equal(22, HorseShow.CoatScore(Resolve(12, ("pattern.eca3", "TO", "baseline"))));
            Assert.Equal(21, HorseShow.CoatScore(Resolve(12, ("pattern.eca3", "Rn", "baseline"))));
            Assert.Equal(24, HorseShow.CoatScore(Resolve(12, ("trpm1", "LP", "N"))));
            Assert.Equal(28, HorseShow.CoatScore(Resolve(12, ("trpm1", "LP", "N"), ("patn1", "PATN1", "N"))));
            Assert.Equal(20, HorseShow.CoatScore(Resolve(12, ("stx17", "G", "N"))));
            var light = Resolve(100, ("stx17", "G", "N"));
            Assert.True(light.GrayAmount >= 6000);
            Assert.Equal(24, HorseShow.CoatScore(light));
            var everything = Resolve(100, ("slc45a2", "Cr", "Cr"), ("pattern.eca3", "TO", "Rn"), ("trpm1", "LP", "N"), ("patn1", "PATN1", "N"), ("stx17", "G", "N"));
            Assert.Equal(40, HorseShow.CoatScore(everything));
        }

        [Fact]
        public void RivalsAreDeterministicAndScoresStayInRange()
        {
            var (_, state) = Starter();
            var rivals = HorseShow.RivalsThisWeek(state);
            Assert.Equal(HorseShow.Rivals, rivals.Count);
            Assert.Equal(rivals.Select(r => r.Horse.HorseId), HorseShow.RivalsThisWeek(state).Select(r => r.Horse.HorseId));
            Assert.Equal(rivals.Select(r => r.Score.Total), HorseShow.RivalsThisWeek(state).Select(r => r.Score.Total));
            Assert.Equal(rivals.Count, rivals.Select(r => r.Horse.Name).Distinct().Count());
            Assert.All(rivals, r =>
            {
                Assert.InRange(r.Score.Total, 0, 100); Assert.InRange(r.Score.Coat, 12, 40); Assert.InRange(r.Score.Grooming, 0, 25);
                Assert.InRange(r.Score.Bond, 6, 12); Assert.InRange(r.Score.Turnout, 0, 15); Assert.InRange(r.Score.Judge, 0, 5);
                Assert.False(string.IsNullOrEmpty(r.CoatLabel));
            });
            var horse = state.Horses.First(h => HorseShow.BlockReason(state, h.HorseId) == null).HorseId;
            var a = HorseShow.Preview(state, horse); var b = HorseShow.Preview(state, horse);
            Assert.Equal(a.Place, b.Place); Assert.Equal(a.Score.Total, b.Score.Total);
            Assert.Equal(HorseShow.Rivals + 1, a.Placings.Count);
            Assert.Equal(HorseShow.PrizeFor(a.Place), a.PrizeCoins);
            Assert.Equal(Enumerable.Range(1, HorseShow.Rivals + 1), a.Placings.Select(p => p.Place));
        }

        [Fact]
        public void EnteringPaysTheFeeAndPrizeOnceAndANewWeekReopens()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First(h => HorseShow.BlockReason(state, h.HorseId) == null).HorseId;
            var expected = HorseShow.Preview(state, horse);
            var (entered, outcome) = store.CommitShowEntry(state.PlayerOwnerId, horse, state.Header.SaveRevision, HorseShow.EntryFee);
            Assert.NotNull(outcome);
            Assert.Equal(expected.Place, outcome!.Place);
            Assert.Equal(state.Coins - HorseShow.EntryFee + outcome.PrizeCoins, entered.Coins);
            Assert.Equal(-HorseShow.EntryFee, entered.Ledger.Single(l => l.Kind == HorseShow.EntryLedgerKind).DeltaCoins);
            Assert.Equal(outcome.PrizeCoins > 0 ? 1 : 0, entered.Ledger.Count(l => l.Kind == HorseShow.PrizeLedgerKind));
            Assert.True(HorseShow.EnteredThisWeek(entered)); Assert.Equal(outcome.PrizeCoins, HorseShow.PrizeThisWeek(entered));
            var bytes = File.ReadAllBytes(SavePath);
            var (again, none) = new AtomicSaveStore(SavePath).CommitShowEntry(state.PlayerOwnerId, horse, state.Header.SaveRevision, HorseShow.EntryFee);
            Assert.Null(none); Assert.Equal(entered.Coins, again.Coins); Assert.Equal(bytes, File.ReadAllBytes(SavePath));
            Assert.Contains("already", HorseShow.BlockReason(entered, horse));
            var reloaded = new AtomicSaveStore(SavePath).Load().State;
            Assert.Equal(entered.Coins, reloaded.Coins);
            var later = reloaded;
            while (HorseFair.Week(later.Header.CalendarDay) == HorseFair.Week(reloaded.Header.CalendarDay)) later = EndDay(store, later);
            Assert.False(HorseShow.EnteredThisWeek(later));
            Assert.NotEqual(HorseShow.RivalsThisWeek(reloaded)[0].Horse.HorseId, HorseShow.RivalsThisWeek(later)[0].Horse.HorseId);
        }

        [Fact]
        public void StaleIneligibleOrRepricedEntriesAreRefused()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First().HorseId;
            Assert.Throws<StableCommandRejectedException>(() => store.CommitShowEntry(state.PlayerOwnerId, horse, state.Header.SaveRevision + 1, HorseShow.EntryFee));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitShowEntry(state.PlayerOwnerId, new HorseId(Guid.NewGuid()), state.Header.SaveRevision, HorseShow.EntryFee));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitShowEntry(state.PlayerOwnerId, horse, state.Header.SaveRevision, HorseShow.EntryFee + 1));
            var broke = new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, 0, state.CapacityRevision, state.Ledger,
                state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
            Assert.Contains("fee", HorseShow.BlockReason(broke, horse));
        }

        [Fact]
        public void ForgedShowLedgerEntriesAreRejected()
        {
            var (_, state) = Starter();
            var save = state.Header.SaveId;
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(HorseShow.PrizeId(save, 0), 120, HorseShow.PrizeLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(HorseShow.EntryId(save, 0), -15, HorseShow.EntryLedgerKind),
                new CoinLedgerEntry(HorseShow.PrizeId(save, 0), 999, HorseShow.PrizeLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(HorseShow.EntryId(save, 0), -1, HorseShow.EntryLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(HorseShow.EntryId(save, 40), -15, HorseShow.EntryLedgerKind)));
            // A cup prize id cannot stand in for a show prize.
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(HorseShow.EntryId(save, 0), -15, HorseShow.EntryLedgerKind),
                new CoinLedgerEntry(ClubRace.PrizeId(save, 0), 120, HorseShow.PrizeLedgerKind)));
            var ok = With(state, new CoinLedgerEntry(HorseShow.EntryId(save, 0), -15, HorseShow.EntryLedgerKind), new CoinLedgerEntry(HorseShow.PrizeId(save, 0), 70, HorseShow.PrizeLedgerKind));
            Assert.Equal(state.Coins + 55, ok.Coins);
        }

        [Fact]
        public void AGroomedFiveHeartTobianoBeatsAnUngroomedPlainBay()
        {
            var (store, state) = Starter();
            var adults = state.Horses.Where(h => HorseShow.IsEligibleHorse(state, h)).Take(2).ToList();
            Assert.Equal(2, adults.Count);
            HorseRecord Recoat(HorseRecord h, GenomeSnapshot genome, int bond) => new HorseRecord(h.HorseId, h.Name, h.Parentage, genome, h.ResolverVersion, h.AppearanceSeed,
                h.OwnerId, h.LifeState, h.ReproductionState, h.DevelopmentalFactors, h.InheritedGamePotential,
                new QuantizedVector(new[] { new System.Collections.Generic.KeyValuePair<string, int>(HorseBond.BondKey, bond) }), h.PlayerKnowledge, h.CosmeticState,
                h.HomeAssignment, h.OriginRecord, h.OriginCommitId, h.BirthDay, h.Revision);
            var fancy = Recoat(adults[0], FrozenFixtureData.With(("pattern.eca3", "TO", "baseline")), 9500);
            var plain = Recoat(adults[1], FrozenFixtureData.With(), 0);
            var swapped = state.Horses.Select(h => h.HorseId.Equals(fancy.HorseId) ? fancy : h.HorseId.Equals(plain.HorseId) ? plain : h);
            var prepared = new StableSnapshot(state.Header, state.PlayerOwnerId, swapped, state.OpeningCoinBalance, state.CapacityRevision, state.Ledger,
                state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
            store = new AtomicSaveStore(Path.Combine(directory, "custom.clover"));
            var day = store.Initialize(prepared);
            for (var i = 0; i < 8; i++) day = EndDay(store, day, fancy.HorseId);
            Assert.Equal(5, HorseBond.Hearts(day.Horses.Single(h => h.HorseId.Equals(fancy.HorseId))));
            var fancyScore = HorseShow.ScoreHorse(day, fancy.HorseId); var plainScore = HorseShow.ScoreHorse(day, plain.HorseId);
            Assert.Equal(22, fancyScore.Coat); Assert.Equal(12, plainScore.Coat);
            Assert.True(fancyScore.Grooming > plainScore.Grooming);
            Assert.Equal(15, fancyScore.Bond); Assert.Equal(0, plainScore.Bond);
            Assert.True(fancyScore.Total >= plainScore.Total + 20, $"{fancyScore.Total} vs {plainScore.Total}");
            // Care and bond make a common coat competitive too: the ungroomed plain bay finishes behind the groomed tobiano.
            Assert.True(HorseShow.Preview(day, fancy.HorseId).Place <= HorseShow.Preview(day, plain.HorseId).Place);
            Assert.True(HorseShow.Preview(day, fancy.HorseId).Place <= 3);
        }
    }
}
