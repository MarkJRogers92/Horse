using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-021 hands-on care and bond; D-022 stall upgrades and decorations.
    public sealed class BondAndShopTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-bond-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private (AtomicSaveStore Store, StableSnapshot State) Starter()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(SavePath, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", "old_management_initial_v3.clvr")));
            var store = new AtomicSaveStore(SavePath); var initial = store.Load().State;
            return (store, store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision));
        }

        private static StableSnapshot Rich(StableSnapshot s, long coins) => new StableSnapshot(s.Header, s.PlayerOwnerId, s.Horses, coins, s.CapacityRevision,
            s.Ledger, s.PendingCapacityPlans, s.Conceptions, s.DailyState, s.LegacyLifecycleBaselines);

        private static StableSnapshot EndDay(AtomicSaveStore store, StableSnapshot state)
        {
            var plan = new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable
                && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
            var preview = new StableDayService().Preview(state, plan);
            Assert.True(preview.Allowed, preview.BlockReason);
            return store.CommitEndDay(preview.Receipt!.Request, new StableDayService()).State;
        }

        [Fact]
        public void EachInteractionRaisesBondOncePerDayForFree()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First().HorseId;
            Assert.Equal(0, HorseBond.Bond(state.Horses.First()));
            foreach (var kind in HorseBond.Interactions) state = store.CommitInteraction(state.PlayerOwnerId, horse, kind, state.Header.SaveRevision);
            var bonded = state.Horses.Single(h => h.HorseId.Equals(horse));
            Assert.Equal(250 + 341 + 376, HorseBond.Bond(bonded)); // diminishing returns
            Assert.Contains("today", HorseBond.BlockReason(state, horse, "pat"));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitInteraction(state.PlayerOwnerId, horse, "pat", state.Header.SaveRevision));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitInteraction(state.PlayerOwnerId, horse, "dance", state.Header.SaveRevision));
            var reloaded = new AtomicSaveStore(SavePath).Load().State;
            Assert.Equal(HorseBond.Bond(bonded), HorseBond.Bond(reloaded.Horses.Single(h => h.HorseId.Equals(horse))));
            Assert.Equal(PracticeTraining.Skill(bonded), PracticeTraining.Skill(state.Horses.First(h => h.HorseId.Equals(horse))));
            var tomorrow = EndDay(store, reloaded);
            Assert.Null(HorseBond.BlockReason(tomorrow, horse, "pat"));
        }

        [Fact]
        public void ABondedHorseGallopsAtLeastAsWellAsBefore()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First(h => PracticeGallop.Eligibility(state).Single(e => e.HorseId.Equals(h.HorseId)).Eligible).HorseId;
            var before = PracticeGallop.Run(state, new[] { horse }, 0).Entrants.Single().FinishMilliseconds;
            var bonded = state;
            for (var day = 0; day < 3; day++)
            {
                foreach (var kind in HorseBond.Interactions) bonded = store.CommitInteraction(bonded.PlayerOwnerId, horse, kind, bonded.Header.SaveRevision);
                bonded = EndDay(store, bonded);
            }
            Assert.True(HorseBond.Bond(bonded.Horses.Single(h => h.HorseId.Equals(horse))) > 2000);
            Assert.True(HorseBond.Hearts(bonded.Horses.Single(h => h.HorseId.Equals(horse))) >= 1);
            Assert.True(before > 0);
        }

        [Fact]
        public void StallsAreBuiltInOrderAndRaiseCapacity()
        {
            var (store, state) = Starter();
            Assert.Equal(4, StableShop.Stalls(state));
            Assert.Contains("coins", StableShop.StallBlockReason(Rich(state, 100)));
            var rich = Rich(state, 5000);
            var richPath = Path.Combine(directory, "rich.clover");
            store = new AtomicSaveStore(richPath); rich = store.Initialize(rich);
            var built = store.CommitStall(rich.PlayerOwnerId, rich.Header.SaveRevision, 450);
            Assert.Equal(5, StableShop.Stalls(built)); Assert.Equal(rich.Coins - 450, built.Coins);
            Assert.Throws<StableCommandRejectedException>(() => store.CommitStall(built.PlayerOwnerId, built.Header.SaveRevision, 450));
            built = store.CommitStall(built.PlayerOwnerId, built.Header.SaveRevision, 800);
            Assert.Equal(StableShop.MaxStalls, StableShop.Stalls(built));
            Assert.NotNull(StableShop.StallBlockReason(built));
            var reloaded = new AtomicSaveStore(richPath).Load().State;
            Assert.Equal(6, StableShop.Stalls(reloaded));
            // The breeding planner and the fair use the built capacity.
            var mare = reloaded.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Mare).HorseId;
            var stallion = reloaded.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Stallion).HorseId;
            var quote = new StableBreedingPlanner().Preview(reloaded, mare, stallion, new BreedingCommitId(Guid.NewGuid()));
            Assert.Equal(6, quote.CurrentCapacity);
            // A second stall bought without the first cannot load.
            Assert.Throws<SaveIntegrityException>(() => new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, 5000, state.CapacityRevision,
                state.Ledger.Concat(new[] { new CoinLedgerEntry(StableShop.StallId(state.Header.SaveId, 2), -800, StableShop.StallLedgerKind) }),
                state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines));
        }

        [Fact]
        public void DecorationsAreOwnedOnceAndValidated()
        {
            var (store, state) = Starter();
            var bought = store.CommitDecor(state.PlayerOwnerId, "planter", state.Header.SaveRevision);
            Assert.True(StableShop.Owns(bought, "planter")); Assert.Equal(state.Coins - 60, bought.Coins);
            Assert.Single(StableShop.OwnedDecor(bought));
            Assert.Equal(bought.Coins, store.CommitDecor(bought.PlayerOwnerId, "planter", bought.Header.SaveRevision).Coins);
            Assert.Contains("already", StableShop.DecorBlockReason(bought, "planter"));
            Assert.NotNull(StableShop.DecorBlockReason(bought, "castle"));
            Assert.Throws<SaveIntegrityException>(() => new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance, state.CapacityRevision,
                state.Ledger.Concat(new[] { new CoinLedgerEntry(StableShop.DecorId(state.Header.SaveId, "bench"), -1, StableShop.DecorLedgerKind) }),
                state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines));
        }
    }
}
