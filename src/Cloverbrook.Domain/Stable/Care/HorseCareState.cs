using System;
using System.Collections.Generic;

namespace Cloverbrook.Domain
{
    /// <summary>Quantized care/comfort values in the inclusive range 0..10000.</summary>
    public sealed class HorseCareState
    {
        public HorseCareState(int nutrition, int bedding, int turnout, int grooming)
        { Nutrition = Level(nutrition, nameof(nutrition)); Bedding = Level(bedding, nameof(bedding)); Turnout = Level(turnout, nameof(turnout)); Grooming = Level(grooming, nameof(grooming)); }
        public int Nutrition { get; }
        public int Bedding { get; }
        public int Turnout { get; }
        public int Grooming { get; }
        public static HorseCareState Initial { get; } = new HorseCareState(7000, 7000, 5000, 5000);
        private static int Level(int value, string name) => value < 0 || value > 10000 ? throw new ArgumentOutOfRangeException(name) : value;
        internal static int Change(int value, int delta) => Math.Max(0, Math.Min(10000, value + delta));
    }

    public sealed class HorseCareOutcome
    {
        internal HorseCareOutcome(HorseId horseId, HorseCareState before, HorseCareState after)
        { HorseId = Check.NotNull(horseId, nameof(horseId)); Before = Check.NotNull(before, nameof(before)); After = Check.NotNull(after, nameof(after)); }
        public HorseId HorseId { get; }
        public HorseCareState Before { get; }
        public HorseCareState After { get; }
    }
}
