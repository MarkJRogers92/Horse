using System;

namespace Cloverbrook.Domain
{
    /// <summary>Versioned 28-day season calendar. Save calendar days are zero-based.</summary>
    public sealed class StableCalendar
    {
        public const string Version = "calendar.v1";
        public const int DaysPerSeason = 28;
        public string ProfileVersion => Version;
        public int SeasonIndex(int day) => Check.NonNegative(day, nameof(day)) / DaysPerSeason;
        public int DayOfSeason(int day) => Check.NonNegative(day, nameof(day)) % DaysPerSeason + 1;
        public int AdvanceOneDay(int day) => checked(Check.NonNegative(day, nameof(day)) + 1);
    }
}
