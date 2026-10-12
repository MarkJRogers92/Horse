using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>One revealed genetic test result, shown exactly as recorded in player knowledge.</summary>
    public sealed class KnownGeneticCall
    {
        internal KnownGeneticCall(string locus, AllelePair call) { Locus = locus; Call = call.A + "/" + call.B; }
        public string Locus { get; }
        public string Call { get; }
    }

    /// <summary>
    /// What a player can observe about one horse (P2-01 inspection). It contains no genome, hidden
    /// potential or diagnostic phenotype: the coat is the visible label of an observable born horse,
    /// and genetics are only the calls recorded in player knowledge.
    /// </summary>
    public sealed class HorseProfile
    {
        internal HorseProfile(HorseRecord horse, string visibleCoat, int? ageDays, string? parentAName, string? parentBName,
            IEnumerable<KnownGeneticCall> knownGenetics, int? dueDay, IEnumerable<string> offspringNames, HorseCareState? care)
        {
            HorseId = horse.HorseId; Name = horse.Name; Stage = horse.LifeState.Stage; Role = horse.ReproductionState.Role; Home = horse.HomeAssignment;
            VisibleCoat = visibleCoat; AgeDays = ageDays; ParentAName = parentAName; ParentBName = parentBName;
            KnownGenetics = Snapshot.List(knownGenetics); DueDay = dueDay; OffspringNames = Snapshot.List(offspringNames); Care = care;
            IsFounder = horse.Parentage.ParentAId == null && horse.Parentage.ParentBId == null;
        }
        public HorseId HorseId { get; }
        public string Name { get; }
        public LifeStage Stage { get; }
        public ReproductiveRole Role { get; }
        public HomeKind Home { get; }
        /// <summary>The coat as it looks, or a plain reason when it cannot be observed or resolved.</summary>
        public string VisibleCoat { get; }
        /// <summary>Days since birth for a born horse with a recorded birth day; null for founders without one and for unborn foals.</summary>
        public int? AgeDays { get; }
        public bool IsFounder { get; }
        public string? ParentAName { get; }
        public string? ParentBName { get; }
        public IReadOnlyList<KnownGeneticCall> KnownGenetics { get; }
        /// <summary>The recorded birth day of this mare's current pregnancy, if any.</summary>
        public int? DueDay { get; }
        public IReadOnlyList<string> OffspringNames { get; }
        public HorseCareState? Care { get; }
    }

    public static class HorseProfiles
    {
        public static HorseProfile For(StableSnapshot state, HorseId horseId)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            var byId = state.Horses.ToDictionary(h => h.HorseId);
            if (!byId.TryGetValue(horseId, out var horse)) throw new ArgumentException("The horse is not in this stable.", nameof(horseId));

            string coat;
            if (horse.LifeState.Stage == LifeStage.Unborn) coat = "Not born yet";
            else
            {
                var resolved = new PhenotypeResolver().ResolveGenetics(horse);
                coat = resolved.Phenotype != null ? PhenotypeResolver.VisibleLabel(resolved.Phenotype) : "Coat not available in this version";
            }
            int? age = horse.LifeState.Stage != LifeStage.Unborn && horse.BirthDay.HasValue && state.Header.CalendarDay >= horse.BirthDay.Value
                ? state.Header.CalendarDay - horse.BirthDay.Value : (int?)null;
            string? Name(HorseId? id) => id == null ? null : byId.TryGetValue(id, out var parent) ? parent.Name : "Unknown horse";
            var genetics = horse.PlayerKnowledge.RevealedCalls.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new KnownGeneticCall(p.Key, p.Value));
            int? due = null;
            if (horse.ReproductionState.PregnancyId != null)
                due = state.Conceptions.Where(c => c.Commit.BreedingCommitId.Equals(horse.ReproductionState.PregnancyId))
                    .Select(c => (int?)c.Commit.PlannedBirthDay).SingleOrDefault();
            var offspring = state.Horses.Where(h => horse.HorseId.Equals(h.Parentage.ParentAId) || horse.HorseId.Equals(h.Parentage.ParentBId))
                .OrderBy(h => h.BirthDay ?? int.MaxValue).ThenBy(h => h.Name, StringComparer.Ordinal).Select(h => h.Name);
            HorseCareState? care = state.DailyState.TryGet(horse.HorseId, out var current) ? current : null;
            return new HorseProfile(horse, coat, age, Name(horse.Parentage.ParentAId), Name(horse.Parentage.ParentBId), genetics, due, offspring, care);
        }
    }
}
