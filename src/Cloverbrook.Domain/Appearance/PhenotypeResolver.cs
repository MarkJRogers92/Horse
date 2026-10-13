using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    public enum ResolutionStatus { Supported, Unsupported }
    public sealed class PhenotypeResolution
    {
        private PhenotypeResolution(Phenotype? phenotype, GameTraitProfile? traits, IEnumerable<string> requiredClasses, string? reason)
        { Phenotype = phenotype; Traits = traits; RequiredCoatClasses = Snapshot.List(requiredClasses); UnsupportedReason = reason; }
        public ResolutionStatus Status => Phenotype == null ? ResolutionStatus.Unsupported : ResolutionStatus.Supported;
        public Phenotype? Phenotype { get; }
        public GameTraitProfile? Traits { get; }
        public IReadOnlyList<string> RequiredCoatClasses { get; }
        public string? UnsupportedReason { get; }
        internal static PhenotypeResolution Supported(Phenotype phenotype, GameTraitProfile traits, IEnumerable<string> requiredClasses) => new PhenotypeResolution(phenotype, traits, requiredClasses, null);
        internal static PhenotypeResolution Unsupported(string reason) => new PhenotypeResolution(null, null, Array.Empty<string>(), reason);
    }
    public sealed class PresentationResolution
    {
        internal PresentationResolution(HorseVisualDescriptor? descriptor, string? displayLabel, IEnumerable<string> reasons)
        { Descriptor = descriptor; DisplayLabel = displayLabel; Reasons = Snapshot.List(reasons); }
        public ResolutionStatus Status => Descriptor == null ? ResolutionStatus.Unsupported : ResolutionStatus.Supported;
        public HorseVisualDescriptor? Descriptor { get; }
        public string? DisplayLabel { get; }
        public IReadOnlyList<string> Reasons { get; }
    }
    public sealed class PhenotypeResolver
    {
        public string Version => CoreGeneticsCatalog.ResolverVersion;
        // Authoritative/internal genetics only. A supported result here is NOT a
        // released visual or a knowledge-limited player forecast.
        public PhenotypeResolution ResolveGenetics(HorseRecord horse)
        {
            Check.NotNull(horse, nameof(horse));
            if (horse.ResolverVersion != Version) return PhenotypeResolution.Unsupported("Resolver version " + horse.ResolverVersion + " is not installed; migration is never implicit.");
            try
            {
                CoreGeneticsCatalog.Model.ValidateGenome(horse.Genome);
                var g = horse.Genome; var traits = GameTraitResolver.Resolve(g, horse.DevelopmentalFactors);
                var baseCoat = g.GetAlleles("mc1r").Count("e") == 2 ? "chestnut" : g.GetAlleles("asip").Contains("A") ? "bay" : "black";
                var pigment = baseCoat == "chestnut" ? "pheomelanin_full" : baseCoat == "bay" ? "eumelanin_points_pheomelanin_body" : "eumelanin_full";
                var coatClass = baseCoat; var diagnostic = baseCoat; var dilution = "none";
                var layers = new List<string> { "base_pigment" }; var required = new HashSet<string>(StringComparer.Ordinal) { baseCoat };
                var notes = new List<string>(); var skin = "pigmented"; var eyes = "brown"; var hair = "base_" + baseCoat;
                int cream = g.GetAlleles("slc45a2").Count("Cr"), pearl = g.GetAlleles("slc45a2").Count("Prl");
                bool dun = g.GetAlleles("tbx3").Contains("D"), nd1 = !dun && g.GetAlleles("tbx3").Contains("nd1");
                bool silver = g.GetAlleles("pmel").Contains("Z") && baseCoat != "chestnut";
                bool champagne = g.GetAlleles("slc36a1").Contains("Ch"), grey = g.GetAlleles("stx17").Contains("G"), lp = g.GetAlleles("trpm1").Contains("LP");
                bool pearlExpression = pearl == 2 || (pearl == 1 && cream == 1);
                // Explicitly unimplemented interactions are never approximated by dropping a layer.
                var advanced = (silver ? 1 : 0) + (champagne ? 1 : 0) + (grey ? 1 : 0) + (lp ? 1 : 0) + (pearlExpression ? 1 : 0);
                if (advanced > 1 || (advanced > 0 && (dun || nd1 || (cream > 0 && !pearlExpression))))
                    return PhenotypeResolution.Unsupported("This advanced modifier interaction has no versioned expression rule yet.");
                if (cream == 1 && pearl == 1)
                {
                    coatClass = "cream_pearl"; diagnostic = "creamPearlOn" + Title(baseCoat); dilution = "cream_pearl";
                    layers.Add("pearl"); skin = "cream_pearl_lightened"; eyes = "cream_pearl_lightened"; hair = "cream_pearl_lightened";
                }
                else if (pearl == 2)
                {
                    coatClass = "pearl"; diagnostic = "pearlOn" + Title(baseCoat); dilution = "pearl";
                    layers.Add("pearl"); skin = "pearl_lightened"; eyes = "pearl_lightened"; hair = "pearl_lightened";
                }
                else if (cream == 2)
                {
                    coatClass = baseCoat == "chestnut" ? "cremello" : baseCoat == "bay" ? "perlino" : "smoky_cream";
                    diagnostic = coatClass; dilution = "cream_double"; layers.Add("cream_double"); skin = "pink"; eyes = "blue"; hair = "double_cream_pale";
                }
                else if (cream == 1)
                {
                    coatClass = baseCoat == "chestnut" ? "palomino" : baseCoat == "bay" ? "buckskin" : "smoky_black";
                    diagnostic = coatClass; dilution = "cream_single"; layers.Add("cream_single");
                    if (baseCoat == "chestnut") hair = "cream_pale_mane_tail";
                }
                else if (pearl == 1) diagnostic = baseCoat + "CarryingPearl"; // diagnostic only, never the visible label
                required.Add(coatClass);
                var primitive = dun ? 10000 : nd1 ? 3000 : 0;
                if (dun)
                {
                    layers.Add("dun_dilution"); layers.Add("primitive_markings");
                    coatClass = cream == 0 ? (baseCoat == "chestnut" ? "red_dun" : baseCoat == "bay" ? "bay_dun" : "grullo")
                        : cream == 1 ? "dunalino_family" : "double_cream_dun";
                    diagnostic = coatClass; dilution += "+dun"; required.Add(coatClass);
                }
                else if (nd1) { layers.Add("primitive_markings"); required.Add("non_dun1_marked"); }
                if (g.GetAlleles("pmel").Contains("Z")) notes.Add("silver_association_codex; no medical events simulated");
                if (silver) { layers.Add("silver"); required.Add("silver_overlay"); hair = "silver_lightened_mane_tail"; diagnostic = "silverOn" + Title(baseCoat); }
                if (champagne)
                {
                    layers.Add("champagne"); required.Add("champagne_overlay"); dilution = "champagne";
                    skin = "champagne_mottled"; eyes = "champagne_amber"; hair = "champagne_lightened"; diagnostic = "champagneOn" + Title(baseCoat);
                }
                // These are explicit artistic age/coverage parameters, not inferred clinical measurements.
                var grayAmount = grey ? horse.LifeState.VisualAge == VisualAge.Foal ? 0 : horse.LifeState.VisualAge == VisualAge.Adult ? 5000 : 9000 : 0;
                if (grey) { layers.Add("grey_age"); required.Add("grey_overlay"); notes.Add("grey.v1 artistic age curve; simplified G state"); }
                var patnCoverage = lp && g.GetAlleles("patn1").Contains("PATN1") ? 8000 : 0;
                if (lp) { layers.Add("leopard_complex"); required.Add("leopard_complex_overlay"); notes.Add("LP/PATN1 coverage is artistic; LP dosage-specific art remains unreleased"); }
                layers.Add("individual_white_markings"); layers.Add("shading_modifiers");
                var phenotype = new Phenotype(horse.HorseId, g.GenomeHash, Version, horse.AppearanceSeed, horse.LifeState.VisualAge, baseCoat, coatClass, diagnostic,
                    pigment, dilution, layers, skin, eyes, hair, lp, patnCoverage, grayAmount, primitive, traits.Morphology, notes,
                    Canonical.Hash(GameTraitResolver.Version, horse.DevelopmentalFactors.Hash));
                return PhenotypeResolution.Supported(phenotype, traits, required.OrderBy(c => c, StringComparer.Ordinal));
            }
            catch (GeneticsSupportException exception) { return PhenotypeResolution.Unsupported(exception.Message); }
        }
        private static string Title(string text) => char.ToUpperInvariant(text[0]) + text.Substring(1);
        public PresentationResolution ResolveForPresentation(HorseRecord horse, CoatSupportMatrix matrix, string visualAssetCatalogVersion,
            string anatomicalMaskSetId, string? cosmeticLayer = null)
        {
            Check.NotNull(matrix, nameof(matrix)); var result = ResolveGenetics(horse);
            if (result.Status == ResolutionStatus.Unsupported) return new PresentationResolution(null, null, new[] { result.UnsupportedReason! });
            var phenotype = result.Phenotype!; var reasons = matrix.ReleasedSupportProblems(result.RequiredCoatClasses, phenotype.CoatLayers);
            if (reasons.Count != 0) return new PresentationResolution(null, null, reasons);
            var mesh = result.Traits!.BodyFamily + (phenotype.VisualAge == VisualAge.Foal ? ".foal.v1" : ".adult.v1");
            var descriptor = new HorseVisualDescriptor(phenotype, result.RequiredCoatClasses, matrix.Version, visualAssetCatalogVersion, mesh, anatomicalMaskSetId, cosmeticLayer);
            return new PresentationResolution(descriptor, VisibleLabel(phenotype), Array.Empty<string>());
        }
        public static string VisibleLabel(Phenotype p)
        {
            Check.NotNull(p, nameof(p));
            var label = p.CoatClassId == "dunalino_family" ? p.BaseCoatClass == "chestnut" ? "Dunalino" : p.BaseCoatClass == "bay" ? "Dunskin" : "Smoky grullo"
                : Title(p.CoatClassId.Replace('_', ' '));
            if (p.PrimitiveMarkingStrength > 0 && !p.CoatLayers.Contains("dun_dilution")) label += " with primitive markings";
            if (p.CoatLayers.Contains("silver")) label = "Silver " + label;
            if (p.CoatLayers.Contains("champagne")) label = "Champagne " + label;
            if (p.CoatLayers.Contains("leopard_complex")) label += " with leopard-complex pattern";
            if (p.CoatLayers.Contains("grey_age")) label += " (progressive grey)";
            return label;
        }
    }
}
