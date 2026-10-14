using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>
    /// D-021 hands-on care and bond. Patting, brushing and a carrot each raise a horse's bond once per calendar day,
    /// with diminishing returns. Bond lives in the horse's existing TrainingState (learned, never inherited), costs
    /// nothing, never decays, and makes a horse a little calmer and quicker in gallops.
    /// </summary>
    public static class HorseBond
    {
        public const string Version = "bond.v1";
        public const string BondKey = "bond";
        public const int MaxBond = 10000;
        public static IReadOnlyList<string> Interactions { get; } = Array.AsReadOnly(new[] { "pat", "brush", "carrot" });

        private static int BaseGain(string kind) => kind switch { "pat" => 250, "brush" => 350, "carrot" => 400, "treat" => 450, _ => 0 };
        private static string DayKey(string kind) => kind + "_day";
        private static int DayMarker(int calendarDay) => calendarDay % 10000 + 1;

        public static int Bond(HorseRecord horse) => Math.Max(0, Math.Min(MaxBond, Check.NotNull(horse, nameof(horse)).TrainingState.Get(BondKey)));

        /// <summary>0-5 hearts for display: any bond shows a first heart; five hearts above 80%.</summary>
        public static int Hearts(HorseRecord horse) => Math.Min(5, (Bond(horse) * 5 + MaxBond - 1) / MaxBond);

        public static bool DoneToday(HorseRecord horse, string kind, int calendarDay) => horse.TrainingState.Get(DayKey(kind)) == DayMarker(calendarDay);

        public static int GainToday(HorseRecord horse, string kind, int calendarDay) => GainToday(horse, kind, calendarDay, 1000);

        /// <summary>D-035: the gain including this horse's personality multiplier.</summary>
        public static int GainToday(StableSnapshot state, HorseRecord horse, string kind, int calendarDay) =>
            GainToday(horse, kind, calendarDay, Personality.BondPermille(state, horse, kind));

        private static int GainToday(HorseRecord horse, string kind, int calendarDay, int permille)
        {
            if (BaseGain(kind) == 0 || DoneToday(horse, kind, calendarDay)) return 0;
            var bond = Bond(horse);
            if (bond >= MaxBond) return 0;
            return Math.Max(1, BaseGain(kind) * permille / 1000 * (MaxBond - bond) / MaxBond);
        }

        /// <summary>Why this horse cannot have this interaction today, or null.</summary>
        public static string? BlockReason(StableSnapshot state, HorseId horseId, string kind)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            if (!Interactions.Contains(kind)) return "that is not something you can do with a horse.";
            var found = state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
            var problem = HorseProblem(state, found);
            if (problem != null || found == null) return problem;
            var horse = found;
            if (DoneToday(horse, kind, state.Header.CalendarDay)) return kind switch
            {
                "pat" => horse.Name + " has had lots of pats today.",
                "brush" => horse.Name + " is already gleaming today.",
                _ => horse.Name + " has had a carrot today.",
            };
            return null;
        }

        /// <summary>Why a horse cannot be cared for at all (not owned, sold or unborn), or null. Shared with the festival treat.</summary>
        internal static string? HorseProblem(StableSnapshot state, HorseRecord? horse)
        {
            if (horse == null || !horse.OwnerId.Equals(state.PlayerOwnerId) || HorseMarket.IsSold(horse)) return "that horse is not in your stable.";
            if (horse.LifeState.Stage == LifeStage.Unborn) return "the foal has not been born yet.";
            return null;
        }

        internal static string DayKeyFor(string kind) => DayKey(kind);
        internal static int MarkerFor(int calendarDay) => DayMarker(calendarDay);

        internal static HorseRecord Apply(HorseRecord horse, string kind, int calendarDay) => Apply(horse, kind, calendarDay, DayKey(kind), null);

        /// <summary>D-035: applies the gain with the horse's personality multiplier.</summary>
        internal static HorseRecord Apply(StableSnapshot state, HorseRecord horse, string kind, int calendarDay) =>
            Apply(horse, kind, calendarDay, DayKey(kind), null, Personality.BondPermille(state, horse, kind));

        /// <summary>Raises bond by the kind's gain and stamps its day marker; <paramref name="extra"/> replaces other keys in the same write.</summary>
        internal static HorseRecord Apply(HorseRecord horse, string kind, int calendarDay, string dayKey, IEnumerable<KeyValuePair<string, int>>? extra, int permille = 1000)
        {
            var gain = BaseGain(kind) == 0 || Bond(horse) >= MaxBond ? 0 : Math.Max(1, BaseGain(kind) * permille / 1000 * (MaxBond - Bond(horse)) / MaxBond);
            var changed = new HashSet<string>(StringComparer.Ordinal) { BondKey, dayKey };
            var extras = extra?.ToList() ?? new List<KeyValuePair<string, int>>();
            foreach (var e in extras) changed.Add(e.Key);
            var values = horse.TrainingState.Values.Where(p => !changed.Contains(p.Key))
                .Concat(new[] { new KeyValuePair<string, int>(BondKey, Math.Min(MaxBond, Bond(horse) + gain)), new KeyValuePair<string, int>(dayKey, DayMarker(calendarDay)) })
                .Concat(extras);
            return new HorseRecord(horse.HorseId, horse.Name, horse.Parentage, horse.Genome, horse.ResolverVersion, horse.AppearanceSeed, horse.OwnerId,
                horse.LifeState, horse.ReproductionState, horse.DevelopmentalFactors, horse.InheritedGamePotential, new QuantizedVector(values),
                horse.PlayerKnowledge, horse.CosmeticState, horse.HomeAssignment, horse.OriginRecord, horse.OriginCommitId, horse.BirthDay, checked(horse.Revision + 1));
        }
    }
}
