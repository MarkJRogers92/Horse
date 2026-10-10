using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public static class CoreGeneticsCatalog
    {
        public const string CatalogVersion = "coat.catalog.v1";
        public const string SyntheticVersion = "game.markers.v1";
        public const string InheritanceVersion = "inheritance.core.v1";
        public const string ResolverVersion = "phenotype.v1";
        public static InheritanceModel Model { get; } = Build();

        private static LocusDefinition Biological(string locus, string gene, string[] alleles, string source, string claim, EvidenceStatus evidence = EvidenceStatus.EstablishedBiological)
        {
            return new LocusDefinition(locus, new VariantCatalogRecord(locus + ".supported", CatalogVersion, gene + " supported calls", evidence,
                locus, alleles, Array.Empty<string>(), "mendelian.phased.v1", "phenotype.v1", new[] { new SourceReference(source, claim) },
                geneSymbol: gene, releaseStatus: CatalogReleaseStatus.Supported,
                gameAbstractionNotice: "Sparse supported calls only. Catalog support is not visual approval or permission to release live horses."));
        }
        private static InheritanceModel Build()
        {
            var defs = new Dictionary<string, LocusDefinition>(StringComparer.Ordinal)
            {
                ["mc1r"] = Biological("mc1r", "MC1R", new[] { "E", "e" }, "https://vgl.ucdavis.edu/test/red-factor-horse", "Supported E/e red-factor inheritance."),
                ["asip"] = Biological("asip", "ASIP", new[] { "A", "a" }, "https://vgl.ucdavis.edu/test/agouti-horse", "Agouti expression is masked by red-only chestnut pigment."),
                ["slc45a2"] = Biological("slc45a2", "SLC45A2", new[] { "N", "Cr", "Prl" }, "https://vgl.ucdavis.edu/resources/horse-coat-color", "Cream and pearl use supported alternative SLC45A2 haplotypes, not independent switches."),
                ["tbx3"] = Biological("tbx3", "TBX3", new[] { "D", "nd1", "nd2" }, "https://vgl.ucdavis.edu/test/dun", "Dun and supported non-dun states; nd1 may show primitive markings without dun dilution."),
                ["pmel"] = Biological("pmel", "PMEL", new[] { "N", "Z" }, "https://vgl.ucdavis.edu/test/silver", "Silver expression on black pigment can be masked on chestnut."),
                ["slc36a1"] = Biological("slc36a1", "SLC36A1", new[] { "N", "Ch" }, "https://vgl.ucdavis.edu/resources/horse-coat-color", "Champagne dilution, with skin and eye effects."),
                ["stx17"] = Biological("stx17", "STX17", new[] { "N", "G" }, "https://vgl.ucdavis.edu/test/gray_copy_number", "Grey is progressive. G is a simplified supported state, not a complete copy-number model."),
                ["trpm1"] = Biological("trpm1", "TRPM1", new[] { "N", "LP" }, "https://vgl.ucdavis.edu/test/leopard-complex", "Leopard-complex inheritance and expression context."),
                ["patn1"] = Biological("patn1", "RFWD3", new[] { "N", "PATN1" }, "https://vgl.ucdavis.edu/test/appaloosa-pattern-1", "PATN1 modifies LP-associated pattern expression; it cannot create LP by itself.")
            };
            foreach (var name in new[] { "eca3", "mitf", "pax3", "ednrb" })
                defs.Add("pattern." + name, Biological("pattern." + name, name == "eca3" ? "KIT-associated region" : name.ToUpperInvariant(),
                    new[] { "baseline" }, "https://vgl.ucdavis.edu/resources/horse-coat-color", "Only the fixed absence-of-supported-pattern state is enabled here; other haplotypes remain deferred.", EvidenceStatus.AssociatedBiological));
            var rules = new List<LinkageBlockRule>();
            // The ECA3 pattern region is fixed at baseline in this slice. Keeping it
            // with MC1R makes future nonbaseline patterns require an explicit map update.
            rules.Add(new LinkageBlockRule("eca3.core", false, new[] { defs["mc1r"], defs["pattern.eca3"] }, new[] { Rational.Zero }, "eca3.fixed-pattern.v1",
                "Pattern region is fixed at baseline; zero is a slice restriction, NOT an empirical horse recombination estimate.",
                new[] { new[] { "E", "baseline" }, new[] { "e", "baseline" } }));
            foreach (var pair in defs.Where(p => p.Key != "mc1r" && p.Key != "pattern.eca3").OrderBy(p => p.Key, StringComparer.Ordinal))
                rules.Add(new LinkageBlockRule(pair.Key, false, new[] { pair.Value }, Array.Empty<Rational>(), "single-locus.v1",
                    "Single supported locus block. MC1R/ASIP independence follows the frozen cross fixture. No physical map distance is claimed by this logical ID."));
            for (var block = 0; block < 8; block++)
            {
                var loci = new List<LocusDefinition>();
                for (var j = 0; j < 4; j++)
                {
                    var id = "game.marker." + (block * 4 + j).ToString("D2", CultureInfo.InvariantCulture);
                    loci.Add(new LocusDefinition(id, new VariantCatalogRecord(id, SyntheticVersion, "Virtual inherited marker", EvidenceStatus.FictionalGameTrait,
                        "game.block." + block.ToString(CultureInfo.InvariantCulture), new[] { "0", "1" }, Array.Empty<string>(), "synthetic.phased.v1", "game.traits.v1", Array.Empty<SourceReference>(),
                        gameAbstractionNotice: "Fictional game marker. Alleles, effects, linkage and aptitude are invented tuning parameters, not real equine genes.")));
                }
                rules.Add(new LinkageBlockRule("game.block." + block.ToString(CultureInfo.InvariantCulture), true, loci,
                    Enumerable.Repeat(new Rational(1, 10), 3), "synthetic.map.v1", "Invented 10% adjacent transition fractions; no biological-map claim."));
            }
            return new InheritanceModel(InheritanceVersion, CatalogVersion, SyntheticVersion, rules);
        }
    }
}
