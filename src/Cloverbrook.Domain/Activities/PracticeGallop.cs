using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class PracticeEligibility
    {
        internal PracticeEligibility(HorseRecord horse, string? blockReason) { HorseId = horse.HorseId; Name = horse.Name; BlockReason = blockReason; }
        public HorseId HorseId { get; }
        public string Name { get; }
        public string? BlockReason { get; }
        public bool Eligible => BlockReason == null;
    }

    public sealed class PracticeGallopEntrant
    {
        internal PracticeGallopEntrant(HorseId horseId, string name, int finishMilliseconds, int place, int firstSplitMilliseconds, int lastSplitMilliseconds)
        { HorseId = horseId; Name = name; FinishMilliseconds = finishMilliseconds; Place = place; FirstSplitMilliseconds = firstSplitMilliseconds; LastSplitMilliseconds = lastSplitMilliseconds; }
        public HorseId HorseId { get; }
        public string Name { get; }
        public int FinishMilliseconds { get; }
        public int Place { get; }
        /// <summary>Time to cover the first 200 m.</summary>
        public int FirstSplitMilliseconds { get; }
        /// <summary>Time to cover the final 200 m.</summary>
        public int LastSplitMilliseconds { get; }
    }

    /// <summary>A finished practice gallop. Frames hold each entrant's progress (0..1000 permille) every <see cref="FrameIntervalMilliseconds"/> for replay only.</summary>
    public sealed class PracticeGallopResult
    {
        internal PracticeGallopResult(string seed, IEnumerable<PracticeGallopEntrant> entrants, IEnumerable<IReadOnlyList<int>> frames, IEnumerable<string> observations)
        { Seed = seed; Entrants = Snapshot.List(entrants); Frames = Snapshot.List(frames); Observations = Snapshot.List(observations); }
        public string RulesVersion => PracticeGallop.Version;
        public string Seed { get; }
        /// <summary>Entrants in entry order (ordered by horse ID); use Place for the finishing order.</summary>
        public IReadOnlyList<PracticeGallopEntrant> Entrants { get; }
        public int FrameIntervalMilliseconds => PracticeGallop.FrameIntervalMilliseconds;
        public IReadOnlyList<IReadOnlyList<int>> Frames { get; }
        public IReadOnlyList<string> Observations { get; }
    }

    /// <summary>
    /// D-009/D-011 practice gallop: a versioned, deterministic 800 m flat-race kernel driven by developed game traits
    /// (speed, acceleration, endurance, composure) plus learned gallop skill. It awards nothing and changes no save state; its seed derives
    /// from the save ID, calendar day, run index and entrants, never from a save RNG stream or frame timing.
    /// </summary>
    public static class PracticeGallop
    {
        public const string Version = "activity.practice-gallop.v3";
        public const int DistanceMeters = 800;
        public const int MaxEntrants = 6;
        public const int FrameIntervalMilliseconds = 500;
        private const long Distance = DistanceMeters * 1000L; // millimetres
        private const int TickMilliseconds = 100;
        private const int MaxTicks = 3000;
        private const long SplitDistance = 200_000;

        public static IReadOnlyList<PracticeEligibility> Eligibility(StableSnapshot state)
        {
            Check.NotNull(state, nameof(state));
            return state.Horses.OrderBy(h => h.Name, StringComparer.Ordinal).Select(h => new PracticeEligibility(h, BlockReason(state, h))).ToArray();
        }

        public static PracticeGallopResult Run(StableSnapshot state, IEnumerable<HorseId> entrants, int runIndex)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(entrants, nameof(entrants)); Check.NonNegative(runIndex, nameof(runIndex));
            var ids = entrants.Distinct().OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
            if (ids.Length == 0 || ids.Length > MaxEntrants) throw new ArgumentException($"A practice gallop needs 1 to {MaxEntrants} horses.", nameof(entrants));
            var horses = state.Horses.ToDictionary(h => h.HorseId);
            var runners = new List<Runner>();
            foreach (var id in ids)
            {
                if (!horses.TryGetValue(id, out var horse)) throw new ArgumentException("A horse in this gallop is not in the stable.", nameof(entrants));
                var reason = BlockReason(state, horse);
                if (reason != null) throw new ArgumentException(horse.Name + " cannot gallop: " + reason, nameof(entrants));
                runners.Add(new Runner(horse, new PhenotypeResolver().ResolveGenetics(horse).Traits!.Developed, PracticeTraining.Skill(horse)));
            }
            var seedHex = Canonical.Hash(new[] { Version, state.Header.SaveId.ToString(), state.Header.CalendarDay.ToString(CultureInfo.InvariantCulture),
                runIndex.ToString(CultureInfo.InvariantCulture) }.Concat(ids.Select(id => id.ToString()))).Substring(0, 32);
            return Simulate(runners, seedHex);
        }

        /// <summary>The race itself: the same physics for practice and for club races (D-019).</summary>
        internal static PracticeGallopResult Simulate(IReadOnlyList<HorseRecord> horses, IReadOnlyList<int> skills, string seedHex)
        {
            if (horses.Count != skills.Count) throw new ArgumentException("Every runner needs a training level.");
            var runners = new List<Runner>();
            for (var i = 0; i < horses.Count; i++)
            {
                var traits = new PhenotypeResolver().ResolveGenetics(horses[i]).Traits
                    ?? throw new ArgumentException(horses[i].Name + "'s traits use rules this version cannot read.");
                runners.Add(new Runner(horses[i], traits.Developed, skills[i]));
            }
            return Simulate(runners, seedHex);
        }

        private static PracticeGallopResult Simulate(List<Runner> runners, string seedHex)
        {
            var seed = Seed128.Parse(seedHex);
            foreach (var runner in runners) runner.Start(seed);

            var frames = new List<IReadOnlyList<int>> { runners.Select(_ => 0).ToArray() };
            for (var tick = 1; tick <= MaxTicks && runners.Any(r => r.FinishMs == null); tick++)
            {
                foreach (var runner in runners) runner.Step(tick);
                if (tick * TickMilliseconds % FrameIntervalMilliseconds == 0)
                    frames.Add(runners.Select(r => (int)Math.Min(1000, r.Position * 1000 / Distance)).ToArray());
            }
            if (runners.Any(r => r.FinishMs == null)) throw new InvalidOperationException("The practice gallop did not finish within its time limit.");
            if (frames[frames.Count - 1].Any(p => p < 1000)) frames.Add(runners.Select(_ => 1000).ToArray());

            var order = runners.OrderBy(r => r.FinishMs!.Value).ThenBy(r => r.Horse.HorseId.ToString(), StringComparer.Ordinal).ToList();
            var results = runners.Select(r => new PracticeGallopEntrant(r.Horse.HorseId, r.Horse.Name, r.FinishMs!.Value, order.IndexOf(r) + 1,
                r.FirstSplitMs!.Value, r.FinishMs.Value - r.LastSplitStartMs!.Value)).ToArray();
            return new PracticeGallopResult(seedHex, results, frames, Observations(results));
        }

        private static IEnumerable<string> Observations(IReadOnlyList<PracticeGallopEntrant> results)
        {
            if (results.Count < 2) yield break;
            var quickest = results.OrderBy(r => r.FirstSplitMilliseconds).ThenBy(r => r.Place).First();
            var strongest = results.OrderBy(r => r.LastSplitMilliseconds).ThenBy(r => r.Place).First();
            yield return $"{quickest.Name} was quickest away from the start.";
            if (strongest.HorseId.Equals(quickest.HorseId)) yield return $"{strongest.Name} also had the strongest finish.";
            else yield return $"{strongest.Name} had the strongest finish.";
        }

        private static string? BlockReason(StableSnapshot state, HorseRecord horse)
        {
            if (!horse.OwnerId.Equals(state.PlayerOwnerId)) return "it belongs to another owner.";
            if (horse.HomeAssignment != HomeKind.ActiveStable) return "it is not at the active stable.";
            if (horse.LifeState.Stage != LifeStage.Adult) return "only adult horses gallop.";
            if (horse.ReproductionState.PregnancyId != null) return "she is in foal and resting.";
            if (horse.ReproductionState.RecoveryUntilDay > state.Header.CalendarDay) return "she is recovering after foaling.";
            if (new PhenotypeResolver().ResolveGenetics(horse).Traits == null) return "its traits use rules this version cannot read.";
            return null;
        }

        private sealed class Runner
        {
            private readonly long topSpeed, acceleration, fadePerSecond, noiseAmplitude; // mm/s, mm/s², mm/s per s, mm/s
            private long form, speed, fadeStartMs = -1;
            private CounterRng rng = null!;
            public Runner(HorseRecord horse, QuantizedVector traits, int gallopSkill)
            {
                Horse = horse;
                // Traits are 0..10000 with 5000 average. Every trait moves its parameter within a bounded range.
                topSpeed = 15000 + (traits.Get("speed", 5000) - 5000) * 3000L / 5000;
                acceleration = 4000 + (traits.Get("acceleration", 5000) - 5000) * 2000L / 5000;
                fadePerSecond = 400 - (traits.Get("endurance", 5000) - 5000) * 300L / 5000;
                noiseAmplitude = 600 - (traits.Get("composure", 5000) - 5000) * 400L / 5000;
                // v2 (D-011): learned gallop skill adds up to 0.8 m/s and holds pace longer; it never changes inherited traits.
                topSpeed += gallopSkill * 800L / PracticeTraining.MaxSkill;
                fadePerSecond -= gallopSkill * 150L / PracticeTraining.MaxSkill;
                // v3 (D-021): a well-bonded horse is calmer (up to 30% less wobble) and a touch quicker (up to 0.3 m/s).
                var bond = HorseBond.Bond(horse);
                topSpeed += bond * 300L / HorseBond.MaxBond;
                noiseAmplitude -= noiseAmplitude * bond * 3L / (10L * HorseBond.MaxBond);
            }
            public HorseRecord Horse { get; }
            public long Position { get; private set; }
            public int? FinishMs { get; private set; }
            public int? FirstSplitMs { get; private set; }
            public int? LastSplitStartMs { get; private set; }
            public void Start(Seed128 seed)
            {
                rng = new CounterRng(seed, "practice-gallop/" + Horse.HorseId);
                form = Draw(noiseAmplitude);
            }
            private long Draw(long amplitude) => (long)rng.NextBelow(amplitude * 2 + 1) - amplitude;
            public void Step(int tick)
            {
                if (FinishMs != null) return;
                var nowMs = tick * TickMilliseconds;
                if (fadeStartMs < 0 && Position * 10 >= Distance * 6) fadeStartMs = nowMs;
                var cap = topSpeed + form;
                if (fadeStartMs >= 0) cap = Math.Max(topSpeed * 6 / 10, cap - fadePerSecond * (nowMs - fadeStartMs) / 1000);
                speed = Math.Min(cap, speed + acceleration * TickMilliseconds / 1000);
                var effective = Math.Max(0, speed + Draw(noiseAmplitude) / 2);
                var before = Position;
                Position += effective * TickMilliseconds / 1000;
                if (FirstSplitMs == null && Position >= SplitDistance) FirstSplitMs = Cross(before, SplitDistance, nowMs);
                if (LastSplitStartMs == null && Position >= Distance - SplitDistance) LastSplitStartMs = Cross(before, Distance - SplitDistance, nowMs);
                if (Position >= Distance) { FinishMs = Cross(before, Distance, nowMs); Position = Distance; }
            }
            private int Cross(long before, long mark, int nowMs) =>
                (int)(nowMs - TickMilliseconds + (mark - before) * TickMilliseconds / Math.Max(1, Position - before));
        }
    }
}
