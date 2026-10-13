using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>A decoration the player can buy once and place in the yard.</summary>
    public sealed class DecorItem
    {
        internal DecorItem(string id, string label, string asset, long price) { Id = id; Label = label; Asset = asset; PriceCoins = price; }
        public string Id { get; }
        public string Label { get; }
        /// <summary>The scenery asset folder the yard draws for this item.</summary>
        public string Asset { get; }
        public long PriceCoins { get; }
    }

    /// <summary>
    /// D-022 stable shop. Extra stalls raise the stable's capacity (used by breeding, foaling and the fair) and
    /// decorations are owned once each. Every purchase is one ledger debit whose ID comes from the save and the item,
    /// so ownership is derived from the wallet history and the save needs no new fields. Where a decoration stands is a
    /// display preference kept beside the save.
    /// </summary>
    public static class StableShop
    {
        public const string Version = "stable-shop.v1";
        public const string StallLedgerKind = "stable-upgrade";
        public const string DecorLedgerKind = "decor-purchase";
        public const int BaseStalls = 4;
        private static readonly long[] StallPrices = { 450, 800 };
        public static int MaxStalls => BaseStalls + StallPrices.Length;

        public static IReadOnlyList<DecorItem> Decor { get; } = Array.AsReadOnly(new[]
        {
            new DecorItem("planter", "Flower planter", "planter_box", 60),
            new DecorItem("hay", "Hay bales", "hay_props", 40),
            new DecorItem("bench", "Garden bench", "bench", 90),
            new DecorItem("wheelbarrow", "Wheelbarrow", "wheelbarrow", 70),
            new DecorItem("saddle_rack", "Saddle rack", "saddle_rack", 80),
            new DecorItem("trough", "Water trough", "water_trough", 60),
            new DecorItem("flower_cart", "Flower cart", "flower_cart", 150),
            new DecorItem("lamp_post", "Lantern post", "lamp_post", 110),
            new DecorItem("birdhouse", "Birdhouse", "birdhouse", 90),
            new DecorItem("picnic_table", "Picnic table", "picnic_table", 130),
            new DecorItem("rose_arch", "Rose arch", "rose_arch", 180),
        });

        public static TransactionId StallId(SaveId saveId, int number) => Id("stable-shop.stall.v1", saveId, number.ToString(CultureInfo.InvariantCulture));
        public static TransactionId DecorId(SaveId saveId, string itemId) => Id("stable-shop.decor.v1", saveId, itemId);
        private static TransactionId Id(string kind, SaveId saveId, string part) => new TransactionId(Guid.ParseExact(
            Canonical.Hash(kind, Check.NotNull(saveId, nameof(saveId)).ToString(), part).Substring(0, 32), "N"));

        public static int StallsBought(StableSnapshot state) =>
            Enumerable.Range(1, StallPrices.Length).TakeWhile(n => state.Ledger.Any(l => l.Id.Equals(StallId(state.Header.SaveId, n)))).Count();

        /// <summary>Home places at this stable: the four originals plus any built.</summary>
        public static int Stalls(StableSnapshot state) => BaseStalls + StallsBought(Check.NotNull(state, nameof(state)));

        /// <summary>The next stall's price, or null when every stall is built.</summary>
        public static long? NextStallPrice(StableSnapshot state) { var n = StallsBought(state); return n < StallPrices.Length ? StallPrices[n] : (long?)null; }

        public static bool Owns(StableSnapshot state, string itemId) => state.Ledger.Any(l => l.Id.Equals(DecorId(state.Header.SaveId, itemId)));
        public static IReadOnlyList<DecorItem> OwnedDecor(StableSnapshot state) => Decor.Where(d => Owns(state, d.Id)).ToArray();

        public static string? StallBlockReason(StableSnapshot state)
        {
            var price = NextStallPrice(Check.NotNull(state, nameof(state)));
            if (price == null) return "every stall has been built.";
            if (state.Coins < price) return $"the builder needs {price} coins.";
            return null;
        }

        public static string? DecorBlockReason(StableSnapshot state, string itemId)
        {
            Check.NotNull(state, nameof(state));
            var item = Decor.SingleOrDefault(d => d.Id == itemId);
            if (item == null) return "that is not in the shop.";
            if (Owns(state, itemId)) return "you already have it.";
            if (state.Coins < item.PriceCoins) return $"it costs {item.PriceCoins} coins.";
            return null;
        }

        internal static StableSnapshot ApplyStall(StableSnapshot state, OwnerId requester, ulong expectedRevision, long quotedPrice)
        {
            Guard(state, requester, expectedRevision);
            var block = StallBlockReason(state);
            if (block != null) throw new StableCommandRejectedException("not_available", "A new stall cannot be built: " + block);
            if (NextStallPrice(state) != quotedPrice) throw new StableCommandRejectedException("quote_changed", "The price changed; look again.");
            return WithDebit(state, new CoinLedgerEntry(StallId(state.Header.SaveId, StallsBought(state) + 1), -quotedPrice, StallLedgerKind));
        }

        internal static StableSnapshot ApplyDecor(StableSnapshot state, OwnerId requester, string itemId, ulong expectedRevision)
        {
            Guard(state, requester, expectedRevision);
            var block = DecorBlockReason(state, itemId);
            if (block != null) throw new StableCommandRejectedException("not_available", "That cannot be bought: " + block);
            var item = Decor.Single(d => d.Id == itemId);
            return WithDebit(state, new CoinLedgerEntry(DecorId(state.Header.SaveId, itemId), -item.PriceCoins, DecorLedgerKind));
        }

        private static void Guard(StableSnapshot state, OwnerId requester, ulong expectedRevision)
        {
            if (!state.PlayerOwnerId.Equals(requester)) throw new StableCommandRejectedException("ownership", "Only the stable owner can buy for the stable.");
            if (state.Header.SaveRevision != expectedRevision) throw new StableCommandRejectedException("stale_save", "The stable changed; reload first.");
        }

        private static StableSnapshot WithDebit(StableSnapshot state, CoinLedgerEntry debit)
        {
            var header = SaveHeaders.Copy(state.Header, revision: checked(state.Header.SaveRevision + 1), schema: StableSnapshot.SchemaVersion, payloadHash: "unwritten");
            return new StableSnapshot(header, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance, state.CapacityRevision,
                state.Ledger.Concat(new[] { debit }), state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
        }

        /// <summary>Save validation: every shop debit must be a known item at its price, and stalls are built in order.</summary>
        internal static string? LedgerProblem(StableSnapshot state, IReadOnlyDictionary<TransactionId, CoinLedgerEntry> ledger)
        {
            var save = state.Header.SaveId;
            foreach (var entry in state.Ledger.Where(l => l.Kind == StallLedgerKind))
            {
                var n = Enumerable.Range(1, StallPrices.Length).FirstOrDefault(i => StallId(save, i).Equals(entry.Id));
                if (n == 0 || entry.BreedingId != null || entry.DeltaCoins != -StallPrices[n - 1]) return "A stall purchase does not match the builder's price list.";
                if (n > 1 && !ledger.ContainsKey(StallId(save, n - 1))) return "A stall was bought before the one it extends.";
            }
            foreach (var entry in state.Ledger.Where(l => l.Kind == DecorLedgerKind))
            {
                var item = Decor.FirstOrDefault(d => DecorId(save, d.Id).Equals(entry.Id));
                if (item == null || entry.BreedingId != null || entry.DeltaCoins != -item.PriceCoins) return "A decoration purchase does not match the shop.";
            }
            return null;
        }
    }
}
