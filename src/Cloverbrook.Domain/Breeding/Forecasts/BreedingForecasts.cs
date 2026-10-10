using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class InheritedCallProbability
    {
        internal InheritedCallProbability(AllelePair call, Rational lower, Rational upper)
        { Call = call; LowerProbability = lower; UpperProbability = upper; }
        public AllelePair Call { get; }
        public Rational LowerProbability { get; }
        public Rational UpperProbability { get; }
        public Rational? ExactProbability => LowerProbability == UpperProbability ? LowerProbability : (Rational?)null;
    }
    public sealed class InheritedVariantForecast
    {
        internal InheritedVariantForecast(string locus, IEnumerable<InheritedCallProbability> outcomes)
        { LocusId = locus; Outcomes = Snapshot.List(outcomes); }
        public string LocusId { get; }
        public IReadOnlyList<InheritedCallProbability> Outcomes { get; }
        public string Notice => "Marginal inherited calls, including potentially masked variants. Ranges use disclosed allele possibilities, without hidden genotypes or frequency priors; they need not sum to one.";
    }
    public enum TraitCategory { Body, Athletic, Temperament }
    public sealed class TraitForecast
    {
        internal TraitForecast(string id, int minimum, int maximum, IEnumerable<Weighted<int>> distribution)
        {
            TraitId = id; InheritedMinimum = minimum; InheritedMaximum = maximum; InheritedDistribution = Snapshot.List(distribution);
            Category = new[] { "composure", "sensitivity", "sociability" }.Contains(id) ? TraitCategory.Temperament
                : new[] { "height", "body_length", "leg_length", "chest_width", "neck_length", "mane_density", "tail_density" }.Contains(id) ? TraitCategory.Body : TraitCategory.Athletic;
        }
        public string TraitId { get; }
        public TraitCategory Category { get; }
        public int InheritedMinimum { get; }
        public int InheritedMaximum { get; }
        public int DevelopedMinimum => Math.Max(0, InheritedMinimum - 200);
        public int DevelopedMaximum => Math.Min(10000, InheritedMaximum + 200);
        // Marginal probabilities, not a joint distribution or a trained performance score.
        public IReadOnlyList<Weighted<int>> InheritedDistribution { get; }
        public ForecastMethod Method => InheritedDistribution.Count == 0 ? ForecastMethod.ConditionalRanges : ForecastMethod.Exact;
        public string Notice => "Fictional game.traits.v1 potential. Development can add -200..200; training and environment are not predicted. Knowledge-only ranges do not assign probabilities to unknown calls or phase.";
    }
    public sealed class BreedingForecast
    {
        internal BreedingForecast(CoatForecast coat, IEnumerable<InheritedVariantForecast> variants, IEnumerable<TraitForecast> traits, KinshipResult? relatedness)
        { Coat = coat; Variants = Snapshot.List(variants); Traits = Snapshot.List(traits); Relatedness = relatedness; }
        public CoatForecast Coat { get; }
        public ForecastAuthority Authority => Coat.Authority;
        public IReadOnlyList<InheritedVariantForecast> Variants { get; }
        public IReadOnlyList<TraitForecast> Traits { get; }
        public KinshipResult? Relatedness { get; }
        public bool AuthorizesConception => false;
    }

    // Actual predictions accept ONLY public inputs. This type cannot consume a
    // HorseRecord, a hidden parental genome, or an authoritative RNG stream.
    public sealed class BreedingForecastService
    {
        private readonly CoatForecastService coats;
        private readonly MeiosisService meiosis = new MeiosisService(CoreGeneticsCatalog.Model);
        private static readonly GenomeSnapshot neutral = new GenomeSnapshot(CoreGeneticsCatalog.Model.Blocks.Values.Select(r =>
            new PhasedBlockPair(r.BlockId, r.IsSynthetic, r.Loci.Select(l => l.LocusId), r.Loci.Select(l => l.Variant.SupportedAlleleIds[0]),
                r.Loci.Select(l => l.Variant.SupportedAlleleIds[0]))), CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.SyntheticVersion);
        private static readonly QuantizedVector baseline = GameTraitResolver.Resolve(neutral, QuantizedVector.Empty).Inherited;
        public BreedingForecastService(CoatSupportMatrix matrix, string artVersion = "unreleased", string maskId = "unreleased")
        { coats = new CoatForecastService(matrix, artVersion, maskId); }

        public BreedingForecast ForKnownParents(PublicCoatKnowledge a, PublicCoatKnowledge b, PublicCoatScope scope,
            PedigreeGraph? publicPedigree = null, ForecastOptions? options = null)
        {
            var coat = coats.ForKnownParents(a, b, scope, options); // validates every revealed call
            var variants = CoreGeneticsCatalog.Model.Blocks.Values.SelectMany(r => r.Loci).Select(l =>
                Variant(l.LocusId, Calls(a.Knowledge, l, scope), Calls(b.Knowledge, l, scope)));
            var ranges = GameTraitResolver.TraitIds.ToDictionary(t => t, t => (Min: baseline.Get(t), Max: baseline.Get(t)), StringComparer.Ordinal);
            foreach (var rule in CoreGeneticsCatalog.Model.Blocks.Values.Where(r => r.IsSynthetic))
            {
                var values = BlockProfiles(rule, PossibleGametes(a.Knowledge, rule), PossibleGametes(b.Knowledge, rule)).ToArray();
                foreach (var trait in GameTraitResolver.TraitIds)
                {
                    var old = ranges[trait];
                    ranges[trait] = (old.Min + values.Min(v => v.Value.Get(trait)) - baseline.Get(trait),
                        old.Max + values.Max(v => v.Value.Get(trait)) - baseline.Get(trait));
                }
            }
            var traits = ranges.Select(p => new TraitForecast(p.Key, p.Value.Min, p.Value.Max, p.Value.Min == p.Value.Max
                ? new[] { new Weighted<int>(p.Value.Min, Rational.One) } : Array.Empty<Weighted<int>>()));
            var relatedness = publicPedigree?.ProposedOffspringInbreeding(a.HorseId, b.HorseId)
                ?? new KinshipResult(null, "no-public-pedigree");
            return new BreedingForecast(coat, variants, traits, relatedness);
        }
        public BreedingForecast Hypothetical(GenomeSnapshot a, GenomeSnapshot b, ForecastOptions? options = null)
        {
            var coat = coats.Hypothetical(a, b, options);
            var variants = CoreGeneticsCatalog.Model.Blocks.Values.SelectMany(r => r.Loci).Select(l =>
                Variant(l.LocusId, new[] { a.GetAlleles(l.LocusId) }, new[] { b.GetAlleles(l.LocusId) }));
            var distributions = GameTraitResolver.TraitIds.ToDictionary(t => t,
                t => new Dictionary<int, Rational> { [baseline.Get(t)] = Rational.One }, StringComparer.Ordinal);
            foreach (var rule in CoreGeneticsCatalog.Model.Blocks.Values.Where(r => r.IsSynthetic))
            {
                var profiles = BlockProfiles(rule, meiosis.EnumerateBlock(a.Blocks.Single(x => x.BlockId == rule.BlockId)),
                    meiosis.EnumerateBlock(b.Blocks.Single(x => x.BlockId == rule.BlockId))).ToArray();
                foreach (var trait in GameTraitResolver.TraitIds)
                {
                    var contributions = new Dictionary<int, Rational>();
                    foreach (var profile in profiles) Add(contributions, profile.Value.Get(trait) - baseline.Get(trait), profile.Probability);
                    var next = new Dictionary<int, Rational>();
                    foreach (var old in distributions[trait]) foreach (var delta in contributions) Add(next, old.Key + delta.Key, old.Value * delta.Value);
                    distributions[trait] = next;
                }
            }
            var traits = distributions.Select(p => new TraitForecast(p.Key, p.Value.Keys.Min(), p.Value.Keys.Max(),
                p.Value.OrderBy(v => v.Key).Select(v => new Weighted<int>(v.Key, v.Value))));
            return new BreedingForecast(coat, variants, traits, null);
        }
        private static void Add<TKey>(IDictionary<TKey, Rational> map, TKey key, Rational probability) where TKey : notnull
        { map[key] = map.TryGetValue(key, out var old) ? old + probability : probability; }
        private static IReadOnlyList<AllelePair> Calls(PlayerKnowledgeRecord knowledge, LocusDefinition locus, PublicCoatScope scope)
        {
            if (knowledge.RevealedCalls.TryGetValue(locus.LocusId, out var call)) return new[] { call };
            var pool = scope.AllelePools.TryGetValue(locus.LocusId, out var biological) ? biological : locus.Variant.SupportedAlleleIds;
            return pool.SelectMany((a, i) => pool.Skip(i).Select(b => new AllelePair(a, b))).ToArray();
        }
        private InheritedVariantForecast Variant(string locus, IReadOnlyList<AllelePair> a, IReadOnlyList<AllelePair> b)
        {
            var rule = CoreGeneticsCatalog.Model.Blocks.Values.Single(r => r.Loci.Any(l => l.LocusId == locus));
            IReadOnlyList<Weighted<GameteBlock>> Gametes(AllelePair call) => meiosis.EnumerateBlock(new PhasedBlockPair(rule.BlockId, rule.IsSynthetic,
                rule.Loci.Select(l => l.LocusId), rule.Loci.Select(l => l.LocusId == locus ? call.A : l.Variant.SupportedAlleleIds[0]),
                rule.Loci.Select(l => l.LocusId == locus ? call.B : l.Variant.SupportedAlleleIds[0])));
            var index = rule.Loci.Select(l => l.LocusId).ToList().IndexOf(locus);
            var scenarios = new List<Dictionary<string, Rational>>(); var calls = new Dictionary<string, AllelePair>();
            foreach (var x in a) foreach (var y in b)
            {
                var result = new Dictionary<string, Rational>();
                foreach (var ga in Gametes(x)) foreach (var gb in Gametes(y))
                {
                    var ordered = new[] { ga.Value.Alleles[index], gb.Value.Alleles[index] }.OrderBy(s => s, StringComparer.Ordinal).ToArray();
                    var key = Canonical.Hash(ordered); calls[key] = new AllelePair(ordered[0], ordered[1]); Add(result, key, ga.Probability * gb.Probability);
                }
                scenarios.Add(result);
            }
            return new InheritedVariantForecast(locus, calls.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
            {
                var probabilities = scenarios.Select(s => s.TryGetValue(p.Key, out var v) ? v : Rational.Zero).ToArray();
                return new InheritedCallProbability(p.Value, probabilities.Min(), probabilities.Max());
            }));
        }
        private IReadOnlyList<Weighted<GameteBlock>> PossibleGametes(PlayerKnowledgeRecord knowledge, LinkageBlockRule rule)
        {
            var haplotypes = new List<string[]> { Array.Empty<string>() };
            foreach (var locus in rule.Loci)
            {
                var alleles = knowledge.RevealedCalls.TryGetValue(locus.LocusId, out var call) ? new[] { call.A, call.B }.Distinct().ToArray() : locus.Variant.SupportedAlleleIds.ToArray();
                haplotypes = haplotypes.SelectMany(h => alleles.Select(a => h.Concat(new[] { a }).ToArray())).ToList();
            }
            var gametes = new Dictionary<string, GameteBlock>();
            for (var i = 0; i < haplotypes.Count; i++) for (var j = i; j < haplotypes.Count; j++)
            {
                var x = haplotypes[i]; var y = haplotypes[j];
                if (rule.Loci.Where((l, k) => knowledge.RevealedCalls.TryGetValue(l.LocusId, out var c) &&
                    !((c.A == x[k] && c.B == y[k]) || (c.A == y[k] && c.B == x[k]))).Any()) continue;
                foreach (var g in meiosis.EnumerateBlock(new PhasedBlockPair(rule.BlockId, true, rule.Loci.Select(l => l.LocusId), x, y))) gametes[g.Value.AlleleKey] = g.Value;
            }
            // Weights are placeholders for support enumeration ONLY. No distribution
            // is computed from these possibilities, and no equal-weight prior exists.
            return gametes.Values.Select(g => new Weighted<GameteBlock>(g, Rational.One)).ToArray();
        }
        private static IEnumerable<Weighted<QuantizedVector>> BlockProfiles(LinkageBlockRule rule,
            IReadOnlyList<Weighted<GameteBlock>> a, IReadOnlyList<Weighted<GameteBlock>> b)
        {
            foreach (var x in a) foreach (var y in b)
            {
                var child = new PhasedBlockPair(rule.BlockId, true, rule.Loci.Select(l => l.LocusId), x.Value.Alleles, y.Value.Alleles);
                var genome = new GenomeSnapshot(neutral.Blocks.Select(block => block.BlockId == rule.BlockId ? child : block), neutral.VariantCatalogVersion, neutral.SyntheticModelVersion);
                yield return new Weighted<QuantizedVector>(GameTraitResolver.Resolve(genome, QuantizedVector.Empty).Inherited, x.Probability * y.Probability);
            }
        }
    }
}
