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
                var kit = g.GetAlleles("pattern.eca3"); bool tobiano = kit.Contains("TO"), roan = kit.Contains("Rn");
                int sabino = kit.Count("SB1"), splash = g.GetAlleles("pattern.mitf").Count("SW1");
                // D-026 and D-037: grey, leopard complex, tobiano, roan, sabino-1, splash, silver, champagne and pearl are
                // versioned rules that compose over every phase-1 base, dilution and each other. Colour-changing layers
                // (silver, champagne) add to the base recipe rather than multiplying recipes; unimplemented interactions are
                // never approximated by dropping a layer.
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
                    var dunClass = baseCoat == "chestnut" ? "red_dun" : baseCoat == "bay" ? "bay_dun" : "grullo";
                    if (pearlExpression) required.Add(dunClass); // the pearl class stays the coat class; the dun layers need their released class too
                    else
                    {
                        coatClass = cream == 0 ? dunClass : cream == 1 ? "dunalino_family" : "double_cream_dun";
                        diagnostic = coatClass; required.Add(coatClass);
                    }
                    dilution += "+dun";
                }
                else if (nd1) { layers.Add("primitive_markings"); required.Add("non_dun1_marked"); }
                if (g.GetAlleles("pmel").Contains("Z")) notes.Add("silver_association_codex; no medical events simulated");
                if (champagne)
                {
                    // Champagne alone has its own three recipes (gold, amber, classic). Over cream, pearl or dun it is a layer on
                    // that dilution's recipe, so the recipe table does not multiply. Skin and eyes keep any stronger dilution's look.
                    layers.Add("champagne"); required.Add("champagne_overlay");
                    if (cream == 0 && !pearlExpression && !dun) dilution = "champagne";
                    if (skin == "pigmented") skin = "champagne_mottled";
                    if (eyes == "brown") eyes = "champagne_amber";
                    if (hair.StartsWith("base_", StringComparison.Ordinal)) hair = "champagne_" + (baseCoat == "chestnut" ? "gold" : baseCoat == "bay" ? "amber" : "classic");
                    diagnostic = "champagneOn" + Title(baseCoat);
                }
                if (silver)
                {
                    layers.Add("silver"); required.Add("silver_overlay"); diagnostic = "silverOn" + Title(baseCoat);
                    if (hair.StartsWith("base_", StringComparison.Ordinal)) hair = "silver_lightened_mane_tail";
                }
                // These are explicit artistic age/coverage parameters, not inferred clinical measurements.
                var grayAmount = grey ? GrayAmount(horse.LifeState) : 0;
                if (grey) { layers.Add("grey_age"); required.Add("grey_overlay"); notes.Add("grey.v2 artistic age curve; simplified G state"); }
                var patnCoverage = lp && g.GetAlleles("patn1").Contains("PATN1") ? 8000 : 0;
                if (lp) { layers.Add("leopard_complex"); required.Add("leopard_complex_overlay"); notes.Add("LP/PATN1 coverage is artistic; LP dosage-specific art remains unreleased"); }
                if (roan) { layers.Add("roan"); required.Add("roan_overlay"); notes.Add("roan: white hairs through the body; solid head and lower legs"); }
                if (tobiano) { layers.Add("white_tobiano"); required.Add("tobiano_overlay"); notes.Add("tobiano white extent is an artistic per-horse realisation"); }
                if (sabino > 0)
                {
                    layers.Add("white_sabino1"); required.Add("sabino1_overlay"); notes.Add("sabino-1: irregular white from below with roaned edges; extent is an artistic per-horse realisation");
                    if (sabino == 2) { layers.Add("white_sabino1_double"); required.Add("sabino1_double"); notes.Add("SB1/SB1: mostly white (sabino white)"); }
                }
                if (splash > 0)
                {
                    layers.Add("white_splash"); required.Add("splash_overlay"); notes.Add("splash white 1: dipped in white from below with a broad blaze; extent is an artistic per-horse realisation");
                    if (splash == 2) { layers.Add("white_splash_double"); required.Add("splash_double"); notes.Add("SW1/SW1: extensive white, blue eyes"); if (eyes == "brown") eyes = "blue"; }
                }
                layers.Add("individual_white_markings"); layers.Add("shading_modifiers");
                var phenotype = new Phenotype(horse.HorseId, g.GenomeHash, Version, horse.AppearanceSeed, horse.LifeState.VisualAge, baseCoat, coatClass, diagnostic,
                    pigment, dilution, layers, skin, eyes, hair, lp, patnCoverage, grayAmount, primitive, traits.Morphology, notes,
                    Canonical.Hash(GameTraitResolver.Version, horse.DevelopmentalFactors.Hash));
                return PhenotypeResolution.Supported(phenotype, traits, required.OrderBy(c => c, StringComparer.Ordinal));
            }
            catch (GeneticsSupportException exception) { return PhenotypeResolution.Unsupported(exception.Message); }
        }
        private static string Title(string text) => char.ToUpperInvariant(text[0]) + text.Substring(1);
        /// <summary>grey.v2 (artistic, labelled): foals keep their birth colour; adults lighten with age in game days
        /// (steel grey, dapple grey around day 40, light grey, flea-bitten white after about a game year); seniors are white.</summary>
        public static int GrayAmount(LifeState life)
        {
            Check.NotNull(life, nameof(life));
            if (life.VisualAge == VisualAge.Foal) return 0;
            var curve = (int)Math.Min(9500L, 3000L + life.DaysSinceConception * 55L);
            return life.VisualAge == VisualAge.Senior ? Math.Max(9000, curve) : Math.Min(curve, 8999);
        }
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
        private static string Low(string text) => char.ToLowerInvariant(text[0]) + text.Substring(1);
        public static string VisibleLabel(Phenotype p)
        {
            Check.NotNull(p, nameof(p));
            var layers = p.CoatLayers; var champagne = layers.Contains("champagne");
            var label = p.CoatClassId == "dunalino_family" ? p.BaseCoatClass == "chestnut" ? "Dunalino" : p.BaseCoatClass == "bay" ? "Dunskin" : "Smoky grullo"
                : p.CoatClassId == "double_cream_dun" ? p.BaseCoatClass == "chestnut" ? "Cremello dun" : p.BaseCoatClass == "bay" ? "Perlino dun" : "Smoky cream dun"
                // Pearl and cream-pearl name the colour underneath ("pearl bay"); cream-pearl is the pseudo-double-dilute.
                : p.CoatClassId == "pearl" ? "Pearl " + p.BaseCoatClass + (layers.Contains("dun_dilution") ? " dun" : "")
                : p.CoatClassId == "cream_pearl" ? "Cream pearl " + p.BaseCoatClass + (layers.Contains("dun_dilution") ? " dun" : "")
                : champagne && p.DilutionExpression == "champagne" ? (p.BaseCoatClass == "chestnut" ? "Gold" : p.BaseCoatClass == "bay" ? "Amber" : "Classic") + " champagne"
                : Title(p.CoatClassId.Replace('_', ' '));
            // Champagne over another dilution is named for that dilution ("champagne buckskin").
            if (champagne && p.DilutionExpression != "champagne") label = "Champagne " + Low(label);
            // Roan and grey name the colour underneath, the way horse people do ("bay roan", "blue roan").
            if (layers.Contains("roan")) label = label == "Black" ? "Blue roan" : label == "Chestnut" ? "Red roan" : label + " roan";
            if (p.PrimitiveMarkingStrength > 0 && !layers.Contains("dun_dilution")) label += " with primitive markings";
            if (layers.Contains("silver")) label = "Silver " + Low(label);
            if (layers.Contains("white_tobiano")) label += " tobiano";
            if (layers.Contains("white_sabino1")) label += layers.Contains("white_sabino1_double") ? " sabino white" : " sabino";
            if (layers.Contains("white_splash")) label += layers.Contains("white_splash_double") ? " splash white" : " splash";
            if (layers.Contains("leopard_complex")) label += p.Patn1Coverage > 0 ? " leopard appaloosa" : " blanket appaloosa";
            if (layers.Contains("grey_age")) label += " (progressive grey)";
            return label;
        }
    }
}
