using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class ConceptionRejectedException : Exception
    {
        public ConceptionRejectedException(string code, string message) : base(message) { Code = Check.Text(code, nameof(code)); }
        public string Code { get; }
    }
    public sealed class ConceptionTerms
    {
        // Trusted versioned game configuration, not a price supplied by the UI.
        public ConceptionTerms(string version, long costCoins)
        { Version = Check.Text(version, nameof(version)); if (costCoins < 0) throw new ArgumentOutOfRangeException(nameof(costCoins)); CostCoins = costCoins; }
        public string Version { get; }
        public long CostCoins { get; }
    }
    public sealed class ConceptionRequest
    {
        public ConceptionRequest(BreedingCommitId eventId, OwnerId requester, HorseId parentAId, HorseId parentBId,
            ulong expectedSaveRevision, ulong expectedParentARevision, ulong expectedParentBRevision,
            ulong expectedCapacityRevision, CapacityReservationId reservationId, string termsVersion)
        {
            EventId = Check.NotNull(eventId, nameof(eventId)); Requester = Check.NotNull(requester, nameof(requester));
            ParentAId = Check.NotNull(parentAId, nameof(parentAId)); ParentBId = Check.NotNull(parentBId, nameof(parentBId));
            if (parentAId.Equals(parentBId)) throw new ArgumentException("Parents must be distinct.");
            ExpectedSaveRevision = expectedSaveRevision; ExpectedParentARevision = expectedParentARevision; ExpectedParentBRevision = expectedParentBRevision;
            ExpectedCapacityRevision = expectedCapacityRevision; ReservationId = Check.NotNull(reservationId, nameof(reservationId)); TermsVersion = Check.Text(termsVersion, nameof(termsVersion));
            RequestHash = Canonical.Hash("conception.request.v1", eventId.ToString(), requester.ToString(), parentAId.ToString(), parentBId.ToString(),
                expectedSaveRevision.ToString(CultureInfo.InvariantCulture), expectedParentARevision.ToString(CultureInfo.InvariantCulture), expectedParentBRevision.ToString(CultureInfo.InvariantCulture),
                expectedCapacityRevision.ToString(CultureInfo.InvariantCulture), reservationId.ToString(), termsVersion);
        }
        public BreedingCommitId EventId { get; }
        public OwnerId Requester { get; }
        public HorseId ParentAId { get; }
        public HorseId ParentBId { get; }
        public ulong ExpectedSaveRevision { get; }
        public ulong ExpectedParentARevision { get; }
        public ulong ExpectedParentBRevision { get; }
        public ulong ExpectedCapacityRevision { get; }
        public CapacityReservationId ReservationId { get; }
        public string TermsVersion { get; }
        public string RequestHash { get; }
    }
    public sealed class PreparedConception
    {
        // Retain historical authoritative snapshots to reconstruct/validate a
        // commit without pretending the parents' CURRENT revisions are historic.
        internal PreparedConception(ConceptionRequest request, ConceptionTerms terms, BreedingCapacityPlan capacity,
            HorseRecord parentA, HorseRecord parentB, HorseRecord unborn, BreedingCommit commit, RngStreamState beforeStream)
        {
            Request = request; Terms = terms; Capacity = capacity; ParentAAtCommit = parentA; ParentBAtCommit = parentB; UnbornAtCommit = unborn; Commit = commit;
            BeforeBreedingStream = beforeStream; AfterBreedingStream = beforeStream.Advance();
            if (beforeStream.Purpose != "breeding" || beforeStream.AlgorithmVersion != Seeds.AlgorithmVersion ||
                commit.BreedingEventIndex != AfterBreedingStream.EventCounter || commit.BreedingSeed != Seeds.Derive(beforeStream.Seed, "breeding/event", commit.BreedingEventIndex))
                throw new ArgumentException("Conception event does not match its authoritative stream position.");
            if (!request.EventId.Equals(commit.BreedingCommitId) || !request.ParentAId.Equals(parentA.HorseId) || !request.ParentBId.Equals(parentB.HorseId) ||
                request.ExpectedParentARevision != parentA.Revision || request.ExpectedParentBRevision != parentB.Revision ||
                request.TermsVersion != terms.Version || !request.Requester.Equals(unborn.OwnerId) || !request.Requester.Equals(parentA.OwnerId) || !request.Requester.Equals(parentB.OwnerId) ||
                !request.ReservationId.Equals(capacity.ReservationId) || !request.EventId.Equals(capacity.BreedingId) || request.ExpectedCapacityRevision != capacity.CapacityRevision)
                throw new ArgumentException("Conception request/receipt mismatch.");
            var meiosis = new MeiosisService(CoreGeneticsCatalog.Model);
            meiosis.ValidateGameteTrace(parentA.HorseId, parentA.Genome, commit.GameteA); meiosis.ValidateGameteTrace(parentB.HorseId, parentB.Genome, commit.GameteB);
            ReceiptHash = Canonical.Hash("conception.receipt.v1", request.RequestHash, terms.Version, terms.CostCoins.ToString(CultureInfo.InvariantCulture), commit.CommitHash);
        }
        public ConceptionRequest Request { get; }
        public ConceptionTerms Terms { get; }
        public BreedingCapacityPlan Capacity { get; }
        public HorseRecord ParentAAtCommit { get; }
        public HorseRecord ParentBAtCommit { get; }
        public HorseRecord UnbornAtCommit { get; }
        public BreedingCommit Commit { get; }
        public RngStreamState BeforeBreedingStream { get; }
        public RngStreamState AfterBreedingStream { get; }
        public string ReceiptHash { get; }
        public bool IsDurablyCommitted => false; // durability is a repository result, not an algorithm promise
        public HorseRecord PregnantMare
        {
            get
            {
                var mare = ParentAAtCommit.ReproductionState.Role == ReproductiveRole.Mare ? ParentAAtCommit : ParentBAtCommit;
                return CopyReproduction(mare, new ReproductionState(ReproductiveRole.Mare, Commit.BreedingCommitId, mare.ReproductionState.RecoveryUntilDay));
            }
        }
        internal static HorseRecord CopyReproduction(HorseRecord h, ReproductionState state) => new HorseRecord(h.HorseId, h.Name, h.Parentage, h.Genome,
            h.ResolverVersion, h.AppearanceSeed, h.OwnerId, h.LifeState, state, h.DevelopmentalFactors, h.InheritedGamePotential, h.TrainingState,
            h.PlayerKnowledge, h.CosmeticState, h.HomeAssignment, h.OriginRecord, h.OriginCommitId, h.BirthDay, checked(h.Revision + 1));
    }
    public sealed class ConceptionService
    {
        private readonly bool textOnlySandboxSupport;
        public ConceptionService(BreedingPolicy policy, ConceptionTerms terms, CoatSupportMatrix matrix)
            : this(policy, terms, matrix, textOnlySandboxSupport: false) { }
        internal ConceptionService(BreedingPolicy policy, ConceptionTerms terms, CoatSupportMatrix matrix, bool textOnlySandboxSupport)
        {
            Policy = Check.NotNull(policy, nameof(policy)); Terms = Check.NotNull(terms, nameof(terms)); Matrix = Check.NotNull(matrix, nameof(matrix));
            this.textOnlySandboxSupport = textOnlySandboxSupport;
            if (textOnlySandboxSupport && (policy.Version != StableBreedingPlanner.PolicyVersion || terms.Version != StableBreedingPlanner.TermsVersion
                || terms.CostCoins != StableBreedingPlanner.CostCoins || policy.PregnancyDays != 4 || policy.FoalDays != 3
                || policy.YoungsterDays != 3 || policy.BreedableDay != 12 || !policy.AllowUnknownAncestry
                || policy.BlockKinshipAtOrAbove != new Rational(1, 8)))
                throw new ArgumentException("The text-only sandbox policy must use the pinned prototype terms.");
        }
        public BreedingPolicy Policy { get; }
        public ConceptionTerms Terms { get; }
        public CoatSupportMatrix Matrix { get; }
        private static void Reject(bool condition, string code, string message) { if (condition) throw new ConceptionRejectedException(code, message); }
        public PreparedConception Prepare(SaveHeader header, IEnumerable<HorseRecord> authoritativeHorses, long availableCoins, ulong actualCapacityRevision,
            ConceptionRequest request, BreedingCapacityPlan confirmedCapacity)
        {
            Check.NotNull(header, nameof(header)); Check.NotNull(request, nameof(request)); Check.NotNull(confirmedCapacity, nameof(confirmedCapacity));
            var horses = Snapshot.List(authoritativeHorses); var map = horses.ToDictionary(h => h.HorseId);
            Reject(header.SaveRevision != request.ExpectedSaveRevision, "stale_save", "The stable changed; refresh the pairing.");
            Reject(actualCapacityRevision != request.ExpectedCapacityRevision || confirmedCapacity.CapacityRevision != actualCapacityRevision, "stale_capacity", "Capacity changed; refresh the confirmed hold.");
            Reject(!confirmedCapacity.ReservationId.Equals(request.ReservationId), "reservation", "Use this command's confirmed reservation.");
            Reject(request.TermsVersion != Terms.Version, "stale_terms", "The breeding quote changed; review the current terms.");
            if (textOnlySandboxSupport)
                Reject(StableBreedingPlanner.PolicyGateProblems(header).Length != 0,
                    "sandbox_policy", string.Join("; ", StableBreedingPlanner.PolicyGateProblems(header)));
            Reject(availableCoins < Terms.CostCoins, "funds", "Insufficient coins; nothing was charged.");
            Reject(header.ActiveVariantCatalogVersion != CoreGeneticsCatalog.CatalogVersion || header.ActiveInheritanceModelVersion != CoreGeneticsCatalog.InheritanceVersion ||
                header.DefaultOffspringResolverVersion != CoreGeneticsCatalog.ResolverVersion, "unsupported_versions", "This save needs its versioned genetics implementation; no implicit migration.");
            var stream = header.RngStreams.Breeding;
            Reject(stream.AlgorithmVersion != Seeds.AlgorithmVersion || stream.EventCounter != header.LastCommittedBreedingEventIndex || stream.EventCounter == ulong.MaxValue,
                "breeding_stream", "Invalid or exhausted breeding event stream.");
            Reject(!map.ContainsKey(request.ParentAId) || !map.ContainsKey(request.ParentBId), "parents", "A selected parent is not in the authoritative stable.");
            var a = map[request.ParentAId]; var b = map[request.ParentBId];
            Reject(!request.Requester.Equals(a.OwnerId) || !request.Requester.Equals(b.OwnerId), "ownership", "This Phase-1 command supports owned parents only; external stud bookings need the marketplace authorization adapter.");
            var pedigree = new PedigreeGraph(horses.Select(h => new PedigreeNode(h.HorseId, h.Parentage)));
            var eligibility = new BreedingEligibility().Evaluate(a, b, pedigree, Policy, header.CalendarDay, request.EventId, confirmedCapacity,
                request.ExpectedParentARevision, request.ExpectedParentBRevision, actualCapacityRevision);
            Reject(!eligibility.IsEligible, "eligibility", string.Join("; ", eligibility.Rejections.Select(i => i.Message)));
            var closure = textOnlySandboxSupport ? StableBreedingPlanner.CheckGeneticClosure(horses) : LiveCatalogCoatGuard.Check(horses, Matrix);
            Reject(!closure.IsSupported, textOnlySandboxSupport ? "genetic_closure" : "unreleased_catalog",
                (textOnlySandboxSupport ? "Complete genetic closure failed: " : "Live coat closure failed: ") + string.Join("; ", closure.Reasons));
            var eventIndex = checked(stream.EventCounter + 1); var eventSeed = Seeds.Derive(stream.Seed, "breeding/event", eventIndex);
            var idScope = Canonical.Hash(header.SaveId.ToString(), eventIndex.ToString(CultureInfo.InvariantCulture));
            var childId = new HorseId(Guid.ParseExact(Canonical.Hash("conception/horse", idScope).Substring(0, 32), "N"));
            Reject(map.ContainsKey(childId), "identity_collision", "The derived unborn identity already exists; investigate save integrity.");
            var transactionId = CostTransactionId(header, eventIndex);
            var draft = new MeiosisService(CoreGeneticsCatalog.Model).CreateDraft(a, b, eventSeed);
            var development = GameTraitResolver.CreateDevelopment(draft.DevelopmentSeed); var traits = GameTraitResolver.Resolve(draft.Genome, development);
            var role = new CounterRng(eventSeed, "offspring/reproductive-role").Chance(Rational.Half) ? ReproductiveRole.Mare : ReproductiveRole.Stallion;
            var unborn = new HorseRecord(childId, "Unborn foal", new Parentage(a.HorseId, b.HorseId), draft.Genome, header.DefaultOffspringResolverVersion,
                draft.AppearanceSeed, request.Requester, new LifeState(LifeStage.Unborn, 0, VisualAge.Foal), new ReproductionState(role), development, traits.Inherited,
                originRecord: "breeding", originCommitId: request.EventId);
            var commit = new BreedingCommit(request.EventId, a, b, unborn, draft.GameteA, draft.GameteB, eventSeed, eventIndex, header.CalendarDay,
                checked(header.CalendarDay + Policy.PregnancyDays), header.CalendarProfileVersion, new[] { confirmedCapacity.ReservationId }, transactionId, Policy.Version);
            return new PreparedConception(request, Terms, confirmedCapacity, a, b, unborn, commit, stream);
        }

        internal static TransactionId CostTransactionId(SaveHeader header, ulong eventIndex)
        {
            var scope = Canonical.Hash(header.SaveId.ToString(), eventIndex.ToString(CultureInfo.InvariantCulture));
            return new TransactionId(Guid.ParseExact(Canonical.Hash("conception/cost", scope).Substring(0, 32), "N"));
        }
    }
}
