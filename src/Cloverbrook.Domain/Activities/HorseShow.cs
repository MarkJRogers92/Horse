using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>The judge's card: each part of a horse's score (total out of 100).</summary>
    public sealed class HorseShowScore
    {
        internal HorseShowScore(int coat, int grooming, int bond, int turnout, int judge)
        { Coat = coat; Grooming = grooming; Bond = bond; Turnout = turnout; Judge = judge; }
        /// <summary>0-40, from the resolved coat.</summary>
        public int Coat { get; }
        /// <summary>0-25, from grooming care.</summary>
        public int Grooming { get; }
        /// <summary>0-15, three points per bond heart.</summary>
        public int Bond { get; }
        /// <summary>0-15, turnout care and tack worn.</summary>
        public int Turnout { get; }
        /// <summary>0-5, the judge's small deterministic whim.</summary>
        public int Judge { get; }
        public int Total => Coat + Grooming + Bond + Turnout + Judge;
    }

    /// <summary>A visiting horse in this week's show. Rivals never join the stable.</summary>
    public sealed class HorseShowRival
    {
        internal HorseShowRival(HorseRecord horse, string stableName, string coatLabel, HorseShowScore score)
        { Horse = horse; StableName = stableName; CoatLabel = coatLabel; Score = score; }
        public HorseRecord Horse { get; }
        public string StableName { get; }
        public string CoatLabel { get; }
        public HorseShowScore Score { get; }
    }

    /// <summary>One line of the results table.</summary>
    public sealed class HorseShowPlacing
    {
        internal HorseShowPlacing(HorseId horseId, string name, bool isPlayer, HorseShowScore score, int place)
        { HorseId = horseId; Name = name; IsPlayer = isPlayer; Score = score; Place = place; }
        public HorseId HorseId { get; }
        public string Name { get; }
        public bool IsPlayer { get; }
        public HorseShowScore Score { get; }
        public int Place { get; }
    }

    public sealed class HorseShowOutcome
    {
        internal HorseShowOutcome(HorseId entrant, HorseShowScore score, IReadOnlyList<HorseShowPlacing> placings, int place, long prize)
        { EntrantId = entrant; Score = score; Placings = placings; Place = place; PrizeCoins = prize; }
        public HorseId EntrantId { get; }
        public HorseShowScore Score { get; }
        /// <summary>Everyone, best first.</summary>
        public IReadOnlyList<HorseShowPlacing> Placings { get; }
        public int Place { get; }
        public long PrizeCoins { get; }
    }

    /// <summary>
    /// D-029 Cloverbrook Coat and Turnout Show. Once per fair week the player may enter one adult horse against five visiting
    /// rivals, judged on coat, grooming, bond and turnout instead of speed. The entry fee and any prize are deterministic ledger
    /// entries whose IDs come from the save and week, so a week holds one entry and a retry never pays twice.
    /// </summary>
    public static class HorseShow
    {
        public const string Version = "horse-show.v1";
        public const string Name = "Coat & Turnout Show";
        public const string EntryLedgerKind = "horse-show-entry";
        public const string PrizeLedgerKind = "horse-show-prize";
        public const long EntryFee = 15;
        public const int Rivals = 5;
        private static readonly long[] Prizes = { 120, 70, 35 };
        public static OwnerId RivalOwner { get; } = new OwnerId(Guid.Parse("c1b00000-0000-4000-8000-00000000d029"));
        private static readonly string[] RivalStables = { "Ashford Farm", "Hollyhock Hill", "Brookside Riders", "Meadowlark Stud", "Fernvale", "Old Mill Stables" };
        private static readonly string[] RivalNames =
        {
            "Dandelion", "Truffle", "Juniper", "Biscuit", "Opal", "Thistle", "Marigold", "Pudding", "Saffron", "Walnut",
            "Moonbeam", "Nutmeg", "Posy", "Ember", "Heather", "Cobweb", "Sorrel", "Periwinkle", "Whisper", "Bracken",
        };

        public static long PrizeFor(int place) => place >= 1 && place <= Prizes.Length ? Prizes[place - 1] : 0;
        public static bool IsPrizeAmount(long coins) => Prizes.Contains(coins);

        public static TransactionId EntryId(SaveId saveId, int week) => Id("horse-show.entry.v1", saveId, week);
        public static TransactionId PrizeId(SaveId saveId, int week) => Id("horse-show.prize.v1", saveId, week);
        private static TransactionId Id(string kind, SaveId saveId, int week) => new TransactionId(Guid.ParseExact(
            Canonical.Hash(kind, Check.NotNull(saveId, nameof(saveId)).ToString(), week.ToString(CultureInfo.InvariantCulture)).Substring(0, 32), "N"));

        public static int Week(StableSnapshot state) => HorseFair.Week(Check.NotNull(state, nameof(state)).Header.CalendarDay);
        public static bool EnteredThisWeek(StableSnapshot state) => state.Ledger.Any(l => l.Id.Equals(EntryId(state.Header.SaveId, Week(state))));

        /// <summary>The winnings from this week's show, if the player has already entered (0 when unplaced).</summary>
        public static long? PrizeThisWeek(StableSnapshot state)
        {
            if (!EnteredThisWeek(state)) return null;
            return state.Ledger.SingleOrDefault(l => l.Id.Equals(PrizeId(state.Header.SaveId, Week(state))))?.DeltaCoins ?? 0;
        }

        /// <summary>Coat points (0-40) for a resolved phenotype; a coat that cannot be resolved still scores the base.</summary>
        public static int CoatScore(Phenotype? p)
        {
            if (p == null) return 12;
            var layers = p.CoatLayers; var score = 12;
            if (layers.Contains("cream_single")) score += 6;
            if (layers.Contains("cream_double")) score += 9;
            if (layers.Contains("dun_dilution")) score += 6;
            else if (layers.Contains("primitive_markings")) score += 3;
            if (layers.Contains("white_tobiano")) score += 10;
            if (layers.Contains("roan")) score += 9;
            if (layers.Contains("leopard_complex")) score += 12 + (p.Patn1Coverage > 0 ? 4 : 0);
            if (layers.Contains("grey_age")) score += 8 + (p.GrayAmount >= 6000 ? 4 : 0);
            return Math.Min(40, score);
        }

        private static int Scale(int level, int max) => Math.Max(0, Math.Min(max, level * max / 10000));
        private static int Judge(SaveId saveId, int week, HorseId horseId) =>
            (int)(Convert.ToUInt32(Canonical.Hash(Version, "judge", saveId.ToString(), week.ToString(CultureInfo.InvariantCulture), horseId.ToString()).Substring(0, 8), 16) % 6);

        /// <summary>The judge's card for any horse, from explicit care levels (0-10000), so rivals and the player share one rule.</summary>
        internal static HorseShowScore Score(StableSnapshot state, HorseRecord horse, int grooming, int turnout, int hearts, bool pad, bool bow)
        {
            var coat = CoatScore(new PhenotypeResolver().ResolveGenetics(horse).Phenotype);
            var turnoutPoints = Scale(turnout, 9) + (pad ? 3 : 0) + (bow ? 3 : 0);
            return new HorseShowScore(coat, Scale(grooming, 25), Math.Min(5, Math.Max(0, hearts)) * 3, Math.Min(15, turnoutPoints), Judge(state.Header.SaveId, Week(state), horse.HorseId));
        }

        /// <summary>The judge's card for one of the player's horses today.</summary>
        public static HorseShowScore ScoreHorse(StableSnapshot state, HorseId horseId)
        {
            Check.NotNull(state, nameof(state));
            var horse = state.Horses.Single(h => h.HorseId.Equals(Check.NotNull(horseId, nameof(horseId))));
            var care = state.DailyState.For(horseId);
            return Score(state, horse, care.Grooming, care.Turnout, HorseBond.Hearts(horse), Tack.Worn(horse, Tack.PadSlot) != null, Tack.Worn(horse, Tack.BowSlot) != null);
        }

        /// <summary>Visiting rivals' care rises gently week by week.</summary>
        public static int FieldGrooming(int week) => Math.Min(7500, 3200 + week * 200);
        public static int FieldTurnout(int week) => Math.Min(7000, 2500 + week * 200);

        public static IReadOnlyList<HorseShowRival> RivalsThisWeek(StableSnapshot state)
        {
            Check.NotNull(state, nameof(state));
            var week = Week(state);
            var usedCoats = new HashSet<int>(); var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rivals = new List<HorseShowRival>();
            for (var slot = 0; slot < Rivals; slot++)
            {
                var scope = Canonical.Hash(Version, state.Header.SaveId.ToString(), week.ToString(CultureInfo.InvariantCulture), slot.ToString(CultureInfo.InvariantCulture));
                var horse = HorseFair.Generate(scope, "horse-show/rival", RivalOwner, "horse-show-rival", usedCoats, usedNames, out _);
                var rng = new CounterRng(Seed128.Parse(Canonical.Hash(scope, "rival").Substring(0, 32)), "horse-show/rival-details");
                string name; var attempts = 0;
                do name = RivalNames[(int)rng.NextBelow(RivalNames.Length)];
                while ((state.Horses.Any(h => h.Name == name) || rivals.Any(r => r.Horse.Name == name)) && ++attempts < 64);
                horse = horse.Rename(name);
                var grooming = FieldGrooming(week) + (int)rng.NextBelow(1801);
                var turnout = FieldTurnout(week) + (int)rng.NextBelow(1801);
                var hearts = 2 + (int)rng.NextBelow(3);
                var pad = rng.NextBelow(100) < 50; var bow = rng.NextBelow(100) < 35;
                var stable = RivalStables[(int)rng.NextBelow(RivalStables.Length)];
                var phenotype = new PhenotypeResolver().ResolveGenetics(horse).Phenotype;
                var label = phenotype != null ? PhenotypeResolver.VisibleLabel(phenotype) : "Mystery coat";
                rivals.Add(new HorseShowRival(horse, stable, label, Score(state, horse, grooming, turnout, hearts, pad, bow)));
            }
            return rivals;
        }

        /// <summary>The player's adult horses that may be shown, whatever the week's state.</summary>
        public static bool IsEligibleHorse(StableSnapshot state, HorseRecord horse) =>
            horse.OwnerId.Equals(state.PlayerOwnerId) && !HorseMarket.IsSold(horse) && horse.LifeState.Stage == LifeStage.Adult;

        /// <summary>Why this horse cannot enter, or null.</summary>
        public static string? BlockReason(StableSnapshot state, HorseId horseId)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            if (EnteredThisWeek(state)) return "you have already entered this week's show.";
            var horse = state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
            if (horse == null || !horse.OwnerId.Equals(state.PlayerOwnerId)) return "that horse is not in your stable.";
            if (HorseMarket.IsSold(horse)) return "it lives at " + HorseMarket.BuyerName + " now.";
            if (horse.LifeState.Stage != LifeStage.Adult) return "only adult horses can be shown.";
            if (state.Coins < EntryFee) return $"the entry fee is {EntryFee} coins.";
            return null;
        }

        /// <summary>Judges this week's show for the horse without saving anything.</summary>
        public static HorseShowOutcome Preview(StableSnapshot state, HorseId horseId)
        {
            var block = BlockReason(state, horseId);
            if (block != null) throw new StableCommandRejectedException("not_eligible", block);
            var horse = state.Horses.Single(h => h.HorseId.Equals(horseId));
            var mine = ScoreHorse(state, horseId);
            var field = new List<(HorseId Id, string Name, bool Player, HorseShowScore Score)> { (horseId, horse.Name, true, mine) };
            field.AddRange(RivalsThisWeek(state).Select(r => (r.Horse.HorseId, r.Horse.Name, false, r.Score)));
            var ranked = field.OrderByDescending(f => f.Score.Total).ThenByDescending(f => f.Score.Judge).ThenBy(f => f.Id.ToString(), StringComparer.Ordinal).ToList();
            var placings = ranked.Select((f, i) => new HorseShowPlacing(f.Id, f.Name, f.Player, f.Score, i + 1)).ToList();
            var place = placings.Single(p => p.IsPlayer).Place;
            return new HorseShowOutcome(horseId, mine, placings, place, PrizeFor(place));
        }

        internal static (StableSnapshot State, HorseShowOutcome Outcome) ApplyEntry(StableSnapshot state, OwnerId requester, HorseId horseId, ulong expectedSaveRevision, long quotedEntry)
        {
            if (!state.PlayerOwnerId.Equals(requester)) throw new StableCommandRejectedException("ownership", "Only the stable owner can enter the show.");
            if (state.Header.SaveRevision != expectedSaveRevision) throw new StableCommandRejectedException("stale_save", "The stable changed; reload before entering.");
            if (quotedEntry != EntryFee) throw new StableCommandRejectedException("price_changed", $"The entry fee is {EntryFee} coins.");
            var outcome = Preview(state, horseId);
            var week = Week(state);
            var ledger = new List<CoinLedgerEntry>(state.Ledger) { new CoinLedgerEntry(EntryId(state.Header.SaveId, week), -EntryFee, EntryLedgerKind) };
            if (outcome.PrizeCoins > 0) ledger.Add(new CoinLedgerEntry(PrizeId(state.Header.SaveId, week), outcome.PrizeCoins, PrizeLedgerKind));
            var header = SaveHeaders.Copy(state.Header, revision: checked(state.Header.SaveRevision + 1), schema: StableSnapshot.SchemaVersion, payloadHash: "unwritten");
            return (new StableSnapshot(header, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance, state.CapacityRevision, ledger,
                state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines), outcome);
        }
    }
}
