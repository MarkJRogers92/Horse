using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    internal static class ForecastTestData
    {
        internal static GenomeSnapshot Genome(params (string Locus, string A, string B)[] changes)
        {
            var edits = changes.ToDictionary(c => c.Locus, c => new AllelePair(c.A, c.B), StringComparer.Ordinal);
            string Normal(LocusDefinition l) => l.LocusId == "mc1r" ? "e" : l.LocusId == "asip" ? "a" : l.LocusId == "tbx3" ? "nd2"
                : l.LocusId.StartsWith("pattern.", StringComparison.Ordinal) ? "baseline" : l.LocusId.StartsWith("game.", StringComparison.Ordinal) ? "0" : "N";
            return new GenomeSnapshot(CoreGeneticsCatalog.Model.Blocks.Values.Select(b => new PhasedBlockPair(b.BlockId, b.IsSynthetic,
                b.Loci.Select(l => l.LocusId), b.Loci.Select(l => edits.TryGetValue(l.LocusId, out var c) ? c.A : Normal(l)),
                b.Loci.Select(l => edits.TryGetValue(l.LocusId, out var c) ? c.B : Normal(l)))), CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.SyntheticVersion);
        }
        internal static HorseId Id(int n) => new HorseId(new Guid("bb000000-0000-0000-0000-" + n.ToString("D12", System.Globalization.CultureInfo.InvariantCulture)));
        internal static readonly OwnerId Owner = new OwnerId(new Guid("bb000000-0000-0000-0001-000000000001"));
        internal static HorseRecord Horse(int n, GenomeSnapshot genome, ReproductiveRole role = ReproductiveRole.Mare) =>
            new HorseRecord(Id(n), "Test horse " + n, new Parentage(founderBackground: new FounderBackground("test-founders", Rational.Zero)), genome,
                CoreGeneticsCatalog.ResolverVersion, default, Owner, new LifeState(LifeStage.Adult, 20, VisualAge.Adult), new ReproductionState(role));
        internal static PublicCoatKnowledge Known(int n, GenomeSnapshot genome, params string[] omit) => new PublicCoatKnowledge(Id(n),
            new PlayerKnowledgeRecord(genome.BiologicalBlocks.SelectMany(b => b.LocusIds).Where(l => !omit.Contains(l))
                .Select(l => new KeyValuePair<string, AllelePair>(l, genome.GetAlleles(l))), Array.Empty<HorseId>()));
        internal static CoatSupportMatrix Unreleased() => new CoatSupportMatrix("nothing-released.v1", Array.Empty<string>(), Array.Empty<CoatClassSupport>());
        internal static CoatSupportMatrix TestMatrix(params string[] classes)
        {
            var layers = new[] { "base_pigment", "cream_single", "cream_double", "dun_dilution", "primitive_markings", "individual_white_markings", "shading_modifiers" };
            return new CoatSupportMatrix("TEST-ONLY-RELEASE", layers, classes.Select(c => new CoatClassSupport(c, layers, SupportLevel.Released, SupportLevel.Released, SupportLevel.Released)));
        }
    }
    public sealed class CoatForecastTests
    {
        [Fact]
        public void FullyKnownPalominosHaveExactFrozenProbabilitiesAndNoLiveExample()
        {
            var genome = ForecastTestData.Genome(("slc45a2", "N", "Cr"));
            var result = new CoatForecastService(ForecastTestData.Unreleased()).ForKnownParents(ForecastTestData.Known(1, genome), ForecastTestData.Known(2, genome), PublicCoatScope.AllSupported());
            Assert.Equal(ForecastAuthority.PlayerKnowledge, result.Authority); Assert.Equal(ForecastMethod.Exact, result.Method);
            Assert.Equal(new Rational(1, 4), result.Outcomes.Single(x => x.Label == "Chestnut").ExactProbability);
            Assert.Equal(Rational.Half, result.Outcomes.Single(x => x.Label == "Palomino").ExactProbability);
            Assert.Equal(new Rational(1, 4), result.Outcomes.Single(x => x.Label == "Cremello").ExactProbability);
            Assert.All(result.Outcomes, x => Assert.Equal(ResolutionStatus.Unsupported, x.Example!.Status)); Assert.False(result.IsCommittedOutcome);
        }
        [Fact]
        public void HiddenAgoutiCannotLeakThroughActualParentForecastsOrCacheKeys()
        {
            var hiddenA = ForecastTestData.Genome(("mc1r", "E", "e"), ("asip", "A", "A"));
            var hiddenB = ForecastTestData.Genome(("mc1r", "E", "e"), ("asip", "a", "a"));
            var mate = ForecastTestData.Known(2, ForecastTestData.Genome()); var service = new CoatForecastService(ForecastTestData.Unreleased());
            var first = service.ForKnownParents(ForecastTestData.Known(1, hiddenA, "asip"), mate, PublicCoatScope.AllSupported());
            var second = service.ForKnownParents(ForecastTestData.Known(1, hiddenB, "asip"), mate, PublicCoatScope.AllSupported());
            Assert.Equal(first.CacheKey, second.CacheKey); Assert.Equal(ForecastMethod.ConditionalRanges, first.Method); Assert.Equal(3, first.ParentScenarios);
            Assert.Equal(first.Outcomes.Select(x => (x.Label, x.LowerProbability, x.UpperProbability)), second.Outcomes.Select(x => (x.Label, x.LowerProbability, x.UpperProbability)));
            var bay = first.Outcomes.Single(x => x.Label == "Bay"); Assert.Null(bay.ExactProbability); Assert.Equal(0, bay.LowerProbability); Assert.Equal(0.5, bay.UpperProbability);
            Assert.Equal(Rational.Half, first.Outcomes.Single(x => x.Label == "Chestnut").ExactProbability);
        }
        [Fact]
        public void HypotheticalGenomesCannotClaimActualParentAuthority()
        {
            var result = new CoatForecastService(ForecastTestData.Unreleased()).Hypothetical(ForecastTestData.Genome(), ForecastTestData.Genome());
            Assert.Equal(ForecastAuthority.Hypothetical, result.Authority); Assert.False(result.IsCommittedOutcome);
            Assert.DoesNotContain(typeof(PublicCoatKnowledge).GetProperties(), p => p.PropertyType == typeof(HorseRecord) || p.PropertyType == typeof(GenomeSnapshot));
        }
        [Fact]
        public void EnumerationLimitsNeverProduceTruncatedOrUniformlyGuessedDistributions()
        {
            var service = new CoatForecastService(ForecastTestData.Unreleased());
            var result = service.ForKnownParents(new PublicCoatKnowledge(ForecastTestData.Id(1), PlayerKnowledgeRecord.Unknown),
                new PublicCoatKnowledge(ForecastTestData.Id(2), PlayerKnowledgeRecord.Unknown), PublicCoatScope.AllSupported(), new ForecastOptions(maxParentHypotheses: 2));
            Assert.Equal(ForecastMethod.Unavailable, result.Method); Assert.Empty(result.Outcomes);
            var g = ForecastTestData.Genome();
            result = service.ForKnownParents(ForecastTestData.Known(1, g, "asip"), ForecastTestData.Known(2, g), PublicCoatScope.AllSupported(), new ForecastOptions(forceSampling: true));
            Assert.Equal(ForecastMethod.Unavailable, result.Method);
        }
        [Fact]
        public void ScopeAndRevealedCallsMustBeExplicitAndConsistent()
        {
            Assert.Throws<ArgumentException>(() => new PublicCoatScope("bad", Array.Empty<KeyValuePair<string, IEnumerable<string>>>()));
            var invalid = new PublicCoatKnowledge(ForecastTestData.Id(1), new PlayerKnowledgeRecord(new[] { new KeyValuePair<string, AllelePair>("asip", new AllelePair("alien", "a")) }, Array.Empty<HorseId>()));
            Assert.Throws<ArgumentException>(() => new CoatForecastService(ForecastTestData.Unreleased()).ForKnownParents(invalid, ForecastTestData.Known(2, ForecastTestData.Genome()), PublicCoatScope.AllSupported()));
        }
        [Fact]
        public void SampledResultsAreLabeledReproducibleAndAgreeWithTheExactCross()
        {
            var g = ForecastTestData.Genome(("slc45a2", "N", "Cr")); var service = new CoatForecastService(ForecastTestData.Unreleased());
            var options = new ForecastOptions(sampleCount: 2000, forceSampling: true);
            var a = service.Hypothetical(g, g, options); var b = service.Hypothetical(g, g, options);
            Assert.Equal(ForecastMethod.Sampled, a.Method); Assert.Equal(2000, a.SampleCount); Assert.True(a.UnobservedOutcomesPossible);
            Assert.Equal(a.Outcomes.Select(x => x.EstimatedProbability), b.Outcomes.Select(x => x.EstimatedProbability));
            Assert.All(a.Outcomes, x => { Assert.Null(x.ExactProbability); Assert.True(x.LowerProbability <= x.EstimatedProbability); Assert.True(x.UpperProbability >= x.EstimatedProbability); });
            Assert.InRange(a.Outcomes.Single(x => x.Label == "Palomino").EstimatedProbability!.Value, 0.44, 0.56);
            Assert.InRange(a.Outcomes.Sum(x => x.EstimatedProbability!.Value), 0.999999, 1.000001);
        }
        [Fact]
        public void ForecastingAndExamplesCannotChangeTheCommittedDraft()
        {
            var g = ForecastTestData.Genome(("slc45a2", "N", "Cr")); var service = new MeiosisService(CoreGeneticsCatalog.Model);
            var a = ForecastTestData.Horse(1, g); var b = ForecastTestData.Horse(2, g, ReproductiveRole.Stallion); var seed = Seeds.Derive(default, "test-conception");
            var before = service.CreateDraft(a, b, seed).CanonicalBytes();
            var forecast = new CoatForecastService(ForecastTestData.TestMatrix("chestnut", "palomino", "cremello"));
            var first = forecast.ForKnownParents(ForecastTestData.Known(1, g), ForecastTestData.Known(2, g), PublicCoatScope.AllSupported());
            var second = forecast.ForKnownParents(ForecastTestData.Known(1, g), ForecastTestData.Known(2, g), PublicCoatScope.AllSupported());
            Assert.Equal(first.CacheKey, second.CacheKey); Assert.All(first.Outcomes, x => Assert.Equal(ResolutionStatus.Supported, x.Example!.Status));
            Assert.Equal(before, service.CreateDraft(a, b, seed).CanonicalBytes());
        }
        [Fact]
        public void UntestedCallsAreNotInferredFromTheAbsenceOfVisibleExpression()
        {
            var g = ForecastTestData.Genome(("slc45a2", "N", "Prl")); var mate = ForecastTestData.Genome(("slc45a2", "Prl", "Prl"));
            var a = ForecastTestData.Known(1, g, "slc45a2");
            var observed = new PublicCoatKnowledge(a.HorseId, a.Knowledge, "Chestnut");
            var result = new CoatForecastService(ForecastTestData.Unreleased()).ForKnownParents(observed, ForecastTestData.Known(2, mate), PublicCoatScope.AllSupported());
            Assert.Equal(ForecastMethod.ConditionalRanges, result.Method);
            Assert.Contains(result.Outcomes, x => x.Label == "Pearl" && x.UpperProbability > 0);
        }
    }
}
