using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>One possible foal coat with the honest chance range given only what the player can see.</summary>
    public sealed class FoalCoatChance
    {
        internal FoalCoatChance(string label, double low, double high) { Label = label; Low = low; High = high; }
        public string Label { get; }
        public double Low { get; }
        public double High { get; }
        /// <summary>"50%", "25–50%" or "up to 25%" (whole percentages).</summary>
        public string Text
        {
            get
            {
                int lo = (int)Math.Round(Low * 100), hi = (int)Math.Round(High * 100);
                if (hi <= 0) return "under 1%";
                if (lo == hi) return hi + "%";
                return lo <= 0 ? "up to " + hi + "%" : lo + "–" + hi + "%";
            }
        }
    }

    /// <summary>
    /// D-027 foal coat odds for the breeding review. Each parent is known only by its visible coat (and any revealed
    /// calls), never its hidden genome: every genotype the player cannot rule out is a separate scenario, and each
    /// coat's chance is reported as the min-max over all scenario pairs (no prior weights are invented), the same
    /// rule as <see cref="CoatForecastService.ForKnownParents"/>. Candidates come from the visible layers per locus and
    /// are confirmed with the real resolver, which keeps the search small enough for a dialog. The allele scope is
    /// what live horses can carry in this game (phase-1 founders plus the D-026 patterns).
    /// </summary>
    public static class FoalCoatOdds
    {
        public const string Version = "foal-coat-odds.v1";
        private const int MaxScenarios = 600, MaxPairs = 6000;
        private static readonly HorseId AnalysisA = new HorseId(new Guid("fa000000-0000-0000-0000-0000000000a1"));
        private static readonly HorseId AnalysisB = new HorseId(new Guid("fa000000-0000-0000-0000-0000000000b2"));

        /// <summary>Possible coats, most likely first, or an empty list when the parents cannot be analysed.</summary>
        public static IReadOnlyList<FoalCoatChance> For(HorseRecord mare, HorseRecord stallion, CoatSupportMatrix matrix)
        {
            Check.NotNull(mare, nameof(mare)); Check.NotNull(stallion, nameof(stallion)); Check.NotNull(matrix, nameof(matrix));
            try
            {
                var resolver = new PhenotypeResolver();
                // Every biological block is one locus or a zero-recombination block, so a parent passes on homolog A or
                // homolog B with probability 1/2 each. Anything else is left to the full forecast service.
                if (CoreGeneticsCatalog.Model.Blocks.Values.Any(r => !r.IsSynthetic && r.RecombinationFractions.Any(f => f != Rational.Zero)))
                    return Array.Empty<FoalCoatChance>();
                // PATN1 only shows on a foal that inherits LP, so hidden PATN1 is only worth exploring when a parent shows LP.
                bool lp = new[] { mare, stallion }.Any(h => resolver.ResolveGenetics(h).Phenotype?.CoatLayers.Contains("leopard_complex") == true);
                var a = Scenarios(mare, resolver, lp); var b = Scenarios(stallion, resolver, lp);
                if (a.Count == 0 || b.Count == 0 || (long)a.Count * b.Count > MaxPairs) return Array.Empty<FoalCoatChance>();
                var labels = new Dictionary<string, string>(StringComparer.Ordinal); // foal genotype key -> label, shared by all pairs
                var tables = new List<Dictionary<string, Rational>>();
                var quarter = new Rational(1, 4);
                foreach (var ga in a) foreach (var gb in b)
                {
                    // Per block: the distinct foal block pairs and their probabilities.
                    var perBlock = ga.BiologicalBlocks.Select(blockA =>
                    {
                        var blockB = gb.BiologicalBlocks.Single(x => x.BlockId == blockA.BlockId);
                        var outcomes = new Dictionary<string, (PhasedBlockPair Pair, Rational P)>(StringComparer.Ordinal);
                        foreach (var fromA in new[] { blockA.HomologA, blockA.HomologB })
                        foreach (var fromB in new[] { blockB.HomologA, blockB.HomologB })
                        {
                            var key = blockA.BlockId + ":" + string.Join(",", fromA) + "|" + string.Join(",", fromB);
                            outcomes[key] = outcomes.TryGetValue(key, out var o) ? (o.Pair, o.P + quarter)
                                : (new PhasedBlockPair(blockA.BlockId, false, blockA.LocusIds, fromA, fromB), quarter);
                        }
                        return outcomes.Values.ToArray();
                    }).ToArray();
                    var table = new Dictionary<string, Rational>(StringComparer.Ordinal);
                    void Walk(int i, List<PhasedBlockPair> chosen, string key, Rational p)
                    {
                        if (i == perBlock.Length)
                        {
                            if (!labels.TryGetValue(key, out var label))
                            {
                                var genome = new GenomeSnapshot(chosen.Concat(ga.Blocks.Where(x => x.IsSynthetic)), ga.VariantCatalogVersion, ga.SyntheticModelVersion);
                                var r = resolver.ResolveGenetics(Analysis(genome, AnalysisA));
                                label = r.Status == ResolutionStatus.Supported && matrix.ReleasedSupportProblems(r.RequiredCoatClasses, r.Phenotype!.CoatLayers).Count == 0
                                    ? PhenotypeResolver.VisibleLabel(r.Phenotype!) : "A colour not in this version";
                                labels[key] = label;
                            }
                            table[label] = table.TryGetValue(label, out var old) ? old + p : p;
                            return;
                        }
                        foreach (var (pair, q) in perBlock[i])
                        {
                            chosen.Add(pair);
                            Walk(i + 1, chosen, key + ";" + pair.BlockId + ":" + string.Join(",", pair.HomologA) + "|" + string.Join(",", pair.HomologB), p * q);
                            chosen.RemoveAt(chosen.Count - 1);
                        }
                    }
                    Walk(0, new List<PhasedBlockPair>(), "", Rational.One);
                    tables.Add(table);
                }
                return tables.SelectMany(t => t.Keys).Distinct(StringComparer.Ordinal).Select(label =>
                    {
                        var values = tables.Select(t => t.TryGetValue(label, out var v) ? v.ToDouble() : 0.0).ToArray();
                        return new FoalCoatChance(label, values.Min(), values.Max());
                    })
                    .Where(c => c.High > 0)
                    .OrderByDescending(c => c.High).ThenByDescending(c => c.Low).ThenBy(c => c.Label, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception exception) when (exception is ExactEnumerationLimitException || exception is GeneticsSupportException || exception is ArgumentException)
            {
                return Array.Empty<FoalCoatChance>();
            }
        }

        private static HorseRecord Analysis(GenomeSnapshot genome, HorseId id) => new HorseRecord(id, "Foal coat odds analysis", new Parentage(), genome,
            CoreGeneticsCatalog.ResolverVersion, default, new OwnerId(new Guid("fa000000-0000-0000-0000-0000000000c3")),
            new LifeState(LifeStage.Adult, 12, VisualAge.Adult), new ReproductionState(ReproductiveRole.NonBreeding));

        /// <summary>Every coat-only genome (neutral fictional markers) that looks exactly like this horse to the player.</summary>
        internal static IReadOnlyList<GenomeSnapshot> Scenarios(HorseRecord horse, PhenotypeResolver resolver, bool exploreHiddenPatn1)
        {
            var phenotype = resolver.ResolveGenetics(horse).Phenotype;
            if (phenotype == null) return Array.Empty<GenomeSnapshot>();
            var seen = PhenotypeResolver.VisibleLabel(phenotype); var layers = phenotype.CoatLayers; var baseCoat = phenotype.BaseCoatClass;
            static (string, string)[] P(params string[] pairs) => pairs.Select(x => { var c = x.Split('/'); return (c[0], c[1]); }).ToArray();
            var options = new Dictionary<string, (string, string)[]>(StringComparer.Ordinal)
            {
                ["mc1r"] = baseCoat == "chestnut" ? P("e/e") : P("E/E", "E/e"),
                ["asip"] = baseCoat == "chestnut" ? P("A/A", "A/a", "a/a") : baseCoat == "black" ? P("a/a") : P("A/A", "A/a"),
                ["slc45a2"] = layers.Contains("cream_double") ? P("Cr/Cr") : layers.Contains("cream_single") ? P("Cr/N") : P("N/N"),
                ["tbx3"] = layers.Contains("dun_dilution") ? P("D/D", "D/nd1", "D/nd2") : layers.Contains("primitive_markings") ? P("nd1/nd1", "nd1/nd2") : P("nd2/nd2"),
                ["stx17"] = layers.Contains("grey_age") ? P("G/G", "G/N") : P("N/N"),
                ["trpm1"] = layers.Contains("leopard_complex") ? P("LP/LP", "LP/N") : P("N/N"),
                ["patn1"] = !layers.Contains("leopard_complex") ? exploreHiddenPatn1 ? P("N/N", "PATN1/N", "PATN1/PATN1") : P("N/N") : phenotype.Patn1Coverage > 0 ? P("PATN1/PATN1", "PATN1/N") : P("N/N"),
                ["pattern.eca3"] = layers.Contains("white_tobiano") && layers.Contains("roan") ? P("TO/Rn")
                    : layers.Contains("white_tobiano") ? P("TO/TO", "TO/baseline") : layers.Contains("roan") ? P("Rn/Rn", "Rn/baseline") : P("baseline/baseline"),
            };
            // Revealed calls narrow the options (unordered match).
            foreach (var call in horse.PlayerKnowledge.RevealedCalls)
                if (options.TryGetValue(call.Key, out var list))
                    options[call.Key] = list.Where(o => (o.Item1 == call.Value.A && o.Item2 == call.Value.B) || (o.Item1 == call.Value.B && o.Item2 == call.Value.A)).ToArray();
            var model = CoreGeneticsCatalog.Model; var results = new List<GenomeSnapshot>();
            var blocks = new List<List<PhasedBlockPair>> { new List<PhasedBlockPair>() };
            foreach (var rule in model.Blocks.Values)
            {
                var possibilities = new List<PhasedBlockPair>();
                if (rule.IsSynthetic || rule.Loci.All(l => !options.ContainsKey(l.LocusId)))
                {
                    var calls = rule.Loci.Select(l => l.Variant.SupportedAlleleIds[0]).ToArray();
                    possibilities.Add(new PhasedBlockPair(rule.BlockId, rule.IsSynthetic, rule.Loci.Select(l => l.LocusId), calls, calls));
                }
                else
                {
                    // Every phase of every unordered genotype per locus (phase matters only in linked blocks).
                    var homologs = new List<(string[] A, string[] B)> { (Array.Empty<string>(), Array.Empty<string>()) };
                    foreach (var locus in rule.Loci)
                    {
                        var choices = options.TryGetValue(locus.LocusId, out var o) ? o : P(locus.Variant.SupportedAlleleIds[0] + "/" + locus.Variant.SupportedAlleleIds[0]);
                        homologs = homologs.SelectMany(h => choices.SelectMany(c => (c.Item1 == c.Item2 || h.A.Length == 0 ? new[] { c } : new[] { c, (c.Item2, c.Item1) })
                            .Select(x => (h.A.Append(x.Item1).ToArray(), h.B.Append(x.Item2).ToArray())))).ToList();
                    }
                    foreach (var (x, y) in homologs)
                        if (rule.AllowsHaplotype(x) && rule.AllowsHaplotype(y))
                            possibilities.Add(new PhasedBlockPair(rule.BlockId, false, rule.Loci.Select(l => l.LocusId), x, y));
                }
                if (possibilities.Count == 0) return Array.Empty<GenomeSnapshot>();
                if ((long)blocks.Count * possibilities.Count > MaxScenarios) return Array.Empty<GenomeSnapshot>();
                blocks = blocks.SelectMany(s => possibilities.Select(p => new List<PhasedBlockPair>(s) { p })).ToList();
            }
            foreach (var genome in blocks.Select(s => new GenomeSnapshot(s, model.VariantCatalogVersion, model.SyntheticModelVersion)))
            {
                var r = resolver.ResolveGenetics(Analysis(genome, AnalysisA));
                if (r.Status == ResolutionStatus.Supported && PhenotypeResolver.VisibleLabel(r.Phenotype!) == seen) results.Add(genome);
            }
            return results;
        }
    }
}
