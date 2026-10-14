using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-020 tack and dress-up.
    public sealed class TackTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-tack-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private (AtomicSaveStore Store, StableSnapshot State) Starter()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(SavePath, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", "old_management_initial_v3.clvr")));
            var store = new AtomicSaveStore(SavePath); var initial = store.Load().State;
            return (store, store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision));
        }

        [Fact]
        public void TackIsSavedFreeAndShowsInTheCosmeticLayer()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First();
            Assert.Null(Tack.Layer(horse));
            var dressed = store.CommitTack(state.PlayerOwnerId, horse.HorseId, Tack.PadSlot, "pad.mint", state.Header.SaveRevision);
            dressed = store.CommitTack(dressed.PlayerOwnerId, horse.HorseId, Tack.BowSlot, "bow.rose", dressed.Header.SaveRevision);
            var worn = dressed.Horses.Single(h => h.HorseId.Equals(horse.HorseId));
            Assert.Equal("bow=bow.rose;pad=pad.mint", Tack.Layer(worn));
            Assert.Equal(state.Coins, dressed.Coins);
            Assert.Equal(state.Ledger.Count, dressed.Ledger.Count);
            var reloaded = new AtomicSaveStore(SavePath).Load().State;
            Assert.Equal("pad.mint", Tack.Worn(reloaded.Horses.Single(h => h.HorseId.Equals(horse.HorseId)), Tack.PadSlot)!.Id);
            var undressed = store.CommitTack(reloaded.PlayerOwnerId, horse.HorseId, Tack.PadSlot, null, reloaded.Header.SaveRevision);
            Assert.Equal("bow=bow.rose", Tack.Layer(undressed.Horses.Single(h => h.HorseId.Equals(horse.HorseId))));
            // Wearing what is already worn is a no-op.
            Assert.Equal(undressed.Header.SaveRevision, store.CommitTack(undressed.PlayerOwnerId, horse.HorseId, Tack.BowSlot, "bow.rose", undressed.Header.SaveRevision).Header.SaveRevision);
        }

        [Fact]
        public void LockedWrongOrStaleTackIsRefused()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First().HorseId;
            Assert.Contains("Willowbrook Cup", Tack.BlockReason(state, horse, Tack.PadSlot, "pad.gold"));
            Assert.NotNull(Tack.BlockReason(state, horse, Tack.PadSlot, "bow.rose"));
            Assert.NotNull(Tack.BlockReason(state, horse, "saddle", "pad.rose"));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitTack(state.PlayerOwnerId, horse, Tack.PadSlot, "pad.gold", state.Header.SaveRevision));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitTack(state.PlayerOwnerId, horse, Tack.PadSlot, "pad.rose", state.Header.SaveRevision + 1));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitTack(state.PlayerOwnerId, new HorseId(Guid.NewGuid()), Tack.PadSlot, "pad.rose", state.Header.SaveRevision));
        }

        [Fact]
        public void TackChangesThePresentationCacheKeyButNotTheCoat()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First();
            var dressed = store.CommitTack(state.PlayerOwnerId, horse.HorseId, Tack.PadSlot, "pad.sky", state.Header.SaveRevision).Horses.Single(h => h.HorseId.Equals(horse.HorseId));
            var matrix = PhenotypeTests.Matrix(); var resolver = new PhenotypeResolver();
            var plain = resolver.ResolveForPresentation(horse, matrix, "a", "b", Tack.Layer(horse)).Descriptor!;
            var worn = resolver.ResolveForPresentation(dressed, matrix, "a", "b", Tack.Layer(dressed)).Descriptor!;
            Assert.NotEqual(plain.CacheKey, worn.CacheKey);
            Assert.Equal(plain.MarkingRealizationKey, worn.MarkingRealizationKey);
            Assert.Equal(dressed.Genome.GenomeHash, horse.Genome.GenomeHash);
        }

        [Fact]
        public void NewSlotsRoundTripAndKeepTheLayerCanonical()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First();
            var pre = store.CommitTack(state.PlayerOwnerId, horse.HorseId, Tack.PadSlot, "pad.mint", state.Header.SaveRevision);
            Assert.Equal("pad=pad.mint", Tack.Layer(pre.Horses.Single(h => h.HorseId.Equals(horse.HorseId))));
            var s2 = pre;
            foreach (var (slot, id) in new[] { (Tack.BlanketSlot, "blanket.navy"), (Tack.HalterSlot, "halter.pink"), (Tack.BraidsSlot, "braids.blue"), (Tack.TailSlot, "tail.rose") })
                s2 = store.CommitTack(s2.PlayerOwnerId, horse.HorseId, slot, id, s2.Header.SaveRevision);
            Assert.Equal(pre.Coins, s2.Coins);
            var reloaded = new AtomicSaveStore(SavePath).Load().State.Horses.Single(h => h.HorseId.Equals(horse.HorseId));
            Assert.Equal("blanket=blanket.navy;braids=braids.blue;halter=halter.pink;pad=pad.mint;tail=tail.rose", Tack.Layer(reloaded));
            Assert.Equal("braids.blue", Tack.Worn(reloaded, Tack.BraidsSlot)!.Id);
            Assert.Equal(6, Tack.Slots.Count);
            Assert.Contains(Tack.Items, i => i.Id == "braids.button");
        }

        [Fact]
        public void SnowflakeRugUnlocksOnlyInWinter()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First().HorseId;
            Assert.Contains("winter", Tack.BlockReason(state, horse, Tack.BlanketSlot, "blanket.snowflake"));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitTack(state.PlayerOwnerId, horse, Tack.BlanketSlot, "blanket.snowflake", state.Header.SaveRevision));
            Assert.Null(Tack.BlockReason(state, horse, Tack.BlanketSlot, "blanket.cream"));
            Assert.False(Tack.Find("blanket.snowflake")!.IsUnlocked(state));
            foreach (var (day, winter) in new[] { (0, false), (55, false), (56, false), (83, false), (84, true), (111, true), (112, false) })
            {
                var h = state.Header;
                var header = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, h.SaveRevision, day, h.CalendarProfileVersion, h.CareMode,
                    h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion, h.RetainedResolverVersions, h.RngStreams,
                    h.LastCommittedBreedingEventIndex, h.MigrationHistory, h.PayloadHash);
                var dated = new StableSnapshot(header, state.PlayerOwnerId, state.Horses, state.Coins, state.CapacityRevision, state.Ledger,
                    state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
                Assert.Equal(winter, Tack.Find("blanket.snowflake")!.IsUnlocked(dated));
                Assert.Equal(winter, Tack.BlockReason(dated, horse, Tack.BlanketSlot, "blanket.snowflake") == null);
            }
            Assert.NotNull(Tack.BlockReason(state, horse, "cape", "blanket.navy"));
            Assert.NotNull(Tack.BlockReason(state, horse, Tack.HalterSlot, "blanket.navy"));
        }
    }
}
