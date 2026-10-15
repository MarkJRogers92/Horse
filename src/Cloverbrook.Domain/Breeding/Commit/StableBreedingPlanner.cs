using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>A read-only, text-only breeding quote for the pinned prototype Sandbox.</summary>
    public sealed class StableBreedingQuote
    {
        internal StableBreedingQuote(ConceptionRequest request, bool eligible, IEnumerable<EligibilityIssue> rejections,
            IEnumerable<EligibilityIssue> warnings, long costCoins, int conceptionDay, int plannedBirthDay,
            int currentCapacity, int currentCapacityUsed, int futureCapacity, int futureCapacityUsed,
            BreedingCapacityPlan capacity)
        {
            Request = request; IsEligible = eligible; Rejections = Snapshot.List(rejections); Warnings = Snapshot.List(warnings);
            CostCoins = costCoins; ConceptionDay = conceptionDay; PlannedBirthDay = plannedBirthDay;
            CurrentCapacity = currentCapacity; CurrentCapacityUsed = currentCapacityUsed;
            FutureCapacity = futureCapacity; FutureCapacityUsed = futureCapacityUsed;
            Capacity = capacity;
        }
        public ConceptionRequest Request { get; }
        public bool IsEligible { get; }
        public IReadOnlyList<EligibilityIssue> Rejections { get; }
        public IReadOnlyList<EligibilityIssue> Warnings { get; }
        public long CostCoins { get; }
        public int ConceptionDay { get; }
        public int PlannedBirthDay { get; }
        public int CurrentCapacity { get; }
        public int CurrentCapacityUsed { get; }
        public int FutureCapacity { get; }
        public int FutureCapacityUsed { get; }
        internal BreedingCapacityPlan Capacity { get; }
    }

    /// <summary>
    /// Fixed terms for text-only breeding in the local stable-management
    /// Sandbox. It does not approve coats, layers, descriptors or rendered art.
    /// </summary>
    public sealed class StableBreedingPlanner
    {
        public const string PolicyVersion = "stable.text-breeding.v1";
        public const string TermsVersion = "stable.text-breeding.terms.v1";
        public const string GameBuildVersion = "stable-management-prototype";
        public const string CalendarProfileVersion = "calendar.v1";
        public const long CostCoins = 50;
        /// <summary>The original home places. A stable's real capacity is <see cref="StableShop.Stalls"/> (D-022).</summary>
        public const int CapacitySlots = StableShop.BaseStalls;
        public const string StarterMigrationMarker = "stable.starter-breeding.v1";

        private const string JuniperAppearanceSuffix = "4a554e49504552";
        private const string MapleAppearancePrefix = "4d41504c45";
        private readonly BreedingPolicy policy = new BreedingPolicy(PolicyVersion, new Rational(1, 8),
            allowUnknownAncestry: true, breedableDay: 12, pregnancyDays: 4, foalDays: 3, youngsterDays: 3);
        private readonly ConceptionTerms terms = new ConceptionTerms(TermsVersion, CostCoins);
        private readonly ConceptionService service;

        public StableBreedingPlanner()
        {
            // This empty matrix is not an approval artifact. The internal
            // service is explicitly pinned to text-only genetic closure.
            var noVisualApproval = new CoatSupportMatrix("text-only.no-visual-approval.v1",
                Array.Empty<string>(), Array.Empty<CoatClassSupport>());
            service = new ConceptionService(policy, terms, noVisualApproval, textOnlySandboxSupport: true);
        }

        public IReadOnlyList<PublicHorseSummary> GetOwnedHorses(StableSnapshot state)
        {
            Check.NotNull(state, nameof(state));
            return Snapshot.List(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId))
                .OrderBy(h => h.Name, StringComparer.Ordinal).ThenBy(h => h.HorseId.ToString(), StringComparer.Ordinal)
                .Select(h => new PublicHorseSummary(h)));
        }

        public StableBreedingQuote Preview(StableSnapshot state, HorseId mareId, HorseId stallionId, BreedingCommitId eventId)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(mareId, nameof(mareId));
            Check.NotNull(stallionId, nameof(stallionId)); Check.NotNull(eventId, nameof(eventId));
            var rejections = new List<EligibilityIssue>(); var warnings = new List<EligibilityIssue>();
            void Reject(bool condition, string code, string message) { if (condition) rejections.Add(new EligibilityIssue(code, message)); }

            var day = state.Header.CalendarDay; var birthDay = day;
            try { birthDay = checked(day + policy.PregnancyDays); }
            catch (OverflowException) { Reject(true, "calendar_overflow", "The projected birth date exceeds the supported calendar range."); }

            var reservationId = ReservationId(eventId);
            var request = new ConceptionRequest(eventId, state.PlayerOwnerId, mareId, stallionId,
                state.Header.SaveRevision, Find(state, mareId)?.Revision ?? 0, Find(state, stallionId)?.Revision ?? 0,
                state.CapacityRevision, reservationId, TermsVersion);

            foreach (var problem in PolicyGateProblems(state.Header)) Reject(true, "sandbox_policy", problem);
            Reject(!state.PlayerOwnerId.Equals(request.Requester), "wallet_owner", "This pairing is not authorized for the saved stable.");
            Reject(state.Header.SaveRevision == ulong.MaxValue, "stale_save", "The stable revision cannot advance beyond its supported range.");
            Reject(state.CapacityRevision == ulong.MaxValue, "capacity_revision", "The stable capacity revision cannot advance beyond its supported range.");
            Reject(state.Coins < CostCoins, "funds", "The stable needs 50 coins before confirming this pairing.");
            Reject(state.Header.ActiveVariantCatalogVersion != CoreGeneticsCatalog.CatalogVersion
                || state.Header.ActiveInheritanceModelVersion != CoreGeneticsCatalog.InheritanceVersion
                || state.Header.DefaultOffspringResolverVersion != CoreGeneticsCatalog.ResolverVersion,
                "unsupported_versions", "This save needs its installed genetics implementation before breeding.");
            var stream = state.Header.RngStreams.Breeding;
            Reject(stream.AlgorithmVersion != Seeds.AlgorithmVersion || stream.EventCounter != state.Header.LastCommittedBreedingEventIndex
                || stream.EventCounter == ulong.MaxValue, "breeding_stream", "The saved breeding event stream is invalid or exhausted.");
            if (stream.EventCounter != ulong.MaxValue)
            {
                var nextIndex = stream.EventCounter + 1;
                var scope = Canonical.Hash(state.Header.SaveId.ToString(), nextIndex.ToString(CultureInfo.InvariantCulture));
                var childId = new HorseId(Guid.ParseExact(Canonical.Hash("conception/horse", scope).Substring(0, 32), "N"));
                Reject(state.Horses.Any(h => h.HorseId.Equals(childId)), "identity_collision", "The next offspring identity already exists; investigate save integrity.");
            }
            Reject(state.Horses.Any(h => HorseMarket.IsSold(h) ? false : !h.OwnerId.Equals(state.PlayerOwnerId)
                || (h.HomeAssignment != HomeKind.ActiveStable && h.HomeAssignment != HomeKind.CooperativePasture)),
                "unsupported_population", "This prototype supports owner-owned horses at the active stable or cooperative pasture.");
            Reject(state.PendingCapacityPlans.Any(p => p.BreedingId.Equals(eventId) || p.ReservationId.Equals(reservationId)),
                "reservation_conflict", "This event or reservation is already held by another pending pairing.");
            Reject(state.Conceptions.Any(c => c.Capacity.ReservationId.Equals(reservationId)),
                "reservation_conflict", "This deterministic reservation identity was already consumed by a committed pairing.");
            Reject(state.Conceptions.Any(c => c.Commit.BreedingCommitId.Equals(eventId)),
                "idempotency_conflict", "This pairing ID has already been committed; reload its saved result.");

            var mare = Find(state, mareId); var stallion = Find(state, stallionId);
            Reject(mare == null || stallion == null, "parents", "Both selected horses must be present in the saved stable.");
            var capacity = BuildCapacity(state, eventId, reservationId, out var futureUsed, out var currentUsed, out var capacityIssues);
            rejections.AddRange(capacityIssues);
            if (mare != null && stallion != null && !capacityIssues.Any(issue => issue.Code == "capacity_overflow"))
            {
                Reject(mare.Revision == ulong.MaxValue, "stale_parent", "The mare record cannot advance beyond its supported revision range.");
                Reject(mare.ReproductionState.Role != ReproductiveRole.Mare || stallion.ReproductionState.Role != ReproductiveRole.Stallion,
                    "reproductive_roles", "Select the horse assigned as mare first and the horse assigned as stallion second.");
                var pedigree = new PedigreeGraph(state.Horses.Select(h => new PedigreeNode(h.HorseId, h.Parentage)));
                var eligibility = new BreedingEligibility().Evaluate(mare, stallion, pedigree, policy, day, eventId, capacity,
                    mare.Revision, stallion.Revision, state.CapacityRevision);
                rejections.AddRange(eligibility.Rejections); warnings.AddRange(eligibility.Warnings);
            }

            var closure = CheckGeneticClosure(state.Horses);
            Reject(!closure.IsSupported, "genetic_closure", "Complete genetic closure failed: " + string.Join("; ", closure.Reasons));
            return new StableBreedingQuote(request, rejections.Count == 0, rejections, warnings, CostCoins, day, birthDay,
                StableShop.Stalls(state), currentUsed, StableShop.Stalls(state), futureUsed, capacity);
        }

        internal ConceptionService Service => service;
        internal BreedingPolicy Policy => policy;
        internal ConceptionTerms Terms => terms;

        internal static string[] PolicyGateProblems(SaveHeader header)
        {
            var problems = new List<string>();
            if (header.SaveMode != SaveMode.Sandbox) problems.Add("Text-only breeding is restricted to Sandbox saves.");
            if (header.GameBuildVersion != GameBuildVersion) problems.Add("This save was not created by the stable-management prototype build.");
            if (header.CalendarProfileVersion != CalendarProfileVersion) problems.Add("This save uses an unsupported calendar profile.");
            return problems.ToArray();
        }

        internal static CatalogCoatAudit CheckGeneticClosure(IEnumerable<HorseRecord> horses) =>
            LiveCatalogCoatGuard.CheckTextOnlyGeneticClosure(horses);

        internal static CapacityReservationId ReservationId(BreedingCommitId eventId) => new CapacityReservationId(
            Guid.ParseExact(Canonical.Hash("stable.text-breeding.reservation.v1", eventId.ToString()).Substring(0, 32), "N"));

        private BreedingCapacityPlan BuildCapacity(StableSnapshot state, BreedingCommitId eventId,
            CapacityReservationId reservationId, out int futureUsed, out int currentUsed, out List<EligibilityIssue> issues)
        {
            issues = new List<EligibilityIssue>();
            var rows = new List<ReservedCapacityWindow>();
            var current = state.Header.CalendarDay;
            foreach (var pending in state.PendingCapacityPlans)
            {
                try
                {
                    if (pending.Validate(current, policy).Count != 0)
                        issues.Add(new EligibilityIssue("pending_capacity", "An existing confirmed capacity hold has unsupported or incomplete windows."));
                }
                catch (OverflowException)
                { issues.Add(new EligibilityIssue("pending_capacity", "An existing confirmed capacity hold exceeds the supported calendar range.")); }
            }
            var slots = StableShop.Stalls(state); // D-022: built stalls add home places
            var atHome = HorsePasture.ActivePlacesUsed(state) - state.PendingCapacityPlans.Count;
            currentUsed = atHome + state.PendingCapacityPlans.Count;
            futureUsed = currentUsed + 1;
            var windowsProjected = false;
            try
            {
                var birth = checked(current + policy.PregnancyDays);
                var young = checked(birth + policy.FoalDays);
                var adult = checked(young + policy.YoungsterDays);
                var occupied = atHome;
                var reservations = state.PendingCapacityPlans.Count;
                rows.Add(new ReservedCapacityWindow(CapacityStage.Pregnancy, current, birth, slots, occupied, reservations, true));
                rows.Add(new ReservedCapacityWindow(CapacityStage.Foal, birth, young, slots, occupied, reservations, true));
                rows.Add(new ReservedCapacityWindow(CapacityStage.Youngster, young, adult, slots, occupied, reservations, true));
                rows.Add(new ReservedCapacityWindow(CapacityStage.Adult, adult, null, slots, occupied, reservations, true));
                windowsProjected = true;
            }
            catch (OverflowException)
            {
                issues.Add(new EligibilityIssue("capacity_overflow", "The projected capacity window exceeds the supported calendar range."));
            }
            if (currentUsed > slots) issues.Add(new EligibilityIssue("capacity_current", $"The current stable already exceeds its {slots} home places."));
            if (futureUsed > slots) issues.Add(new EligibilityIssue("capacity_future", $"The pairing would exceed {slots} occupied or reserved stable places; build a stall, board an adult in pasture or sell a horse."));
            var plan = new BreedingCapacityPlan(eventId, reservationId, state.CapacityRevision, rows);
            if (windowsProjected && plan.Validate(current, policy).Count != 0)
                issues.AddRange(plan.Validate(current, policy));
            return plan;
        }

        private static HorseRecord? Find(StableSnapshot state, HorseId id) => state.Horses.SingleOrDefault(h => h.HorseId.Equals(id));
    }
}
