using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class GeneticsSupportException : Exception
    {
        public GeneticsSupportException(string message) : base(message) { }
    }
    public sealed class ExactEnumerationLimitException : Exception
    {
        public ExactEnumerationLimitException(int limit) : base("Exact enumeration exceeds the explicit limit of " + limit + "; no truncated distribution was returned.") { }
    }
    public sealed class LocusDefinition
    {
        public LocusDefinition(string locusId, VariantCatalogRecord variant)
        {
            LocusId = Check.Text(locusId, nameof(locusId)); Variant = Check.NotNull(variant, nameof(variant));
        }
        public string LocusId { get; }
        public VariantCatalogRecord Variant { get; }
    }
    public sealed class LinkageBlockRule
    {
        public LinkageBlockRule(string blockId, bool isSynthetic, IEnumerable<LocusDefinition> loci,
            IEnumerable<Rational> recombinationFractions, string mapVersion, string abstractionNotice,
            IEnumerable<IEnumerable<string>>? supportedHaplotypes = null)
        {
            BlockId = Check.Text(blockId, nameof(blockId)); IsSynthetic = isSynthetic; Loci = Snapshot.List(loci);
            if (Loci.Count == 0 || Loci.Count > 16 || Loci.Select(l => l.LocusId).Distinct(StringComparer.Ordinal).Count() != Loci.Count) throw new ArgumentException("A supported block needs 1-16 distinct ordered loci.");
            if (Loci.Any(l => (l.Variant.EvidenceStatus == EvidenceStatus.FictionalGameTrait) != isSynthetic)) throw new ArgumentException("Biological and fictional markers cannot share a block.");
            RecombinationFractions = Snapshot.List(recombinationFractions);
            if (RecombinationFractions.Count != Loci.Count - 1 || RecombinationFractions.Any(r => r < Rational.Zero || r > Rational.Half)) throw new ArgumentException("Declare one recombination fraction in [0,1/2] per adjacent marker pair.");
            MapVersion = Check.Text(mapVersion, nameof(mapVersion)); AbstractionNotice = Check.Text(abstractionNotice, nameof(abstractionNotice));
            if (supportedHaplotypes != null)
            {
                var copies = supportedHaplotypes.Select(h => Snapshot.List(h)).ToArray();
                if (copies.Length == 0) throw new ArgumentException("An explicit haplotype catalog cannot be empty.");
                foreach (var calls in copies)
                {
                    if (calls.Count != Loci.Count) throw new ArgumentException("Haplotype layout differs from its block.");
                    for (var i = 0; i < calls.Count; i++) if (!Loci[i].Variant.SupportedAlleleIds.Contains(calls[i])) throw new ArgumentException("Haplotype contains an unsupported allele.");
                }
                SupportedHaplotypeKeys = Snapshot.List(copies.Select(h => Canonical.Hash(h)).Distinct(StringComparer.Ordinal));
            }
        }
        public string BlockId { get; }
        public bool IsSynthetic { get; }
        public IReadOnlyList<LocusDefinition> Loci { get; }
        public IReadOnlyList<Rational> RecombinationFractions { get; }
        public string MapVersion { get; }
        public string AbstractionNotice { get; }
        public IReadOnlyList<string>? SupportedHaplotypeKeys { get; }
        public bool AllowsHaplotype(IEnumerable<string> calls) => SupportedHaplotypeKeys == null || SupportedHaplotypeKeys.Contains(Canonical.Hash(calls));
    }

    public sealed class InheritanceModel
    {
        public InheritanceModel(string version, string variantCatalogVersion, string syntheticModelVersion, IEnumerable<LinkageBlockRule> blocks)
        {
            Version = Check.Text(version, nameof(version)); VariantCatalogVersion = Check.Text(variantCatalogVersion, nameof(variantCatalogVersion));
            SyntheticModelVersion = Check.Text(syntheticModelVersion, nameof(syntheticModelVersion));
            var list = Snapshot.List(blocks);
            if (list.Count == 0) throw new ArgumentException("An inheritance model cannot be empty.");
            Blocks = Snapshot.Map(list.Select(b => new KeyValuePair<string, LinkageBlockRule>(b.BlockId, b)));
            var loci = list.SelectMany(b => b.Loci).ToArray();
            if (loci.Select(l => l.LocusId).Distinct(StringComparer.Ordinal).Count() != loci.Length) throw new ArgumentException("A locus has more than one map owner.");
            foreach (var block in list)
                foreach (var locus in block.Loci)
                    if (locus.Variant.CatalogVersion != (block.IsSynthetic ? syntheticModelVersion : variantCatalogVersion)) throw new ArgumentException("Locus catalog version does not match its model.");
        }
        public string Version { get; }
        public string VariantCatalogVersion { get; }
        public string SyntheticModelVersion { get; }
        public IReadOnlyDictionary<string, LinkageBlockRule> Blocks { get; }
        public void ValidateGenome(GenomeSnapshot genome)
        {
            Check.NotNull(genome, nameof(genome));
            if (genome.VariantCatalogVersion != VariantCatalogVersion || genome.SyntheticModelVersion != SyntheticModelVersion) throw new GeneticsSupportException("Genome versions require an explicit supported model or migration.");
            if (genome.Blocks.Count != Blocks.Count) throw new GeneticsSupportException("Genome has missing or additional linkage blocks.");
            foreach (var block in genome.Blocks)
            {
                if (!Blocks.TryGetValue(block.BlockId, out var rule) || block.IsSynthetic != rule.IsSynthetic || !block.LocusIds.SequenceEqual(rule.Loci.Select(l => l.LocusId))) throw new GeneticsSupportException("Unsupported block layout: " + block.BlockId);
                for (var i = 0; i < rule.Loci.Count; i++)
                    if (!rule.Loci[i].Variant.SupportedAlleleIds.Contains(block.HomologA[i]) || !rule.Loci[i].Variant.SupportedAlleleIds.Contains(block.HomologB[i])) throw new GeneticsSupportException("Unsupported allele at " + rule.Loci[i].LocusId);
                if (!rule.AllowsHaplotype(block.HomologA) || !rule.AllowsHaplotype(block.HomologB)) throw new GeneticsSupportException("Unsupported cis haplotype in " + block.BlockId);
            }
        }
    }
}
