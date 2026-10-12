using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>
    /// D-011 gallop training. Learned skill lives in the horse's existing TrainingState (never in inherited
    /// potential, so foals do not inherit it). A horse gains at most once per calendar day, with diminishing returns.
    /// </summary>
    public static class PracticeTraining
    {
        public const string SkillKey = "gallop";
        /// <summary>Last trained calendar day + 1 (0 = never), kept within the vector's bounded range.</summary>
        public const string DayKey = "gallop_day";
        public const int MaxSkill = 10000;
        public const int BaseGain = 600;

        public static int Skill(HorseRecord horse) => Math.Max(0, Math.Min(MaxSkill, Check.NotNull(horse, nameof(horse)).TrainingState.Get(SkillKey)));
        private static int DayMarker(int calendarDay) => calendarDay % 10000 + 1;
        public static bool TrainedToday(HorseRecord horse, int calendarDay) => horse.TrainingState.Get(DayKey) == DayMarker(calendarDay);

        /// <summary>The skill this horse would gain from practising today (0 when already trained today or at maximum).</summary>
        public static int GainToday(HorseRecord horse, int calendarDay)
        {
            if (TrainedToday(horse, calendarDay)) return 0;
            var skill = Skill(horse);
            if (skill >= MaxSkill) return 0;
            return Math.Max(1, BaseGain * (MaxSkill - skill) / MaxSkill);
        }

        internal static HorseRecord Apply(HorseRecord horse, int calendarDay)
        {
            var gain = GainToday(horse, calendarDay);
            if (gain == 0) return horse;
            var values = horse.TrainingState.Values.Where(p => p.Key != SkillKey && p.Key != DayKey)
                .Concat(new[] { new KeyValuePair<string, int>(SkillKey, Skill(horse) + gain), new KeyValuePair<string, int>(DayKey, DayMarker(calendarDay)) });
            return new HorseRecord(horse.HorseId, horse.Name, horse.Parentage, horse.Genome, horse.ResolverVersion, horse.AppearanceSeed, horse.OwnerId,
                horse.LifeState, horse.ReproductionState, horse.DevelopmentalFactors, horse.InheritedGamePotential, new QuantizedVector(values),
                horse.PlayerKnowledge, horse.CosmeticState, horse.HomeAssignment, horse.OriginRecord, horse.OriginCommitId, horse.BirthDay, checked(horse.Revision + 1));
        }
    }
}
