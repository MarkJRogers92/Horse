using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class BreedingForecastTests
    {
        private static BreedingForecastService Service() => new BreedingForecastService(ForecastTestData.Unreleased());
        private static PublicCoatKnowledge Revealed(int id, GenomeSnapshot genome) => new PublicCoatKnowledge(ForecastTestData.Id(id),
            new PlayerKnowledgeRecord(genome.Blocks.SelectMany(b => b.LocusIds).Select(l => new KeyValuePair<string, AllelePair>(l, genome.GetAlleles(l))), Array.Empty<HorseId>()));
        [Fact]
        public void LinkedTraitMarginalsHaveExactRecombinationProbabilities()
        {
            var cis = ForecastTestData.Genome(("game.marker.00", "0", "1"), ("game.marker.01", "0", "1"));
            var forecast = Service().Hypothetical(cis, ForecastTestData.Genome());
            var height = forecast.Traits.Single(t => t.TraitId == "height");
            var distribution = height.InheritedDistribution.ToDictionary(p => p.Value, p => p.Probability);
            Assert.Equal(new Rational(9, 20), distribution[4150]); Assert.Equal(new Rational(9, 20), distribution[5000]);
            Assert.Equal(new Rational(1, 20), distribution[4500]); Assert.Equal(new Rational(1, 20), distribution[4650]);
            Assert.Equal(3950, height.DevelopedMinimum); Assert.Equal(5200, height.DevelopedMaximum);
            Assert.All(forecast.Traits, t => Assert.Equal(Rational.One, t.InheritedDistribution.Aggregate(Rational.Zero, (s, p) => s + p.Probability)));
            Assert.Equal(ForecastAuthority.Hypothetical, forecast.Authority); Assert.False(forecast.AuthorizesConception);
        }
        [Fact]
        public void TraitMarginalsAgreeWithProductionEnumerationIncludingTradeoffs()
        {
            var a = ForecastTestData.Genome(("game.marker.12", "0", "1"), ("game.marker.13", "0", "1"), ("game.marker.20", "1", "1"));
            var b = ForecastTestData.Genome(("game.marker.12", "1", "1"));
            var actual = new MeiosisService(CoreGeneticsCatalog.Model).EnumerateOffspring(ForecastTestData.Horse(1, a), ForecastTestData.Horse(2, b));
            var forecast = Service().Hypothetical(a, b);
            foreach (var trait in forecast.Traits)
            {
                var expected = actual.GroupBy(o => GameTraitResolver.Resolve(o.Value, QuantizedVector.Empty).Inherited.Get(trait.TraitId))
                    .ToDictionary(g => g.Key, g => g.Aggregate(Rational.Zero, (s, p) => s + p.Probability));
                Assert.Equal(expected.OrderBy(p => p.Key), trait.InheritedDistribution.Select(p => new KeyValuePair<int, Rational>(p.Value, p.Probability)));
            }
            Assert.Contains(forecast.Traits, t => t.Category == TraitCategory.Temperament);
            Assert.Contains(forecast.Traits, t => t.TraitId == "turning" && t.InheritedMinimum != t.InheritedMaximum);
        }
        [Fact]
        public void PublicTraitCallsDoNotInventLinkagePhaseOrUnknownProbabilities()
        {
            var g = ForecastTestData.Genome(("game.marker.00", "0", "1"), ("game.marker.01", "0", "1"));
            var known = Service().ForKnownParents(Revealed(1, g), Revealed(2, ForecastTestData.Genome()), PublicCoatScope.AllSupported());
            var height = known.Traits.Single(t => t.TraitId == "height");
            Assert.Equal(4150, height.InheritedMinimum); Assert.Equal(5000, height.InheritedMaximum); Assert.Empty(height.InheritedDistribution);
            var unknown = Service().ForKnownParents(ForecastTestData.Known(1, g), ForecastTestData.Known(2, g), PublicCoatScope.AllSupported());
            height = unknown.Traits.Single(t => t.TraitId == "height");
            Assert.Equal(4150, height.InheritedMinimum); Assert.Equal(5850, height.InheritedMaximum); Assert.Empty(height.InheritedDistribution);
            Assert.False(unknown.Relatedness!.IsComplete);
            var parameters = typeof(BreedingForecastService).GetMethod(nameof(BreedingForecastService.ForKnownParents))!.GetParameters();
            Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(HorseRecord) || p.ParameterType == typeof(GenomeSnapshot));
        }
        [Fact]
        public void MaskedPearlUsesOneLocusAndUndiscoveredVariantsStayConditional()
        {
            var pearl = ForecastTestData.Genome(("slc45a2", "N", "Prl"));
            var result = Service().Hypothetical(pearl, pearl).Variants.Single(v => v.LocusId == "slc45a2");
            Assert.Equal(new Rational(1, 4), result.Outcomes.Single(o => o.Call.Count("Prl") == 2).ExactProbability);
            Assert.Equal(Rational.Half, result.Outcomes.Single(o => o.Call.Count("Prl") == 1).ExactProbability);
            var publicResult = Service().ForKnownParents(ForecastTestData.Known(1, pearl, "slc45a2"), ForecastTestData.Known(2, pearl), PublicCoatScope.AllSupported());
            Assert.Contains(publicResult.Variants.Single(v => v.LocusId == "slc45a2").Outcomes,
                o => o.Call.Count("Prl") == 2 && o.LowerProbability == Rational.Zero && o.UpperProbability == Rational.Half && o.ExactProbability == null);
        }
        [Fact]
        public void PublicPedigreeRelatednessUsesFullRecordedGenerations()
        {
            var parents = ConceptionTestData.Parents(); var nodes = parents.Select(h => new PedigreeNode(h.HorseId, h.Parentage)).ToList();
            var previous = parents[0].HorseId;
            for (var i = 3; i < 12; i++)
            { var id = ForecastTestData.Id(i); nodes.Add(new PedigreeNode(id, new Parentage(previous, parents[1].HorseId))); previous = id; }
            var graph = new PedigreeGraph(nodes);
            var result = Service().ForKnownParents(new PublicCoatKnowledge(previous, PlayerKnowledgeRecord.Unknown),
                ForecastTestData.Known(2, parents[1].Genome), PublicCoatScope.AllSupported(), graph);
            Assert.Equal(new Rational(511, 1024), result.Relatedness!.Coefficient);
            Assert.Equal(graph.PedigreeHash, result.Relatedness.PedigreeHash);
        }
    }
}
