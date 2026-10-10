using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class ConceptionDraft
    {
        internal ConceptionDraft(GameteSnapshot gameteA, GameteSnapshot gameteB, string parentAGenomeHash, string parentBGenomeHash, Seed128 eventSeed)
        {
            GameteA = gameteA; GameteB = gameteB; ParentAGenomeHash = parentAGenomeHash; ParentBGenomeHash = parentBGenomeHash;
            Genome = GenomeSnapshot.FromGametes(gameteA, gameteB); EventSeed = eventSeed;
            AppearanceSeed = Seeds.Derive(eventSeed, "appearance"); DevelopmentSeed = Seeds.Derive(eventSeed, "development");
            OutcomeHash = Canonical.Hash(Fields());
        }
        public GameteSnapshot GameteA { get; }
        public GameteSnapshot GameteB { get; }
        public string ParentAGenomeHash { get; }
        public string ParentBGenomeHash { get; }
        public GenomeSnapshot Genome { get; }
        public Seed128 EventSeed { get; }
        public Seed128 AppearanceSeed { get; }
        public Seed128 DevelopmentSeed { get; }
        public string OutcomeHash { get; }
        private IEnumerable<string> Fields() => new[] { "conception.draft.v1", GameteA.ParentId.ToString(), GameteB.ParentId.ToString(), ParentAGenomeHash, ParentBGenomeHash,
            GameteA.ProvenanceHash, GameteB.ProvenanceHash, EventSeed.ToString(), AppearanceSeed.ToString(), DevelopmentSeed.ToString() }.Concat(Genome.CanonicalFields());
        public byte[] CanonicalBytes() => Canonical.Bytes(Fields());
    }

    public sealed class MeiosisService
    {
        public MeiosisService(InheritanceModel model) { Model = Check.NotNull(model, nameof(model)); }
        public InheritanceModel Model { get; }

        // Exact probabilities aggregate allele-identical paths; the gamete's source
        // indices are a REPRESENTATIVE path, not a claim about all aggregated meioses.
        public IReadOnlyList<Weighted<GameteBlock>> EnumerateBlock(PhasedBlockPair block, int maxPaths = 65536)
        {
            if (maxPaths < 1) throw new ArgumentOutOfRangeException(nameof(maxPaths));
            var rule = GetRule(block);
            var results = new Dictionary<string, Weighted<GameteBlock>>(StringComparer.Ordinal);
            var alleles = new string[block.LocusIds.Count]; var sources = new int[block.LocusIds.Count]; var paths = 0;
            void Walk(int locus, int source, Rational probability)
            {
                if (probability == Rational.Zero) return;
                alleles[locus] = source == 0 ? block.HomologA[locus] : block.HomologB[locus]; sources[locus] = source;
                if (locus + 1 == block.LocusIds.Count)
                {
                    if (++paths > maxPaths) throw new ExactEnumerationLimitException(maxPaths);
                    if (!rule.AllowsHaplotype(alleles)) throw new GeneticsSupportException("The declared map can produce an unsupported recombinant in " + block.BlockId + "; the whole operation is rejected, not rerolled.");
                    var gamete = new GameteBlock(block.BlockId, block.IsSynthetic, block.LocusIds, alleles, sources);
                    if (results.TryGetValue(gamete.AlleleKey, out var existing)) results[gamete.AlleleKey] = new Weighted<GameteBlock>(existing.Value, existing.Probability + probability);
                    else results.Add(gamete.AlleleKey, new Weighted<GameteBlock>(gamete, probability));
                    return;
                }
                var fraction = rule.RecombinationFractions[locus];
                Walk(locus + 1, source, probability * (Rational.One - fraction));
                Walk(locus + 1, 1 - source, probability * fraction);
            }
            Walk(0, 0, Rational.Half); Walk(0, 1, Rational.Half);
            return Snapshot.List(results.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value));
        }

        private LinkageBlockRule GetRule(PhasedBlockPair block)
        {
            Check.NotNull(block, nameof(block));
            if (!Model.Blocks.TryGetValue(block.BlockId, out var rule) || block.IsSynthetic != rule.IsSynthetic || !block.LocusIds.SequenceEqual(rule.Loci.Select(l => l.LocusId))) throw new GeneticsSupportException("Unknown block layout: " + block.BlockId);
            for (var i = 0; i < rule.Loci.Count; i++)
                if (!rule.Loci[i].Variant.SupportedAlleleIds.Contains(block.HomologA[i]) || !rule.Loci[i].Variant.SupportedAlleleIds.Contains(block.HomologB[i])) throw new GeneticsSupportException("Unsupported allele at " + block.LocusIds[i]);
            if (!rule.AllowsHaplotype(block.HomologA) || !rule.AllowsHaplotype(block.HomologB)) throw new GeneticsSupportException("Unsupported parental haplotype.");
            return rule;
        }

        public IReadOnlyList<Weighted<GameteSnapshot>> EnumerateGametes(HorseId parentId, GenomeSnapshot parent, int maxOutcomes = 65536)
        {
            Check.NotNull(parentId, nameof(parentId)); Model.ValidateGenome(parent);
            if (maxOutcomes < 1) throw new ArgumentOutOfRangeException(nameof(maxOutcomes));
            var states = new List<(List<GameteBlock> Blocks, Rational Probability)> { (new List<GameteBlock>(), Rational.One) };
            foreach (var block in parent.Blocks)
            {
                var possibilities = EnumerateBlock(block);
                if ((long)states.Count * possibilities.Count > maxOutcomes) throw new ExactEnumerationLimitException(maxOutcomes);
                var next = new List<(List<GameteBlock> Blocks, Rational Probability)>();
                foreach (var state in states)
                    foreach (var possibility in possibilities)
                    {
                        var contributions = new List<GameteBlock>(state.Blocks) { possibility.Value };
                        next.Add((contributions, state.Probability * possibility.Probability));
                    }
                states = next;
            }
            return Snapshot.List(states.Select(s => new Weighted<GameteSnapshot>(new GameteSnapshot(parentId, s.Blocks,
                Model.VariantCatalogVersion, Model.SyntheticModelVersion, Model.Version), s.Probability)));
        }

        public IReadOnlyList<Weighted<GenomeSnapshot>> EnumerateOffspring(HorseRecord parentA, HorseRecord parentB, int maxOutcomes = 65536)
        {
            RequireParents(parentA, parentB);
            var a = EnumerateGametes(parentA.HorseId, parentA.Genome, maxOutcomes); var b = EnumerateGametes(parentB.HorseId, parentB.Genome, maxOutcomes);
            if ((long)a.Count * b.Count > maxOutcomes) throw new ExactEnumerationLimitException(maxOutcomes);
            var outcomes = new Dictionary<string, Weighted<GenomeSnapshot>>(StringComparer.Ordinal);
            foreach (var x in a) foreach (var y in b)
            {
                var genome = GenomeSnapshot.FromGametes(x.Value, y.Value); var probability = x.Probability * y.Probability;
                if (outcomes.TryGetValue(genome.GenomeHash, out var existing)) outcomes[genome.GenomeHash] = new Weighted<GenomeSnapshot>(existing.Value, existing.Probability + probability);
                else outcomes.Add(genome.GenomeHash, new Weighted<GenomeSnapshot>(genome, probability));
            }
            return Snapshot.List(outcomes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value));
        }

        public GameteSnapshot SampleGamete(HorseId parentId, GenomeSnapshot parent, Seed128 seed)
        {
            Check.NotNull(parentId, nameof(parentId)); Model.ValidateGenome(parent);
            // Fail unsupported closures BEFORE any draw, never conditional on seed.
            foreach (var block in parent.Blocks) if (Model.Blocks[block.BlockId].SupportedHaplotypeKeys != null) EnumerateBlock(block);
            var contributions = new List<GameteBlock>();
            foreach (var block in parent.Blocks)
            {
                var rule = Model.Blocks[block.BlockId]; var rng = new CounterRng(seed, "gamete/block/" + block.BlockId);
                var source = rng.Chance(Rational.Half) ? 1 : 0; var alleles = new string[block.LocusIds.Count]; var sources = new int[alleles.Length];
                for (var i = 0; i < alleles.Length; i++)
                {
                    if (i > 0 && rng.Chance(rule.RecombinationFractions[i - 1])) source = 1 - source;
                    sources[i] = source; alleles[i] = source == 0 ? block.HomologA[i] : block.HomologB[i];
                }
                contributions.Add(new GameteBlock(block.BlockId, block.IsSynthetic, block.LocusIds, alleles, sources));
            }
            return new GameteSnapshot(parentId, contributions, Model.VariantCatalogVersion, Model.SyntheticModelVersion, Model.Version);
        }

        public ConceptionDraft CreateDraft(HorseRecord parentA, HorseRecord parentB, Seed128 eventSeed)
        {
            RequireParents(parentA, parentB);
            var a = SampleGamete(parentA.HorseId, parentA.Genome, Seeds.Derive(eventSeed, "gameteA"));
            var b = SampleGamete(parentB.HorseId, parentB.Genome, Seeds.Derive(eventSeed, "gameteB"));
            return new ConceptionDraft(a, b, parentA.Genome.GenomeHash, parentB.Genome.GenomeHash, eventSeed);
        }
        private static void RequireParents(HorseRecord a, HorseRecord b)
        {
            Check.NotNull(a, nameof(a)); Check.NotNull(b, nameof(b));
            if (a.HorseId.Equals(b.HorseId)) throw new ArgumentException("A horse cannot be crossed with itself.");
        }
        public void ValidateGameteTrace(HorseId parentId, GenomeSnapshot parent, GameteSnapshot gamete)
        {
            Model.ValidateGenome(parent); Check.NotNull(gamete, nameof(gamete));
            if (!parentId.Equals(gamete.ParentId) || gamete.VariantCatalogVersion != Model.VariantCatalogVersion || gamete.SyntheticModelVersion != Model.SyntheticModelVersion || gamete.InheritanceModelVersion != Model.Version || parent.Blocks.Count != gamete.Blocks.Count) throw new GeneticsSupportException("Gamete provenance/version mismatch.");
            for (var b = 0; b < parent.Blocks.Count; b++)
            {
                var p = parent.Blocks[b]; var g = gamete.Blocks[b]; var rule = Model.Blocks[p.BlockId];
                if (p.BlockId != g.BlockId || p.IsSynthetic != g.IsSynthetic || !p.LocusIds.SequenceEqual(g.LocusIds)) throw new GeneticsSupportException("Gamete block mismatch.");
                for (var i = 0; i < p.LocusIds.Count; i++)
                {
                    if (g.Alleles[i] != (g.SourceHomologIndices[i] == 0 ? p.HomologA[i] : p.HomologB[i])) throw new GeneticsSupportException("Child allele does not trace to its recorded parental homolog.");
                    if (i > 0 && g.SourceHomologIndices[i] != g.SourceHomologIndices[i - 1] && rule.RecombinationFractions[i - 1] == Rational.Zero) throw new GeneticsSupportException("An impossible crossover is recorded.");
                }
                if (!rule.AllowsHaplotype(g.Alleles)) throw new GeneticsSupportException("Unsupported recombinant gamete.");
            }
        }
    }
}
