using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class CatalogCoatAudit
    {
        internal CatalogCoatAudit(bool complete, int combinations, string catalogHash, IEnumerable<string> classes, IEnumerable<string> reasons)
        { Complete = complete; CombinationCount = combinations; CatalogHash = catalogHash; CoatClasses = Snapshot.List(classes); Reasons = Snapshot.List(reasons); }
        public bool Complete { get; }
        public int CombinationCount { get; }
        public string CatalogHash { get; }
        public IReadOnlyList<string> CoatClasses { get; }
        public IReadOnlyList<string> Reasons { get; }
        public bool IsSupported => Complete && CombinationCount > 0 && Reasons.Count == 0;
        public string Qualification => "Conservative all-generation coat closure for inheritance.core.v1/phenotype.v1. Independent-block combinations may overestimate reachability. This is not body/mesh approval.";
    }

    public static class LiveCatalogCoatGuard
    {
        // Close every block's haplotype pool under every nonzero-probability
        // recombination, then check every biological diplotype combination.
        // This finite superset covers arbitrary future generations, not just F1.
        // Fictional blocks are closed too, but do not multiply coat combinations:
        // their neutrality for coat expression is pinned to phenotype.v1.
        public static CatalogCoatAudit Check(IEnumerable<HorseRecord> catalog, CoatSupportMatrix matrix, int maxCoatCombinations = 65536, int maxHaplotypesPerBlock = 64)
        {
            var horses = Snapshot.List(catalog); global::Cloverbrook.Domain.Check.NotNull(matrix, nameof(matrix));
            if (maxCoatCombinations < 1 || maxCoatCombinations > 1048576 || maxHaplotypesPerBlock < 1 || maxHaplotypesPerBlock > 256) throw new ArgumentOutOfRangeException(nameof(maxCoatCombinations));
            var hash = Canonical.Hash(new[] { "live.catalog.coat.v1" }.Concat(horses.OrderBy(h => h.HorseId.ToString(), StringComparer.Ordinal)
                .SelectMany(h => new[] { h.HorseId.ToString(), h.Genome.GenomeHash, h.ResolverVersion })));
            var reasons = new HashSet<string>(StringComparer.Ordinal); var classes = new HashSet<string>(StringComparer.Ordinal); var examined = 0;
            CatalogCoatAudit Result(bool complete) => new CatalogCoatAudit(complete, examined, hash, classes.OrderBy(x => x, StringComparer.Ordinal), reasons.OrderBy(x => x, StringComparer.Ordinal));
            if (horses.Count == 0) { reasons.Add("An empty catalog is not a successful reachability proof."); return Result(false); }
            if (horses.Select(h => h.HorseId).Distinct().Count() != horses.Count) { reasons.Add("Duplicate catalog identity."); return Result(false); }
            var model = CoreGeneticsCatalog.Model; var meiosis = new MeiosisService(model); var resolver = new PhenotypeResolver();
            try
            {
                foreach (var horse in horses)
                {
                    model.ValidateGenome(horse.Genome);
                    if (horse.ResolverVersion != CoreGeneticsCatalog.ResolverVersion) throw new GeneticsSupportException("A catalog resolver has no installed closure proof.");
                }
                var perBlock = new List<IReadOnlyList<PhasedBlockPair>>(); long combinations = 1;
                foreach (var rule in model.Blocks.Values)
                {
                    var pool = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                    void Add(IEnumerable<string> haplotype)
                    {
                        var copy = Snapshot.List(haplotype); var key = Canonical.Hash(copy);
                        if (pool.ContainsKey(key)) return;
                        if (pool.Count == maxHaplotypesPerBlock) throw new ExactEnumerationLimitException(maxHaplotypesPerBlock);
                        pool.Add(key, copy);
                    }
                    foreach (var horse in horses)
                    {
                        var block = horse.Genome.Blocks.Single(b => b.BlockId == rule.BlockId); Add(block.HomologA); Add(block.HomologB);
                    }
                    int previous;
                    do
                    {
                        previous = pool.Count; var current = pool.Values.ToArray();
                        for (var i = 0; i < current.Length; i++) for (var j = i; j < current.Length; j++)
                        {
                            var block = new PhasedBlockPair(rule.BlockId, rule.IsSynthetic, rule.Loci.Select(l => l.LocusId), current[i], current[j]);
                            foreach (var gamete in meiosis.EnumerateBlock(block)) Add(gamete.Value.Alleles);
                        }
                    } while (pool.Count != previous);
                    var haplotypes = pool.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToArray();
                    var diplotypes = new List<PhasedBlockPair>();
                    if (rule.IsSynthetic) diplotypes.Add(new PhasedBlockPair(rule.BlockId, true, rule.Loci.Select(l => l.LocusId), haplotypes[0], haplotypes[0]));
                    else
                        for (var i = 0; i < haplotypes.Length; i++) for (var j = i; j < haplotypes.Length; j++)
                            diplotypes.Add(new PhasedBlockPair(rule.BlockId, false, rule.Loci.Select(l => l.LocusId), haplotypes[i], haplotypes[j]));
                    combinations *= diplotypes.Count;
                    if (combinations > maxCoatCombinations) throw new ExactEnumerationLimitException(maxCoatCombinations);
                    perBlock.Add(Snapshot.List(diplotypes));
                }
                var selected = new PhasedBlockPair[perBlock.Count];
                void Visit(int depth)
                {
                    if (depth != selected.Length) { foreach (var possibility in perBlock[depth]) { selected[depth] = possibility; Visit(depth + 1); } return; }
                    examined++;
                    var genome = new GenomeSnapshot(selected, model.VariantCatalogVersion, model.SyntheticModelVersion);
                    var r = resolver.ResolveGenetics(CoatForecastService.AnalysisHorse(genome));
                    if (r.Status == ResolutionStatus.Unsupported) { reasons.Add("Reachable unsupported expression: " + r.UnsupportedReason); return; }
                    foreach (var c in r.RequiredCoatClasses) classes.Add(c);
                    foreach (var problem in matrix.ReleasedSupportProblems(r.RequiredCoatClasses, r.Phenotype!.CoatLayers)) reasons.Add(problem);
                }
                Visit(0); return Result(true);
            }
            catch (ExactEnumerationLimitException e) { reasons.Add(e.Message + " Catalog remains disabled; no sampled closure proof is accepted."); }
            catch (GeneticsSupportException e) { reasons.Add(e.Message); }
            return Result(false);
        }
    }
}
