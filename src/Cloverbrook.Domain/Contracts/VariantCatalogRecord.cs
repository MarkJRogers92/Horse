using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public enum EvidenceStatus { EstablishedBiological, AssociatedBiological, UncertainBiological, FictionalGameTrait }
    public enum CatalogReleaseStatus { Proposed, Supported, Deferred }

    public sealed class SourceReference
    {
        public SourceReference(string url, string claim)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) throw new ArgumentException("A public source URL is required.", nameof(url));
            Url = url; Claim = Check.Text(claim, nameof(claim));
        }
        public string Url { get; }
        public string Claim { get; }
    }

    public sealed class VariantCatalogRecord
    {
        public VariantCatalogRecord(string variantId, string catalogVersion, string displayName, EvidenceStatus evidenceStatus,
            string regionId, IEnumerable<string> supportedAlleleIds, IEnumerable<string> supportedHaplotypeIds,
            string inheritanceRuleId, string expressionRuleId, IEnumerable<SourceReference> sourceReferences,
            string? geneSymbol = null, string? chromosomeId = null, string? compatibilityRuleId = null,
            string? reviewedOn = null, CatalogReleaseStatus releaseStatus = CatalogReleaseStatus.Proposed, string? gameAbstractionNotice = null)
        {
            VariantId = Check.Text(variantId, nameof(variantId)); CatalogVersion = Check.Text(catalogVersion, nameof(catalogVersion));
            DisplayName = Check.Text(displayName, nameof(displayName)); RegionId = Check.Text(regionId, nameof(regionId));
            if (!Enum.IsDefined(typeof(EvidenceStatus), evidenceStatus) || !Enum.IsDefined(typeof(CatalogReleaseStatus), releaseStatus)) throw new ArgumentOutOfRangeException(nameof(evidenceStatus));
            EvidenceStatus = evidenceStatus; ReleaseStatus = releaseStatus;
            SupportedAlleleIds = Snapshot.List(supportedAlleleIds); SupportedHaplotypeIds = Snapshot.List(supportedHaplotypeIds);
            if (SupportedAlleleIds.Count == 0) throw new ArgumentException("An allele catalog cannot be empty.");
            foreach (var id in SupportedAlleleIds.Concat(SupportedHaplotypeIds)) Check.Text(id, nameof(supportedAlleleIds));
            if (SupportedAlleleIds.Distinct(StringComparer.Ordinal).Count() != SupportedAlleleIds.Count) throw new ArgumentException("Duplicate allele ID.");
            InheritanceRuleId = Check.Text(inheritanceRuleId, nameof(inheritanceRuleId)); ExpressionRuleId = Check.Text(expressionRuleId, nameof(expressionRuleId));
            SourceReferences = Snapshot.List(sourceReferences); GeneSymbol = geneSymbol; ChromosomeId = chromosomeId;
            CompatibilityRuleId = compatibilityRuleId; ReviewedOn = reviewedOn; GameAbstractionNotice = gameAbstractionNotice;
            if (reviewedOn != null && !DateTime.TryParseExact(reviewedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new ArgumentException("Review date must be ISO yyyy-MM-dd.");
            if (evidenceStatus == EvidenceStatus.FictionalGameTrait)
            {
                if (geneSymbol != null || chromosomeId != null || string.IsNullOrWhiteSpace(gameAbstractionNotice)) throw new ArgumentException("Fictional markers must have an abstraction notice and no real gene/chromosome claim.");
            }
            else if (SourceReferences.Count == 0) throw new ArgumentException("Biological records require provenance.");
        }
        public string VariantId { get; }
        public string CatalogVersion { get; }
        public string DisplayName { get; }
        public EvidenceStatus EvidenceStatus { get; }
        public string RegionId { get; }
        public IReadOnlyList<string> SupportedAlleleIds { get; }
        public IReadOnlyList<string> SupportedHaplotypeIds { get; }
        public string InheritanceRuleId { get; }
        public string ExpressionRuleId { get; }
        public IReadOnlyList<SourceReference> SourceReferences { get; }
        public string? GeneSymbol { get; }
        public string? ChromosomeId { get; }
        public string? CompatibilityRuleId { get; }
        public string? ReviewedOn { get; }
        public CatalogReleaseStatus ReleaseStatus { get; }
        public string? GameAbstractionNotice { get; }
    }
}
