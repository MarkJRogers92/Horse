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

        private static int BaseGain(string kind) => kind switch { "pat" => 250, "brush" => 350, "carrot" => 400, _ => 0 };
        private static string DayKey(string kind) => kind + "_day";
        private static int DayMarker(int calendarDay) => calendarDay % 10000 + 1;

        public static int Bond(HorseRecord horse) => Math.Max(0, Math.Min(MaxBond, Check.NotNull(horse, nameof(horse)).TrainingState.Get(BondKey)));

        /// <summary>0-5 hearts for display: any bond shows a first heart; five hearts above 80%.</summary>
        public static int Hearts(HorseRecord horse) => Math.Min(5, (Bond(horse) * 5 + MaxBond - 1) / MaxBond);

        public static bool DoneToday(HorseRecord horse, string kind, int calendarDay) => horse.TrainingState.Get(DayKey(kind)) == DayMarker(calendarDay);

        public static int GainToday(HorseRecord horse, string kind, int calendarDay)
        {
            if (BaseGain(kind) == 0 || DoneToday(horse, kind, calendarDay)) return 0;
            var bond = Bond(horse);
            if (bond >= MaxBond) return 0;
            return Math.Max(1, BaseGain(kind) * (MaxBond - bond) / MaxBond);
        }

        /// <summary>Why this horse cannot have this interaction today, or null.</summary>
        public static string? BlockReason(StableSnapshot state, HorseId horseId, string kind)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            if (!Interactions.Contains(kind)) return "that is not something you can do with a horse.";
            var horse = state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
            if (horse == null || !horse.OwnerId.Equals(state.PlayerOwnerId) || HorseMarket.IsSold(horse)) return "that horse is not in your stable.";
            if (horse.LifeState.Stage == LifeStage.Unborn) return "the foal has not been born yet.";
            if (DoneToday(horse, kind, state.Header.CalendarDay)) return kind switch
            {
                "pat" => horse.Name + " has had lots of pats today.",
                "brush" => horse.Name + " is already gleaming today.",
                _ => horse.Name + " has had a carrot today.",
            };
            return null;
        }

        internal static HorseRecord Apply(HorseRecord horse, string kind, int calendarDay)
        {
            var gain = GainToday(horse, kind, calendarDay);
            var values = horse.TrainingState.Values.Where(p => p.Key != BondKey && p.Key != DayKey(kind))
                .Concat(new[] { new KeyValuePair<string, int>(BondKey, Math.Min(MaxBond, Bond(horse) + gain)), new KeyValuePair<string, int>(DayKey(kind), DayMarker(calendarDay)) });
            return new HorseRecord(horse.HorseId, horse.Name, horse.Parentage, horse.Genome, horse.ResolverVersion, horse.AppearanceSeed, horse.OwnerId,
                horse.LifeState, horse.ReproductionState, horse.DevelopmentalFactors, horse.InheritedGamePotential, new QuantizedVector(values),
                horse.PlayerKnowledge, horse.CosmeticState, horse.HomeAssignment, horse.OriginRecord, horse.OriginCommitId, horse.BirthDay, checked(horse.Revision + 1));
        }
    }
}
