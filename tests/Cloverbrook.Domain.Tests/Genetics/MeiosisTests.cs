using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class MeiosisTests
    {
        internal static (MeiosisService Service, GenomeSnapshot Genome) Linked(Rational fraction, bool restrictRecombinants = false)
        {
            var fixture = FrozenFixtureData.Find("syntheticLinkedBlock"); var homologs = fixture.GetProperty("homologs");
            LocusDefinition Locus(string id, string[] alleles) => new LocusDefinition(id, new VariantCatalogRecord(id, "synthetic.fixture.v1", id,
                EvidenceStatus.FictionalGameTrait, "synthetic.fixture", alleles, Array.Empty<string>(), "synthetic.fixture.v1", "none", Array.Empty<SourceReference>(),
                gameAbstractionNotice: "Synthetic algorithm fixture, not a horse map distance."));
            var a = homologs[0].EnumerateArray().Select(v => v.GetString()!).ToArray(); var b = homologs[1].EnumerateArray().Select(v => v.GetString()!).ToArray();
            var rule = new LinkageBlockRule("synthetic.fixture", true, new[] { Locus("synthetic.first", new[] { a[0], b[0] }), Locus("synthetic.second", new[] { a[1], b[1] }) },
                new[] { fraction }, "synthetic.fixture.v1", "Invented recombination parameter.", restrictRecombinants ? new[] { a, b } : null);
            var model = new InheritanceModel("linked.fixture.v1", "empty.bio.v1", "synthetic.fixture.v1", new[] { rule });
            return (new MeiosisService(model), new GenomeSnapshot(new[] { new PhasedBlockPair(rule.BlockId, true, rule.Loci.Select(l => l.LocusId), a, b) }, model.VariantCatalogVersion, model.SyntheticModelVersion));
        }
        [Fact]
        public void FrozenLinkedFixtureHasExactFortyFiveFortyFiveFiveFiveDistribution()
        {
            var fixture = FrozenFixtureData.Find("syntheticLinkedBlock"); var setup = Linked(FrozenFixtureData.Fraction(fixture.GetProperty("recombinationFraction")));
            var actual = setup.Service.EnumerateGametes(RecordContractTests.Id(1), setup.Genome).ToDictionary(g => string.Join("/", g.Value.Blocks[0].Alleles), g => g.Probability);
            Assert.Equal(fixture.GetProperty("expectedGametes").GetArrayLength(), actual.Count);
            foreach (var row in fixture.GetProperty("expectedGametes").EnumerateArray())
                Assert.Equal(FrozenFixtureData.Fraction(row.GetProperty("probability")), actual[string.Join("/", row.GetProperty("alleles").EnumerateArray().Select(a => a.GetString()))]);
            Assert.Equal(Rational.One, actual.Values.Aggregate(Rational.Zero, (a, b) => a + b));
        }
        [Theory]
        [InlineData(0, 1, 2)]
        [InlineData(1, 2, 4)]
        public void BoundaryRecombinationFractionsBehaveCorrectly(int n, int d, int count)
        {
            var setup = Linked(new Rational(n, d)); var outcomes = setup.Service.EnumerateGametes(RecordContractTests.Id(1), setup.Genome);
            Assert.Equal(count, outcomes.Count); Assert.All(outcomes, g => Assert.Equal(new Rational(1, count), g.Probability));
        }
        [Fact]
        public void SeededSampleDistributionMatchesExactRulesWithinSixSigma()
        {
            var setup = Linked(new Rational(1, 10)); var id = RecordContractTests.Id(1); const int samples = 10000;
            var exact = setup.Service.EnumerateGametes(id, setup.Genome); var counts = new Dictionary<string, int>();
            for (var i = 0; i < samples; i++)
            {
                var gamete = setup.Service.SampleGamete(id, setup.Genome, new Seed128(71, (ulong)i)); setup.Service.ValidateGameteTrace(id, setup.Genome, gamete);
                var key = gamete.AlleleKey; counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            foreach (var outcome in exact)
            {
                var p = outcome.Probability.ToDouble(); var tolerance = 6 * Math.Sqrt(samples * p * (1 - p)) + 2;
                Assert.InRange((double)counts[outcome.Value.AlleleKey], samples * p - tolerance, samples * p + tolerance);
            }
        }
        [Fact]
        public void FrozenCreamPearlCrossUsesTwoAlternativeHaplotypesNotFourSwitches()
        {
            var fixture = FrozenFixtureData.Find("sharedCreamPearlLocus"); var parents = FrozenFixtureData.Parents(fixture); var service = new MeiosisService(CoreGeneticsCatalog.Model);
            var actual = new Dictionary<string, Rational>();
            foreach (var outcome in service.EnumerateOffspring(parents.A, parents.B))
            {
                var key = FrozenFixtureData.PairKey(outcome.Value.GetAlleles("slc45a2")); actual[key] = (actual.TryGetValue(key, out var previous) ? previous : Rational.Zero) + outcome.Probability;
            }
            Assert.Equal(4, actual.Count);
            foreach (var row in fixture.GetProperty("expectedOutcomes").EnumerateArray())
            {
                var calls = row.GetProperty("slc45a2"); var key = FrozenFixtureData.PairKey(new AllelePair(calls[0].GetString()!, calls[1].GetString()!));
                Assert.Equal(FrozenFixtureData.Fraction(row.GetProperty("probability")), actual[key]);
            }
        }
        [Fact]
        public void EverySampledOffspringAlleleTracesToAnActualParentalHomolog()
        {
            var parents = FrozenFixtureData.Parents(FrozenFixtureData.Find("baseEpistasis934")); var service = new MeiosisService(CoreGeneticsCatalog.Model);
            for (var i = 0; i < 100; i++)
            {
                var draft = service.CreateDraft(parents.A, parents.B, new Seed128(19, (ulong)i));
                service.ValidateGameteTrace(parents.A.HorseId, parents.A.Genome, draft.GameteA); service.ValidateGameteTrace(parents.B.HorseId, parents.B.Genome, draft.GameteB);
                Assert.Equal(GenomeSnapshot.FromGametes(draft.GameteA, draft.GameteB).GenomeHash, draft.Genome.GenomeHash);
            }
        }
        [Fact]
        public void PreviewWorkCannotChangeByteIdenticalConceptionDraft()
        {
            var parents = FrozenFixtureData.Parents(FrozenFixtureData.Find("palominoPair")); var service = new MeiosisService(CoreGeneticsCatalog.Model); var seed = new Seed128(11, 22);
            var first = service.CreateDraft(parents.A, parents.B, seed); service.EnumerateOffspring(parents.A, parents.B);
            var preview = new CounterRng(seed, "forecast"); var cosmetic = new CounterRng(seed, "cosmetic"); for (var i = 0; i < 100; i++) { preview.NextUInt64(); cosmetic.NextUInt64(); }
            var again = service.CreateDraft(parents.A, parents.B, seed);
            Assert.Equal(first.CanonicalBytes(), again.CanonicalBytes()); Assert.Equal(first.AppearanceSeed, again.AppearanceSeed); Assert.Equal(first.DevelopmentSeed, again.DevelopmentSeed);
        }
        [Fact]
        public void UnsupportedAllelesAndVersionsFailClosed()
        {
            var service = new MeiosisService(CoreGeneticsCatalog.Model);
            Assert.Throws<GeneticsSupportException>(() => service.SampleGamete(RecordContractTests.Id(1), FrozenFixtureData.With(("slc45a2", "Cr+Prl", "N")), default));
            var original = FrozenFixtureData.Genome(); var wrongVersion = new GenomeSnapshot(original.Blocks, "unknown", original.SyntheticModelVersion);
            Assert.Throws<GeneticsSupportException>(() => service.SampleGamete(RecordContractTests.Id(1), wrongVersion, default));
            Assert.Throws<GeneticsSupportException>(() => service.SampleGamete(RecordContractTests.Id(1), FrozenFixtureData.With(("pattern.eca3", "tobiano", "baseline")), default));
        }
        [Fact]
        public void UnsupportedRecombinantsRejectTheOperationRatherThanRerolling()
        {
            var setup = Linked(new Rational(1, 10), true);
            Assert.Throws<GeneticsSupportException>(() => setup.Service.EnumerateGametes(RecordContractTests.Id(1), setup.Genome));
            Assert.Throws<GeneticsSupportException>(() => setup.Service.SampleGamete(RecordContractTests.Id(1), setup.Genome, default));
        }
        [Fact]
        public void ExactEnumerationNeverSilentlyTruncates()
        {
            var parents = FrozenFixtureData.Parents(FrozenFixtureData.Find("baseEpistasis934")); var service = new MeiosisService(CoreGeneticsCatalog.Model);
            Assert.Throws<ExactEnumerationLimitException>(() => service.EnumerateGametes(parents.A.HorseId, parents.A.Genome, 1));
            Assert.Throws<ExactEnumerationLimitException>(() => service.EnumerateOffspring(parents.A, parents.B, 4));
        }
        [Fact]
        public void ThirtyTwoGameMarkersHaveNoRealGeneClaim()
        {
            var synthetic = CoreGeneticsCatalog.Model.Blocks.Values.Where(b => b.IsSynthetic).SelectMany(b => b.Loci).ToArray();
            Assert.Equal(32, synthetic.Length);
            Assert.All(synthetic, l => { Assert.Equal(EvidenceStatus.FictionalGameTrait, l.Variant.EvidenceStatus); Assert.Null(l.Variant.GeneSymbol); Assert.False(string.IsNullOrWhiteSpace(l.Variant.GameAbstractionNotice)); });
        }
    }
}
