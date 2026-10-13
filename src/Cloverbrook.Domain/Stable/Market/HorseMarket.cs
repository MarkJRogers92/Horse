using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>A deterministic offer from the club buyer. Reasons explain the price in player terms.</summary>
    public sealed class SaleOffer
    {
        internal SaleOffer(HorseRecord horse, long price, IEnumerable<string> reasons, string? blockReason)
        { HorseId = horse.HorseId; Name = horse.Name; HorseRevision = horse.Revision; PriceCoins = price; Reasons = Snapshot.List(reasons); BlockReason = blockReason; }
        public HorseId HorseId { get; }
        public string Name { get; }
        public ulong HorseRevision { get; }
        public string BuyerName => HorseMarket.BuyerName;
        public long PriceCoins { get; }
        public IReadOnlyList<string> Reasons { get; }
        public string? BlockReason { get; }
        public bool Allowed => BlockReason == null;
    }

    /// <summary>
    /// D-010 club buyer. Players may sell adult horses bred at their own stable; starter horses stay. A sold horse
    /// keeps its canonical record (pedigree and history stay intact) with the club as owner, and the sale is one
    /// deterministic ledger credit. Prices come from age, developed running traits and gallop training.
    /// </summary>
    public static class HorseMarket
    {
        public const string Version = "market.club-buyer.v1";
        public const string BuyerName = "Willowbrook Riding Club";
        public const string SaleLedgerKind = "horse-sale";
        public const long MinimumPrice = 150;
        public const long MaximumPrice = 1200;
        public const long AdultBasePrice = 380;
        public static OwnerId ClubBuyer { get; } = new OwnerId(Guid.Parse("c1b00000-0000-4000-8000-00000000b0b0"));

        public static bool IsSold(HorseRecord horse) => Check.NotNull(horse, nameof(horse)).OwnerId.Equals(ClubBuyer);

        public static TransactionId SaleId(HorseId horseId) => new TransactionId(Guid.ParseExact(
            Canonical.Hash("market.sale.v1", Check.NotNull(horseId, nameof(horseId)).ToString()).Substring(0, 32), "N"));

        public static SaleOffer Quote(StableSnapshot state, HorseId horseId)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            var horse = state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId)) ?? throw new ArgumentException("The horse is not in this stable.", nameof(horseId));
            var reasons = new List<string>();
            var price = Price(horse, reasons);
            return new SaleOffer(horse, price, reasons, BlockReason(state, horse));
        }

        private static long Price(HorseRecord horse, List<string> reasons)
        {
            var traits = new PhenotypeResolver().ResolveGenetics(horse).Traits;
            if (traits == null) { reasons.Add("The buyer cannot assess this horse yet."); return MinimumPrice; }
            long price = AdultBasePrice; reasons.Add($"Healthy adult: {AdultBasePrice} coins");
            var running = new[] { "speed", "acceleration", "endurance", "composure" }.Sum(t => (long)traits.Developed.Get(t, 5000) - 5000) / 4;
            var ability = Math.Max(-150, Math.Min(250, running * 60 / 1000));
            if (ability != 0) reasons.Add(ability > 0 ? $"Promising runner: +{ability}" : $"Steady rather than speedy: {ability}");
            var training = PracticeTraining.Skill(horse) * 250L / 10000;
            if (training > 0) reasons.Add($"Gallop training: +{training}");
            price = Math.Max(MinimumPrice, Math.Min(MaximumPrice, price + ability + training));
            return price;
        }

        private static string? BlockReason(StableSnapshot state, HorseRecord horse)
        {
            if (IsSold(horse)) return "it already lives at " + BuyerName + ".";
            if (!horse.OwnerId.Equals(state.PlayerOwnerId)) return "it belongs to another owner.";
            if (HorseFair.IsPurchased(horse)) return "club rules keep horses bought at the fair with you; you can sell horses you bred.";
            if (horse.OriginCommitId == null) return "club rules keep your starter horses with you; you can sell horses you bred.";
            if (horse.LifeState.Stage != LifeStage.Adult) return "the club buys adult horses only.";
            if (horse.ReproductionState.PregnancyId != null) return "she is in foal.";
            if (horse.ReproductionState.RecoveryUntilDay > state.Header.CalendarDay) return "she is recovering after foaling.";
            return null;
        }

        internal static StableSnapshot ApplySale(StableSnapshot state, OwnerId requester, HorseId horseId, ulong expectedSaveRevision, long quotedPrice)
        {
            if (!state.PlayerOwnerId.Equals(requester)) throw new StableCommandRejectedException("ownership", "Only the stable owner can sell this horse.");
            if (state.Header.SaveRevision != expectedSaveRevision) throw new StableCommandRejectedException("stale_save", "The stable changed; reload before selling.");
            var offer = Quote(state, horseId);
            if (!offer.Allowed) throw new StableCommandRejectedException("not_for_sale", offer.Name + " cannot be sold: " + offer.BlockReason);
            if (offer.PriceCoins != quotedPrice) throw new StableCommandRejectedException("quote_changed", "The offer changed; review it again.");
            var saleId = SaleId(horseId);
            if (state.Ledger.Any(l => l.Id.Equals(saleId))) throw new StableCommandRejectedException("ledger_id_conflict", "This sale identity is already in the wallet history.");
            var horses = state.Horses.Select(h => !h.HorseId.Equals(horseId) ? h : new HorseRecord(h.HorseId, h.Name, h.Parentage, h.Genome,
                h.ResolverVersion, h.AppearanceSeed, ClubBuyer, h.LifeState, h.ReproductionState, h.DevelopmentalFactors, h.InheritedGamePotential,
                h.TrainingState, h.PlayerKnowledge, h.CosmeticState, h.HomeAssignment, h.OriginRecord, h.OriginCommitId, h.BirthDay, checked(h.Revision + 1)));
            var header = SaveHeaders.Copy(state.Header, revision: checked(state.Header.SaveRevision + 1), schema: StableSnapshot.SchemaVersion, payloadHash: "unwritten");
            return new StableSnapshot(header, state.PlayerOwnerId, horses, state.OpeningCoinBalance, state.CapacityRevision,
                state.Ledger.Concat(new[] { new CoinLedgerEntry(saleId, quotedPrice, SaleLedgerKind) }), state.PendingCapacityPlans, state.Conceptions,
                state.DailyState, state.LegacyLifecycleBaselines);
        }
    }
}
