using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>One horse for sale at this week's fair. The record is complete, so buying adds exactly this horse.</summary>
    public sealed class FairOffer
    {
        internal FairOffer(int slot, HorseRecord horse, string coatClass, long price, IEnumerable<string> reasons, bool bought)
        { Slot = slot; Horse = horse; CoatClass = coatClass; PriceCoins = price; Reasons = Snapshot.List(reasons); AlreadyBought = bought; }
        public int Slot { get; }
        public HorseRecord Horse { get; }
        public HorseId HorseId => Horse.HorseId;
        public string Name => Horse.Name;
        public string CoatClass { get; }
        public long PriceCoins { get; }
        public IReadOnlyList<string> Reasons { get; }
        public bool AlreadyBought { get; }
    }

    /// <summary>
    /// D-017 weekly horse fair. Each calendar week offers three adult horses with a released phase-1 coat. Offers are
    /// derived only from the save identity, the week and the slot, so reloading never rerolls them. A bought horse is
    /// one deterministic ledger debit plus one founder record; it cannot be resold to the club (D-010 sells bred horses).
    /// </summary>
    public static class HorseFair
    {
        public const string Version = "horse-fair.v1";
        public const string PurchaseLedgerKind = "horse-purchase";
        public const string OriginRecord = "horse-fair-purchase";
        public const int OffersPerWeek = 3;
        public const int DaysPerWeek = 7;
        public const long MinimumPrice = 220;
        public const long MaximumPrice = 900;
        public const long BasePrice = 360;

        /// <summary>Released phase-1 genotypes (mc1r, asip, slc45a2 pair, tbx3 pair). D-013 released exactly these classes.</summary>
        private static readonly (string Coat, string Mc1r, string Asip, string CreamA, string CreamB, string DunA, string DunB)[] Coats =
        {
            ("chestnut", "e", "A", "N", "N", "nd2", "nd2"), ("bay", "E", "A", "N", "N", "nd2", "nd2"), ("black", "E", "a", "N", "N", "nd2", "nd2"),
            ("palomino", "e", "A", "N", "Cr", "nd2", "nd2"), ("buckskin", "E", "A", "N", "Cr", "nd2", "nd2"), ("smoky_black", "E", "a", "N", "Cr", "nd2", "nd2"),
            ("cremello", "e", "A", "Cr", "Cr", "nd2", "nd2"), ("perlino", "E", "A", "Cr", "Cr", "nd2", "nd2"), ("smoky_cream", "E", "a", "Cr", "Cr", "nd2", "nd2"),
            ("red_dun", "e", "A", "N", "N", "D", "nd2"), ("bay_dun", "E", "A", "N", "N", "D", "nd2"), ("grullo", "E", "a", "N", "N", "D", "nd2"),
            ("non_dun1_marked", "E", "A", "N", "N", "nd1", "nd2"),
        };

        internal static readonly string[] Names =
        {
            "Bramble", "Clementine", "Dandelion", "Ember", "Fennel", "Hazel", "Indigo", "Jasper", "Lark", "Marigold", "Nutmeg",
            "Oakley", "Pippin", "Quill", "Rosehip", "Saffron", "Thistle", "Willow", "Biscuit", "Cinder", "Duchess", "Fable",
            "Gingersnap", "Honeybee", "Juniper Moon", "Kestrel", "Lavender", "Moss", "Nightingale", "Poppy", "Rowan", "Sorrel",
        };

        public static int Week(int calendarDay) => Math.Max(0, calendarDay) / DaysPerWeek;
        public static int DaysUntilNextFair(int calendarDay) => DaysPerWeek - Math.Max(0, calendarDay) % DaysPerWeek;

        public static TransactionId PurchaseId(HorseId horseId) => new TransactionId(Guid.ParseExact(
            Canonical.Hash("market.purchase.v1", Check.NotNull(horseId, nameof(horseId)).ToString()).Substring(0, 32), "N"));

        public static bool IsPurchased(HorseRecord horse) => Check.NotNull(horse, nameof(horse)).OriginRecord == OriginRecord;

        /// <summary>This week's offers. Bought offers stay listed (marked) so the fair does not refill until next week.</summary>
        public static IReadOnlyList<FairOffer> Offers(StableSnapshot state)
        {
            Check.NotNull(state, nameof(state));
            var week = Week(state.Header.CalendarDay);
            var owned = state.Horses.Select(h => h.HorseId).ToHashSet();
            var usedCoats = new HashSet<int>(); var usedNames = new HashSet<string>(state.Horses.Select(h => h.Name), StringComparer.OrdinalIgnoreCase);
            var offers = new List<FairOffer>();
            for (var slot = 0; slot < OffersPerWeek; slot++)
            {
                var horse = Create(state, week, slot, usedCoats, usedNames, out var coat);
                var reasons = new List<string>();
                offers.Add(new FairOffer(slot, horse, coat, Price(horse, coat, reasons), reasons, owned.Contains(horse.HorseId)));
            }
            return offers;
        }

        /// <summary>Why this offer cannot be bought right now, or null.</summary>
        public static string? BlockReason(StableSnapshot state, FairOffer offer)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(offer, nameof(offer));
            if (offer.AlreadyBought) return offer.Name + " already lives at your stable.";
            var used = state.Horses.Count(h => !HorseMarket.IsSold(h)) + state.PendingCapacityPlans.Count;
            if (used >= StableBreedingPlanner.CapacitySlots) return "every stall is taken; sell a horse or wait for space.";
            if (state.Coins < offer.PriceCoins) return "you need " + offer.PriceCoins.ToString(CultureInfo.InvariantCulture) + " coins.";
            return null;
        }

        private static HorseRecord Create(StableSnapshot state, int week, int slot, HashSet<int> usedCoats, HashSet<string> usedNames, out string coatClass)
        {
            var scope = Canonical.Hash(Version, state.Header.SaveId.ToString(), week.ToString(CultureInfo.InvariantCulture), slot.ToString(CultureInfo.InvariantCulture));
            return Generate(scope, "fair/offer", state.PlayerOwnerId, OriginRecord, usedCoats, usedNames, out coatClass);
        }

        /// <summary>A complete adult founder with a released phase-1 coat, derived only from <paramref name="scope"/> (also used for D-019 cup rivals).</summary>
        internal static HorseRecord Generate(string scope, string purpose, OwnerId owner, string originRecord, HashSet<int> usedCoats, HashSet<string> usedNames, out string coatClass,
            int markerOnePercent = 50)
        {
            var seed = Seed128.Parse(scope.Substring(0, 32));
            var rng = new CounterRng(seed, purpose);
            int coatIndex; do coatIndex = (int)rng.NextBelow(Coats.Length); while (!usedCoats.Add(coatIndex));
            var coat = Coats[coatIndex]; coatClass = coat.Coat;
            string name; var attempts = 0;
            do name = Names[(int)rng.NextBelow(Names.Length)]; while (!usedNames.Add(name) && ++attempts < 64);
            var genome = new GenomeSnapshot(CoreGeneticsCatalog.Model.Blocks.Values.Select(block =>
            {
                var a = new List<string>(); var b = new List<string>();
                foreach (var locus in block.Loci)
                {
                    var (x, y) = Alleles(locus.LocusId, locus.Variant.SupportedAlleleIds[0], coat, block.IsSynthetic, rng, markerOnePercent);
                    a.Add(x); b.Add(y);
                }
                return new PhasedBlockPair(block.BlockId, block.IsSynthetic, block.Loci.Select(l => l.LocusId), a, b);
            }), CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.SyntheticVersion);
            var development = GameTraitResolver.CreateDevelopment(Seed128.Parse(Canonical.Hash(scope, "development").Substring(0, 32)));
            var traits = GameTraitResolver.Resolve(genome, development);
            var role = rng.NextBelow(2) == 0 ? ReproductiveRole.Mare : ReproductiveRole.Stallion;
            var id = new HorseId(Guid.ParseExact(Canonical.Hash(scope, "horse").Substring(0, 32), "N"));
            var age = 10 + (int)rng.NextBelow(10);
            return new HorseRecord(id, name, new Parentage(), genome, CoreGeneticsCatalog.ResolverVersion,
                Seed128.Parse(Canonical.Hash(scope, "appearance").Substring(0, 32)), owner,
                new LifeState(LifeStage.Adult, age, VisualAge.Adult), new ReproductionState(role), development, traits.Inherited,
                homeAssignment: HomeKind.ActiveStable, originRecord: originRecord);
        }

        private static (string, string) Alleles(string locus, string fallback,
            (string Coat, string Mc1r, string Asip, string CreamA, string CreamB, string DunA, string DunB) coat, bool synthetic, CounterRng rng,
            int markerOnePercent)
        {
            switch (locus)
            {
                case "mc1r": return coat.Mc1r == "e" ? ("e", "e") : rng.NextBelow(2) == 0 ? ("E", "E") : ("E", "e");
                case "asip": return coat.Asip == "a" ? ("a", "a") : rng.NextBelow(2) == 0 ? ("A", "A") : ("A", "a");
                case "slc45a2": return (coat.CreamA, coat.CreamB);
                case "tbx3": return (coat.DunA, coat.DunB);
            }
            // Fictional trait markers vary so fair horses differ in ability; every other real locus stays at its baseline.
            if (synthetic && markerOnePercent == 50) return (rng.NextBelow(2) == 0 ? "0" : "1", rng.NextBelow(2) == 0 ? "0" : "1");
            if (synthetic) return (rng.NextBelow(100) < (ulong)markerOnePercent ? "1" : "0", rng.NextBelow(100) < (ulong)markerOnePercent ? "1" : "0");
            return (fallback, fallback);
        }

        private static long Price(HorseRecord horse, string coat, List<string> reasons)
        {
            long price = BasePrice; reasons.Add($"Healthy adult: {BasePrice} coins");
            var developed = GameTraitResolver.Resolve(horse.Genome, horse.DevelopmentalFactors).Developed;
            var running = new[] { "speed", "acceleration", "endurance", "composure" }.Sum(t => (long)developed.Get(t, 5000) - 5000) / 4;
            var ability = Math.Max(-120, Math.Min(260, running * 60 / 1000));
            if (ability != 0) reasons.Add(ability > 0 ? $"Promising runner: +{ability}" : $"Steady rather than speedy: {ability}");
            long rarity = coat switch
            {
                "cremello" or "perlino" or "smoky_cream" => 120,
                "palomino" or "buckskin" or "smoky_black" or "grullo" or "red_dun" => 70,
                _ => 0,
            };
            if (rarity != 0) reasons.Add($"Sought-after coat: +{rarity}");
            return Math.Max(MinimumPrice, Math.Min(MaximumPrice, price + ability + rarity));
        }

        internal static StableSnapshot ApplyPurchase(StableSnapshot state, OwnerId requester, HorseId horseId, ulong expectedSaveRevision, long quotedPrice)
        {
            if (!state.PlayerOwnerId.Equals(requester)) throw new StableCommandRejectedException("ownership", "Only the stable owner can buy horses.");
            if (state.Header.SaveRevision != expectedSaveRevision) throw new StableCommandRejectedException("stale_save", "The stable changed; reload before buying.");
            var offer = Offers(state).SingleOrDefault(o => o.HorseId.Equals(horseId))
                ?? throw new StableCommandRejectedException("offer_gone", "That horse is no longer at the fair.");
            var block = BlockReason(state, offer);
            if (block != null) throw new StableCommandRejectedException("not_available", offer.Name + " cannot be bought: " + block);
            if (offer.PriceCoins != quotedPrice) throw new StableCommandRejectedException("quote_changed", "The price changed; review it again.");
            var purchaseId = PurchaseId(horseId);
            if (state.Ledger.Any(l => l.Id.Equals(purchaseId))) throw new StableCommandRejectedException("ledger_id_conflict", "This purchase identity is already in the wallet history.");
            var header = SaveHeaders.Copy(state.Header, revision: checked(state.Header.SaveRevision + 1), schema: StableSnapshot.SchemaVersion, payloadHash: "unwritten");
            return new StableSnapshot(header, state.PlayerOwnerId, state.Horses.Concat(new[] { offer.Horse }), state.OpeningCoinBalance, state.CapacityRevision,
                state.Ledger.Concat(new[] { new CoinLedgerEntry(purchaseId, -quotedPrice, PurchaseLedgerKind) }), state.PendingCapacityPlans, state.Conceptions,
                state.DailyState, state.LegacyLifecycleBaselines);
        }
    }
}
