using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>One wearable look: a saddle pad or a mane bow in a colour.</summary>
    public sealed class TackItem
    {
        internal TackItem(string slot, string id, string label, string colourHex, Func<StableSnapshot, bool>? unlocked = null, string? unlockHint = null)
        { Slot = slot; Id = id; Label = label; ColourHex = colourHex; this.unlocked = unlocked; UnlockHint = unlockHint; }
        private readonly Func<StableSnapshot, bool>? unlocked;
        public string Slot { get; }
        public string Id { get; }
        public string Label { get; }
        public string ColourHex { get; }
        public string? UnlockHint { get; }
        public bool IsUnlocked(StableSnapshot state) => unlocked?.Invoke(state) ?? true;
    }

    /// <summary>
    /// D-020 tack and dress-up. Purely cosmetic: a horse's saddle pad and mane bow live in its existing
    /// <see cref="HorseRecord.CosmeticState"/>, cost nothing to change, and never touch genetics, care or coins.
    /// Special colours unlock from what happened in the stable (derived from the save, never stored).
    /// </summary>
    public static class Tack
    {
        public const string Version = "tack.v1";
        public const string PadSlot = "pad";
        public const string BowSlot = "bow";
        /// <summary>D-032 festival reward slots.</summary>
        public const string HalterSlot = "halter";
        public const string BraidsSlot = "braids";
        public const string TailSlot = "tail";
        public static IReadOnlyList<string> Slots { get; } = Array.AsReadOnly(new[] { PadSlot, BowSlot, HalterSlot, BraidsSlot, TailSlot });

        private static bool WonCup(StableSnapshot s) => s.Ledger.Any(l => l.Kind == ClubRace.PrizeLedgerKind && l.DeltaCoins == ClubRace.PrizeFor(1));
        private static bool BredFoal(StableSnapshot s) => s.Horses.Any(h => h.OriginCommitId != null && h.BirthDay.HasValue);

        public static IReadOnlyList<TackItem> Items { get; } = Array.AsReadOnly(Colours(PadSlot, "pad").Concat(Colours(BowSlot, "bow")).Concat(new[]
        {
            new TackItem(PadSlot, "pad.gold", "Champion gold pad", "#f2c14e", WonCup, "Win the Willowbrook Cup"),
            new TackItem(BowSlot, "bow.gold", "Champion gold bow", "#f2c14e", WonCup, "Win the Willowbrook Cup"),
            new TackItem(PadSlot, "pad.daisy", "Daisy chain pad", "#fff6d8", BredFoal, "Welcome a foal born in your stable"),
            FestivalReward(BowSlot, "spring", "Flower crown", "#f6b8cf"),
            FestivalReward(TailSlot, "summer", "Sunflower tail bow", "#f5c518"),
            FestivalReward(BraidsSlot, "autumn", "Harvest braids", "#e0762a"),
            FestivalReward(HalterSlot, "winter", "Fairy-light halter", "#fff1c9"),
        }).ToArray());

        private static TackItem FestivalReward(string slot, string season, string label, string hex) =>
            new TackItem(slot, Festivals.RewardItemFor(season), label, hex, s => Festivals.RewardUnlocked(s, season),
                "Give festival treats on " + Festivals.RewardThreshold + " days of the " + Festivals.NameOf(season));

        private static IEnumerable<TackItem> Colours(string slot, string prefix)
        {
            foreach (var (id, label, hex) in new[] { ("rose", "Rose", "#f3a6c0"), ("mint", "Mint", "#a9dcc4"), ("sky", "Sky", "#a8cdf0"),
                         ("lavender", "Lavender", "#c9b3ec"), ("buttercup", "Buttercup", "#f7dc7a"), ("cream", "Cream", "#fbf1df") })
                yield return new TackItem(slot, prefix + "." + id, label + (slot == PadSlot ? " pad" : " bow"), hex);
        }

        public static TackItem? Find(string? id) => id == null ? null : Items.FirstOrDefault(i => i.Id == id);

        /// <summary>The item worn in a slot, if any (unknown or stale IDs read as nothing).</summary>
        public static TackItem? Worn(HorseRecord horse, string slot) =>
            Check.NotNull(horse, nameof(horse)).CosmeticState.TryGetValue(slot, out var id) && Find(id) is { } item && item.Slot == slot ? item : null;

        /// <summary>Canonical cosmetic layer for the visual descriptor ("bow=…;pad=…"), or null when the horse wears nothing.</summary>
        public static string? Layer(HorseRecord horse)
        {
            var parts = Slots.Select(slot => Worn(horse, slot)).Where(i => i != null).Select(i => i!.Slot + "=" + i.Id).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            return parts.Length == 0 ? null : string.Join(";", parts);
        }

        /// <summary>Why this change is not allowed, or null.</summary>
        public static string? BlockReason(StableSnapshot state, HorseId horseId, string slot, string? itemId)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            if (!Slots.Contains(slot)) return "that is not a tack slot.";
            var horse = state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
            if (horse == null || !horse.OwnerId.Equals(state.PlayerOwnerId) || HorseMarket.IsSold(horse)) return "that horse is not in your stable.";
            if (horse.LifeState.Stage == LifeStage.Unborn) return "the foal has not been born yet.";
            if (itemId == null) return null;
            var item = Find(itemId);
            if (item == null || item.Slot != slot) return "that item does not fit there.";
            if (!item.IsUnlocked(state)) return item.UnlockHint + " to unlock it.";
            return null;
        }

        internal static HorseRecord Apply(HorseRecord h, string slot, string? itemId)
        {
            var cosmetics = h.CosmeticState.Where(p => p.Key != slot).ToList();
            if (itemId != null) cosmetics.Add(new KeyValuePair<string, string>(slot, itemId));
            return new HorseRecord(h.HorseId, h.Name, h.Parentage, h.Genome, h.ResolverVersion, h.AppearanceSeed, h.OwnerId, h.LifeState,
                h.ReproductionState, h.DevelopmentalFactors, h.InheritedGamePotential, h.TrainingState, h.PlayerKnowledge, cosmetics,
                h.HomeAssignment, h.OriginRecord, h.OriginCommitId, h.BirthDay, checked(h.Revision + 1));
        }
    }
}
