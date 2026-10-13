using System;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    /// <summary>D-026 tobiano, roan, grey and leopard complex overlays plus cream-dun stacking.</summary>
    public sealed class PatternFamilyTests
    {
        private static readonly PhenotypeResolver Resolver = new PhenotypeResolver();
        private static Phenotype Resolve(params (string, string, string)[] calls) => Resolver.ResolveGenetics(FrozenFixtureData.Horse(FrozenFixtureData.With(calls))).Phenotype!;

        private static Rational Share(HorseRecord a, HorseRecord b, Func<Phenotype, bool> test)
        {
            var total = Rational.Zero;
            foreach (var outcome in new MeiosisService(CoreGeneticsCatalog.Model).EnumerateOffspring(a, b))
            {
                var child = FrozenFixtureData.Horse(outcome.Value, 300);
                if (test(Resolver.ResolveGenetics(child).Phenotype!)) total += outcome.Probability;
            }
            return total;
        }

        [Fact]
        public void TobianoAndRoanAreDominantAndHomozygotesBreedTrue()
        {
            var tobiano = FrozenFixtureData.Horse(FrozenFixtureData.With(("pattern.eca3", "TO", "baseline")), 1);
            var plain = FrozenFixtureData.Horse(FrozenFixtureData.Genome(), 2, ReproductiveRole.Stallion);
            Assert.Equal(new Rational(1, 2), Share(tobiano, plain, p => p.CoatLayers.Contains("white_tobiano")));
            var homozygous = FrozenFixtureData.Horse(FrozenFixtureData.With(("pattern.eca3", "TO", "TO")), 3);
            Assert.Equal(Rational.One, Share(homozygous, plain, p => p.CoatLayers.Contains("white_tobiano")));
            var roan = FrozenFixtureData.Horse(FrozenFixtureData.With(("pattern.eca3", "Rn", "baseline")), 4);
            Assert.Equal(new Rational(1, 2), Share(roan, plain, p => p.CoatLayers.Contains("roan")));
            // A tobiano x roan cross: one haplotype each, so a quarter of foals carry both patterns.
            Assert.Equal(new Rational(1, 4), Share(tobiano, roan, p => p.CoatLayers.Contains("roan") && p.CoatLayers.Contains("white_tobiano")));
        }

        [Fact]
        public void OverlaysComposeOverDilutionsAndEachOtherAndPresentWithTheRealMatrix()
        {
            var matrix = PhenotypeTests.Matrix();
            var horse = FrozenFixtureData.Horse(FrozenFixtureData.With(("slc45a2", "Cr", "N"), ("tbx3", "D", "nd2"), ("stx17", "G", "N"),
                ("pattern.eca3", "TO", "baseline"), ("trpm1", "LP", "N"), ("patn1", "PATN1", "N")));
            var result = Resolver.ResolveForPresentation(horse, matrix, "assets.v1", "anatomy.v1");
            Assert.Equal(ResolutionStatus.Supported, result.Status);
            foreach (var layer in new[] { "cream_single", "dun_dilution", "grey_age", "white_tobiano", "leopard_complex" })
                Assert.Contains(layer, result.Descriptor!.CoatLayers);
            Assert.Equal("Dunskin tobiano leopard appaloosa (progressive grey)", result.DisplayLabel);
        }

        [Fact]
        public void SilverChampagneAndPearlStillRefuseEveryCombination()
        {
            foreach (var overlay in new[] { ("stx17", "G", "N"), ("pattern.eca3", "TO", "baseline"), ("pattern.eca3", "Rn", "baseline"), ("trpm1", "LP", "N") })
            {
                var horse = FrozenFixtureData.Horse(FrozenFixtureData.With(("pmel", "Z", "N"), overlay));
                Assert.Equal(ResolutionStatus.Unsupported, Resolver.ResolveGenetics(horse).Status);
            }
            Assert.NotEmpty(PhenotypeTests.Matrix().ReleasedSupportProblems(new[] { "bay", "silver_overlay" }, new[] { "base_pigment", "silver" }));
        }

        [Fact]
        public void LabelsFollowHorsePeopleNaming()
        {
            Assert.Equal("Bay roan", PhenotypeResolver.VisibleLabel(Resolve(("pattern.eca3", "Rn", "baseline"))));
            Assert.Equal("Blue roan", PhenotypeResolver.VisibleLabel(Resolve(("asip", "a", "a"), ("pattern.eca3", "Rn", "baseline"))));
            Assert.Equal("Red roan", PhenotypeResolver.VisibleLabel(Resolve(("mc1r", "e", "e"), ("pattern.eca3", "Rn", "baseline"))));
            Assert.Equal("Chestnut blanket appaloosa", PhenotypeResolver.VisibleLabel(Resolve(("mc1r", "e", "e"), ("trpm1", "LP", "N"))));
            Assert.Equal("Black leopard appaloosa", PhenotypeResolver.VisibleLabel(Resolve(("asip", "a", "a"), ("trpm1", "LP", "N"), ("patn1", "PATN1", "N"))));
            Assert.Equal("Cremello dun", PhenotypeResolver.VisibleLabel(Resolve(("mc1r", "e", "e"), ("slc45a2", "Cr", "Cr"), ("tbx3", "D", "nd2"))));
            Assert.Equal("Dunalino", PhenotypeResolver.VisibleLabel(Resolve(("mc1r", "e", "e"), ("slc45a2", "Cr", "N"), ("tbx3", "D", "nd2"))));
        }

        [Fact]
        public void GreyLightensWithAgeAndFoalsKeepTheirBirthColour()
        {
            var g = FrozenFixtureData.With(("stx17", "G", "N"));
            int At(int days, VisualAge age, LifeStage stage = LifeStage.Adult) => Resolver.ResolveGenetics(FrozenFixtureData.Horse(g, days: days, stage: stage, age: age)).Phenotype!.GrayAmount;
            Assert.Equal(0, At(5, VisualAge.Foal, LifeStage.Foal));
            Assert.True(At(10, VisualAge.Adult) < At(40, VisualAge.Adult) && At(40, VisualAge.Adult) < At(100, VisualAge.Adult));
            Assert.True(At(100000, VisualAge.Adult) < 9000 && At(10, VisualAge.Senior) >= 9000);
        }

        [Fact]
        public void ExistingBaselineGenomesResolveExactlyAsBefore()
        {
            // Additive release: a plain bay keeps its class, layers and label (phenotype.v1 is not bumped).
            var p = Resolve();
            Assert.Equal("bay", p.CoatClassId);
            Assert.Equal(new[] { "base_pigment", "individual_white_markings", "shading_modifiers" }, p.CoatLayers);
            Assert.Equal("Bay", PhenotypeResolver.VisibleLabel(p));
        }
    }
}
