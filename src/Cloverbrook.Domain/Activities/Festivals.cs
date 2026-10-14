using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>One seasonal festival as it applies to a calendar day.</summary>
    public sealed class FestivalInfo
    {
        internal FestivalInfo(string id, string name, string treatLabel, string rewardItemId, int year, int startDay, int daysRemaining, int daysUntil)
        { Id = id; Name = name; TreatLabel = treatLabel; RewardItemId = rewardItemId; Year = year; StartDay = startDay; DaysRemaining = daysRemaining; DaysUntilStart = daysUntil; }
        /// <summary>"spring", "summer", "autumn" or "winter".</summary>
        public string Id { get; }
        public string Name { get; }
        /// <summary>Capitalised, such as "Flower garland".</summary>
        public string TreatLabel { get; }
        public string RewardItemId { get; }
        /// <summary>Zero-based year of the festival (calendar day / 112).</summary>
        public int Year { get; }
        public int StartDay { get; }
        /// <summary>Days left including today (1-7) for <see cref="Festivals.Current"/>; 7 for <see cref="Festivals.Next"/>.</summary>
        public int DaysRemaining { get; }
        /// <summary>0 while the festival is on, otherwise the days until it opens.</summary>
        public int DaysUntilStart { get; }
        public string TreatLower => TreatLabel.ToLowerInvariant();
        public string CountKey => Festivals.CountKey(Year, Id);
    }

    /// <summary>
    /// D-032 seasonal festivals. Each 28-day season has a festival in its third week (season days 14 to 20, zero-based day % 28).
    /// During it every horse can have one festival treat a day: a fourth once-per-day care interaction that raises bond like a carrot
    /// (base gain 450). The per-day marker and the per-festival count live in the horse's existing TrainingState, so there is no save
    /// schema change. A festival's reward tack unlocks permanently once any of the player's horses has had treats on three days of it.
    /// </summary>
    public static class Festivals
    {
        public const string Version = "festivals.v1";
        public const string TreatKind = "treat";
        public const string DayKey = "festival_treat_day";
        public const int FirstDayOfSeason = 14;
        public const int Length = 7;
        public const int RewardThreshold = 3;

        private static readonly (string Id, string Name, string Treat, string Reward)[] All =
        {
            ("spring", "Blossom Fair", "Flower garland", "bow.blossom"),
            ("summer", "Summer Picnic", "Watermelon slice", "tail.sunflower"),
            ("autumn", "Harvest Festival", "Crunchy apple", "braids.harvest"),
            ("winter", "Winter Lights", "Peppermint", "halter.fairy"),
        };

        public static IReadOnlyList<string> SeasonIds { get; } = Array.AsReadOnly(All.Select(a => a.Id).ToArray());
        public static string NameOf(string seasonId) => All.First(a => a.Id == seasonId).Name;
        public static string RewardItemFor(string seasonId) => All.First(a => a.Id == seasonId).Reward;
        public static string CountKey(int year, string seasonId) => "fest_" + year.ToString(CultureInfo.InvariantCulture) + "_" + seasonId;

        private static FestivalInfo Make(int seasonNumber, int daysRemaining, int daysUntil)
        {
            var a = All[seasonNumber % 4];
            return new FestivalInfo(a.Id, a.Name, a.Treat, a.Reward, seasonNumber / 4, seasonNumber * StableCalendar.DaysPerSeason + FirstDayOfSeason, daysRemaining, daysUntil);
        }

        /// <summary>The festival on this day, or null.</summary>
        public static FestivalInfo? Current(int calendarDay)
        {
            Check.NonNegative(calendarDay, nameof(calendarDay));
            var inSeason = calendarDay % StableCalendar.DaysPerSeason;
            if (inSeason < FirstDayOfSeason || inSeason >= FirstDayOfSeason + Length) return null;
            return Make(calendarDay / StableCalendar.DaysPerSeason, FirstDayOfSeason + Length - inSeason, 0);
        }

        /// <summary>The next festival that has not started yet (never today's), with <see cref="FestivalInfo.DaysUntilStart"/> set.</summary>
        public static FestivalInfo Next(int calendarDay)
        {
            Check.NonNegative(calendarDay, nameof(calendarDay));
            var season = calendarDay / StableCalendar.DaysPerSeason;
            if (calendarDay % StableCalendar.DaysPerSeason >= FirstDayOfSeason) season++;
            var start = season * StableCalendar.DaysPerSeason + FirstDayOfSeason;
            return Make(season, Length, start - calendarDay);
        }

        private static IEnumerable<HorseRecord> Owned(StableSnapshot state) => state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId));

        public static bool DoneToday(HorseRecord horse, int calendarDay) =>
            Check.NotNull(horse, nameof(horse)).TrainingState.Get(DayKey) == HorseBond.MarkerFor(calendarDay);

        /// <summary>How many days this horse has had a treat in the given festival (0-7).</summary>
        public static int TreatDays(HorseRecord horse, FestivalInfo festival) => Math.Max(0, Math.Min(Length, horse.TrainingState.Get(festival.CountKey)));

        /// <summary>The most days any one of the player's horses has had a treat in any year's festival of this season.</summary>
        public static int BestTreatDays(StableSnapshot state, string seasonId)
        {
            Check.NotNull(state, nameof(state));
            var suffix = "_" + seasonId; var best = 0;
            foreach (var horse in Owned(state))
                foreach (var pair in horse.TrainingState.Values)
                    if (pair.Key.StartsWith("fest_", StringComparison.Ordinal) && pair.Key.EndsWith(suffix, StringComparison.Ordinal)) best = Math.Max(best, pair.Value);
            return Math.Min(Length, best);
        }

        public static bool RewardUnlocked(StableSnapshot state, string seasonId) => BestTreatDays(state, seasonId) >= RewardThreshold;
        public static int RewardsUnlocked(StableSnapshot state) => SeasonIds.Count(id => RewardUnlocked(state, id));

        /// <summary>Why this horse cannot have a festival treat today, or null.</summary>
        public static string? BlockReason(StableSnapshot state, HorseId horseId)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            var festival = Current(state.Header.CalendarDay);
            if (festival == null) return "there is no festival today.";
            var horse = state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
            var problem = HorseBond.HorseProblem(state, horse);
            if (problem != null || horse == null) return problem;
            if (DoneToday(horse, state.Header.CalendarDay)) return horse.Name + " has had a " + festival.TreatLower + " today.";
            return null;
        }

        internal static HorseRecord Apply(HorseRecord horse, FestivalInfo festival, int calendarDay) =>
            HorseBond.Apply(horse, TreatKind, calendarDay, DayKey,
                new[] { new KeyValuePair<string, int>(festival.CountKey, Math.Min(Length, TreatDays(horse, festival) + 1)) });
    }
}
