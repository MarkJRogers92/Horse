using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-034 family tree and coat story.
    public sealed class FamilyTreeTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-family-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private static HorseRecord Clone(HorseRecord h, string name, Parentage parentage, ReproductiveRole role) => new HorseRecord(new HorseId(Guid.NewGuid()), name, parentage,
            h.Genome, h.ResolverVersion, h.AppearanceSeed, h.OwnerId, h.LifeState, new ReproductionState(role), h.DevelopmentalFactors, h.InheritedGamePotential,
            h.TrainingState, h.PlayerKnowledge, h.CosmeticState, h.HomeAssignment, h.OriginRecord, null, h.BirthDay, 0);

        private static HorseRecord Rename(HorseRecord h, string name, Parentage parentage, ReproductiveRole role) => new HorseRecord(h.HorseId, name, parentage,
            h.Genome, h.ResolverVersion, h.AppearanceSeed, h.OwnerId, h.LifeState, new ReproductionState(role), h.DevelopmentalFactors, h.InheritedGamePotential,
            h.TrainingState, h.PlayerKnowledge, h.CosmeticState, h.HomeAssignment, h.OriginRecord, h.OriginCommitId, h.BirthDay, h.Revision);

        private static StableSnapshot Starter()
        {
            var state = SaveTestData.Initial();
            var mare = state.Horses.First(h => h.ReproductionState.Role == ReproductiveRole.Mare);
            var stallion = state.Horses.First(h => h.ReproductionState.Role == ReproductiveRole.Stallion);
            return state.WithHorses(new[] { Rename(mare, "Maple", mare.Parentage, ReproductiveRole.Mare), Rename(stallion, "Rowan", stallion.Parentage, ReproductiveRole.Stallion) });
        }

        /// <summary>Three generations: Pip (foal) of Rowan and Maple; Rowan of Oak and Daisy; Maple of Birch and Clover.</summary>
        private static (StableSnapshot State, HorseRecord Foal) ThreeGenerations()
        {
            var state = Starter(); var mare = state.Horses.Single(h => h.Name == "Maple"); var stallion = state.Horses.Single(h => h.Name == "Rowan");
            var oak = Clone(stallion, "Oak", new Parentage(), ReproductiveRole.Stallion); var daisy = Clone(mare, "Daisy", new Parentage(), ReproductiveRole.Mare);
            var birch = Clone(stallion, "Birch", new Parentage(), ReproductiveRole.Stallion); var clover = Clone(mare, "Clover", new Parentage(), ReproductiveRole.Mare);
            var maple = Rename(mare, "Maple", new Parentage(birch.HorseId, clover.HorseId), ReproductiveRole.Mare);
            var rowan = Rename(stallion, "Rowan", new Parentage(oak.HorseId, daisy.HorseId), ReproductiveRole.Stallion);
            var pip = Clone(mare, "Pip", new Parentage(maple.HorseId, rowan.HorseId), ReproductiveRole.Mare); // recorded dam-first on purpose
            return (state.WithHorses(new[] { maple, rowan, oak, daisy, birch, clover, pip }), pip);
        }

        [Fact]
        public void FounderHasNoRelativesAndNoStory()
        {
            var state = Starter(); var founder = state.Horses.First();
            var tree = FamilyTree.For(state, founder.HorseId);
            Assert.Equal(founder.Name, tree.Subject.Name); Assert.True(tree.Subject.IsFounder); Assert.False(string.IsNullOrWhiteSpace(tree.Subject.VisibleCoat));
            Assert.Null(tree.Sire); Assert.Null(tree.Dam); Assert.All(tree.Grandparents, g => Assert.Null(g)); Assert.Null(tree.Story);
            Assert.Throws<ArgumentException>(() => FamilyTree.For(state, new HorseId(Guid.NewGuid())));
        }

        [Fact]
        public void FoalWithBothParentsShowsSireAndDam()
        {
            var change = SaveTestData.Commit(); var state = change.State;
            var foal = state.Horses.Single(h => h.HorseId.Equals(change.Receipt.Commit.OffspringHorseId));
            var tree = FamilyTree.For(state, foal.HorseId);
            Assert.False(tree.Subject.IsFounder); Assert.Equal("Not born yet", tree.Subject.VisibleCoat);
            Assert.NotNull(tree.Sire); Assert.NotNull(tree.Dam);
            Assert.Equal(ReproductiveRole.Stallion, state.Horses.Single(h => h.HorseId.Equals(tree.Sire!.HorseId)).ReproductionState.Role);
            Assert.Equal(ReproductiveRole.Mare, state.Horses.Single(h => h.HorseId.Equals(tree.Dam!.HorseId)).ReproductionState.Role);
            Assert.True(tree.Sire!.IsFounder && tree.Dam!.IsFounder); Assert.Null(tree.SireSire); Assert.Null(tree.DamDam);
            Assert.Null(tree.Story); // an unborn foal has no visible coat yet
        }

        [Fact]
        public void GrandparentsAreFilledInSireSideThenDamSide()
        {
            var (state, pip) = ThreeGenerations();
            var tree = FamilyTree.For(state, pip.HorseId);
            Assert.Equal("Rowan", tree.Sire!.Name); Assert.Equal("Maple", tree.Dam!.Name);
            Assert.Equal(new[] { "Oak", "Daisy", "Birch", "Clover" }, tree.Grandparents.Select(g => g!.Name));
            Assert.True(tree.SireSire!.IsFounder); Assert.False(tree.Sire.IsFounder);
            // Navigating up: a grandparent's own tree starts there and is a founder.
            var up = FamilyTree.For(state, tree.Sire.HorseId);
            Assert.Equal("Rowan", up.Subject.Name); Assert.Equal("Oak", up.Sire!.Name); Assert.Equal("Daisy", up.Dam!.Name); Assert.Null(up.SireSire);
        }

        [Fact]
        public void MissingParentLeavesAnUnknownSlot()
        {
            var state = Starter(); var mare = state.Horses.Single(h => h.Name == "Maple");
            // One parent recorded in the save and the other id not in it (or not recorded at all): both leave an unknown slot.
            var missing = Clone(mare, "Wren", new Parentage(mare.HorseId, new HorseId(Guid.NewGuid())), ReproductiveRole.Mare);
            var oneParent = Clone(mare, "Wren", new Parentage(mare.HorseId), ReproductiveRole.Mare);
            var gone = FamilyTree.For(state.WithHorses(state.Horses.Concat(new[] { missing })), missing.HorseId);
            Assert.Equal("Maple", gone.Dam!.Name); Assert.Null(gone.Sire); Assert.False(gone.Subject.IsFounder);
            var tree = FamilyTree.For(state.WithHorses(state.Horses.Concat(new[] { oneParent })), oneParent.HorseId);
            Assert.Equal("Maple", tree.Dam!.Name); Assert.Null(tree.Sire); Assert.False(tree.Subject.IsFounder);
        }

        [Fact]
        public void SoldAncestorsStayInTheTreeAndAreMarkedSold()
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "stable.clover");
            File.WriteAllBytes(path, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", "old_management_initial_v3.clvr")));
            var store = new AtomicSaveStore(path); var initial = store.Load().State;
            var state = store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision);
            var mare = state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Mare); var stallion = state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Stallion);
            var planner = new StableBreedingPlanner();
            var committed = store.CommitStableBreeding(planner.Preview(state, mare.HorseId, stallion.HorseId, new BreedingCommitId(Guid.Parse("ce000000-0000-0000-0000-000000000034"))).Request, planner);
            var child = committed.Receipt.Commit.OffspringHorseId; state = committed.State;
            while (state.Horses.Single(h => h.HorseId.Equals(child)).LifeState.Stage != LifeStage.Adult)
            {
                var plan = new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable
                    && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
                var preview = new StableDayService().Preview(state, plan); Assert.True(preview.Allowed, preview.BlockReason);
                state = store.CommitEndDay(preview.Receipt!.Request, new StableDayService()).State;
            }
            var sold = store.CommitSale(state.PlayerOwnerId, child, state.Header.SaveRevision, HorseMarket.Quote(state, child).PriceCoins);
            var soldTree = FamilyTree.For(sold, child);
            Assert.True(soldTree.Subject.IsSold); Assert.NotNull(soldTree.Sire); Assert.NotNull(soldTree.Dam); Assert.False(soldTree.Sire!.IsSold);
            var soldHorse = sold.Horses.Single(h => h.HorseId.Equals(child));
            // A later foal whose parent was sold still lists that parent, marked Sold.
            var otherParent = sold.Horses.First(h => !h.HorseId.Equals(child) && h.ReproductionState.Role != soldHorse.ReproductionState.Role && h.LifeState.Stage == LifeStage.Adult);
            var grandfoal = Clone(otherParent, "Echo", new Parentage(child, otherParent.HorseId), ReproductiveRole.Mare);
            var next = FamilyTree.For(sold.WithHorses(sold.Horses.Concat(new[] { grandfoal })), grandfoal.HorseId);
            var shown = new[] { next.Sire, next.Dam }.Single(n => n!.HorseId.Equals(child))!;
            Assert.True(shown.IsSold); Assert.Equal(soldHorse.Name, shown.Name);
            Assert.NotNull(next.SireSire ?? next.DamSire); // the sold horse's own parents are still found
        }

        [Fact]
        public void HeartsComeFromBond()
        {
            var state = Starter(); var horse = state.Horses.First();
            Assert.Equal(HorseBond.Hearts(horse), FamilyTree.For(state, horse.HorseId).Subject.Hearts);
        }

        // ---- Coat story sentence picker ----

        private static CoatStoryParent Parent(string name, string role, params string[] shows) => new CoatStoryParent(name, role, shows);

        [Fact]
        public void DominantPatternFromOneParentNamesThatParent()
        {
            var line = CoatStory.Pick("her", new[] { CoatStory.Tobiano }, Parent("Rowan", "sire"), Parent("Maple", "dam", CoatStory.Tobiano));
            Assert.Equal("Tobiano, like her dam Maple.", line);
            Assert.Equal("Grey, like his sire Rowan.", CoatStory.Pick("his", new[] { CoatStory.Grey }, Parent("Rowan", "sire", CoatStory.Grey), Parent("Maple", "dam")));
        }

        [Fact]
        public void BothParentsShowingIsCreditedToBoth()
        {
            Assert.Equal("Roan, from both Rowan and Maple.", CoatStory.Pick("her", new[] { CoatStory.Roan }, Parent("Rowan", "sire", CoatStory.Roan), Parent("Maple", "dam", CoatStory.Roan)));
        }

        [Fact]
        public void DominantPatternNeitherParentShowedIsASurprise()
        {
            Assert.Equal("A surprise leopard spotting: neither parent showed it.", CoatStory.Pick("her", new[] { CoatStory.Leopard }, Parent("Rowan", "sire"), Parent("Maple", "dam")));
        }

        [Fact]
        public void CreamFromAShowingParentOrAHiddenCarrier()
        {
            Assert.Equal("Cream, like her sire Rowan.", CoatStory.Pick("her", new[] { CoatStory.Cream }, Parent("Rowan", "sire", CoatStory.Cream), Parent("Maple", "dam")));
            var hidden = CoatStory.Pick("her", new[] { CoatStory.Cream }, Parent("Rowan", "sire"), Parent("Maple", "dam"));
            Assert.Equal("A surprise cream: neither parent showed it, so it was carried hidden.", hidden);
            // Hidden carriers are named only when that parent was DNA-tested.
            var tested = new CoatStoryParent("Maple", "dam", Array.Empty<string>(), new[] { CoatStory.Cream }, tested: true);
            Assert.Equal("A surprise cream: the DNA test shows Maple carried it hidden.", CoatStory.Pick("her", new[] { CoatStory.Cream }, Parent("Rowan", "sire"), tested));
        }

        [Fact]
        public void UntestedParentsNeverLeakHiddenCarriage()
        {
            // Carries is filled in but Tested is false, so the story must keep to the visible wording.
            var untested = new CoatStoryParent("Maple", "dam", Array.Empty<string>(), new[] { CoatStory.Cream }, tested: false);
            var line = CoatStory.Pick("her", new[] { CoatStory.Cream }, Parent("Rowan", "sire"), untested);
            Assert.DoesNotContain("DNA", line);
        }

        [Fact]
        public void ChestnutNeedsRedFromBothParents()
        {
            Assert.Equal("Chestnut, from both Rowan and Maple.", CoatStory.Pick("her", new[] { CoatStory.Chestnut }, Parent("Rowan", "sire", CoatStory.Chestnut), Parent("Maple", "dam", CoatStory.Chestnut)));
            Assert.Equal("Chestnut, like her dam Maple, and the other parent carried red too.", CoatStory.Pick("her", new[] { CoatStory.Chestnut }, Parent("Rowan", "sire"), Parent("Maple", "dam", CoatStory.Chestnut)));
            Assert.Equal("A surprise chestnut: neither parent showed red, so both carried it hidden.", CoatStory.Pick("her", new[] { CoatStory.Chestnut }, Parent("Rowan", "sire"), Parent("Maple", "dam")));
            var a = new CoatStoryParent("Rowan", "sire", Array.Empty<string>(), new[] { CoatStory.Chestnut }, true); var b = new CoatStoryParent("Maple", "dam", Array.Empty<string>(), new[] { CoatStory.Chestnut }, true);
            Assert.Equal("A surprise chestnut: the DNA tests show both parents carried red.", CoatStory.Pick("her", new[] { CoatStory.Chestnut }, a, b));
        }

        [Fact]
        public void MissingParentsSayNothingUnlessAKnownParentShowsTheTrait()
        {
            Assert.Null(CoatStory.Pick("her", new[] { CoatStory.Tobiano }, null, null));
            Assert.Null(CoatStory.Pick("her", new[] { CoatStory.Tobiano }, Parent("Rowan", "sire"), null));
            Assert.Equal("Tobiano, like his sire Rowan.", CoatStory.Pick("his", new[] { CoatStory.Tobiano }, Parent("Rowan", "sire", CoatStory.Tobiano), null));
            Assert.Null(CoatStory.Pick("her", Array.Empty<string>(), Parent("Rowan", "sire"), Parent("Maple", "dam")));
        }

        [Fact]
        public void TheFirstExplainableTraitWins()
        {
            var line = CoatStory.Pick("her", new[] { CoatStory.Chestnut, CoatStory.Tobiano }, Parent("Rowan", "sire", CoatStory.Tobiano), Parent("Maple", "dam"));
            Assert.Equal("Tobiano, like her sire Rowan.", line);
        }

        [Fact]
        public void TestedCarriesComeOnlyFromRecordedCalls()
        {
            var state = Starter(); var horse = state.Horses.First();
            Assert.Empty(CoatStory.TestedCarries(horse));
            var calls = DnaTest.Loci.Where(horse.Genome.HasLocus).Select(l => new KeyValuePair<string, AllelePair>(l, horse.Genome.GetAlleles(l)));
            var tested = new HorseRecord(horse.HorseId, horse.Name, horse.Parentage, horse.Genome, horse.ResolverVersion, horse.AppearanceSeed, horse.OwnerId, horse.LifeState,
                horse.ReproductionState, horse.DevelopmentalFactors, horse.InheritedGamePotential, horse.TrainingState, new PlayerKnowledgeRecord(calls, horse.PlayerKnowledge.KnownAncestors),
                horse.CosmeticState, horse.HomeAssignment, horse.OriginRecord, horse.OriginCommitId, horse.BirthDay, horse.Revision);
            Assert.True(DnaTest.IsTested(tested));
            var truth = horse.Genome.GetAlleles("mc1r"); var redCarrier = truth.A == "e" || truth.B == "e";
            Assert.Equal(redCarrier, CoatStory.TestedCarries(tested).Contains(CoatStory.Chestnut));
        }

        [Fact]
        public void VisibleTraitsMatchTheVisibleCoatLabel()
        {
            var state = Starter();
            foreach (var horse in state.Horses)
            {
                var traits = CoatStory.VisibleTraits(horse); var label = HorseProfiles.For(state, horse.HorseId).VisibleCoat.ToLowerInvariant();
                Assert.Equal(label.Contains("tobiano"), traits.Contains(CoatStory.Tobiano));
                Assert.Equal(label.Contains("roan"), traits.Contains(CoatStory.Roan));
                Assert.Equal(label.Contains("grey"), traits.Contains(CoatStory.Grey));
            }
        }
    }
}
