using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-028 DNA tests at the vet.
    public sealed class DnaTestTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-dna-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private (AtomicSaveStore Store, StableSnapshot State) Starter()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(SavePath, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", "old_management_initial_v3.clvr")));
            var store = new AtomicSaveStore(SavePath); var initial = store.Load().State;
            return (store, store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision));
        }

        private static HorseRecord WithKnowledge(HorseRecord h, IEnumerable<KeyValuePair<string, AllelePair>> calls) => new HorseRecord(h.HorseId, h.Name, h.Parentage,
            h.Genome, h.ResolverVersion, h.AppearanceSeed, h.OwnerId, h.LifeState, h.ReproductionState, h.DevelopmentalFactors, h.InheritedGamePotential,
            h.TrainingState, new PlayerKnowledgeRecord(calls, h.PlayerKnowledge.KnownAncestors), h.CosmeticState, h.HomeAssignment, h.OriginRecord,
            h.OriginCommitId, h.BirthDay, h.Revision);

        private static StableSnapshot Replace(StableSnapshot s, IEnumerable<HorseRecord> horses, IEnumerable<CoinLedgerEntry> ledger) => new StableSnapshot(
            s.Header, s.PlayerOwnerId, horses, s.OpeningCoinBalance, s.CapacityRevision, ledger, s.PendingCapacityPlans, s.Conceptions, s.DailyState, s.LegacyLifecycleBaselines);

        private static HorseRecord Untested(StableSnapshot s) => s.Horses.First(h => DnaTest.BlockReason(s, h.HorseId) == null);

        [Fact]
        public void CommitRevealsExactCallsAndDebitsOnce()
        {
            var (store, state) = Starter();
            var horse = Untested(state);
            Assert.False(DnaTest.IsTested(horse));
            var tested = store.CommitDnaTest(state.PlayerOwnerId, horse.HorseId, state.Header.SaveRevision, DnaTest.Price);
            Assert.Equal(state.Coins - 60, tested.Coins);
            var after = tested.Horses.Single(h => h.HorseId.Equals(horse.HorseId));
            Assert.Equal(horse.Revision + 1, after.Revision);
            foreach (var locus in DnaTest.Loci.Where(horse.Genome.HasLocus))
            {
                var truth = horse.Genome.GetAlleles(locus); var call = after.PlayerKnowledge.RevealedCalls[locus];
                Assert.True(call.A == truth.A && call.B == truth.B, locus);
            }
            Assert.Equal(horse.PlayerKnowledge.KnownAncestors, after.PlayerKnowledge.KnownAncestors);
            Assert.Contains("already", DnaTest.BlockReason(tested, horse.HorseId));
            // A retry is idempotent; a second attempt with a fresh revision is refused too.
            Assert.Equal(tested.Coins, store.CommitDnaTest(state.PlayerOwnerId, horse.HorseId, state.Header.SaveRevision, DnaTest.Price).Coins);
            Assert.Throws<StableCommandRejectedException>(() => store.CommitDnaTest(state.PlayerOwnerId, Untested(tested).HorseId, tested.Header.SaveRevision, 10));
        }

        [Fact]
        public void StrangersAndPoorPlayersAreTurnedAway()
        {
            var (_, state) = Starter();
            Assert.Contains("not in your stable", DnaTest.BlockReason(state, new HorseId(Guid.NewGuid())));
            var poor = new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, 10 - state.Ledger.Sum(l => l.DeltaCoins), state.CapacityRevision,
                state.Ledger, state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
            Assert.True(poor.Coins < DnaTest.Price);
            Assert.Contains("60 coins", DnaTest.BlockReason(poor, state.Horses.First(h => h.LifeState.Stage != LifeStage.Unborn).HorseId));
        }

        [Fact]
        public void RevealedCallsSurviveReload()
        {
            var (store, state) = Starter();
            var horse = Untested(state);
            var tested = store.CommitDnaTest(state.PlayerOwnerId, horse.HorseId, state.Header.SaveRevision, DnaTest.Price);
            var reloaded = new AtomicSaveStore(SavePath).Load().State;
            var before = tested.Horses.Single(h => h.HorseId.Equals(horse.HorseId)); var after = reloaded.Horses.Single(h => h.HorseId.Equals(horse.HorseId));
            Assert.Equal(before.PlayerKnowledge.KnowledgeHash, after.PlayerKnowledge.KnowledgeHash);
            Assert.True(after.PlayerKnowledge.RevealedCalls.Count >= 1);
            Assert.True(DnaTest.IsTested(after));
        }

        [Fact]
        public void ForgedCallsAndOrphanDebitsAreRejected()
        {
            var (store, state) = Starter();
            var horse = Untested(state);
            var tested = store.CommitDnaTest(state.PlayerOwnerId, horse.HorseId, state.Header.SaveRevision, DnaTest.Price);
            var good = tested.Horses.Single(h => h.HorseId.Equals(horse.HorseId));
            // A debit without a tested horse.
            Assert.Throws<SaveIntegrityException>(() => Replace(tested, state.Horses, tested.Ledger));
            // A wrong amount.
            Assert.Throws<SaveIntegrityException>(() => Replace(tested, tested.Horses, tested.Ledger.Select(l => l.Kind == DnaTest.LedgerKind
                ? new CoinLedgerEntry(l.Id, -1, l.Kind) : l)));
            // A contradictory call, with and without the debit.
            var locus = good.PlayerKnowledge.RevealedCalls.First().Key;
            var forged = good.PlayerKnowledge.RevealedCalls.Select(p => p.Key == locus
                ? new KeyValuePair<string, AllelePair>(p.Key, new AllelePair("zz", "zz")) : p).ToArray();
            Assert.Throws<SaveIntegrityException>(() => Replace(tested, tested.Horses.Select(h => h.HorseId.Equals(horse.HorseId) ? WithKnowledge(h, forged) : h), tested.Ledger));
            Assert.Throws<SaveIntegrityException>(() => Replace(state, state.Horses.Select(h => h.HorseId.Equals(horse.HorseId) ? WithKnowledge(h, forged) : h), state.Ledger));
        }

        [Fact]
        public void TestedParentsGiveExactFoalOdds()
        {
            HorseRecord Dun(int id, ReproductiveRole role, bool tested)
            {
                var h = FrozenFixtureData.Horse(FrozenFixtureData.With(("tbx3", "D", "D")), id, role);
                return tested ? WithKnowledge(h, DnaTest.Loci.Where(h.Genome.HasLocus).Select(l => new KeyValuePair<string, AllelePair>(l, h.Genome.GetAlleles(l)))) : h;
            }
            var matrix = PhenotypeTests.Matrix();
            var vague = FoalCoatOdds.For(Dun(1, ReproductiveRole.Mare, false), Dun(2, ReproductiveRole.Stallion, false), matrix);
            Assert.Contains(vague, o => o.Low != o.High);
            var exact = FoalCoatOdds.For(Dun(1, ReproductiveRole.Mare, true), Dun(2, ReproductiveRole.Stallion, true), matrix);
            var top = exact.Single();
            Assert.Equal("Bay dun", top.Label); Assert.Equal(1.0, top.Low, 6); Assert.Equal(1.0, top.High, 6); Assert.Equal("100%", top.Text);
        }

        [Fact]
        public void DescribeUsesFriendlyWords()
        {
            Assert.Equal("Red factor (E/e): carries red — can have chestnut foals", DnaTest.Describe("mc1r", new AllelePair("E", "e")));
            Assert.Equal("Cream (Cr/N): one cream copy", DnaTest.Describe("slc45a2", new AllelePair("Cr", "N")));
            Assert.Contains("half its foals turn grey", DnaTest.Describe("stx17", new AllelePair("G", "N")));
            Assert.Contains("every foal is tobiano", DnaTest.Describe("pattern.eca3", new AllelePair("TO", "TO")));
            Assert.Contains("one tobiano copy", DnaTest.Describe("pattern.eca3", new AllelePair("TO", "baseline")));
            Assert.Contains("boosts leopard spotting", DnaTest.Describe("patn1", new AllelePair("PATN1", "N")));
            Assert.Equal("Agouti (A/a): carries black", DnaTest.Describe("asip", new AllelePair("A", "a")));
            // D-037 loci.
            Assert.Contains("only shows on black and bay", DnaTest.Describe("pmel", new AllelePair("Z", "N")));
            Assert.Contains("one champagne copy", DnaTest.Describe("slc36a1", new AllelePair("Ch", "N")));
            Assert.Contains("one splash copy", DnaTest.Describe("pattern.mitf", new AllelePair("SW1", "baseline")));
            Assert.Contains("one sabino copy", DnaTest.Describe("pattern.eca3", new AllelePair("SB1", "baseline")));
            Assert.Contains("does not show", DnaTest.Describe("slc45a2", new AllelePair("N", "Prl")));
        }

        [Fact]
        public void NewTestsRevealTheD037LociAndHorsesTestedBeforeStillCount()
        {
            Assert.Contains("pmel", DnaTest.Loci); Assert.Contains("slc36a1", DnaTest.Loci); Assert.Contains("pattern.mitf", DnaTest.Loci);
            var (_, state) = Starter();
            var horse = Untested(state);
            // A horse tested before D-037 revealed only the eight original loci; it must stay "tested" so old saves validate.
            var legacy = WithKnowledge(horse, new[] { "mc1r", "asip", "slc45a2", "tbx3", "stx17", "trpm1", "patn1", "pattern.eca3" }
                .Where(horse.Genome.HasLocus).Select(l => new KeyValuePair<string, AllelePair>(l, horse.Genome.GetAlleles(l))));
            Assert.True(DnaTest.IsTested(legacy));
            Assert.False(DnaTest.IsTested(horse));
        }
    }
}
