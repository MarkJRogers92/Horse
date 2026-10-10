using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    // Public projections deliberately have no genome, diagnostic phenotype,
    // hidden inherited potential, or historical authoritative parent snapshot.
    public sealed class PublicHorseSummary
    {
        internal PublicHorseSummary(HorseRecord h)
        { HorseId = h.HorseId; Name = h.Name; OwnerId = h.OwnerId; Revision = h.Revision; Life = h.LifeState; Role = h.ReproductionState.Role; Home = h.HomeAssignment; }
        public HorseId HorseId { get; }
        public string Name { get; }
        public OwnerId OwnerId { get; }
        public ulong Revision { get; }
        public LifeState Life { get; }
        public ReproductiveRole Role { get; }
        public HomeKind Home { get; }
    }
    public sealed class PublicPregnancy
    {
        internal PublicPregnancy(BreedingCommit c) { EventId = c.BreedingCommitId; OffspringHorseId = c.OffspringHorseId; ConceptionDay = c.ConceptionDay; PlannedBirthDay = c.PlannedBirthDay; }
        public BreedingCommitId EventId { get; }
        public HorseId OffspringHorseId { get; }
        public int ConceptionDay { get; }
        public int PlannedBirthDay { get; }
    }
    public sealed class BreedingReadiness
    {
        internal BreedingReadiness(IEnumerable<EligibilityIssue> errors, IEnumerable<EligibilityIssue> warnings, bool replay)
        { Rejections = Snapshot.List(errors); Warnings = Snapshot.List(warnings); IsIdenticalRetry = replay; }
        public IReadOnlyList<EligibilityIssue> Rejections { get; }
        public IReadOnlyList<EligibilityIssue> Warnings { get; }
        public bool IsEligible => Rejections.Count == 0;
        public bool IsIdenticalRetry { get; }
    }
    public sealed class StableBreedingQueries
    {
        private readonly StableSnapshot state;
        private readonly ConceptionService conception;
        private readonly BreedingForecastService forecasts;
        private readonly IReadOnlyDictionary<HorseId, HorseRecord> horses;
        private readonly string artVersion;
        private readonly string maskId;
        public StableBreedingQueries(StableSnapshot state, ConceptionService conception, string artVersion = "unreleased", string maskId = "unreleased")
        {
            this.state = Check.NotNull(state, nameof(state)); this.conception = Check.NotNull(conception, nameof(conception));
            this.artVersion = Check.Text(artVersion, nameof(artVersion)); this.maskId = Check.Text(maskId, nameof(maskId));
            horses = state.Horses.ToDictionary(h => h.HorseId); forecasts = new BreedingForecastService(conception.Matrix, artVersion, maskId);
        }
        private HorseRecord Horse(HorseId id) => horses.TryGetValue(Check.NotNull(id, nameof(id)), out var h) ? h : throw new KeyNotFoundException("Horse is absent from this stable snapshot.");
        public PublicHorseSummary GetHorse(HorseId id) => new PublicHorseSummary(Horse(id));
        public PlayerKnowledgeRecord GetKnownGenetics(HorseId id) => Horse(id).PlayerKnowledge;
        public PedigreeGraph GetKnownPedigree() => new PedigreeGraph(state.Horses.Select(h => new PedigreeNode(h.HorseId,
            new Parentage(h.Parentage.ParentAId != null && h.PlayerKnowledge.KnownAncestors.Contains(h.Parentage.ParentAId) ? h.Parentage.ParentAId : null,
                h.Parentage.ParentBId != null && h.PlayerKnowledge.KnownAncestors.Contains(h.Parentage.ParentBId) ? h.Parentage.ParentBId : null))));
        public BreedingForecast PreviewBreeding(HorseId first, HorseId second, PublicCoatScope scope, ForecastOptions? options = null)
        {
            // Availability checks inspect version metadata only, never hidden
            // allele calls. Unsupported historical metadata may be preserved by
            // storage, but cannot be interpreted using today's forecast rules.
            var a = Horse(first); var b = Horse(second);
            if (state.Header.ActiveVariantCatalogVersion != CoreGeneticsCatalog.CatalogVersion || state.Header.ActiveInheritanceModelVersion != CoreGeneticsCatalog.InheritanceVersion ||
                new[] { a, b }.Any(h => h.Genome.VariantCatalogVersion != CoreGeneticsCatalog.CatalogVersion || h.Genome.SyntheticModelVersion != CoreGeneticsCatalog.SyntheticVersion || h.ResolverVersion != CoreGeneticsCatalog.ResolverVersion))
                throw new SaveCompatibilityException("Install the recorded genetics/phenotype implementation before forecasting these actual horses.");
            return forecasts.ForKnownParents(new PublicCoatKnowledge(first, a.PlayerKnowledge), new PublicCoatKnowledge(second, b.PlayerKnowledge), scope, GetKnownPedigree(), options);
        }
        public BreedingForecast CompareHypotheticalPairing(GenomeSnapshot assumedFirst, GenomeSnapshot assumedSecond, ForecastOptions? options = null) =>
            forecasts.Hypothetical(assumedFirst, assumedSecond, options);
        public PublicPregnancy? GetPregnancy(HorseId mare)
        {
            var id = Horse(mare).ReproductionState.PregnancyId;
            return id == null ? null : new PublicPregnancy(state.Conceptions.Single(c => c.Commit.BreedingCommitId.Equals(id)).Commit);
        }
        public PublicHorseSummary GetOffspring(BreedingCommitId eventId) => GetHorse(state.Conceptions.Single(c => c.Commit.BreedingCommitId.Equals(eventId)).Commit.OffspringHorseId);
        public PresentationResolution ResolveVisualDescriptor(HorseId id) => new PhenotypeResolver().ResolveForPresentation(Horse(id), conception.Matrix, artVersion, maskId);
        // Advisory projection, not a reservation or commitment. The durable
        // transaction repeats these checks against the latest locked state.
        public BreedingReadiness CheckBreedingEligibility(ConceptionRequest request)
        {
            Check.NotNull(request, nameof(request)); var errors = new List<EligibilityIssue>(); var warnings = new List<EligibilityIssue>();
            void Reject(bool reject, string code, string message) { if (reject) errors.Add(new EligibilityIssue(code, message)); }
            var existing = state.Conceptions.SingleOrDefault(c => c.Commit.BreedingCommitId.Equals(request.EventId));
            if (existing != null)
            {
                Reject(existing.Request.RequestHash != request.RequestHash, "idempotency_conflict", "This command ID belongs to different inputs.");
                return new BreedingReadiness(errors, warnings, errors.Count == 0);
            }
            Reject(!request.Requester.Equals(state.PlayerOwnerId), "wallet_owner", "The request is not authorized for this wallet.");
            Reject(request.ExpectedSaveRevision != state.Header.SaveRevision, "stale_save", "The stable changed; refresh the pairing.");
            Reject(request.ExpectedCapacityRevision != state.CapacityRevision, "stale_capacity", "Capacity changed; refresh the confirmed hold.");
            Reject(request.TermsVersion != conception.Terms.Version, "stale_terms", "The breeding quote changed.");
            Reject(state.Coins < conception.Terms.CostCoins, "funds", "Insufficient coins.");
            Reject(state.Header.ActiveVariantCatalogVersion != CoreGeneticsCatalog.CatalogVersion || state.Header.ActiveInheritanceModelVersion != CoreGeneticsCatalog.InheritanceVersion ||
                state.Header.DefaultOffspringResolverVersion != CoreGeneticsCatalog.ResolverVersion || state.Header.RngStreams.Breeding.AlgorithmVersion != Seeds.AlgorithmVersion ||
                state.Header.RngStreams.Breeding.EventCounter != state.Header.LastCommittedBreedingEventIndex || state.Header.RngStreams.Breeding.EventCounter == ulong.MaxValue,
                "unsupported_versions", "This snapshot requires a compatible versioned breeding implementation.");
            if (!horses.TryGetValue(request.ParentAId, out var a) || !horses.TryGetValue(request.ParentBId, out var b))
            { Reject(true, "parents", "A selected parent is absent."); return new BreedingReadiness(errors, warnings, false); }
            Reject(!request.Requester.Equals(a.OwnerId) || !request.Requester.Equals(b.OwnerId), "ownership", "The Phase-1 operation requires owned parents.");
            var hold = state.PendingCapacityPlans.SingleOrDefault(p => p.ReservationId.Equals(request.ReservationId));
            var pedigree = new PedigreeGraph(state.Horses.Select(h => new PedigreeNode(h.HorseId, h.Parentage)));
            var eligibility = new BreedingEligibility().Evaluate(a, b, pedigree, conception.Policy, state.Header.CalendarDay, request.EventId, hold,
                request.ExpectedParentARevision, request.ExpectedParentBRevision, state.CapacityRevision);
            errors.AddRange(eligibility.Rejections); warnings.AddRange(eligibility.Warnings);
            Reject(!LiveCatalogCoatGuard.Check(state.Horses, conception.Matrix).IsSupported, "unreleased_catalog", "The population has no complete live visual-support approval.");
            return new BreedingReadiness(errors, warnings, false);
        }
    }
}
