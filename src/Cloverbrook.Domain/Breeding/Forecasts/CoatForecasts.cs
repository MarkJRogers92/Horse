using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public enum ForecastAuthority { PlayerKnowledge, Hypothetical }
    public enum ForecastMethod { Exact, ConditionalRanges, Sampled, Unavailable }

    // Intentionally cannot hold a HorseRecord or authoritative genome. The UI
    // supplies only facts already revealed to the player, never diagnostic labels.
    public sealed class PublicCoatKnowledge
    {
        public PublicCoatKnowledge(HorseId horseId, PlayerKnowledgeRecord knowledge, string? observedCoatLabel = null)
        {
            HorseId = Check.NotNull(horseId, nameof(horseId)); Knowledge = Check.NotNull(knowledge, nameof(knowledge));
            if (observedCoatLabel != null) Check.Text(observedCoatLabel, nameof(observedCoatLabel));
            ObservedCoatLabel = observedCoatLabel;
            PublicHash = Canonical.Hash("public.coat.knowledge.v1", horseId.ToString(), knowledge.KnowledgeHash, observedCoatLabel ?? "");
        }
        public HorseId HorseId { get; }
        public PlayerKnowledgeRecord Knowledge { get; }
        public string? ObservedCoatLabel { get; }
        public string PublicHash { get; }
    }

    // Publicly disclosed population assumptions, not inferred from either parent's
    // hidden genome. Pools are possibilities, NOT allele-frequency priors.
    public sealed class PublicCoatScope
    {
        public PublicCoatScope(string version, IEnumerable<KeyValuePair<string, IEnumerable<string>>> biologicalAllelePools)
        {
            Version = Check.Text(version, nameof(version));
            var loci = CoreGeneticsCatalog.Model.Blocks.Values.Where(b => !b.IsSynthetic).SelectMany(b => b.Loci).ToDictionary(l => l.LocusId, StringComparer.Ordinal);
            AllelePools = Snapshot.Map(Check.NotNull(biologicalAllelePools, nameof(biologicalAllelePools)).Select(p =>
                new KeyValuePair<string, IReadOnlyList<string>>(p.Key, Snapshot.List(p.Value.Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal)))));
            if (AllelePools.Count != loci.Count || AllelePools.Keys.Any(k => !loci.ContainsKey(k))) throw new ArgumentException("A public scope must explicitly cover every supported biological locus.");
            foreach (var pool in AllelePools)
                if (pool.Value.Count == 0 || pool.Value.Any(a => !loci[pool.Key].Variant.SupportedAlleleIds.Contains(a))) throw new ArgumentException("An allele pool is empty or unsupported.");
            ScopeHash = Canonical.Hash(new[] { "public.coat.scope.v1", version }.Concat(AllelePools.Select(p => Canonical.Hash(new[] { p.Key }.Concat(p.Value)))));
        }
        public string Version { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> AllelePools { get; }
        public string ScopeHash { get; }
        public static PublicCoatScope AllSupported() => new PublicCoatScope("all-supported-not-live.v1",
            CoreGeneticsCatalog.Model.Blocks.Values.Where(b => !b.IsSynthetic).SelectMany(b => b.Loci)
                .Select(l => new KeyValuePair<string, IEnumerable<string>>(l.LocusId, l.Variant.SupportedAlleleIds)));
    }

    public sealed class ForecastOptions
    {
        public ForecastOptions(int maxParentHypotheses = 256, int maxPairScenarios = 4096, int maxExactOutcomes = 65536,
            int sampleCount = 4096, bool forceSampling = false)
        {
            if (maxParentHypotheses < 1 || maxParentHypotheses > 4096 || maxPairScenarios < 1 || maxPairScenarios > 65536 ||
                maxExactOutcomes < 1 || maxExactOutcomes > 262144 || sampleCount < 100 || sampleCount > 100000) throw new ArgumentOutOfRangeException(nameof(maxParentHypotheses));
            MaxParentHypotheses = maxParentHypotheses; MaxPairScenarios = maxPairScenarios; MaxExactOutcomes = maxExactOutcomes;
            SampleCount = sampleCount; ForceSampling = forceSampling;
        }
        public int MaxParentHypotheses { get; }
        public int MaxPairScenarios { get; }
        public int MaxExactOutcomes { get; }
        public int SampleCount { get; }
        public bool ForceSampling { get; }
        internal string Key => Canonical.Hash(MaxParentHypotheses.ToString(CultureInfo.InvariantCulture), MaxPairScenarios.ToString(CultureInfo.InvariantCulture),
            MaxExactOutcomes.ToString(CultureInfo.InvariantCulture), SampleCount.ToString(CultureInfo.InvariantCulture), ForceSampling ? "sample" : "exact-first");
    }

    public sealed class CoatForecastOutcome
    {
        internal CoatForecastOutcome(string label, Rational? exact, double? estimate, double lower, double upper, PresentationResolution? example)
        { Label = label; ExactProbability = exact; EstimatedProbability = estimate; LowerProbability = lower; UpperProbability = upper; Example = example; }
        public string Label { get; }
        public Rational? ExactProbability { get; }
        public double? EstimatedProbability { get; }
        public double LowerProbability { get; }
        public double UpperProbability { get; }
        public PresentationResolution? Example { get; }
        // Preserved underlying base coat class (e.g. "bay" for a grey-over-bay outcome).
        public string? BaseCoatClass => Example?.Descriptor?.BaseCoatClass;
        // Active overlay layers on top of the base coat, excluding base_pigment.
        public IReadOnlyList<string> OverlayLabels => Example?.Descriptor?.CoatLayers
            .Where(l => l != "base_pigment" && l != Example.Descriptor.BaseCoatClass).ToArray() ?? Array.Empty<string>();
        // Display-friendly label that keeps the hidden underlying colour separate from
        // the grey overlay, matching the reference calculator's "Gray (Base)" form.
        public string DisplayLabel
        {
            get
            {
                if (Example?.Descriptor == null) return Label;
                var baseClass = Example.Descriptor.BaseCoatClass;
                if (Example.Descriptor.CoatLayers.Contains("grey_age") && baseClass != null)
                    return "Gray (" + Title(baseClass.Replace('_', ' ')) + ")";
                return Label;
            }
        }
        private static string Title(string text) => char.ToUpperInvariant(text[0]) + text.Substring(1);
        public string ExampleNotice => "Possible coat outcome only; markings and body are illustrative, not a committed foal.";
    }
    public sealed class CoatForecast
    {
        internal CoatForecast(ForecastAuthority authority, ForecastMethod method, string cacheKey, IEnumerable<CoatForecastOutcome> outcomes,
            int scenarios, int samples, string notice)
        { Authority = authority; Method = method; CacheKey = cacheKey; Outcomes = Snapshot.List(outcomes); ParentScenarios = scenarios; SampleCount = samples; Notice = notice; }
        public ForecastAuthority Authority { get; }
        public ForecastMethod Method { get; }
        public string CacheKey { get; }
        public IReadOnlyList<CoatForecastOutcome> Outcomes { get; }
        public int ParentScenarios { get; }
        public int SampleCount { get; }
        public string Notice { get; }
        public bool IsCommittedOutcome => false;
        public bool UnobservedOutcomesPossible => Method == ForecastMethod.Sampled;
    }

    public sealed class CoatForecastService
    {
        private readonly MeiosisService meiosis = new MeiosisService(CoreGeneticsCatalog.Model);
        private readonly PhenotypeResolver resolver = new PhenotypeResolver();
        private readonly CoatSupportMatrix matrix;
        private readonly string artVersion;
        private readonly string maskId;
        private static readonly HorseId analysisA = new HorseId(new Guid("fa000000-0000-0000-0000-000000000001"));
        private static readonly HorseId analysisB = new HorseId(new Guid("fa000000-0000-0000-0000-000000000002"));
        private static readonly OwnerId analysisOwner = new OwnerId(new Guid("fa000000-0000-0000-0000-000000000003"));
        public CoatForecastService(CoatSupportMatrix matrix, string artVersion = "unreleased", string maskId = "unreleased")
        { this.matrix = Check.NotNull(matrix, nameof(matrix)); this.artVersion = Check.Text(artVersion, nameof(artVersion)); this.maskId = Check.Text(maskId, nameof(maskId)); }

        public CoatForecast ForKnownParents(PublicCoatKnowledge a, PublicCoatKnowledge b, PublicCoatScope scope, ForecastOptions? options = null)
        {
            Check.NotNull(a, nameof(a)); Check.NotNull(b, nameof(b)); Check.NotNull(scope, nameof(scope)); options ??= new ForecastOptions();
            if (a.HorseId.Equals(b.HorseId)) throw new ArgumentException("Parents must be distinct.");
            var key = Key("knowledge", a.PublicHash, b.PublicHash, scope.ScopeHash, options);
            try
            {
                return Calculate(BuildHypotheses(a, scope, options.MaxParentHypotheses), BuildHypotheses(b, scope, options.MaxParentHypotheses),
                    ForecastAuthority.PlayerKnowledge, key, options);
            }
            catch (ExactEnumerationLimitException e) { return Unavailable(ForecastAuthority.PlayerKnowledge, key, e.Message + " More revealed calls or a narrower publicly disclosed scope are needed."); }
        }
        public CoatForecast Hypothetical(GenomeSnapshot a, GenomeSnapshot b, ForecastOptions? options = null)
        {
            options ??= new ForecastOptions(); CoreGeneticsCatalog.Model.ValidateGenome(a); CoreGeneticsCatalog.Model.ValidateGenome(b);
            var pa = CoatOnly(a); var pb = CoatOnly(b);
            return Calculate(new[] { pa }, new[] { pb }, ForecastAuthority.Hypothetical, Key("hypothetical", pa.GenomeHash, pb.GenomeHash, "no-actual-parent-authority", options), options);
        }
        private string Key(string authority, string a, string b, string scope, ForecastOptions o) => Canonical.Hash("coat.forecast.v1", authority, a, b, scope,
            CoreGeneticsCatalog.InheritanceVersion, resolver.Version, matrix.Version, MatrixHash(), artVersion, maskId, o.Key);
        private string MatrixHash() => Canonical.Hash(matrix.LayerIds.Concat(matrix.Classes.Values.Select(c => Canonical.Hash(new[] { c.Id, c.Genetics.ToString(), c.Visuals.ToString(), c.Release.ToString() }.Concat(c.LayerIds)))));
        private static CoatForecast Unavailable(ForecastAuthority authority, string key, string reason) => new CoatForecast(authority, ForecastMethod.Unavailable, key, Array.Empty<CoatForecastOutcome>(), 0, 0, reason);
        private static PhasedBlockPair Neutral(LinkageBlockRule rule)
        {
            var calls = rule.Loci.Select(l => l.Variant.SupportedAlleleIds[0]).ToArray();
            return new PhasedBlockPair(rule.BlockId, rule.IsSynthetic, rule.Loci.Select(l => l.LocusId), calls, calls);
        }
        // Coat-only hypotheses carry neutral synthetic markers; they never predict
        // aptitude, morphology or hidden performance traits from a displayed coat.
        internal static GenomeSnapshot CoatOnly(GenomeSnapshot genome) => new GenomeSnapshot(genome.Blocks.Select(b => b.IsSynthetic ? Neutral(CoreGeneticsCatalog.Model.Blocks[b.BlockId]) : b), genome.VariantCatalogVersion, genome.SyntheticModelVersion);
        internal static HorseRecord AnalysisHorse(GenomeSnapshot genome, Seed128 appearanceSeed = default, bool second = false) =>
            new HorseRecord(second ? analysisB : analysisA, "Forecast analysis only", new Parentage(), genome, CoreGeneticsCatalog.ResolverVersion,
                appearanceSeed, analysisOwner, new LifeState(LifeStage.Adult, 12, VisualAge.Adult), new ReproductionState(ReproductiveRole.NonBreeding));

        private IReadOnlyList<GenomeSnapshot> BuildHypotheses(PublicCoatKnowledge parent, PublicCoatScope scope, int limit)
        {
            var model = CoreGeneticsCatalog.Model; var allLoci = model.Blocks.Values.SelectMany(b => b.Loci).ToDictionary(l => l.LocusId, StringComparer.Ordinal);
            foreach (var call in parent.Knowledge.RevealedCalls)
            {
                if (!allLoci.TryGetValue(call.Key, out var locus) || !locus.Variant.SupportedAlleleIds.Contains(call.Value.A) || !locus.Variant.SupportedAlleleIds.Contains(call.Value.B)) throw new ArgumentException("Revealed call is unsupported.");
                if (scope.AllelePools.TryGetValue(call.Key, out var pool) && (!pool.Contains(call.Value.A) || !pool.Contains(call.Value.B))) throw new ArgumentException("A revealed call contradicts the public population scope.");
            }
            var states = new List<List<PhasedBlockPair>> { new List<PhasedBlockPair>() };
            foreach (var rule in model.Blocks.Values)
            {
                var possibilities = new List<PhasedBlockPair>();
                if (rule.IsSynthetic) possibilities.Add(Neutral(rule));
                else
                {
                    var haplotypes = new List<string[]> { Array.Empty<string>() };
                    foreach (var locus in rule.Loci)
                    {
                        var alleles = parent.Knowledge.RevealedCalls.TryGetValue(locus.LocusId, out var known)
                            ? new[] { known.A, known.B }.Distinct(StringComparer.Ordinal).ToArray() : scope.AllelePools[locus.LocusId].ToArray();
                        if ((long)haplotypes.Count * alleles.Length > limit) throw new ExactEnumerationLimitException(limit);
                        haplotypes = haplotypes.SelectMany(h => alleles.Select(a => h.Concat(new[] { a }).ToArray())).ToList();
                    }
                    haplotypes = haplotypes.Where(h => rule.AllowsHaplotype(h)).ToList();
                    for (var i = 0; i < haplotypes.Count; i++) for (var j = i; j < haplotypes.Count; j++)
                    {
                        var x = haplotypes[i]; var y = haplotypes[j]; var matches = true;
                        for (var k = 0; k < rule.Loci.Count; k++)
                            if (parent.Knowledge.RevealedCalls.TryGetValue(rule.Loci[k].LocusId, out var call) &&
                                !((x[k] == call.A && y[k] == call.B) || (x[k] == call.B && y[k] == call.A))) { matches = false; break; }
                        if (matches) possibilities.Add(new PhasedBlockPair(rule.BlockId, false, rule.Loci.Select(l => l.LocusId), x, y));
                    }
                }
                if (possibilities.Count == 0) throw new ArgumentException("Revealed evidence admits no supported phased genome.");
                if ((long)states.Count * possibilities.Count > limit) throw new ExactEnumerationLimitException(limit);
                states = states.SelectMany(s => possibilities.Select(p => new List<PhasedBlockPair>(s) { p })).ToList();
            }
            var genomes = states.Select(s => new GenomeSnapshot(s, model.VariantCatalogVersion, model.SyntheticModelVersion)).ToArray();
            if (parent.ObservedCoatLabel != null)
                genomes = genomes.Where(g => { var r = resolver.ResolveGenetics(AnalysisHorse(g)); return r.Status == ResolutionStatus.Supported && PhenotypeResolver.VisibleLabel(r.Phenotype!) == parent.ObservedCoatLabel; }).ToArray();
            if (genomes.Length == 0) throw new ArgumentException("The public observation and calls are inconsistent; no hidden data was consulted.");
            return Array.AsReadOnly(genomes);
        }
        private CoatForecast Calculate(IReadOnlyList<GenomeSnapshot> a, IReadOnlyList<GenomeSnapshot> b, ForecastAuthority authority, string key, ForecastOptions options)
        {
            if ((long)a.Count * b.Count > options.MaxPairScenarios) return Unavailable(authority, key, "Too many knowledge-consistent parent scenarios; no prior or truncated forecast was substituted.");
            var scenarios = new List<Dictionary<string, Rational>>(); var examples = new Dictionary<string, GenomeSnapshot>(StringComparer.Ordinal);
            if (options.ForceSampling && a.Count * b.Count != 1) return Unavailable(authority, key, "Sampling cannot invent weights for unknown parental genotypes. Reveal more information or use an explicitly hypothetical pairing.");
            if (options.ForceSampling) return Sample(a[0], b[0], authority, key, options.SampleCount);
            try
            {
                foreach (var ga in a) foreach (var gb in b)
                {
                    var probabilities = new Dictionary<string, Rational>(StringComparer.Ordinal);
                    foreach (var child in meiosis.EnumerateOffspring(AnalysisHorse(ga), AnalysisHorse(gb, second: true), options.MaxExactOutcomes))
                        Add(probabilities, examples, child.Value, child.Probability);
                    scenarios.Add(probabilities);
                }
            }
            catch (ExactEnumerationLimitException)
            {
                return a.Count == 1 && b.Count == 1 ? Sample(a[0], b[0], authority, key, options.SampleCount)
                    : Unavailable(authority, key, "Exact knowledge ranges exceed the budget. Unknown-parent priors will not be invented to enable sampling.");
            }
            var outcomes = new List<CoatForecastOutcome>(); var allExact = true;
            foreach (var label in scenarios.SelectMany(s => s.Keys).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal))
            {
                var values = scenarios.Select(s => s.TryGetValue(label, out var value) ? value : Rational.Zero).ToArray();
                var min = values.Aggregate((x, y) => x < y ? x : y); var max = values.Aggregate((x, y) => x > y ? x : y);
                if (min != max) allExact = false;
                outcomes.Add(new CoatForecastOutcome(label, min == max ? min : (Rational?)null, null, min.ToDouble(), max.ToDouble(), Example(examples[label], key, label)));
            }
            return new CoatForecast(authority, allExact ? ForecastMethod.Exact : ForecastMethod.ConditionalRanges, key, outcomes, scenarios.Count, 0,
                allExact ? "Exact coat probabilities across all admitted public-evidence scenarios. Scope assumptions are explicit; no aptitude forecast or committed foal is implied."
                    : "Minimum/maximum conditional probabilities over all admitted parental genotypes and phases. These ranges are not a probability distribution and need not sum to one. No equal-weight prior was assumed.");
        }
        private void Add(IDictionary<string, Rational> counts, IDictionary<string, GenomeSnapshot> examples, GenomeSnapshot child, Rational weight)
        {
            var r = resolver.ResolveGenetics(AnalysisHorse(child)); var label = r.Status == ResolutionStatus.Supported ? PhenotypeResolver.VisibleLabel(r.Phenotype!) : "Unsupported expression";
            counts[label] = counts.TryGetValue(label, out var old) ? old + weight : weight;
            if (!examples.ContainsKey(label)) examples.Add(label, child);
        }
        private PresentationResolution Example(GenomeSnapshot genome, string key, string label) => resolver.ResolveForPresentation(
            AnalysisHorse(genome, Seeds.Derive(default, "forecast/example/" + Canonical.Hash(key, label))), matrix, artVersion, maskId);
        private CoatForecast Sample(GenomeSnapshot a, GenomeSnapshot b, ForecastAuthority authority, string key, int count)
        {
            var counts = new Dictionary<string, Rational>(StringComparer.Ordinal); var examples = new Dictionary<string, GenomeSnapshot>(StringComparer.Ordinal);
            var seed = Seeds.Derive(default, "forecast/sampling/" + key);
            for (var i = 0; i < count; i++)
            {
                var eventSeed = Seeds.Derive(seed, "draw/" + i.ToString(CultureInfo.InvariantCulture));
                var ga = meiosis.SampleGamete(analysisA, a, Seeds.Derive(eventSeed, "gameteA"));
                var gb = meiosis.SampleGamete(analysisB, b, Seeds.Derive(eventSeed, "gameteB"));
                Add(counts, examples, GenomeSnapshot.FromGametes(ga, gb), new Rational(1, count));
            }
            var outcomes = counts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
            {
                var probability = p.Value.ToDouble(); const double z = 1.959963984540054; var z2 = z * z;
                var denominator = 1 + z2 / count; var center = (probability + z2 / (2 * count)) / denominator;
                var half = z * Math.Sqrt(probability * (1 - probability) / count + z2 / (4.0 * count * count)) / denominator;
                return new CoatForecastOutcome(p.Key, null, probability, Math.Max(0, center - half), Math.Min(1, center + half), Example(examples[p.Key], key, p.Key));
            });
            return new CoatForecast(authority, ForecastMethod.Sampled, key, outcomes, 1, count,
                "Monte Carlo coat estimates from production meiosis. Bounds are approximate 95% pointwise Wilson intervals, not simultaneous guarantees. Unobserved outcomes remain possible. All example foals are illustrative.");
        }
    }
}
