using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Cloverbrook.Domain.Tests
{
    /// <summary>D-037: silver, champagne, pearl and cream-pearl, sabino-1 and splash white.</summary>
    public sealed class RarerCoatFamilyTests
    {
        private readonly ITestOutputHelper output;
        public RarerCoatFamilyTests(ITestOutputHelper output) { this.output = output; }
        private static readonly PhenotypeResolver Resolver = new PhenotypeResolver();
        private static Phenotype Resolve(params (string, string, string)[] calls) => Resolver.ResolveGenetics(FrozenFixtureData.Horse(FrozenFixtureData.With(calls))).Phenotype!;
        private static string Label(params (string, string, string)[] calls) => PhenotypeResolver.VisibleLabel(Resolve(calls));
        private static readonly (string, string, string) Chestnut = ("mc1r", "e", "e"), Black = ("asip", "a", "a");

        private static Rational Share(HorseRecord a, HorseRecord b, Func<Phenotype, bool> test)
        {
            var total = Rational.Zero;
            foreach (var outcome in new MeiosisService(CoreGeneticsCatalog.Model).EnumerateOffspring(a, b))
                if (test(Resolver.ResolveGenetics(FrozenFixtureData.Horse(outcome.Value, 300)).Phenotype!)) total += outcome.Probability;
            return total;
        }

        [Fact]
        public void EveryGenomeThatResolvedBeforeResolvesIdentically()
        {
            var (count, digest) = LegacyCoatGrid.Compute();
            Assert.Equal(4320, count);
            Assert.Equal(LegacyCoatGrid.BaselineDigest, digest);
        }

        [Fact]
        public void ExistingAllelesKeepTheirPositionAndOnlyNewOnesAreAdded()
        {
            var kit = CoreGeneticsCatalog.Model.Blocks["eca3.core"].Loci.Single(l => l.LocusId == "pattern.eca3").Variant.SupportedAlleleIds;
            Assert.Equal(new[] { "baseline", "TO", "Rn", "SB1" }, kit);
            Assert.Equal(new[] { "baseline", "SW1" }, CoreGeneticsCatalog.Model.Blocks["pattern.mitf"].Loci.Single().Variant.SupportedAlleleIds);
            Assert.Equal(new[] { "baseline" }, CoreGeneticsCatalog.Model.Blocks["pattern.pax3"].Loci.Single().Variant.SupportedAlleleIds);
            Assert.Equal("phenotype.v1", Resolver.Version);
        }

        [Fact]
        public void SilverShowsOnBlackAndBayAndIsMaskedOnChestnut()
        {
            var silverBlack = Resolve(Black, ("pmel", "Z", "N")); var silverBay = Resolve(("pmel", "Z", "N"));
            Assert.Contains("silver", silverBlack.CoatLayers); Assert.Equal("silver_lightened_mane_tail", silverBlack.HairExpression);
            Assert.Equal("Silver black", PhenotypeResolver.VisibleLabel(silverBlack)); Assert.Equal("Silver bay", PhenotypeResolver.VisibleLabel(silverBay));
            var masked = Resolve(Chestnut, ("pmel", "Z", "Z"));
            Assert.DoesNotContain("silver", masked.CoatLayers); Assert.Equal("Chestnut", PhenotypeResolver.VisibleLabel(masked));
            Assert.Equal(Resolve(Chestnut).HairExpression, masked.HairExpression);
        }

        [Fact]
        public void ChampagneNamesGoldAmberAndClassicWithSkinAndEyes()
        {
            Assert.Equal("Gold champagne", Label(Chestnut, ("slc36a1", "Ch", "N")));
            Assert.Equal("Amber champagne", Label(("slc36a1", "Ch", "N")));
            Assert.Equal("Classic champagne", Label(Black, ("slc36a1", "Ch", "Ch")));
            var amber = Resolve(("slc36a1", "Ch", "N"));
            Assert.Equal("champagne_mottled", amber.SkinExpression); Assert.Equal("champagne_amber", amber.EyeExpression);
            Assert.Equal("champagne", amber.DilutionExpression); Assert.Contains("champagne", amber.CoatLayers);
            // Over another dilution it layers on that dilution's recipe and keeps the stronger dilution's skin and eyes.
            var over = Resolve(Chestnut, ("slc36a1", "Ch", "N"), ("slc45a2", "Cr", "Cr"));
            Assert.Equal("cream_double", over.DilutionExpression); Assert.Equal("pink", over.SkinExpression); Assert.Equal("blue", over.EyeExpression);
            Assert.Equal("Champagne cremello", PhenotypeResolver.VisibleLabel(over));
            Assert.Equal("Champagne buckskin", Label(("slc36a1", "Ch", "N"), ("slc45a2", "Cr", "N")));
        }

        [Fact]
        public void PearlIsRecessiveAndCreamPearlIsThePseudoDoubleDilute()
        {
            Assert.Equal("Chestnut", Label(Chestnut, ("slc45a2", "N", "Prl")));
            Assert.Equal("Pearl chestnut", Label(Chestnut, ("slc45a2", "Prl", "Prl")));
            Assert.Equal("Pearl bay", Label(("slc45a2", "Prl", "Prl")));
            Assert.Equal("Cream pearl black", Label(Black, ("slc45a2", "Cr", "Prl")));
            var pearl = Resolve(("slc45a2", "Prl", "Prl")); var compound = Resolve(("slc45a2", "Cr", "Prl"));
            Assert.Equal("pearl", pearl.DilutionExpression); Assert.Equal("cream_pearl", compound.DilutionExpression);
            Assert.Equal("pearl_lightened", pearl.SkinExpression); Assert.Equal("cream_pearl_lightened", compound.EyeExpression);
            Assert.Equal("Pearl bay dun", Label(("slc45a2", "Prl", "Prl"), ("tbx3", "D", "nd2")));
            Assert.Equal("pearl+dun", Resolve(("slc45a2", "Prl", "Prl"), ("tbx3", "D", "nd2")).DilutionExpression);
            var a = FrozenFixtureData.Horse(FrozenFixtureData.With(("slc45a2", "N", "Prl")), 1); var b = FrozenFixtureData.Horse(a.Genome, 2, ReproductiveRole.Stallion);
            Assert.Equal(new Rational(1, 4), Share(a, b, p => p.CoatClassId == "pearl"));
        }

        [Fact]
        public void Sabino1IsDominantOnTheLinkedKitHaplotypeAndHomozygotesAreMostlyWhite()
        {
            var one = Resolve(("pattern.eca3", "SB1", "baseline")); var two = Resolve(("pattern.eca3", "SB1", "SB1"));
            Assert.Contains("white_sabino1", one.CoatLayers); Assert.DoesNotContain("white_sabino1_double", one.CoatLayers);
            Assert.Contains("white_sabino1_double", two.CoatLayers);
            Assert.Equal("Bay sabino", PhenotypeResolver.VisibleLabel(one)); Assert.Equal("Bay sabino white", PhenotypeResolver.VisibleLabel(two));
            Assert.Equal("Bay tobiano sabino", Label(("pattern.eca3", "TO", "SB1")));
            var carrier = FrozenFixtureData.Horse(FrozenFixtureData.With(("pattern.eca3", "SB1", "baseline")), 1); var plain = FrozenFixtureData.Horse(FrozenFixtureData.Genome(), 2, ReproductiveRole.Stallion);
            Assert.Equal(new Rational(1, 2), Share(carrier, plain, p => p.CoatLayers.Contains("white_sabino1")));
            Assert.Equal(Rational.One, Share(FrozenFixtureData.Horse(FrozenFixtureData.With(("pattern.eca3", "SB1", "SB1")), 3), plain, p => p.CoatLayers.Contains("white_sabino1")));
            // SB1 travels with the extension allele it started on (the haplotype list allows both E and e carriers).
            var linked = FrozenFixtureData.Horse(FrozenFixtureData.With(("mc1r", "E", "e"), ("pattern.eca3", "SB1", "baseline")), 4);
            Assert.Equal(ResolutionStatus.Supported, Resolver.ResolveGenetics(linked).Status);
        }

        [Fact]
        public void SplashIsDominantAndHomozygotesAreExtensiveWithBlueEyes()
        {
            var one = Resolve(("pattern.mitf", "SW1", "baseline")); var two = Resolve(("pattern.mitf", "SW1", "SW1"));
            Assert.Contains("white_splash", one.CoatLayers); Assert.Equal("brown", one.EyeExpression);
            Assert.Contains("white_splash_double", two.CoatLayers); Assert.Equal("blue", two.EyeExpression);
            Assert.Equal("Bay splash", PhenotypeResolver.VisibleLabel(one)); Assert.Equal("Bay splash white", PhenotypeResolver.VisibleLabel(two));
            var a = FrozenFixtureData.Horse(FrozenFixtureData.With(("pattern.mitf", "SW1", "baseline")), 1); var plain = FrozenFixtureData.Horse(FrozenFixtureData.Genome(), 2, ReproductiveRole.Stallion);
            Assert.Equal(new Rational(1, 2), Share(a, plain, p => p.CoatLayers.Contains("white_splash")));
        }

        [Fact]
        public void EveryCombinationOfTheCoatLociResolvesAndPresentsWithTheRealCatalog()
        {
            // Exhaustive over the coat-affecting loci a live horse can now carry. Every combination must be supported by the real
            // matrix AND resolve to recipes that exist in the game's visual catalog, so no coat can reach the yard as a silhouette.
            var matrix = PhenotypeTests.Matrix(); var catalogPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "visual_catalog.json");
            using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath)); var root = catalog.RootElement;
            HashSet<string> Keys(string name) => root.GetProperty(name).EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var coats = Keys("coatRecipes"); var hair = Keys("hairRecipes"); var skin = Keys("skinRecipes"); var eyes = Keys("eyeRecipes");
            var layers = root.GetProperty("implementedLayers").EnumerateArray().Select(v => v.GetString()!).ToHashSet(StringComparer.Ordinal);
            var missing = new SortedSet<string>(StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal); var count = 0; var stopwatch = Stopwatch.StartNew();
            foreach (var m in new[] { ("e", "e"), ("E", "e") }) foreach (var a in new[] { ("A", "A"), ("a", "a") })
            foreach (var c in new[] { ("N", "N"), ("N", "Cr"), ("Cr", "Cr"), ("N", "Prl"), ("Prl", "Prl"), ("Cr", "Prl") }) foreach (var d in new[] { ("nd2", "nd2"), ("D", "nd2"), ("nd1", "nd2") })
            foreach (var z in new[] { ("N", "N"), ("Z", "N") }) foreach (var ch in new[] { ("N", "N"), ("Ch", "N") }) foreach (var g in new[] { ("N", "N"), ("G", "N") })
            foreach (var lp in new[] { ("N", "N"), ("LP", "N") })
            foreach (var kit in new[] { ("baseline", "baseline"), ("TO", "baseline"), ("Rn", "baseline"), ("SB1", "baseline"), ("SB1", "SB1"), ("TO", "SB1") })
            foreach (var sw in new[] { ("baseline", "baseline"), ("SW1", "baseline"), ("SW1", "SW1") })
            {
                var horse = FrozenFixtureData.Horse(FrozenFixtureData.With(("mc1r", m.Item1, m.Item2), ("asip", a.Item1, a.Item2), ("slc45a2", c.Item1, c.Item2), ("tbx3", d.Item1, d.Item2),
                    ("pmel", z.Item1, z.Item2), ("slc36a1", ch.Item1, ch.Item2), ("stx17", g.Item1, g.Item2), ("trpm1", lp.Item1, lp.Item2), ("pattern.eca3", kit.Item1, kit.Item2),
                    ("pattern.mitf", sw.Item1, sw.Item2)));
                var result = Resolver.ResolveForPresentation(horse, matrix, "reference-assets.v1", "canonical-anatomy.v1"); count++;
                Assert.True(result.Status == ResolutionStatus.Supported, string.Join("; ", result.Reasons));
                var desc = result.Descriptor!;
                foreach (var layer in desc.CoatLayers) if (!layers.Contains(layer)) missing.Add("layer " + layer);
                if (!coats.Contains(desc.CoatRecipe)) missing.Add("coat " + desc.CoatRecipe);
                if (!hair.Contains(desc.HairRecipe)) missing.Add("hair " + desc.HairRecipe);
                if (!skin.Contains(desc.SkinRecipe)) missing.Add("skin " + desc.SkinRecipe);
                if (!eyes.Contains(desc.EyeRecipe)) missing.Add("eye " + desc.EyeRecipe);
                seen.Add(result.DisplayLabel!);
            }
            output.WriteLine($"{count} genomes in {stopwatch.ElapsedMilliseconds} ms, {seen.Count} distinct labels; missing: " + string.Join(", ", missing));
            Assert.Empty(missing);
            Assert.Equal(2 * 2 * 6 * 3 * 2 * 2 * 2 * 2 * 6 * 3, count);
        }

        [Fact]
        public void PhysicalPigmentRulesStayHonest()
        {
            // Silver on a chestnut is a hidden carrier: no layer, no class, same cache-relevant recipe as plain chestnut.
            var carrier = Resolver.ResolveGenetics(FrozenFixtureData.Horse(FrozenFixtureData.With(Chestnut, ("pmel", "Z", "N")))).Phenotype!;
            var plain = Resolve(Chestnut);
            Assert.Equal(plain.CoatLayers, carrier.CoatLayers); Assert.Equal(plain.HairExpression, carrier.HairExpression);
            var silver = Resolver.ResolveGenetics(FrozenFixtureData.Horse(FrozenFixtureData.With(Black, ("pmel", "Z", "N"))));
            Assert.Contains("silver_overlay", silver.RequiredCoatClasses);
            var pearl = Resolver.ResolveGenetics(FrozenFixtureData.Horse(FrozenFixtureData.With(("slc45a2", "Prl", "Prl"))));
            Assert.Contains("pearl", pearl.RequiredCoatClasses);
        }
    }
}
