using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-035 horse personalities: derived from ids, never stored.
    public sealed class PersonalityTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-personality-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private (AtomicSaveStore Store, StableSnapshot State) Starter()
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "stable.clover");
            File.WriteAllBytes(path, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", "old_management_initial_v3.clvr")));
            var store = new AtomicSaveStore(path); var initial = store.Load().State;
            return (store, store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision));
        }

        private static HorseRecord Clone(HorseRecord h, HorseId id, Parentage? parentage = null) =>
            new HorseRecord(id, h.Name, parentage ?? new Parentage(), h.Genome, h.ResolverVersion, h.AppearanceSeed, h.OwnerId, h.LifeState, h.ReproductionState,
                h.DevelopmentalFactors, h.InheritedGamePotential, h.TrainingState, h.PlayerKnowledge, h.CosmeticState, h.HomeAssignment, h.OriginRecord,
                h.OriginCommitId, h.BirthDay, h.Revision);

        private static HorseId Id(int i) => new HorseId(new Guid(i, 7, 9, 1, 2, 3, 4, 5, 6, 7, 8));

        [Fact]
        public void TraitIsDeterministicAndAlwaysOneOfSix()
        {
            var (_, state) = Starter();
            var h = state.Horses.First();
            var a = Personality.For(state, h); var b = Personality.For(state, h);
            Assert.Equal(a.Id, b.Id);
            Assert.Contains(a.Id, Personality.Ids);
            Assert.Equal(6, Personality.Ids.Count);
            Assert.StartsWith(a.Label + " — ", a.Description);
            Assert.True(a.PipLines.Count >= 1 && a.QuirkLines.Count >= 1);
            foreach (var id in Personality.Ids) foreach (var kind in HorseBond.Interactions) Assert.Equal(3, Personality.Reactions(id, kind).Count);
        }

        [Fact]
        public void FreshTraitsAreRoughlyEvenOverManyIds()
        {
            var (_, state) = Starter();
            var h = state.Horses.First();
            var counts = new Dictionary<string, int>();
            const int n = 3000;
            for (var i = 1; i <= n; i++) { var t = Personality.Fresh(Clone(h, Id(i))); counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1; }
            Assert.Equal(6, counts.Count);
            foreach (var c in counts.Values) Assert.InRange(c, n / 6 - 90, n / 6 + 90);
        }

        [Fact]
        public void FoalsTakeAParentsTraitAboutSixtyPercentOfTheTime()
        {
            var (_, state) = Starter();
            var h = state.Horses.First();
            var mare = Clone(h, Id(1000001)); var stallion = Clone(h, Id(1000002));
            var foals = Enumerable.Range(1, 2000).Select(i => Clone(h, Id(i), new Parentage(mare.HorseId, stallion.HorseId))).ToList();
            var world = state.WithHorses(new[] { mare, stallion }.Concat(foals));
            var parentTraits = new[] { Personality.Fresh(mare), Personality.Fresh(stallion) };
            // Foals flagged as inheriting always carry a parent's trait; the flag fires about 60% of the time.
            var inheriting = foals.Where(Personality.Inherits).ToList();
            Assert.All(inheriting, f => Assert.Contains(Personality.TraitId(world, f), parentTraits));
            Assert.InRange(inheriting.Count * 100.0 / foals.Count, 56, 64);
            // Missing parents fall back to a fresh roll.
            var orphanWorld = state.WithHorses(foals);
            Assert.All(foals, f => Assert.Equal(Personality.Fresh(f), Personality.TraitId(orphanWorld, f)));
        }

        [Fact]
        public void BondModifiersMatchTheDocumentedTable()
        {
            Assert.Equal(1250, Personality.BondPermille(Personality.Cuddly, "pat", 0));
            Assert.Equal(1250, Personality.BondPermille(Personality.Cuddly, "brush", 9000));
            Assert.Equal(1000, Personality.BondPermille(Personality.Cuddly, "carrot", 0));
            Assert.Equal(1300, Personality.BondPermille(Personality.Greedy, "carrot", 0));
            Assert.Equal(1300, Personality.BondPermille(Personality.Greedy, "treat", 0));
            Assert.Equal(1000, Personality.BondPermille(Personality.Greedy, "pat", 0));
            Assert.Equal(800, Personality.BondPermille(Personality.Shy, "pat", 0));
            Assert.Equal(800, Personality.BondPermille(Personality.Shy, "carrot", 4000));
            Assert.Equal(1200, Personality.BondPermille(Personality.Shy, "pat", 4001));
            foreach (var t in new[] { Personality.Cheeky, Personality.Brave, Personality.Dreamy }) foreach (var k in HorseBond.Interactions) Assert.Equal(1000, Personality.BondPermille(t, k, 0));
            Assert.True(Personality.LovesInteraction(Personality.Greedy, "carrot") && Personality.LovesInteraction(Personality.Cheeky, "carrot"));
            Assert.False(Personality.LovesInteraction(Personality.Shy, "carrot"));
            Assert.Equal(1, Personality.ShowJudgeBonus(Personality.Brave)); Assert.Equal(0, Personality.ShowJudgeBonus(Personality.Dreamy));
        }

        [Fact]
        public void EveryTraitScalesTheGainAppliedToTheHorse()
        {
            var (_, state) = Starter();
            var h = state.Horses.First();
            var seen = new HashSet<string>();
            for (var i = 1; i < 400 && seen.Count < 6; i++)
            {
                var c = Clone(h, Id(i)); var t = Personality.Fresh(c);
                if (!seen.Add(t)) continue;
                var world = state.WithHorses(new[] { c });
                foreach (var kind in HorseBond.Interactions)
                {
                    var baseGain = kind == "pat" ? 250 : kind == "brush" ? 350 : 400;
                    var bond = HorseBond.Bond(c);
                    var expected = baseGain * Personality.BondPermille(t, kind, bond) / 1000 * (HorseBond.MaxBond - bond) / HorseBond.MaxBond;
                    Assert.Equal(expected, HorseBond.GainToday(world, c, kind, world.Header.CalendarDay));
                }
            }
            Assert.Equal(6, seen.Count);
        }

        [Fact]
        public void CommittedCareUsesThePersonalityGainAndStoresNothingNew()
        {
            var (store, state) = Starter();
            var h = state.Horses.First(x => HorseBond.BlockReason(state, x.HorseId, "pat") == null);
            var predicted = HorseBond.GainToday(state, h, "pat", state.Header.CalendarDay);
            var keysBefore = h.TrainingState.Values.Keys.ToList();
            var after = store.CommitInteraction(state.PlayerOwnerId, h.HorseId, "pat", state.Header.SaveRevision);
            var horseAfter = after.Horses.Single(x => x.HorseId.Equals(h.HorseId));
            Assert.Equal(HorseBond.Bond(h) + predicted, HorseBond.Bond(horseAfter));
            // Only the existing bond and day-marker keys change: no personality key is written.
            Assert.Empty(horseAfter.TrainingState.Values.Keys.Except(keysBefore).Except(new[] { HorseBond.BondKey, "pat_day" }));
            var reloaded = store.Load().State;
            Assert.Equal(Personality.For(state, h).Id, Personality.For(reloaded, reloaded.Horses.Single(x => x.HorseId.Equals(h.HorseId))).Id);
        }
    }
}
