using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    public enum CapacityStage { Pregnancy, Foal, Youngster, Adult }
    public sealed class EligibilityIssue
    {
        public EligibilityIssue(string code, string message) { Code = Check.Text(code, nameof(code)); Message = Check.Text(message, nameof(message)); }
        public string Code { get; }
        public string Message { get; }
    }
    public sealed class BreedingPolicy
    {
        // The relationship threshold is an explicit GAME POLICY input, not a
        // clinical safety threshold or a newly inferred biological rule.
        public BreedingPolicy(string version, Rational blockKinshipAtOrAbove, bool allowUnknownAncestry = false,
            int breedableDay = 12, int pregnancyDays = 4, int foalDays = 3, int youngsterDays = 3)
        {
            Version = Check.Text(version, nameof(version));
            if (blockKinshipAtOrAbove <= Rational.Zero || blockKinshipAtOrAbove > Rational.One) throw new ArgumentOutOfRangeException(nameof(blockKinshipAtOrAbove));
            if (breedableDay < 1 || pregnancyDays < 1 || foalDays < 1 || youngsterDays < 1) throw new ArgumentOutOfRangeException(nameof(breedableDay));
            BlockKinshipAtOrAbove = blockKinshipAtOrAbove; AllowUnknownAncestry = allowUnknownAncestry;
            BreedableDay = breedableDay; PregnancyDays = pregnancyDays; FoalDays = foalDays; YoungsterDays = youngsterDays;
        }
        public string Version { get; }
        public Rational BlockKinshipAtOrAbove { get; }
        public bool AllowUnknownAncestry { get; }
        public int BreedableDay { get; }
        public int PregnancyDays { get; }
        public int FoalDays { get; }
        public int YoungsterDays { get; }
    }
    public sealed class ReservedCapacityWindow
    {
        // A forecast snapshot of a confirmed hold for this specific breeding.
        // Totals already include known construction changes, pregnancies and lease
        // returns. Adjacent intervals must be split at EVERY projected change.
        public ReservedCapacityWindow(CapacityStage stage, int startDay, int? endDayExclusive, int totalSlots,
            int occupiedSlots, int reservationsForOthers, bool reservedForThisBreeding)
        {
            if (!Enum.IsDefined(typeof(CapacityStage), stage)) throw new ArgumentOutOfRangeException(nameof(stage));
            Stage = stage; StartDay = Check.NonNegative(startDay, nameof(startDay)); EndDayExclusive = endDayExclusive;
            if (endDayExclusive.HasValue && endDayExclusive.Value <= startDay) throw new ArgumentOutOfRangeException(nameof(endDayExclusive));
            TotalSlots = Check.NonNegative(totalSlots, nameof(totalSlots)); OccupiedSlots = Check.NonNegative(occupiedSlots, nameof(occupiedSlots));
            ReservationsForOthers = Check.NonNegative(reservationsForOthers, nameof(reservationsForOthers)); ReservedForThisBreeding = reservedForThisBreeding;
        }
        public CapacityStage Stage { get; }
        public int StartDay { get; }
        public int? EndDayExclusive { get; }
        public int TotalSlots { get; }
        public int OccupiedSlots { get; }
        public int ReservationsForOthers { get; }
        public bool ReservedForThisBreeding { get; }
        public bool HasValidHold => ReservedForThisBreeding && (long)OccupiedSlots + ReservationsForOthers + 1 <= TotalSlots;
    }
    public sealed class BreedingCapacityPlan
    {
        public BreedingCapacityPlan(BreedingCommitId breedingId, CapacityReservationId reservationId, ulong capacityRevision,
            IEnumerable<ReservedCapacityWindow> windows)
        {
            BreedingId = Check.NotNull(breedingId, nameof(breedingId)); ReservationId = Check.NotNull(reservationId, nameof(reservationId));
            CapacityRevision = capacityRevision; Windows = Snapshot.List(windows);
        }
        public BreedingCommitId BreedingId { get; }
        public CapacityReservationId ReservationId { get; }
        public ulong CapacityRevision { get; }
        public IReadOnlyList<ReservedCapacityWindow> Windows { get; }
        public IReadOnlyList<EligibilityIssue> Validate(int conceptionDay, BreedingPolicy policy)
        {
            Check.NonNegative(conceptionDay, nameof(conceptionDay)); Check.NotNull(policy, nameof(policy));
            int born = checked(conceptionDay + policy.PregnancyDays), young = checked(born + policy.FoalDays), adult = checked(young + policy.YoungsterDays);
            var issues = new List<EligibilityIssue>();
            var spans = new[] { (CapacityStage.Pregnancy, conceptionDay, (int?)born), (CapacityStage.Foal, born, (int?)young),
                (CapacityStage.Youngster, young, (int?)adult), (CapacityStage.Adult, adult, (int?)null) };
            foreach (var span in spans)
            {
                var rows = Windows.Where(w => w.Stage == span.Item1).OrderBy(w => w.StartDay).ToArray();
                int? cursor = span.Item2; var valid = rows.Length != 0;
                foreach (var row in rows)
                {
                    if (!cursor.HasValue || row.StartDay != cursor.Value || !row.HasValidHold
                        || (span.Item3.HasValue && (!row.EndDayExclusive.HasValue || row.EndDayExclusive.Value > span.Item3.Value))) valid = false;
                    cursor = row.EndDayExclusive;
                }
                if (cursor != span.Item3) valid = false;
                if (!valid) issues.Add(new EligibilityIssue("capacity." + span.Item1.ToString().ToLowerInvariant(),
                    "Confirmed, non-overbooked " + span.Item1.ToString().ToLowerInvariant() + " capacity must cover every day, including known returns and future transitions."));
            }
            return Snapshot.List(issues);
        }
    }
    public sealed class BreedingEligibilityResult
    {
        internal BreedingEligibilityResult(IEnumerable<EligibilityIssue> rejections, IEnumerable<EligibilityIssue> warnings, KinshipResult relatedness)
        {
            Rejections = Snapshot.List(rejections); Warnings = Snapshot.List(warnings); Relatedness = relatedness;
        }
        public bool IsEligible => Rejections.Count == 0;
        public IReadOnlyList<EligibilityIssue> Rejections { get; }
        public IReadOnlyList<EligibilityIssue> Warnings { get; }
        public KinshipResult Relatedness { get; }
    }
    public sealed class BreedingEligibility
    {
        public BreedingEligibilityResult Evaluate(HorseRecord parentA, HorseRecord parentB, PedigreeGraph pedigree,
            BreedingPolicy policy, int currentDay, BreedingCommitId proposedEventId, BreedingCapacityPlan? capacityPlan,
            ulong expectedParentARevision, ulong expectedParentBRevision, ulong expectedCapacityRevision)
        {
            Check.NotNull(parentA, nameof(parentA)); Check.NotNull(parentB, nameof(parentB)); Check.NotNull(pedigree, nameof(pedigree)); Check.NotNull(policy, nameof(policy));
            Check.NonNegative(currentDay, nameof(currentDay)); Check.NotNull(proposedEventId, nameof(proposedEventId));
            var errors = new List<EligibilityIssue>(); var warnings = new List<EligibilityIssue>();
            if (parentA.HorseId.Equals(parentB.HorseId)) errors.Add(new EligibilityIssue("same_horse", "Select two different horses."));
            if (parentA.Revision != expectedParentARevision || parentB.Revision != expectedParentBRevision) errors.Add(new EligibilityIssue("stale_parent", "A parent changed after this pairing was prepared; refresh the plan."));
            foreach (var horse in new[] { parentA, parentB })
            {
                if (horse.LifeState.Stage != LifeStage.Adult)
                    errors.Add(new EligibilityIssue("age", horse.Name + " has not reached adult breeding age."));
                if (horse.BirthDay.HasValue)
                {
                    try
                    {
                        var daysAfterBirth = Math.Max(0, checked(policy.BreedableDay - policy.PregnancyDays));
                        if (currentDay < checked(horse.BirthDay.Value + daysAfterBirth))
                            errors.Add(new EligibilityIssue("age", horse.Name + " needs more time after birth before breeding eligibility."));
                    }
                    catch (OverflowException)
                    { errors.Add(new EligibilityIssue("age", horse.Name + " has a breeding eligibility date outside the supported calendar range.")); }
                }
                else if (horse.LifeState.DaysSinceConception < policy.BreedableDay)
                    errors.Add(new EligibilityIssue("age", horse.Name + " has not reached breeding eligibility."));
                if (horse.HomeAssignment != HomeKind.ActiveStable) errors.Add(new EligibilityIssue("home", horse.Name + " must return from cooperative pasture or lease before breeding."));
                if (horse.ReproductionState.PregnancyId != null) errors.Add(new EligibilityIssue("pregnant", horse.Name + " already has a pending pregnancy."));
                if (currentDay < horse.ReproductionState.RecoveryUntilDay) errors.Add(new EligibilityIssue("recovery", horse.Name + " is still in recovery."));
                if (!pedigree.MatchesParentage(horse)) errors.Add(new EligibilityIssue("pedigree_mismatch", "The pedigree does not match " + horse.Name + "'s current authoritative parentage."));
            }
            var roles = new[] { parentA.ReproductionState.Role, parentB.ReproductionState.Role };
            if (!roles.Contains(ReproductiveRole.Mare) || !roles.Contains(ReproductiveRole.Stallion)) errors.Add(new EligibilityIssue("reproductive_roles", "This pairing requires one eligible mare and one eligible stallion."));
            var relatedness = pedigree.ProposedOffspringInbreeding(parentA.HorseId, parentB.HorseId);
            if (!relatedness.IsComplete)
            {
                var issue = new EligibilityIssue("unknown_ancestry", relatedness.Qualification);
                if (policy.AllowUnknownAncestry) warnings.Add(issue); else errors.Add(issue);
            }
            if (relatedness.MinimumCoefficient >= policy.BlockKinshipAtOrAbove)
                errors.Add(new EligibilityIssue("relationship_policy", "This pair exceeds the selected game's relatedness policy; choose a less-related mate."));
            if (capacityPlan == null) errors.Add(new EligibilityIssue("capacity_missing", "Reserve foaling and future housing capacity before committing this pairing."));
            else
            {
                if (!capacityPlan.BreedingId.Equals(proposedEventId)) errors.Add(new EligibilityIssue("capacity_event", "These capacity reservations belong to a different breeding event."));
                if (capacityPlan.CapacityRevision != expectedCapacityRevision) errors.Add(new EligibilityIssue("stale_capacity", "Capacity changed after the reservation snapshot; refresh the plan."));
                errors.AddRange(capacityPlan.Validate(currentDay, policy));
            }
            return new BreedingEligibilityResult(errors, warnings, relatedness);
        }
    }
}
