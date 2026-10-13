using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>A visiting horse in this week's cup. Rivals never join the stable; they exist only for the race.</summary>
    public sealed class ClubRaceRival
    {
        internal ClubRaceRival(HorseRecord horse, string stableName, int skill) { Horse = horse; StableName = stableName; Skill = skill; }
        public HorseRecord Horse { get; }
        public string StableName { get; }
        public int Skill { get; }
    }

    /// <summary>The committed race: the replayable gallop plus where the player's horse finished and what it won.</summary>
    public sealed class ClubRaceOutcome
    {
        internal ClubRaceOutcome(HorseId entrant, PracticeGallopResult race, int place, long prize) { EntrantId = entrant; Race = race; Place = place; PrizeCoins = prize; }
        public HorseId EntrantId { get; }
        public PracticeGallopResult Race { get; }
        public int Place { get; }
        public long PrizeCoins { get; }
    }

    /// <summary>
    /// D-019 Willowbrook Cup. Once per calendar week the player may enter one adult horse against five visiting rivals
    /// (derived from the save, week and slot; they grow stronger as the weeks pass). The race uses the practice-gallop
    /// physics. The entry fee and any prize are deterministic ledger entries whose IDs come from the save and week, so
    /// a week can hold only one entry and a retry never pays twice. Entering also counts as that day's gallop training.
    /// </summary>
    public static class ClubRace
    {
        public const string Version = "club-race.v1";
        public const string Name = "Willowbrook Cup";
        public const string EntryLedgerKind = "race-entry";
        public const string PrizeLedgerKind = "race-prize";
        public const long EntryFee = 20;
        public const int Rivals = 5;
        private static readonly long[] Prizes = { 160, 90, 45 };
        public static OwnerId RivalOwner { get; } = new OwnerId(Guid.Parse("c1b00000-0000-4000-8000-00000000c0c0"));

        private static readonly string[] RivalNames =
        {
            "Comet", "Starlight", "Bluebell", "Copper", "Misty", "Tumbleweed", "Blossom", "Maverick", "Pepper", "Clover",
            "Ziggy", "Velvet", "Buttons", "Sundance", "Primrose", "Rascal", "Toffee", "Wildflower", "Marble", "Bramblewood",
        };
        private static readonly string[] RivalStables = { "Ashford Farm", "Hollyhock Hill", "Brookside Riders", "Meadowlark Stud", "Fernvale", "Old Mill Stables" };

        public static long PrizeFor(int place) => place >= 1 && place <= Prizes.Length ? Prizes[place - 1] : 0;
        public static bool IsPrizeAmount(long coins) => Prizes.Contains(coins);

        public static TransactionId EntryId(SaveId saveId, int week) => Id("club-race.entry.v1", saveId, week);
        public static TransactionId PrizeId(SaveId saveId, int week) => Id("club-race.prize.v1", saveId, week);
        private static TransactionId Id(string kind, SaveId saveId, int week) => new TransactionId(Guid.ParseExact(
            Canonical.Hash(kind, Check.NotNull(saveId, nameof(saveId)).ToString(), week.ToString(CultureInfo.InvariantCulture)).Substring(0, 32), "N"));

        public static int Week(StableSnapshot state) => HorseFair.Week(Check.NotNull(state, nameof(state)).Header.CalendarDay);
        public static bool EnteredThisWeek(StableSnapshot state) => state.Ledger.Any(l => l.Id.Equals(EntryId(state.Header.SaveId, Week(state))));

        /// <summary>The winnings from this week's cup, if the player has already raced it (0 when it finished unplaced).</summary>
        public static long? PrizeThisWeek(StableSnapshot state)
        {
            if (!EnteredThisWeek(state)) return null;
            return state.Ledger.SingleOrDefault(l => l.Id.Equals(PrizeId(state.Header.SaveId, Week(state))))?.DeltaCoins ?? 0;
        }

        /// <summary>Typical rival training this week: novices early on, seasoned runners later.</summary>
        public static int FieldSkill(int week) => Math.Min(6800, week * 420);

        /// <summary>How well bred the visiting field is (chance of each favourable trait marker): homebred ponies at first, fair-quality later.</summary>
        public static int FieldBreeding(int week) => Math.Min(55, 3 + week * 4);

        public static IReadOnlyList<ClubRaceRival> RivalsThisWeek(StableSnapshot state)
        {
            Check.NotNull(state, nameof(state));
            var week = Week(state);
            var usedCoats = new HashSet<int>(); var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rivals = new List<ClubRaceRival>();
            for (var slot = 0; slot < Rivals; slot++)
            {
                var scope = Canonical.Hash(Version, state.Header.SaveId.ToString(), week.ToString(CultureInfo.InvariantCulture), slot.ToString(CultureInfo.InvariantCulture));
                var horse = HorseFair.Generate(scope, "club-race/rival", RivalOwner, "club-race-rival", usedCoats, usedNames, out _, FieldBreeding(week));
                var rng = new CounterRng(Seed128.Parse(Canonical.Hash(scope, "rival").Substring(0, 32)), "club-race/rival-details");
                string name; var attempts = 0;
                do name = RivalNames[(int)rng.NextBelow(RivalNames.Length)];
                while ((state.Horses.Any(h => h.Name == name) || rivals.Any(r => r.Horse.Name == name)) && ++attempts < 64);
                var skill = Math.Max(0, Math.Min(PracticeTraining.MaxSkill, FieldSkill(week) + (int)rng.NextBelow(1601) - 800));
                rivals.Add(new ClubRaceRival(horse.Rename(name), RivalStables[(int)rng.NextBelow(RivalStables.Length)], skill));
            }
            return rivals;
        }

        /// <summary>Why this horse cannot enter today, or null.</summary>
        public static string? BlockReason(StableSnapshot state, HorseId horseId)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            if (EnteredThisWeek(state)) return "you have already raced in this week's cup.";
            var eligibility = PracticeGallop.Eligibility(state).SingleOrDefault(e => e.HorseId.Equals(horseId));
            if (eligibility == null) return "that horse is not in your stable.";
            if (HorseMarket.IsSold(state.Horses.Single(h => h.HorseId.Equals(horseId)))) return "it lives at " + HorseMarket.BuyerName + " now.";
            if (!eligibility.Eligible) return eligibility.BlockReason;
            if (state.Coins < EntryFee) return $"the entry fee is {EntryFee} coins.";
            return null;
        }

        /// <summary>Runs this week's cup for the horse without saving anything.</summary>
        public static ClubRaceOutcome Preview(StableSnapshot state, HorseId horseId)
        {
            var block = BlockReason(state, horseId);
            if (block != null) throw new StableCommandRejectedException("not_eligible", block);
            var horse = state.Horses.Single(h => h.HorseId.Equals(horseId));
            var rivals = RivalsThisWeek(state);
            var field = new[] { horse }.Concat(rivals.Select(r => r.Horse)).OrderBy(h => h.HorseId.ToString(), StringComparer.Ordinal).ToList();
            var skills = field.Select(h => h.HorseId.Equals(horseId) ? PracticeTraining.Skill(horse) : rivals.Single(r => r.Horse.HorseId.Equals(h.HorseId)).Skill).ToList();
            var seed = Canonical.Hash(Version, state.Header.SaveId.ToString(), Week(state).ToString(CultureInfo.InvariantCulture), horseId.ToString()).Substring(0, 32);
            var race = PracticeGallop.Simulate(field, skills, seed);
            var place = race.Entrants.Single(e => e.HorseId.Equals(horseId)).Place;
            return new ClubRaceOutcome(horseId, race, place, PrizeFor(place));
        }

        internal static (StableSnapshot State, ClubRaceOutcome Outcome) ApplyRace(StableSnapshot state, OwnerId requester, HorseId horseId, ulong expectedSaveRevision)
        {
            if (!state.PlayerOwnerId.Equals(requester)) throw new StableCommandRejectedException("ownership", "Only the stable owner can enter the cup.");
            if (state.Header.SaveRevision != expectedSaveRevision) throw new StableCommandRejectedException("stale_save", "The stable changed; reload before racing.");
            var outcome = Preview(state, horseId);
            var week = Week(state); var day = state.Header.CalendarDay;
            var ledger = new List<CoinLedgerEntry>(state.Ledger) { new CoinLedgerEntry(EntryId(state.Header.SaveId, week), -EntryFee, EntryLedgerKind) };
            if (outcome.PrizeCoins > 0) ledger.Add(new CoinLedgerEntry(PrizeId(state.Header.SaveId, week), outcome.PrizeCoins, PrizeLedgerKind));
            var horses = state.Horses.Select(h => h.HorseId.Equals(horseId) && PracticeTraining.GainToday(h, day) > 0 ? PracticeTraining.Apply(h, day) : h);
            var header = SaveHeaders.Copy(state.Header, revision: checked(state.Header.SaveRevision + 1), schema: StableSnapshot.SchemaVersion, payloadHash: "unwritten");
            return (new StableSnapshot(header, state.PlayerOwnerId, horses, state.OpeningCoinBalance, state.CapacityRevision, ledger,
                state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines), outcome);
        }
    }
}
