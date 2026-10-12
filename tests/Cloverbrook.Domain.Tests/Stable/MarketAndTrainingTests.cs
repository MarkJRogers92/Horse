using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-010 club buyer sales and D-011 gallop training.
    public sealed class MarketAndTrainingTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-market-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", name));

        private static StableSnapshot EndDay(AtomicSaveStore store, StableSnapshot state)
        {
            var plan = new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable
                && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
            var preview = new StableDayService().Preview(state, plan);
            Assert.True(preview.Allowed, preview.BlockReason);
            return store.CommitEndDay(preview.Receipt!.Request, new StableDayService()).State;
        }

        /// <summary>Normal starter stable that bred one foal and grew it to adulthood (day 10).</summary>
        private (AtomicSaveStore Store, StableSnapshot State, HorseId Child) BredAdult()
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_management_initial_v3.clvr"));
            var store = new AtomicSaveStore(SavePath); var initial = store.Load().State;
            var state = store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision);
            var mare = state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Mare).HorseId;
            var stallion = state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Stallion).HorseId;
            var planner = new StableBreedingPlanner();
            var committed = store.CommitStableBreeding(planner.Preview(state, mare, stallion, new BreedingCommitId(Guid.Parse("ce000000-0000-0000-0000-000000000001"))).Request, planner);
            var child = committed.Receipt.Commit.OffspringHorseId; state = committed.State;
            while (state.Horses.Single(h => h.HorseId.Equals(child)).LifeState.Stage != LifeStage.Adult) state = EndDay(store, state);
            return (store, state, child);
        }

        [Fact]
        public void StarterHorsesAndYoungFoalsAreNotForSale()
        {
            var state = SaveTestData.Commit().State;
            Assert.All(state.Horses, h => Assert.False(HorseMarket.Quote(state, h.HorseId).Allowed));
            Assert.Contains("starter", HorseMarket.Quote(state, state.Horses.First(h => h.OriginCommitId == null).HorseId).BlockReason);
        }

        [Fact]
        public void SellingABredAdultPaysOnceFreesItsHomePlaceAndKeepsItsRecord()
        {
            var (store, state, child) = BredAdult();
            var offer = HorseMarket.Quote(state, child);
            Assert.True(offer.Allowed, offer.BlockReason); Assert.InRange(offer.PriceCoins, HorseMarket.MinimumPrice, HorseMarket.MaximumPrice);
            Assert.NotEmpty(offer.Reasons);
            var coins = state.Coins; var revision = state.Header.SaveRevision;
            var sold = store.CommitSale(state.PlayerOwnerId, child, revision, offer.PriceCoins);
            Assert.Equal(coins + offer.PriceCoins, sold.Coins); Assert.True(HorseMarket.IsSold(sold.Horses.Single(h => h.HorseId.Equals(child))));
            Assert.Single(sold.Ledger, l => l.Kind == HorseMarket.SaleLedgerKind);
            var bytes = File.ReadAllBytes(SavePath);
            var replay = new AtomicSaveStore(SavePath).CommitSale(state.PlayerOwnerId, child, revision, offer.PriceCoins);
            Assert.Equal(sold.Coins, replay.Coins); Assert.Equal(bytes, File.ReadAllBytes(SavePath));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitSale(state.PlayerOwnerId, child, revision, offer.PriceCoins + 1));
            var reloaded = new AtomicSaveStore(SavePath).Load().State;
            Assert.Equal(sold.Coins, reloaded.Coins); Assert.Contains(reloaded.Horses, h => h.HorseId.Equals(child));
            // The sold horse leaves daily care and frees its home place, and breeding the remaining pair still works.
            var next = EndDay(store, reloaded);
            Assert.DoesNotContain(next.DailyState.Receipts.Last().HorseOutcomes, o => o.HorseId.Equals(child));
            var mare = next.Horses.Single(h => h.OwnerId.Equals(next.PlayerOwnerId) && h.ReproductionState.Role == ReproductiveRole.Mare).HorseId;
            var stallion = next.Horses.Single(h => h.OwnerId.Equals(next.PlayerOwnerId) && h.ReproductionState.Role == ReproductiveRole.Stallion).HorseId;
            var quote = new StableBreedingPlanner().Preview(next, mare, stallion, new BreedingCommitId(Guid.Parse("ce000000-0000-0000-0000-000000000002")));
            Assert.DoesNotContain(quote.Rejections, r => r.Code == "unsupported_population");
            Assert.Equal(2, quote.CurrentCapacityUsed);
        }

        [Fact]
        public void ForgedOrMissingSaleCreditsAreRejected()
        {
            var (store, state, child) = BredAdult();
            var sold = store.CommitSale(state.PlayerOwnerId, child, state.Header.SaveRevision, HorseMarket.Quote(state, child).PriceCoins);
            StableSnapshot With(System.Collections.Generic.IEnumerable<CoinLedgerEntry> ledger) => new StableSnapshot(sold.Header, sold.PlayerOwnerId,
                sold.Horses, sold.OpeningCoinBalance, sold.CapacityRevision, ledger, sold.PendingCapacityPlans, sold.Conceptions, sold.DailyState, sold.LegacyLifecycleBaselines);
            Assert.Throws<SaveIntegrityException>(() => With(sold.Ledger.Where(l => l.Kind != HorseMarket.SaleLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(sold.Ledger.Concat(new[] { new CoinLedgerEntry(new TransactionId(Guid.NewGuid()), 300, HorseMarket.SaleLedgerKind) })));
        }

        [Fact]
        public void PracticeTrainsOncePerDayWithDiminishingGainsAndRaisesTheOffer()
        {
            var (store, state, child) = BredAdult();
            var priceBefore = HorseMarket.Quote(state, child).PriceCoins;
            var entrants = new[] { child };
            var first = store.RecordPractice(state.PlayerOwnerId, state.Header.SaveRevision, entrants);
            var gain1 = PracticeTraining.Skill(first.Horses.Single(h => h.HorseId.Equals(child)));
            Assert.Equal(PracticeTraining.BaseGain, gain1);
            var again = store.RecordPractice(first.PlayerOwnerId, first.Header.SaveRevision, entrants);
            Assert.Equal(first.Header.SaveRevision, again.Header.SaveRevision);
            var nextDay = EndDay(store, again);
            var second = store.RecordPractice(nextDay.PlayerOwnerId, nextDay.Header.SaveRevision, entrants);
            var skill2 = PracticeTraining.Skill(second.Horses.Single(h => h.HorseId.Equals(child)));
            Assert.InRange(skill2 - gain1, 1, PracticeTraining.BaseGain - 1);
            Assert.True(HorseMarket.Quote(second, child).PriceCoins > priceBefore);
            var starter = second.Horses.First(h => h.OriginCommitId == null && h.ReproductionState.PregnancyId == null && h.LifeState.Stage == LifeStage.Adult);
            Assert.Equal(0, PracticeTraining.Skill(starter));
            Assert.Throws<StableCommandRejectedException>(() => store.RecordPractice(second.PlayerOwnerId, second.Header.SaveRevision - 1, entrants));
        }

        [Fact]
        public void TrainedHorsesGallopFaster()
        {
            var state = SaveTestData.Initial(); var template = state.Horses[0];
            HorseRecord Copy(string id, string name, int skill) => new HorseRecord(new HorseId(Guid.Parse(id)), name, template.Parentage, template.Genome,
                template.ResolverVersion, template.AppearanceSeed, template.OwnerId, template.LifeState, new ReproductionState(template.ReproductionState.Role),
                template.DevelopmentalFactors, template.InheritedGamePotential,
                new QuantizedVector(new[] { new System.Collections.Generic.KeyValuePair<string, int>(PracticeTraining.SkillKey, skill) }));
            var trained = Copy("aa000000-0000-0000-0000-00000000000c", "Trained", 8000); var fresh = Copy("aa000000-0000-0000-0000-00000000000d", "Fresh", 0);
            var pair = state.WithHorses(state.Horses.Concat(new[] { trained, fresh }));
            var wins = Enumerable.Range(0, 20).Count(run => PracticeGallop.Run(pair, new[] { trained.HorseId, fresh.HorseId }, run)
                .Entrants.Single(e => e.HorseId.Equals(trained.HorseId)).Place == 1);
            Assert.InRange(wins, 15, 20);
        }
    }
}
