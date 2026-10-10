using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class Phenotype
    {
        public Phenotype(HorseId horseId, string genomeHash, string resolverVersion, Seed128 appearanceSeed, VisualAge visualAge,
            string baseCoatClass, string coatClassId, string diagnosticCoatClass, string basePigmentRecipe, string dilutionExpression,
            IEnumerable<string> coatLayers, string skinExpression, string eyeExpression, string hairExpression,
            bool lpPatternPresent, int patn1Coverage, int grayAmount, int primitiveMarkingStrength,
            QuantizedVector morphology, IEnumerable<string> biologicalAssociationNotes, string fixedFactorHash)
        {
            HorseId = Check.NotNull(horseId, nameof(horseId)); GenomeHash = Check.Text(genomeHash, nameof(genomeHash));
            ResolverVersion = Check.Text(resolverVersion, nameof(resolverVersion)); VisualAge = visualAge;
            if (!Enum.IsDefined(typeof(VisualAge), visualAge)) throw new ArgumentOutOfRangeException(nameof(visualAge));
            BaseCoatClass = Check.Text(baseCoatClass, nameof(baseCoatClass)); CoatClassId = Check.Text(coatClassId, nameof(coatClassId));
            DiagnosticCoatClass = Check.Text(diagnosticCoatClass, nameof(diagnosticCoatClass)); BasePigmentRecipe = Check.Text(basePigmentRecipe, nameof(basePigmentRecipe));
            DilutionExpression = Check.Text(dilutionExpression, nameof(dilutionExpression)); CoatLayers = Snapshot.List(coatLayers);
            SkinExpression = Check.Text(skinExpression, nameof(skinExpression)); EyeExpression = Check.Text(eyeExpression, nameof(eyeExpression)); HairExpression = Check.Text(hairExpression, nameof(hairExpression));
            foreach (var v in new[] { patn1Coverage, grayAmount, primitiveMarkingStrength }) if (v < 0 || v > 10000) throw new ArgumentOutOfRangeException(nameof(grayAmount));
            if (!lpPatternPresent && patn1Coverage != 0) throw new ArgumentException("PATN1 coverage requires LP.");
            LpPatternPresent = lpPatternPresent; Patn1Coverage = patn1Coverage; GrayAmount = grayAmount; PrimitiveMarkingStrength = primitiveMarkingStrength;
            Morphology = Check.NotNull(morphology, nameof(morphology)); BiologicalAssociationNotes = Snapshot.List(biologicalAssociationNotes);
            MarkingRealizationKey = Canonical.Hash("markings.v1", resolverVersion, appearanceSeed.ToString());
            InputHash = Canonical.Hash("phenotype.inputs.v1", horseId.ToString(), genomeHash, resolverVersion, appearanceSeed.ToString(), visualAge.ToString(),
                Check.Text(fixedFactorHash, nameof(fixedFactorHash)), morphology.Hash);
        }
        public HorseId HorseId { get; }
        public string GenomeHash { get; }
        public string ResolverVersion { get; }
        public VisualAge VisualAge { get; }
        public string BaseCoatClass { get; }
        public string CoatClassId { get; }
        // Genetics diagnostics may expose carrier state. UI gets the visual descriptor,
        // not this authoritative record; knowledge-limited forecasts are a later task.
        public string DiagnosticCoatClass { get; }
        public string BasePigmentRecipe { get; }
        public string DilutionExpression { get; }
        public IReadOnlyList<string> CoatLayers { get; }
        public string SkinExpression { get; }
        public string EyeExpression { get; }
        public string HairExpression { get; }
        public bool LpPatternPresent { get; }
        public int Patn1Coverage { get; }
        public int GrayAmount { get; }
        public int PrimitiveMarkingStrength { get; }
        public QuantizedVector Morphology { get; }
        public IReadOnlyList<string> BiologicalAssociationNotes { get; }
        public string MarkingRealizationKey { get; }
        public string InputHash { get; }
    }

    public sealed class HorseVisualDescriptor
    {
        public HorseVisualDescriptor(Phenotype phenotype, IEnumerable<string> requiredCoatClasses, string visualCapabilityVersion,
            string visualAssetCatalogVersion, string meshFamilyId, string anatomicalMaskSetId, string? cosmeticLayer = null)
        {
            Check.NotNull(phenotype, nameof(phenotype)); HorseId = phenotype.HorseId; PhenotypeInputHash = phenotype.InputHash;
            ResolverVersion = phenotype.ResolverVersion; CoatClassId = phenotype.CoatClassId; RequiredCoatClasses = Snapshot.List(requiredCoatClasses);
            if (RequiredCoatClasses.Count == 0) throw new ArgumentException("A visual descriptor must state its required coat support.");
            VisualCapabilityVersion = Check.Text(visualCapabilityVersion, nameof(visualCapabilityVersion)); VisualAssetCatalogVersion = Check.Text(visualAssetCatalogVersion, nameof(visualAssetCatalogVersion));
            MeshFamilyId = Check.Text(meshFamilyId, nameof(meshFamilyId)); AnatomicalMaskSetId = Check.Text(anatomicalMaskSetId, nameof(anatomicalMaskSetId));
            MorphWeights = phenotype.Morphology.Values; CoatLayers = phenotype.CoatLayers;
            CoatRecipe = phenotype.BasePigmentRecipe + ":" + phenotype.DilutionExpression;
            SkinRecipe = phenotype.SkinExpression; EyeRecipe = phenotype.EyeExpression; HairRecipe = phenotype.HairExpression;
            GrayAmount = phenotype.GrayAmount; PrimitiveMarkingStrength = phenotype.PrimitiveMarkingStrength;
            LpPatternPresent = phenotype.LpPatternPresent; Patn1Coverage = phenotype.Patn1Coverage;
            MarkingRealizationKey = phenotype.MarkingRealizationKey; CosmeticLayer = cosmeticLayer;
            CacheKey = Canonical.Hash(new[] { "visual.descriptor.v1", PhenotypeInputHash, VisualCapabilityVersion, VisualAssetCatalogVersion, MeshFamilyId,
                AnatomicalMaskSetId, CoatRecipe, SkinRecipe, EyeRecipe, HairRecipe, GrayAmount.ToString(CultureInfo.InvariantCulture),
                PrimitiveMarkingStrength.ToString(CultureInfo.InvariantCulture), Patn1Coverage.ToString(CultureInfo.InvariantCulture),
                LpPatternPresent.ToString(), MarkingRealizationKey, cosmeticLayer ?? "" }.Concat(RequiredCoatClasses).Concat(CoatLayers));
        }
        public HorseId HorseId { get; }
        public string PhenotypeInputHash { get; }
        public string ResolverVersion { get; }
        public string CoatClassId { get; }
        public IReadOnlyList<string> RequiredCoatClasses { get; }
        public string VisualCapabilityVersion { get; }
        public string VisualAssetCatalogVersion { get; }
        public string MeshFamilyId { get; }
        public string AnatomicalMaskSetId { get; }
        public IReadOnlyDictionary<string, int> MorphWeights { get; }
        public IReadOnlyList<string> CoatLayers { get; }
        public string CoatRecipe { get; }
        public string SkinRecipe { get; }
        public string EyeRecipe { get; }
        public string HairRecipe { get; }
        public int GrayAmount { get; }
        public int PrimitiveMarkingStrength { get; }
        public bool LpPatternPresent { get; }
        public int Patn1Coverage { get; }
        public string MarkingRealizationKey { get; }
        public string? CosmeticLayer { get; }
        public string CacheKey { get; }
    }
}
