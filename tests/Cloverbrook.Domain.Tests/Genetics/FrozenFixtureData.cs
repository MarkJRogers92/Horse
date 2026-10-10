using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Cloverbrook.Domain.Tests
{
    // Translation belongs ONLY in the fixture harness. Production never fills
    // missing/unknown genotype with normal alleles or reads this JSON file.
    internal static class FrozenFixtureData
    {
        internal static JsonElement Find(string id)
        {
            using var file = FixtureFiles.LoadGenetics();
            return file.RootElement.GetProperty("geneticsFixtures").EnumerateArray().Single(f => f.GetProperty("id").GetString() == id).Clone();
        }
        internal static Rational Fraction(JsonElement value) => new Rational(value.GetProperty("numerator").GetInt64(), value.GetProperty("denominator").GetInt64());
        internal static GenomeSnapshot Genome(JsonElement overrides = default)
        {
            using var file = FixtureFiles.LoadGenetics();
            var calls = new Dictionary<string, string[]>(StringComparer.Ordinal);
            void Apply(JsonElement values)
            {
                if (values.ValueKind == JsonValueKind.Undefined) return;
                foreach (var property in values.EnumerateObject())
                {
                    if (property.Name == "patternRegionHaplotypes")
                        foreach (var region in property.Value.EnumerateObject()) calls["pattern." + region.Name] = region.Value.EnumerateArray().Select(a => a.GetString()!).ToArray();
                    else calls[property.Name] = property.Value.EnumerateArray().Select(a => a.GetString()!).ToArray();
                }
            }
            Apply(file.RootElement.GetProperty("fixtureNotation").GetProperty("sharedDefaults")); Apply(overrides);
            foreach (var locus in CoreGeneticsCatalog.Model.Blocks.Values.Where(b => b.IsSynthetic).SelectMany(b => b.Loci))
                if (!calls.ContainsKey(locus.LocusId)) calls[locus.LocusId] = new[] { "0", "0" }; // fixed fictional background for coat-only fixtures
            var known = CoreGeneticsCatalog.Model.Blocks.Values.SelectMany(b => b.Loci).Select(l => l.LocusId).ToHashSet(StringComparer.Ordinal);
            if (calls.Keys.Any(k => !known.Contains(k)) || calls.Values.Any(v => v.Length != 2)) throw new InvalidOperationException("Unknown or malformed fixture override.");
            var blocks = CoreGeneticsCatalog.Model.Blocks.Values.Select(b => new PhasedBlockPair(b.BlockId, b.IsSynthetic, b.Loci.Select(l => l.LocusId),
                b.Loci.Select(l => calls[l.LocusId][0]), b.Loci.Select(l => calls[l.LocusId][1])));
            return new GenomeSnapshot(blocks, CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.SyntheticVersion);
        }
        internal static GenomeSnapshot With(params (string Locus, string A, string B)[] changes)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(changes.ToDictionary(c => c.Locus, c => new[] { c.A, c.B })));
            return Genome(json.RootElement);
        }
        internal static HorseRecord Horse(GenomeSnapshot genome, int id = 100, ReproductiveRole role = ReproductiveRole.Mare,
            Parentage? parentage = null, int days = 12, HomeKind home = HomeKind.ActiveStable, ReproductionState? reproduction = null,
            LifeStage stage = LifeStage.Adult, VisualAge age = VisualAge.Adult, QuantizedVector? factors = null, QuantizedVector? training = null)
        {
            return new HorseRecord(RecordContractTests.Id(id), "Fixture horse " + id, parentage ?? new Parentage(), genome, CoreGeneticsCatalog.ResolverVersion, new Seed128(1, 2),
                new OwnerId(Guid.Parse("10000000-0000-0000-0000-000000000001")), new LifeState(stage, days, age), reproduction ?? new ReproductionState(role),
                developmentalFactors: factors, trainingState: training, homeAssignment: home);
        }
        internal static (HorseRecord A, HorseRecord B) Parents(JsonElement fixture)
        {
            var parents = fixture.GetProperty("parents");
            return (Horse(Genome(parents[0].GetProperty("genotypeOverrides")), 100), Horse(Genome(parents[1].GetProperty("genotypeOverrides")), 101, ReproductiveRole.Stallion));
        }
        internal static string PairKey(AllelePair pair) => string.Join("/", new[] { pair.A, pair.B }.OrderBy(s => s, StringComparer.Ordinal));
    }
}
