using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class PhenotypeTests
    {
        internal static CoatSupportMatrix Matrix(bool testReleasePhase1 = false)
        {
            using var file = FixtureFiles.Load("COAT_SUPPORT_MATRIX.json"); var root = file.RootElement;
            SupportLevel Level(string value) => Enum.Parse<SupportLevel>(value, true);
            var rows = root.GetProperty("coatClasses").EnumerateArray().Select(row =>
            {
                var promote = testReleasePhase1 && row.GetProperty("phase").GetInt32() == 1;
                return new CoatClassSupport(row.GetProperty("id").GetString()!, row.GetProperty("layers").EnumerateArray().Select(v => v.GetString()!),
                    promote ? SupportLevel.Released : Level(row.GetProperty("geneticStatus").GetString()!),
                    promote ? SupportLevel.Released : Level(row.GetProperty("visualStatus").GetString()!),
                    promote ? SupportLevel.Released : Level(row.GetProperty("releaseStatus").GetString()!));
            }).ToArray();
            return new CoatSupportMatrix(root.GetProperty("formatVersion").GetString()! + (testReleasePhase1 ? ".test-only" : ""),
                root.GetProperty("layers").EnumerateArray().Select(v => v.GetProperty("id").GetString()!), rows);
        }
        [Fact]
        public void DunThreeToOneAndNd1PrimitiveMarksMatchTheSliceRules()
        {
            var a = FrozenFixtureData.Horse(FrozenFixtureData.With(("tbx3", "D", "nd2")), 1); var b = FrozenFixtureData.Horse(a.Genome, 2, ReproductiveRole.Stallion);
            var distribution = FrozenGeneticsTests.Distribution(a, b);
            Assert.Equal(new Rational(3, 4), distribution["bay_dun"]); Assert.Equal(new Rational(1, 4), distribution["bay"]);
            var normal = FrozenGeneticsTests.Resolve(FrozenFixtureData.Genome()); var nd1 = FrozenGeneticsTests.Resolve(FrozenFixtureData.With(("tbx3", "nd1", "nd2")));
            Assert.Equal(normal.BasePigmentRecipe, nd1.BasePigmentRecipe); Assert.Equal("none", nd1.DilutionExpression); Assert.True(nd1.PrimitiveMarkingStrength > 0);
            Assert.DoesNotContain("dun_dilution", nd1.CoatLayers); Assert.Contains("primitive_markings", nd1.CoatLayers);
        }
        [Fact]
        public void BaseAndCreamLociComposeWithoutDroppingRecessiveOutcomes()
        {
            var a = FrozenFixtureData.Horse(FrozenFixtureData.With(("mc1r", "E", "e"), ("asip", "A", "a"), ("slc45a2", "N", "Cr")), 1);
            var b = FrozenFixtureData.Horse(FrozenFixtureData.With(("mc1r", "e", "e"), ("asip", "a", "a")), 2, ReproductiveRole.Stallion);
            var actual = FrozenGeneticsTests.Distribution(a, b); Assert.Equal(6, actual.Count);
            foreach (var label in new[] { "bay", "buckskin", "black", "smoky_black" }) Assert.Equal(new Rational(1, 8), actual[label]);
            Assert.Equal(new Rational(1, 4), actual["chestnut"]); Assert.Equal(new Rational(1, 4), actual["palomino"]);
        }
        [Fact]
        public void DoubleCreamAffectsSkinAndEyesWhileHiddenPearlDoesNotLeakIntoVisibleLabels()
        {
            var pale = FrozenGeneticsTests.Resolve(FrozenFixtureData.With(("mc1r", "e", "e"), ("slc45a2", "Cr", "Cr")));
            Assert.Equal("pink", pale.SkinExpression); Assert.Equal("blue", pale.EyeExpression); Assert.Contains("cream_double", pale.CoatLayers);
            var carrier = FrozenGeneticsTests.Resolve(FrozenFixtureData.With(("mc1r", "e", "e"), ("slc45a2", "N", "Prl")));
            Assert.Equal("chestnutCarryingPearl", carrier.DiagnosticCoatClass); Assert.Equal("Chestnut", PhenotypeResolver.VisibleLabel(carrier));
        }
        [Fact]
        public void GreyChangesPresentationNotGenomeOrPermanentMarkings()
        {
            var genome = FrozenFixtureData.With(("stx17", "G", "N")); var resolver = new PhenotypeResolver();
            var foal = resolver.ResolveGenetics(FrozenFixtureData.Horse(genome, stage: LifeStage.Foal, age: VisualAge.Foal)).Phenotype!;
            var adult = resolver.ResolveGenetics(FrozenFixtureData.Horse(genome)).Phenotype!;
            var senior = resolver.ResolveGenetics(FrozenFixtureData.Horse(genome, age: VisualAge.Senior)).Phenotype!;
            Assert.True(foal.GrayAmount < adult.GrayAmount && adult.GrayAmount < senior.GrayAmount);
            Assert.Equal(foal.GenomeHash, senior.GenomeHash); Assert.Equal(foal.MarkingRealizationKey, senior.MarkingRealizationKey);
            Assert.NotEqual(foal.InputHash, adult.InputHash);
        }
        [Fact]
        public void UnsupportedVersionsAllelesAndAdvancedInteractionsHaveNoFallback()
        {
            var resolver = new PhenotypeResolver(); var original = FrozenFixtureData.Horse(FrozenFixtureData.Genome());
            var unknownVersion = new HorseRecord(original.HorseId, original.Name, original.Parentage, original.Genome, "unknown-resolver", original.AppearanceSeed,
                original.OwnerId, original.LifeState, original.ReproductionState);
            Assert.Equal(ResolutionStatus.Unsupported, resolver.ResolveGenetics(unknownVersion).Status);
            Assert.Equal(ResolutionStatus.Unsupported, resolver.ResolveGenetics(FrozenFixtureData.Horse(FrozenFixtureData.With(("pattern.eca3", "tobiano", "baseline")))).Status);
            Assert.Equal(ResolutionStatus.Unsupported, resolver.ResolveGenetics(FrozenFixtureData.Horse(FrozenFixtureData.With(("pmel", "Z", "N"), ("stx17", "G", "N")))).Status);
        }
        [Fact]
        public void RealMatrixReleasesThePhase1ClassesAndTheD026Overlays()
        {
            // D-013 released the 13 phase-1 classes; D-026 released cream-dun stacking and the tobiano, roan, grey and
            // leopard-complex overlays. Everything else stays unreleased.
            var matrix = Matrix();
            using var file = FixtureFiles.Load("COAT_SUPPORT_MATRIX.json");
            foreach (var row in file.RootElement.GetProperty("coatClasses").EnumerateArray())
            {
                var by = row.TryGetProperty("releasedBy", out var value) ? value.GetString() : null;
                Assert.Equal(row.GetProperty("phase").GetInt32() == 1 || by == "D-026", matrix.Classes[row.GetProperty("id").GetString()!].Release == SupportLevel.Released);
            }
            Assert.Equal(new[] { "dunalino_family", "double_cream_dun", "grey_overlay", "leopard_complex_overlay", "roan_overlay", "tobiano_overlay" }.OrderBy(x => x, StringComparer.Ordinal),
                file.RootElement.GetProperty("coatClasses").EnumerateArray().Where(r => r.TryGetProperty("releasedBy", out var v) && v.GetString() == "D-026")
                    .Select(r => r.GetProperty("id").GetString()!).OrderBy(x => x, StringComparer.Ordinal));
            var bay = new PhenotypeResolver().ResolveForPresentation(FrozenFixtureData.Horse(FrozenFixtureData.Genome()), matrix, "assets.v1", "anatomy.v1");
            Assert.Equal(ResolutionStatus.Supported, bay.Status); Assert.Equal("Bay", bay.DisplayLabel);
            Assert.Empty(matrix.ReleasedSupportProblems(new[] { "buckskin", "dunalino_family" }, new[] { "base_pigment", "cream_single", "dun_dilution", "primitive_markings" }));
            Assert.NotEmpty(matrix.ReleasedSupportProblems(new[] { "bay" }, new[] { "base_pigment", "silver" }));
        }
        [Fact]
        public void TestOnlyReleaseFixtureMakesStableDescriptorsWithoutReleasingRealArt()
        {
            var resolver = new PhenotypeResolver(); var horse = FrozenFixtureData.Horse(FrozenFixtureData.Genome()); var matrix = Matrix(true);
            var a = resolver.ResolveForPresentation(horse, matrix, "test-assets.v1", "test-anatomy.v1");
            var b = resolver.ResolveForPresentation(horse, matrix, "test-assets.v1", "test-anatomy.v1");
            Assert.Equal(ResolutionStatus.Supported, a.Status); Assert.Equal(a.Descriptor!.CacheKey, b.Descriptor!.CacheKey); Assert.Equal("Bay", a.DisplayLabel);
            var cosmetic = resolver.ResolveForPresentation(horse, matrix, "test-assets.v1", "test-anatomy.v1", "lavender-ribbon").Descriptor!;
            Assert.Equal(a.Descriptor.MarkingRealizationKey, cosmetic.MarkingRealizationKey); Assert.NotEqual(a.Descriptor.CacheKey, cosmetic.CacheKey);
            var newAssets = resolver.ResolveForPresentation(horse, matrix, "test-assets.v2", "test-anatomy.v1").Descriptor!;
            Assert.NotEqual(a.Descriptor.CacheKey, newAssets.CacheKey);
        }
        [Fact]
        public void AnUnreleasedLayerCannotSneakThroughAReleasedBaseClass()
        {
            Assert.Throws<ArgumentException>(() => new CoatClassSupport("bay", new[] { "base_pigment" }, SupportLevel.Approved, SupportLevel.None, SupportLevel.Released));
            Assert.NotEmpty(Matrix(true).ReleasedSupportProblems(new[] { "bay" }, new[] { "base_pigment", "silver" }));
            // Silver resolves genetically but has no released class, so it never reaches a descriptor (D-026 kept it unreleased).
            var horse = FrozenFixtureData.Horse(FrozenFixtureData.With(("pmel", "Z", "N")));
            var result = new PhenotypeResolver().ResolveForPresentation(horse, Matrix(true), "test", "test");
            Assert.Equal(ResolutionStatus.Unsupported, result.Status); Assert.Contains(result.Reasons, r => r.Contains("silver_overlay"));
        }
        [Fact]
        public void ReachabilityGuardCatchesUnreleasedCombinationsBeforeAnyRoll()
        {
            // D-026 released cream x dun stacking, so the guard is exercised with silver, which stays unreleased.
            var a = FrozenFixtureData.Horse(FrozenFixtureData.With(("pmel", "Z", "N")), 1);
            var b = FrozenFixtureData.Horse(FrozenFixtureData.With(("tbx3", "D", "nd2")), 2, ReproductiveRole.Stallion);
            var check = LiveCoatGuard.CheckPair(a, b, Matrix(true));
            Assert.True(check.EvaluatedAll); Assert.False(check.IsSupported); Assert.Contains(check.Reasons, r => r.Contains("silver") || r.Contains("advanced modifier"));
            var creamDun = LiveCoatGuard.CheckPair(FrozenFixtureData.Horse(FrozenFixtureData.With(("mc1r", "e", "e"), ("slc45a2", "Cr", "N")), 1), b, Matrix());
            Assert.True(creamDun.IsSupported, string.Join("; ", creamDun.Reasons));
            var safe = FrozenFixtureData.Parents(FrozenFixtureData.Find("palominoPair"));
            Assert.True(LiveCoatGuard.CheckPair(safe.A, safe.B, Matrix(true)).IsSupported);
            Assert.True(LiveCoatGuard.CheckPair(safe.A, safe.B, Matrix()).IsSupported); // palomino family released by D-013
            Assert.False(LiveCoatGuard.CheckPair(safe.A, safe.B, Matrix(true), 1).EvaluatedAll);
        }
        [Fact]
        public void FictionalTraitsAreBoundedStableAndIndependentOfCoatAndTraining()
        {
            var resolver = new PhenotypeResolver(); var normal = FrozenFixtureData.Genome();
            var rare = FrozenFixtureData.With(("mc1r", "e", "e"), ("slc45a2", "Cr", "Cr"));
            var training = new QuantizedVector(new Dictionary<string, int> { ["speed"] = 10000 });
            var a = resolver.ResolveGenetics(FrozenFixtureData.Horse(normal)); var b = resolver.ResolveGenetics(FrozenFixtureData.Horse(rare, training: training));
            Assert.Equal(a.Traits!.Inherited.Hash, b.Traits!.Inherited.Hash); Assert.Equal(a.Traits.Morphology.Hash, b.Traits.Morphology.Hash);
            var seed = new Seed128(17, 29); var factors = GameTraitResolver.CreateDevelopment(seed);
            Assert.Equal(factors.Hash, GameTraitResolver.CreateDevelopment(seed).Hash);
            var extremes = Enumerable.Range(0, 32).Select(i => ("game.marker." + i.ToString("D2"), "1", "1")).ToArray();
            var maximum = resolver.ResolveGenetics(FrozenFixtureData.Horse(FrozenFixtureData.With(extremes), factors: factors));
            Assert.Equal(ResolutionStatus.Supported, maximum.Status); Assert.All(maximum.Traits!.Morphology.Values.Values, v => Assert.InRange(v, -2000, 2000));
            Assert.All(maximum.Traits.Inherited.Values.Values, v => Assert.InRange(v, 0, 10000));
            var invalidFactors = new QuantizedVector(new Dictionary<string, int> { ["unmodeled"] = 1 });
            Assert.Equal(ResolutionStatus.Unsupported, resolver.ResolveGenetics(FrozenFixtureData.Horse(normal, factors: invalidFactors)).Status);
        }
    }
}
