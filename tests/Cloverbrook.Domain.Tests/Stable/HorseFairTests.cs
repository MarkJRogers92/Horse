using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-017 weekly horse fair.
    public sealed class HorseFairTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-fair-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private (AtomicSaveStore Store, StableSnapshot State) Starter()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(SavePath, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", "old_management_initial_v3.clvr")));
            var store = new AtomicSaveStore(SavePath); var initial = store.Load().State;
            return (store, store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision));
        }

        private static SaveHeader OnDay(SaveHeader h, int day) => new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, h.SaveRevision, day,
            h.CalendarProfileVersion, h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion,
            h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, h.PayloadHash);

        private static StableSnapshot EndDay(AtomicSaveStore store, StableSnapshot state)
        {
            var plan = new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable
                && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
            var preview = new StableDayService().Preview(state, plan);
            Assert.True(preview.Allowed, preview.BlockReason);
            return store.CommitEndDay(preview.Receipt!.Request, new StableDayService()).State;
        }

        [Fact]
        public void EveryOfferIsAReleasedPhase1CoatThatTheRealMatrixPresents()
        {
            var (_, state) = Starter(); var matrix = PhenotypeTests.Matrix(); var resolver = new PhenotypeResolver();
            var seen = new System.Collections.Generic.HashSet<string>();
            // Walk many fair weeks by varying the save identity's week through the calendar day.
            for (var day = 0; day < 7 * 40; day += 7)
            {
                var header = OnDay(state.Header, day);
                foreach (var offer in HorseFair.Offers(state.WithHeader(header)))
                {
                    var genetics = resolver.ResolveGenetics(offer.Horse);
                    Assert.Equal(ResolutionStatus.Supported, genetics.Status);
                    Assert.Contains(offer.CoatClass, genetics.RequiredCoatClasses);
                    var presented = resolver.ResolveForPresentation(offer.Horse, matrix, "assets.v1", "anatomy.v1");
                    Assert.Equal(ResolutionStatus.Supported, presented.Status);
                    Assert.InRange(offer.PriceCoins, HorseFair.MinimumPrice, HorseFair.MaximumPrice);
                    seen.Add(offer.CoatClass);
                }
            }
            Assert.True(seen.Count >= 10, "The fair should show most released coats over a season: " + string.Join(",", seen));
        }

        [Fact]
        public void OffersAreStableWithinAWeekAndChangeTheNextWeek()
        {
            var (_, state) = Starter();
            var a = HorseFair.Offers(state.WithHeader(OnDay(state.Header, 7)));
            var b = HorseFair.Offers(state.WithHeader(OnDay(state.Header, 13)));
            var c = HorseFair.Offers(state.WithHeader(OnDay(state.Header, 14)));
            Assert.Equal(HorseFair.OffersPerWeek, a.Count);
            Assert.Equal(a.Select(o => o.HorseId), b.Select(o => o.HorseId)); Assert.Equal(a.Select(o => o.PriceCoins), b.Select(o => o.PriceCoins));
            Assert.Empty(a.Select(o => o.HorseId).Intersect(c.Select(o => o.HorseId)));
            Assert.Equal(a.Count, a.Select(o => o.CoatClass).Distinct().Count());
        }

        [Fact]
        public void BuyingDebitsOnceAddsTheHorseAndSurvivesReload()
        {
            var (store, state) = Starter();
            var offer = HorseFair.Offers(state).First(o => HorseFair.BlockReason(state, o) == null);
            var coins = state.Coins; var revision = state.Header.SaveRevision;
            var bought = store.CommitPurchase(state.PlayerOwnerId, offer.HorseId, revision, offer.PriceCoins);
            Assert.Equal(coins - offer.PriceCoins, bought.Coins);
            var horse = bought.Horses.Single(h => h.HorseId.Equals(offer.HorseId));
            Assert.True(HorseFair.IsPurchased(horse)); Assert.Equal(bought.PlayerOwnerId, horse.OwnerId);
            Assert.True(HorseFair.Offers(bought).Single(o => o.HorseId.Equals(offer.HorseId)).AlreadyBought);
            var bytes = File.ReadAllBytes(SavePath);
            Assert.Equal(bought.Coins, new AtomicSaveStore(SavePath).CommitPurchase(state.PlayerOwnerId, offer.HorseId, revision, offer.PriceCoins).Coins);
            Assert.Equal(bytes, File.ReadAllBytes(SavePath));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitPurchase(state.PlayerOwnerId, offer.HorseId, revision, offer.PriceCoins + 1));
            var reloaded = new AtomicSaveStore(SavePath).Load().State;
            Assert.Contains(reloaded.Horses, h => h.HorseId.Equals(offer.HorseId));
            Assert.False(HorseMarket.Quote(reloaded, offer.HorseId).Allowed);
            // The new horse joins daily care like any other.
            var next = EndDay(store, reloaded);
            Assert.Contains(next.DailyState.Receipts.Last().HorseOutcomes, o => o.HorseId.Equals(offer.HorseId));
        }

        [Fact]
        public void StallsCoinsAndStaleRevisionsBlockPurchases()
        {
            var (store, state) = Starter();
            var offers = HorseFair.Offers(state);
            Assert.Throws<StableCommandRejectedException>(() => store.CommitPurchase(state.PlayerOwnerId, offers[0].HorseId, state.Header.SaveRevision + 1, offers[0].PriceCoins));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitPurchase(state.PlayerOwnerId, new HorseId(Guid.NewGuid()), state.Header.SaveRevision, 300));
            var broke = new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, 0, state.CapacityRevision, state.Ledger,
                state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
            Assert.Contains("coins", HorseFair.BlockReason(broke, HorseFair.Offers(broke)[0]));
            var full = state;
            foreach (var offer in offers.Take(StableBreedingPlanner.CapacitySlots - state.Horses.Count))
                full = new StableSnapshot(full.Header, full.PlayerOwnerId, full.Horses.Concat(new[] { offer.Horse }), 5000, full.CapacityRevision,
                    full.Ledger.Concat(new[] { new CoinLedgerEntry(HorseFair.PurchaseId(offer.HorseId), -offer.PriceCoins, HorseFair.PurchaseLedgerKind) }),
                    full.PendingCapacityPlans, full.Conceptions, full.DailyState, full.LegacyLifecycleBaselines);
            var last = HorseFair.Offers(full).First(o => !o.AlreadyBought);
            Assert.Contains("stall", HorseFair.BlockReason(full, last));
        }

        [Fact]
        public void ForgedPurchasesAreRejected()
        {
            var (store, state) = Starter();
            var offer = HorseFair.Offers(state)[0];
            var bought = store.CommitPurchase(state.PlayerOwnerId, offer.HorseId, state.Header.SaveRevision, offer.PriceCoins);
            // A bought horse without its debit, or a debit without its horse, cannot load.
            Assert.Throws<SaveIntegrityException>(() => new StableSnapshot(bought.Header, bought.PlayerOwnerId, bought.Horses, bought.OpeningCoinBalance,
                bought.CapacityRevision, bought.Ledger.Where(l => l.Kind != HorseFair.PurchaseLedgerKind), bought.PendingCapacityPlans, bought.Conceptions,
                bought.DailyState, bought.LegacyLifecycleBaselines));
            Assert.Throws<SaveIntegrityException>(() => new StableSnapshot(bought.Header, bought.PlayerOwnerId, bought.Horses.Where(h => !HorseFair.IsPurchased(h)),
                bought.OpeningCoinBalance, bought.CapacityRevision, bought.Ledger, bought.PendingCapacityPlans, bought.Conceptions,
                bought.DailyState, bought.LegacyLifecycleBaselines));
        }
    }
}
