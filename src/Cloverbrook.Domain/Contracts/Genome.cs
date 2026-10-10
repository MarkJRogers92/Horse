using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class AllelePair
    {
        public AllelePair(string a, string b) { A = Check.Text(a, nameof(a)); B = Check.Text(b, nameof(b)); }
        public string A { get; }
        public string B { get; }
        public int Count(string allele) => (A == allele ? 1 : 0) + (B == allele ? 1 : 0);
        public bool Contains(string allele) => Count(allele) != 0;
    }

    // A sparse, phase-aware linkage block, NOT an assertion of a complete physical
    // chromosome or a measured map distance. Its versioned map defines segregation.
    public sealed class PhasedBlockPair
    {
        public PhasedBlockPair(string blockId, bool isSynthetic, IEnumerable<string> locusIds,
            IEnumerable<string> homologA, IEnumerable<string> homologB)
        {
            BlockId = Check.Text(blockId, nameof(blockId)); IsSynthetic = isSynthetic;
            LocusIds = Snapshot.List(locusIds); HomologA = Snapshot.List(homologA); HomologB = Snapshot.List(homologB);
            if (LocusIds.Count == 0 || HomologA.Count != LocusIds.Count || HomologB.Count != LocusIds.Count)
                throw new ArgumentException("Both phased homologs must cover the same nonempty ordered loci.");
            if (LocusIds.Distinct(StringComparer.Ordinal).Count() != LocusIds.Count) throw new ArgumentException("Duplicate locus in block.");
            foreach (var value in LocusIds.Concat(HomologA).Concat(HomologB)) Check.Text(value, nameof(locusIds));
        }
        public string BlockId { get; }
        public bool IsSynthetic { get; }
        public IReadOnlyList<string> LocusIds { get; }
        public IReadOnlyList<string> HomologA { get; }
        public IReadOnlyList<string> HomologB { get; }
        internal IEnumerable<string> CanonicalFields()
        {
            yield return IsSynthetic ? "synthetic" : "biological"; yield return BlockId;
            yield return LocusIds.Count.ToString(CultureInfo.InvariantCulture);
            for (var i = 0; i < LocusIds.Count; i++) { yield return LocusIds[i]; yield return HomologA[i]; yield return HomologB[i]; }
        }
    }

    public sealed class GenomeSnapshot
    {
        public GenomeSnapshot(IEnumerable<PhasedBlockPair> blocks, string variantCatalogVersion, string syntheticModelVersion)
        {
            VariantCatalogVersion = Check.Text(variantCatalogVersion, nameof(variantCatalogVersion));
            SyntheticModelVersion = Check.Text(syntheticModelVersion, nameof(syntheticModelVersion));
            Blocks = Snapshot.List(Check.NotNull(blocks, nameof(blocks)).OrderBy(b => b.BlockId, StringComparer.Ordinal));
            if (Blocks.Count == 0) throw new ArgumentException("A genome cannot be empty.", nameof(blocks));
            if (Blocks.Select(b => b.BlockId).Distinct(StringComparer.Ordinal).Count() != Blocks.Count) throw new ArgumentException("Duplicate block.");
            var loci = Blocks.SelectMany(b => b.LocusIds).ToArray();
            if (loci.Distinct(StringComparer.Ordinal).Count() != loci.Length) throw new ArgumentException("A locus must belong to exactly one linkage block.");
            BiologicalBlocks = Snapshot.List(Blocks.Where(b => !b.IsSynthetic));
            SyntheticBlocks = Snapshot.List(Blocks.Where(b => b.IsSynthetic));
            GenomeHash = Canonical.Hash(CanonicalFields());
        }
        public IReadOnlyList<PhasedBlockPair> Blocks { get; }
        public IReadOnlyList<PhasedBlockPair> BiologicalBlocks { get; }
        public IReadOnlyList<PhasedBlockPair> SyntheticBlocks { get; }
        public string VariantCatalogVersion { get; }
        public string SyntheticModelVersion { get; }
        public string GenomeHash { get; }
        public AllelePair GetAlleles(string locusId)
        {
            foreach (var block in Blocks)
                for (var i = 0; i < block.LocusIds.Count; i++)
                    if (block.LocusIds[i] == locusId) return new AllelePair(block.HomologA[i], block.HomologB[i]);
            throw new KeyNotFoundException("Genome lacks locus " + locusId + "; no normal allele is inferred.");
        }
        public bool HasLocus(string locusId) => Blocks.Any(b => b.LocusIds.Contains(locusId));
        public IEnumerable<string> CanonicalFields()
        {
            yield return "genome.v1"; yield return VariantCatalogVersion; yield return SyntheticModelVersion;
            yield return Blocks.Count.ToString(CultureInfo.InvariantCulture);
            foreach (var block in Blocks) foreach (var part in block.CanonicalFields()) yield return part;
        }
        public static GenomeSnapshot FromGametes(GameteSnapshot a, GameteSnapshot b)
        {
            Check.NotNull(a, nameof(a)); Check.NotNull(b, nameof(b));
            if (a.VariantCatalogVersion != b.VariantCatalogVersion || a.SyntheticModelVersion != b.SyntheticModelVersion || a.InheritanceModelVersion != b.InheritanceModelVersion)
                throw new ArgumentException("Gamete versions differ; explicit migration is required.");
            if (a.Blocks.Count != b.Blocks.Count) throw new ArgumentException("Gamete layouts differ.");
            var blocks = new List<PhasedBlockPair>();
            for (var i = 0; i < a.Blocks.Count; i++)
            {
                var x = a.Blocks[i]; var y = b.Blocks[i];
                if (x.BlockId != y.BlockId || x.IsSynthetic != y.IsSynthetic || !x.LocusIds.SequenceEqual(y.LocusIds)) throw new ArgumentException("Gamete layouts differ.");
                blocks.Add(new PhasedBlockPair(x.BlockId, x.IsSynthetic, x.LocusIds, x.Alleles, y.Alleles));
            }
            return new GenomeSnapshot(blocks, a.VariantCatalogVersion, a.SyntheticModelVersion);
        }
    }

    public sealed class GameteBlock
    {
        public GameteBlock(string blockId, bool isSynthetic, IEnumerable<string> locusIds,
            IEnumerable<string> alleles, IEnumerable<int> sourceHomologIndices)
        {
            BlockId = Check.Text(blockId, nameof(blockId)); IsSynthetic = isSynthetic;
            LocusIds = Snapshot.List(locusIds); Alleles = Snapshot.List(alleles); SourceHomologIndices = Snapshot.List(sourceHomologIndices);
            if (LocusIds.Count == 0 || Alleles.Count != LocusIds.Count || SourceHomologIndices.Count != LocusIds.Count)
                throw new ArgumentException("Gamete calls and provenance must cover every ordered locus.");
            if (LocusIds.Distinct(StringComparer.Ordinal).Count() != LocusIds.Count) throw new ArgumentException("Duplicate gamete locus.");
            foreach (var value in LocusIds.Concat(Alleles)) Check.Text(value, nameof(locusIds));
            if (SourceHomologIndices.Any(v => v != 0 && v != 1)) throw new ArgumentException("Homolog index must be 0 or 1.");
            CrossoverAfterIndices = Snapshot.List(Enumerable.Range(0, LocusIds.Count - 1).Where(i => SourceHomologIndices[i] != SourceHomologIndices[i + 1]));
        }
        public string BlockId { get; }
        public bool IsSynthetic { get; }
        public IReadOnlyList<string> LocusIds { get; }
        public IReadOnlyList<string> Alleles { get; }
        public IReadOnlyList<int> SourceHomologIndices { get; }
        public IReadOnlyList<int> CrossoverAfterIndices { get; }
        public string AlleleKey => Canonical.Hash(new[] { "gamete.block.alleles.v1", BlockId, IsSynthetic.ToString() }.Concat(LocusIds.Zip(Alleles, (l, a) => Canonical.Hash(l, a))));
    }

    public sealed class GameteSnapshot
    {
        public GameteSnapshot(HorseId parentId, IEnumerable<GameteBlock> blocks, string variantCatalogVersion,
            string syntheticModelVersion, string inheritanceModelVersion)
        {
            ParentId = Check.NotNull(parentId, nameof(parentId));
            Blocks = Snapshot.List(Check.NotNull(blocks, nameof(blocks)).OrderBy(b => b.BlockId, StringComparer.Ordinal));
            if (Blocks.Count == 0 || Blocks.Select(b => b.BlockId).Distinct(StringComparer.Ordinal).Count() != Blocks.Count) throw new ArgumentException("Invalid gamete block set.");
            var loci = Blocks.SelectMany(b => b.LocusIds).ToArray();
            if (loci.Distinct(StringComparer.Ordinal).Count() != loci.Length) throw new ArgumentException("Duplicate gamete locus across blocks.");
            VariantCatalogVersion = Check.Text(variantCatalogVersion, nameof(variantCatalogVersion));
            SyntheticModelVersion = Check.Text(syntheticModelVersion, nameof(syntheticModelVersion));
            InheritanceModelVersion = Check.Text(inheritanceModelVersion, nameof(inheritanceModelVersion));
            AlleleKey = Canonical.Hash(new[] { "gamete.alleles.v1", VariantCatalogVersion, SyntheticModelVersion, InheritanceModelVersion }.Concat(Blocks.Select(b => b.AlleleKey)));
            ProvenanceHash = Canonical.Hash(new[] { "gamete.provenance.v1", ParentId.ToString(), AlleleKey }.Concat(Blocks.Select(b => Canonical.Hash(b.SourceHomologIndices.Select(i => i.ToString(CultureInfo.InvariantCulture))))));
        }
        public HorseId ParentId { get; }
        public IReadOnlyList<GameteBlock> Blocks { get; }
        public string VariantCatalogVersion { get; }
        public string SyntheticModelVersion { get; }
        public string InheritanceModelVersion { get; }
        public string AlleleKey { get; }
        public string ProvenanceHash { get; }
    }
}
