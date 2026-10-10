using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class RecordContractTests
    {
        internal static HorseId Id(int n) => new HorseId(Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12")));
        internal static GenomeSnapshot Genome() => new GenomeSnapshot(new[] { new PhasedBlockPair("extension", false, new[] { "mc1r" }, new[] { "E" }, new[] { "e" }) }, "catalog.v1", "game.v1");
        internal static HorseRecord Horse(GenomeSnapshot? genome = null) => new HorseRecord(Id(1), "Clover", new Parentage(), genome ?? Genome(), "resolver.v1", new Seed128(1, 2),
            new OwnerId(Guid.Parse("10000000-0000-0000-0000-000000000001")), new LifeState(LifeStage.Adult, 12, VisualAge.Adult), new ReproductionState(ReproductiveRole.Mare));

        [Fact]
        public void GenomeTakesDeepDefensiveSnapshots()
        {
            var alleles = new[] { "E" }; var loci = new List<string> { "mc1r" };
            var block = new PhasedBlockPair("extension", false, loci, alleles, new[] { "e" });
            var blocks = new List<PhasedBlockPair> { block }; var genome = new GenomeSnapshot(blocks, "c1", "s1"); var hash = genome.GenomeHash;
            alleles[0] = "e"; loci[0] = "other"; blocks.Clear();
            Assert.Equal("E", genome.GetAlleles("mc1r").A); Assert.Equal(hash, genome.GenomeHash);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)block.HomologA)[0] = "e");
            Assert.Throws<NotSupportedException>(() => ((IList<PhasedBlockPair>)genome.Blocks).Clear());
        }
        [Fact]
        public void IndependentBlockOrderIsCanonicalButPhaseIsPreserved()
        {
            var a = new PhasedBlockPair("a", false, new[] { "x", "y" }, new[] { "A", "B" }, new[] { "a", "b" });
            var b = new PhasedBlockPair("b", true, new[] { "game.z" }, new[] { "1" }, new[] { "0" });
            Assert.Equal(new GenomeSnapshot(new[] { a, b }, "c", "s").GenomeHash, new GenomeSnapshot(new[] { b, a }, "c", "s").GenomeHash);
            var repulsion = new PhasedBlockPair("a", false, a.LocusIds, new[] { "A", "b" }, new[] { "a", "B" });
            Assert.NotEqual(new GenomeSnapshot(new[] { a }, "c", "s").GenomeHash, new GenomeSnapshot(new[] { repulsion }, "c", "s").GenomeHash);
        }
        [Fact]
        public void MalformedGenomesAreRejectedRatherThanDefaulted()
        {
            Assert.Throws<ArgumentException>(() => new PhasedBlockPair("x", false, new[] { "x", "y" }, new[] { "A" }, new[] { "a" }));
            var a = new PhasedBlockPair("a", false, new[] { "x" }, new[] { "A" }, new[] { "a" });
            var b = new PhasedBlockPair("b", false, new[] { "x" }, new[] { "A" }, new[] { "a" });
            Assert.Throws<ArgumentException>(() => new GenomeSnapshot(new[] { a, b }, "c", "s"));
            Assert.Throws<KeyNotFoundException>(() => Genome().GetAlleles("missing"));
        }
        [Fact]
        public void RenamingCannotChangeInheritedStateOrRevealKnowledge()
        {
            var original = Horse(); var renamed = original.Rename("Buttercup");
            Assert.Equal(original.HorseId, renamed.HorseId); Assert.Same(original.Genome, renamed.Genome);
            Assert.Equal(original.AppearanceSeed, renamed.AppearanceSeed); Assert.Same(original.Parentage, renamed.Parentage);
            Assert.Equal(original.ResolverVersion, renamed.ResolverVersion); Assert.Empty(renamed.PlayerKnowledge.RevealedCalls);
            Assert.Equal(1UL, renamed.Revision); Assert.Equal("Clover", original.Name);
        }
        [Fact]
        public void QuantizedStateAndKnowledgeDoNotAliasCallers()
        {
            var values = new Dictionary<string, int> { ["height"] = 500 }; var vector = new QuantizedVector(values); values["height"] = 900;
            Assert.Equal(500, vector.Values["height"]);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)vector.Values)["height"] = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuantizedVector(new Dictionary<string, int> { ["height"] = 10001 }));
            var calls = new Dictionary<string, AllelePair> { ["mc1r"] = new AllelePair("E", "e") };
            var knowledge = new PlayerKnowledgeRecord(calls, new[] { Id(2) }); calls.Clear(); Assert.Single(knowledge.RevealedCalls);
        }
        [Fact]
        public void FounderAssumptionsAreExplicitAndInvalidParentageFails()
        {
            Assert.Null(new Parentage().FounderBackground);
            Assert.Throws<ArgumentException>(() => new Parentage(Id(1), Id(1)));
            Assert.Throws<ArgumentException>(() => new Parentage(Id(1), null, new FounderBackground("test", Rational.Zero)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FounderBackground("test", new Rational(2, 1)));
        }
        [Fact]
        public void FictionalMarkersCannotPretendToBeRealGenes()
        {
            VariantCatalogRecord Make(string? gene, string? notice) => new VariantCatalogRecord("game.height", "game.v1", "Height marker", EvidenceStatus.FictionalGameTrait,
                "game.body", new[] { "0", "1" }, Array.Empty<string>(), "mendelian.v1", "additive.v1", Array.Empty<SourceReference>(), geneSymbol: gene, gameAbstractionNotice: notice);
            Assert.Throws<ArgumentException>(() => Make("MC1R", "fictional")); Assert.Throws<ArgumentException>(() => Make(null, null));
            Assert.Null(Make(null, "A fictional game parameter, not an equine genetic finding.").GeneSymbol);
        }
        [Fact]
        public void CoreContractsAreReadOnlyAndVisualDescriptorHasNoGenomeReference()
        {
            foreach (var type in new[] { typeof(HorseRecord), typeof(GenomeSnapshot), typeof(Phenotype), typeof(HorseVisualDescriptor), typeof(BreedingCommit), typeof(SaveHeader), typeof(VariantCatalogRecord) })
                Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
            Assert.DoesNotContain(typeof(HorseVisualDescriptor).GetProperties(), p => p.PropertyType == typeof(GenomeSnapshot) || p.PropertyType == typeof(HorseRecord) || p.PropertyType == typeof(Phenotype));
        }
        [Fact]
        public void SaveHeaderRetainsVersionsWithoutAliasing()
        {
            var versions = new List<string> { "old.v1", "new.v2" }; var migrations = new List<string> { "explicit-import" };
            var header = new SaveHeader(new SaveId(Guid.NewGuid()), SaveMode.Standard, "schema.v1", "build.v1", 1, 0, "calendar.v1", CareMode.Gentle,
                "catalog.v1", "inheritance.v1", "new.v2", versions, RngStreams.FromSeed(default), 0, migrations, Canonical.Hash("payload"));
            versions.Clear(); migrations.Clear(); Assert.Equal(2, header.RetainedResolverVersions.Count); Assert.Single(header.MigrationHistory);
            Assert.Contains("old.v1", header.RetainedResolverVersions);
        }
    }
}
