using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class FrozenGeneticsTests
    {
        public static IEnumerable<object[]> Cases()
        {
            using var file = FixtureFiles.LoadGenetics();
            foreach (var fixture in file.RootElement.GetProperty("geneticsFixtures").EnumerateArray()) yield return new object[] { fixture.GetProperty("id").GetString()! };
        }
        internal static Phenotype Resolve(GenomeSnapshot genome)
        {
            var result = new PhenotypeResolver().ResolveGenetics(FrozenFixtureData.Horse(genome));
            Assert.Equal(ResolutionStatus.Supported, result.Status); Assert.NotNull(result.Phenotype); return result.Phenotype!;
        }
        internal static Dictionary<string, Rational> Distribution(HorseRecord a, HorseRecord b)
        {
            var result = new Dictionary<string, Rational>(StringComparer.Ordinal);
            foreach (var outcome in new MeiosisService(CoreGeneticsCatalog.Model).EnumerateOffspring(a, b))
            {
                var label = Resolve(outcome.Value).DiagnosticCoatClass;
                result[label] = (result.TryGetValue(label, out var previous) ? previous : Rational.Zero) + outcome.Probability;
            }
            return result;
        }
        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryFrozenGeneticsFixtureRunsAgainstTheProductionAlgorithms(string id)
        {
            var fixture = FrozenFixtureData.Find(id); var kind = fixture.GetProperty("kind").GetString();
            switch (kind)
            {
                case "crossDistribution":
                    var parents = FrozenFixtureData.Parents(fixture); var actual = Distribution(parents.A, parents.B); var reverse = Distribution(parents.B, parents.A);
                    var expectedRows = fixture.TryGetProperty("expectedPhenotypes", out var phenotypeRows) ? phenotypeRows : fixture.GetProperty("expectedOutcomes");
                    Assert.Equal(expectedRows.GetArrayLength(), actual.Count); Assert.Equal(actual.OrderBy(p => p.Key), reverse.OrderBy(p => p.Key));
                    foreach (var row in expectedRows.EnumerateArray()) Assert.Equal(FrozenFixtureData.Fraction(row.GetProperty("probability")), actual[row.GetProperty("coatClass").GetString()!]);
                    Assert.Equal(Rational.One, actual.Values.Aggregate(Rational.Zero, (a, b) => a + b));
                    var offspring = new MeiosisService(CoreGeneticsCatalog.Model).EnumerateOffspring(parents.A, parents.B);
                    if (id == "baseEpistasis934")
                    {
                        var hiddenAgouti = offspring.Where(o => Resolve(o.Value).BaseCoatClass == "chestnut").Select(o => FrozenFixtureData.PairKey(o.Value.GetAlleles("asip"))).Distinct().ToArray();
                        Assert.Contains("A/A", hiddenAgouti); Assert.Contains("A/a", hiddenAgouti); Assert.Contains("a/a", hiddenAgouti);
                    }
                    if (id == "palominoPair") Assert.All(offspring, o => Assert.Equal(2, o.Value.GetAlleles("mc1r").Count("e")));
                    if (id == "sharedCreamPearlLocus")
                    {
                        foreach (var row in expectedRows.EnumerateArray())
                        {
                            var calls = row.GetProperty("slc45a2"); var key = FrozenFixtureData.PairKey(new AllelePair(calls[0].GetString()!, calls[1].GetString()!));
                            var matching = offspring.Where(o => FrozenFixtureData.PairKey(o.Value.GetAlleles("slc45a2")) == key).ToArray();
                            Assert.All(matching, o => Assert.Equal(row.GetProperty("coatClass").GetString(), Resolve(o.Value).DiagnosticCoatClass));
                            Assert.Equal(FrozenFixtureData.Fraction(row.GetProperty("probability")), matching.Aggregate(Rational.Zero, (sum, o) => sum + o.Probability));
                        }
                    }
                    break;
                case "phenotypeInvariance":
                    var ga = FrozenFixtureData.Genome(fixture.GetProperty("genotypeA")); var gb = FrozenFixtureData.Genome(fixture.GetProperty("genotypeB")); var a = Resolve(ga); var b = Resolve(gb);
                    if (id == "silverHiddenOnChestnut")
                    {
                        foreach (var output in fixture.GetProperty("expectedEqualOutputs").EnumerateArray())
                            switch (output.GetString())
                            {
                                case "baseCoatClass": Assert.Equal(a.BaseCoatClass, b.BaseCoatClass); break;
                                case "basePigmentRecipe": Assert.Equal(a.BasePigmentRecipe, b.BasePigmentRecipe); break;
                                default: throw new InvalidOperationException("Unhandled equality output: " + output.GetString());
                            }
                        Assert.NotEqual(ga.GenomeHash, gb.GenomeHash); Assert.DoesNotContain("silver", b.CoatLayers);
                        var gametes = new MeiosisService(CoreGeneticsCatalog.Model).EnumerateGametes(RecordContractTests.Id(1), gb);
                        Assert.Contains(gametes, g => g.Value.Blocks.Single(block => block.BlockId == "pmel").Alleles[0] == "Z");
                        Assert.Equal(1, gb.GetAlleles("pmel").Count("Z"));
                    }
                    else if (id == "patn1NeedsLp")
                    {
                        Assert.Equal(fixture.GetProperty("expectedLpPatternPresent").GetBoolean(), a.LpPatternPresent); Assert.False(b.LpPatternPresent);
                        Assert.Equal(0, b.Patn1Coverage); Assert.Equal(2, gb.GetAlleles("patn1").Count("PATN1"));
                    }
                    else throw new InvalidOperationException("Unhandled invariance fixture: " + id);
                    break;
                case "gameteDistribution":
                    var setup = MeiosisTests.Linked(FrozenFixtureData.Fraction(fixture.GetProperty("recombinationFraction")));
                    var probabilities = setup.Service.EnumerateGametes(RecordContractTests.Id(1), setup.Genome).ToDictionary(g => string.Join("/", g.Value.Blocks[0].Alleles), g => g.Probability);
                    Assert.Equal(fixture.GetProperty("expectedGametes").GetArrayLength(), probabilities.Count);
                    foreach (var row in fixture.GetProperty("expectedGametes").EnumerateArray())
                        Assert.Equal(FrozenFixtureData.Fraction(row.GetProperty("probability")), probabilities[string.Join("/", row.GetProperty("alleles").EnumerateArray().Select(v => v.GetString()))]);
                    break;
                default: throw new InvalidOperationException("Unhandled frozen fixture kind: " + kind);
            }
        }
    }
}
