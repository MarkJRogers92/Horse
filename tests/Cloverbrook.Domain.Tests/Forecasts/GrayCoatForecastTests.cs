using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class GrayCoatForecastTests
    {
        // Helpers duplicated from CoatForecastTests to keep this test file self-contained.
        private static GenomeSnapshot Genome(params (string Locus, string A, string B)[] changes)
        {
            var edits = changes.ToDictionary(c => c.Locus, c => new AllelePair(c.A, c.B), StringComparer.Ordinal);
            string Normal(LocusDefinition l) => l.LocusId == "mc1r" ? "e" : l.LocusId == "asip" ? "a" : l.LocusId == "tbx3" ? "nd2"
                : l.LocusId.StartsWith("pattern.", StringComparison.Ordinal) ? "baseline" : l.LocusId.StartsWith("game.", StringComparison.Ordinal) ? "0" : "N";
            return new GenomeSnapshot(CoreGeneticsCatalog.Model.Blocks.Values.Select(b => new PhasedBlockPair(b.BlockId, b.IsSynthetic,
                b.Loci.Select(l => l.LocusId), b.Loci.Select(l => edits.TryGetValue(l.LocusId, out var c) ? c.A : Normal(l)),
                b.Loci.Select(l => edits.TryGetValue(l.LocusId, out var c) ? c.B : Normal(l)))), CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.SyntheticVersion);
        }
        private static HorseId Id(int n) => new HorseId(new Guid("bb000000-0000-0000-0000-" + n.ToString("D12", System.Globalization.CultureInfo.InvariantCulture)));
        private static readonly OwnerId Owner = new OwnerId(new Guid("bb000000-0000-0000-0001-000000000001"));
        private static HorseRecord Horse(int n, GenomeSnapshot genome, ReproductiveRole role = ReproductiveRole.Mare) =>
            new HorseRecord(Id(n), "Test horse " + n, new Parentage(founderBackground: new FounderBackground("test-founders", Rational.Zero)), genome,
                CoreGeneticsCatalog.ResolverVersion, default, Owner, new LifeState(LifeStage.Adult, 20, VisualAge.Adult), new ReproductionState(role));
        private static PublicCoatKnowledge Known(int n, GenomeSnapshot genome, params string[] omit) => new PublicCoatKnowledge(Id(n),
            new PlayerKnowledgeRecord(genome.BiologicalBlocks.SelectMany(b => b.LocusIds).Where(l => !omit.Contains(l))
                .Select(l => new KeyValuePair<string, AllelePair>(l, genome.GetAlleles(l))), Array.Empty<HorseId>()));
        // Builds knowledge from a reference genome, overriding specific loci and omitting
        // selected loci so they remain unknown. All other biological loci are revealed
        // as they appear in the reference genome.
        private static PlayerKnowledgeRecord PartialKnowledge(GenomeSnapshot reference,
            (string Locus, string A, string B)[] overrides,
            string[]? omit = null)
        {
            var omitted = overrides.Select(o => o.Locus).Concat(omit ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            var calls = reference.BiologicalBlocks.SelectMany(b => b.LocusIds).Where(l => !omitted.Contains(l))
                .Select(l => new KeyValuePair<string, AllelePair>(l, reference.GetAlleles(l)))
                .ToList();
            foreach (var o in overrides) calls.Add(new KeyValuePair<string, AllelePair>(o.Locus, new AllelePair(o.A, o.B)));
            return new PlayerKnowledgeRecord(calls, Array.Empty<HorseId>());
        }
        private static CoatSupportMatrix TestMatrix(params string[] classes)
        {
            var layers = new[] { "base_pigment", "cream_single", "cream_double", "dun_dilution", "primitive_markings", "grey_age", "individual_white_markings", "shading_modifiers" };
            var all = classes.Concat(new[] { "grey_overlay" }).Distinct(StringComparer.Ordinal);
            return new CoatSupportMatrix("TEST-ONLY-RELEASE", layers, all.Select(c => new CoatClassSupport(c, layers, SupportLevel.Released, SupportLevel.Released, SupportLevel.Released)));
        }

        [Fact]
        public void KnownGrayHeterozygoteOverBayPreservesUnderlyingColourAndGivesExactProbabilities()
        {
            var grayBay = Genome(("mc1r", "E", "e"), ("asip", "A", "a"), ("stx17", "G", "N"));
            var chestnut = Genome(("mc1r", "e", "e"), ("asip", "a", "a"));
            var matrix = TestMatrix("bay", "chestnut", "black");
            var service = new CoatForecastService(matrix);
            var dam = Known(1, grayBay);
            var sire = Known(2, chestnut);
            var result = service.ForKnownParents(dam, sire, PublicCoatScope.AllSupported());

            Assert.Equal(ForecastAuthority.PlayerKnowledge, result.Authority);
            Assert.Equal(ForecastMethod.Exact, result.Method);

            // Six mutually exclusive outcomes: each base coat may inherit G or not.
            var grayBayOutcome = result.Outcomes.Single(o => o.DisplayLabel == "Gray (Bay)");
            var bayOutcome = result.Outcomes.Single(o => o.Label == "Bay");
            var grayBlackOutcome = result.Outcomes.Single(o => o.DisplayLabel == "Gray (Black)");
            var blackOutcome = result.Outcomes.Single(o => o.Label == "Black");
            var grayChestnutOutcome = result.Outcomes.Single(o => o.DisplayLabel == "Gray (Chestnut)");
            var chestnutOutcome = result.Outcomes.Single(o => o.Label == "Chestnut");

            Assert.Equal("bay", grayBayOutcome.BaseCoatClass);
            Assert.Equal("black", grayBlackOutcome.BaseCoatClass);
            Assert.Equal("chestnut", grayChestnutOutcome.BaseCoatClass);
            Assert.Contains("grey_age", grayBayOutcome.OverlayLabels);
            Assert.DoesNotContain("grey_age", bayOutcome.OverlayLabels);

            // Gg x nn -> 50% gray carriers; chestnut sire contributes e and a.
            // Bay: 1/4, Black: 1/4, Chestnut: 1/2 before gray; each is split 50/50 by G.
            Assert.Equal(new Rational(1, 8), grayBayOutcome.ExactProbability);
            Assert.Equal(new Rational(1, 8), bayOutcome.ExactProbability);
            Assert.Equal(new Rational(1, 8), grayBlackOutcome.ExactProbability);
            Assert.Equal(new Rational(1, 8), blackOutcome.ExactProbability);
            Assert.Equal(new Rational(1, 4), grayChestnutOutcome.ExactProbability);
            Assert.Equal(new Rational(1, 4), chestnutOutcome.ExactProbability);
            Assert.Equal(Rational.Half, result.Outcomes.Where(o => o.DisplayLabel.StartsWith("Gray (", StringComparison.Ordinal)).Aggregate(Rational.Zero, (sum, o) => sum + o.ExactProbability!.Value));
            Assert.Equal(Rational.Half, result.Outcomes.Where(o => !o.DisplayLabel.StartsWith("Gray (", StringComparison.Ordinal)).Aggregate(Rational.Zero, (sum, o) => sum + o.ExactProbability!.Value));
            Assert.Equal(Rational.One, result.Outcomes.Aggregate(Rational.Zero, (sum, o) => sum + o.ExactProbability!.Value));
        }

        [Fact]
        public void ObservedGrayWithUnknownGenotypeProducesConditionalRangesAndPreservesUnderlyingBaseCoat()
        {
            var defaultGenome = Genome();
            var chestnut = Genome(("mc1r", "e", "e"), ("asip", "a", "a"));
            var matrix = TestMatrix("bay", "chestnut", "black");
            var service = new CoatForecastService(matrix);

            // Parent observed as gray over bay. E and G are revealed, all dilutions and
            // patterns are known absent, but agouti is omitted so the forecast must use
            // conditional ranges rather than invent a prior.
            var observedKnowledge = PartialKnowledge(defaultGenome,
                new[] { ("mc1r", "E", "e"), ("stx17", "G", "N") },
                new[] { "asip" });
            var observed = new PublicCoatKnowledge(Id(1), observedKnowledge, "Bay (progressive grey)");
            var sire = Known(2, chestnut);
            var result = service.ForKnownParents(observed, sire, PublicCoatScope.AllSupported());

            Assert.Equal(ForecastAuthority.PlayerKnowledge, result.Authority);
            Assert.Equal(ForecastMethod.ConditionalRanges, result.Method);

            // Gray outcomes preserve the underlying base coat even when genotype is uncertain.
            var grayOutcomes = result.Outcomes.Where(o => o.DisplayLabel.StartsWith("Gray (", StringComparison.Ordinal)).ToList();
            Assert.All(grayOutcomes, o => Assert.NotNull(o.BaseCoatClass));
            Assert.Contains(grayOutcomes, o => o.BaseCoatClass == "bay");

            // Ranges reflect that the gray parent's agouti genotype is unrevealed.
            var grayBayOutcome = result.Outcomes.Single(o => o.DisplayLabel == "Gray (Bay)");
            Assert.Null(grayBayOutcome.ExactProbability);
            Assert.True(grayBayOutcome.LowerProbability < grayBayOutcome.UpperProbability);
            Assert.True(grayBayOutcome.LowerProbability >= 0);
            Assert.True(grayBayOutcome.UpperProbability <= 1);
        }

        [Fact]
        public void SameUnderlyingCoatWithAndWithoutGrayAreSeparateNormalizedOutcomes()
        {
            var grayBay = Genome(("mc1r", "E", "e"), ("asip", "A", "a"), ("stx17", "G", "N"));
            var chestnut = Genome(("mc1r", "e", "e"), ("asip", "a", "a"));
            var matrix = TestMatrix("bay", "chestnut", "black");
            var result = new CoatForecastService(matrix).ForKnownParents(Known(1, grayBay), Known(2, chestnut), PublicCoatScope.AllSupported());

            // Each base-coat group is split 50/50 by inherited G; gray and non-gray
            // versions are mutually exclusive, not stacked probabilities.
            var bayGroup = result.Outcomes.Where(o => o.BaseCoatClass == "bay").ToList();
            Assert.Equal(2, bayGroup.Count);
            Assert.Equal(new Rational(1, 4), bayGroup.Aggregate(Rational.Zero, (sum, o) => sum + o.ExactProbability!.Value));

            var blackGroup = result.Outcomes.Where(o => o.BaseCoatClass == "black").ToList();
            Assert.Equal(2, blackGroup.Count);
            Assert.Equal(new Rational(1, 4), blackGroup.Aggregate(Rational.Zero, (sum, o) => sum + o.ExactProbability!.Value));

            var chestnutGroup = result.Outcomes.Where(o => o.BaseCoatClass == "chestnut").ToList();
            Assert.Equal(2, chestnutGroup.Count);
            Assert.Equal(Rational.Half, chestnutGroup.Aggregate(Rational.Zero, (sum, o) => sum + o.ExactProbability!.Value));
        }

        [Fact]
        public void KnownVersusUnknownParentHandlingChangesForecastMethodButNotTotalPossibilities()
        {
            var bayEeAa = Genome(("mc1r", "E", "e"), ("asip", "A", "a"));
            var defaultGenome = Genome();
            var chestnut = Genome(("mc1r", "e", "e"), ("asip", "a", "a"));
            var matrix = TestMatrix("bay", "chestnut", "black");
            var service = new CoatForecastService(matrix);

            var known = service.ForKnownParents(Known(1, bayEeAa), Known(2, chestnut), PublicCoatScope.AllSupported());
            // Same E call as the known parent, but agouti is omitted; phenotype observed as bay.
            var unknownDam = new PublicCoatKnowledge(Id(1), PartialKnowledge(defaultGenome,
                new[] { ("mc1r", "E", "e") }, new[] { "asip" }), "Bay");
            var unknown = service.ForKnownParents(unknownDam, Known(2, chestnut), PublicCoatScope.AllSupported());

            Assert.Equal(ForecastMethod.Exact, known.Method);
            Assert.Equal(ForecastMethod.ConditionalRanges, unknown.Method);

            // Both forecasts cover the same coat classes; the unknown one only widens probabilities.
            Assert.Equal(
                known.Outcomes.Select(o => o.Label).OrderBy(l => l),
                unknown.Outcomes.Select(o => o.Label).OrderBy(l => l));
        }

        [Fact]
        public void GrayAmountIsZeroForFoalExampleButUnderlyingBaseCoatIsStillPreserved()
        {
            var grayBay = Genome(("mc1r", "E", "e"), ("asip", "A", "a"), ("stx17", "G", "N"));
            var chestnut = Genome(("mc1r", "e", "e"), ("asip", "a", "a"));
            var matrix = TestMatrix("bay", "chestnut", "black");
            var result = new CoatForecastService(matrix).ForKnownParents(Known(1, grayBay), Known(2, chestnut), PublicCoatScope.AllSupported());

            var grayOutcome = result.Outcomes.Single(o => o.DisplayLabel == "Gray (Bay)");
            Assert.NotNull(grayOutcome.Example);
            Assert.NotNull(grayOutcome.Example!.Descriptor);
            Assert.Equal("bay", grayOutcome.Example.Descriptor.BaseCoatClass);
            // The example foal has a gray layer but age-zero expression; the layer
            // presence still drives the DisplayLabel.
            Assert.Contains("grey_age", grayOutcome.Example.Descriptor.CoatLayers);
        }
    }
}
