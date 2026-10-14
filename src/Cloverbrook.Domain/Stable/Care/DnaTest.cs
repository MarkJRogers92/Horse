using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>
    /// D-028 DNA tests at the vet. For a fee the vet reveals a horse's true calls at the coat loci this game uses. The
    /// calls are stored in the horse's existing PlayerKnowledge.RevealedCalls (no save schema change), so the breeding
    /// review's foal coat odds narrow on their own. One ledger debit per horse, with an ID derived from the horse.
    /// </summary>
    public static class DnaTest
    {
        public const string Version = "dna-test.v1";
        public const string LedgerKind = "dna-test";
        public const long Price = 60;
        /// <summary>Every locus a new test reveals (D-037 adds silver, champagne and splash white).</summary>
        public static IReadOnlyList<string> Loci { get; } = Array.AsReadOnly(new[]
            { "mc1r", "asip", "slc45a2", "tbx3", "stx17", "trpm1", "patn1", "pattern.eca3", "pmel", "slc36a1", "pattern.mitf" });
        // Horses tested before D-037 revealed only these eight; they still count as tested so saves validate unchanged.
        private static readonly string[] LegacyLoci = { "mc1r", "asip", "slc45a2", "tbx3", "stx17", "trpm1", "patn1", "pattern.eca3" };

        public static TransactionId TestId(HorseId horseId) => new TransactionId(Guid.ParseExact(
            Canonical.Hash("vet.dna-test.v1", Check.NotNull(horseId, nameof(horseId)).ToString()).Substring(0, 32), "N"));

        /// <summary>True when every test locus the horse's genome has carries a revealed call.</summary>
        public static bool IsTested(HorseRecord horse) => LegacyLoci.Where(Check.NotNull(horse, nameof(horse)).Genome.HasLocus)
            .All(l => horse.PlayerKnowledge.RevealedCalls.ContainsKey(l)) && horse.PlayerKnowledge.RevealedCalls.Count > 0;

        /// <summary>How many of the player's own horses have been tested.</summary>
        public static int TestedCount(StableSnapshot state) => Check.NotNull(state, nameof(state)).Horses
            .Count(h => h.OwnerId.Equals(state.PlayerOwnerId) && !HorseMarket.IsSold(h) && IsTested(h));

        private static bool SameCall(AllelePair a, AllelePair b) => (a.A == b.A && a.B == b.B) || (a.A == b.B && a.B == b.A);

        // A call at a locus the genome does not have is not contradictory (older tests and saves used free-form keys); only a
        // call that disagrees with a locus the genome does have is forged.
        private static bool CallsMatchGenome(HorseRecord horse) => horse.PlayerKnowledge.RevealedCalls.All(p =>
            !horse.Genome.HasLocus(p.Key) || SameCall(horse.Genome.GetAlleles(p.Key), p.Value));

        /// <summary>Why this horse cannot be tested now, or null.</summary>
        public static string? BlockReason(StableSnapshot state, HorseId horseId)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            var horse = state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
            if (horse == null || !horse.OwnerId.Equals(state.PlayerOwnerId) || HorseMarket.IsSold(horse)) return "that horse is not in your stable.";
            if (horse.LifeState.Stage == LifeStage.Unborn) return "the foal has not been born yet.";
            if (IsTested(horse)) return horse.Name + " has already had a DNA test.";
            if (state.Coins < Price) return $"a DNA test costs {Price} coins.";
            return null;
        }

        internal static StableSnapshot Apply(StableSnapshot state, OwnerId requester, HorseId horseId, ulong expectedRevision, long quotedPrice)
        {
            if (!state.PlayerOwnerId.Equals(requester)) throw new StableCommandRejectedException("ownership", "Only the stable owner can book the vet.");
            if (state.Header.SaveRevision != expectedRevision) throw new StableCommandRejectedException("stale_save", "The stable changed; reload first.");
            var block = BlockReason(state, horseId);
            if (block != null) throw new StableCommandRejectedException("not_available", "That test cannot be done: " + block);
            if (quotedPrice != Price) throw new StableCommandRejectedException("quote_changed", "The price changed; look again.");
            var horses = state.Horses.Select(h => h.HorseId.Equals(horseId) ? Reveal(h) : h);
            var header = SaveHeaders.Copy(state.Header, revision: checked(state.Header.SaveRevision + 1), schema: StableSnapshot.SchemaVersion, payloadHash: "unwritten");
            return new StableSnapshot(header, state.PlayerOwnerId, horses, state.OpeningCoinBalance, state.CapacityRevision,
                state.Ledger.Concat(new[] { new CoinLedgerEntry(TestId(horseId), -Price, LedgerKind) }), state.PendingCapacityPlans, state.Conceptions,
                state.DailyState, state.LegacyLifecycleBaselines);
        }

        private static HorseRecord Reveal(HorseRecord horse)
        {
            var calls = horse.PlayerKnowledge.RevealedCalls.Where(p => !Loci.Contains(p.Key))
                .Concat(Loci.Where(horse.Genome.HasLocus).Select(l => new KeyValuePair<string, AllelePair>(l, horse.Genome.GetAlleles(l))));
            return new HorseRecord(horse.HorseId, horse.Name, horse.Parentage, horse.Genome, horse.ResolverVersion, horse.AppearanceSeed, horse.OwnerId,
                horse.LifeState, horse.ReproductionState, horse.DevelopmentalFactors, horse.InheritedGamePotential, horse.TrainingState,
                new PlayerKnowledgeRecord(calls, horse.PlayerKnowledge.KnownAncestors), horse.CosmeticState, horse.HomeAssignment, horse.OriginRecord,
                horse.OriginCommitId, horse.BirthDay, checked(horse.Revision + 1));
        }

        /// <summary>Save validation: every DNA debit is exactly the price for a tested horse; no revealed call contradicts the genome.</summary>
        internal static string? LedgerProblem(StableSnapshot state)
        {
            foreach (var horse in state.Horses)
                if (!CallsMatchGenome(horse)) return "A horse's revealed DNA calls do not match its genome.";
            foreach (var entry in state.Ledger.Where(l => l.Kind == LedgerKind))
                if (entry.BreedingId != null || entry.DeltaCoins != -Price
                    || !state.Horses.Any(h => TestId(h.HorseId).Equals(entry.Id) && IsTested(h)))
                    return "A DNA test debit does not match a tested horse at the vet's price.";
            return null;
        }

        /// <summary>Short, warm player text for one revealed call.</summary>
        public static string Describe(string locus, AllelePair call)
        {
            Check.NotNull(locus, nameof(locus)); Check.NotNull(call, nameof(call));
            string pair = call.A + "/" + call.B;
            switch (locus)
            {
                case "mc1r":
                    return call.Count("e") == 2 ? $"Red factor ({pair}): chestnut, every foal carries red"
                        : call.Count("e") == 1 ? $"Red factor ({pair}): carries red — can have chestnut foals"
                        : $"Red factor ({pair}): no red to pass on";
                case "asip":
                    return call.Count("a") == 2 ? $"Agouti ({pair}): black-based" : call.Count("a") == 1 ? $"Agouti ({pair}): carries black" : $"Agouti ({pair})";
                case "slc45a2":
                    if (call.Count("Prl") == 2) return $"Pearl ({pair}): two pearl copies — pale apricot, every foal carries pearl";
                    if (call.Count("Prl") == 1 && call.Count("Cr") == 1) return $"Cream and pearl ({pair}): the pale pseudo-double-dilute";
                    if (call.Count("Prl") == 1) return $"Pearl ({pair}): carries one pearl copy — it does not show";
                    return call.Count("Cr") == 2 ? $"Cream ({pair}): two cream copies — every foal gets cream"
                        : call.Count("Cr") == 1 ? $"Cream ({pair}): one cream copy" : $"Cream ({pair}): no cream";
                case "tbx3":
                    return call.Count("D") == 2 ? $"Dun ({pair}): two dun copies — every foal is dun"
                        : call.Count("D") == 1 ? $"Dun ({pair}): one dun copy" : $"Dun ({pair})";
                case "stx17":
                    return call.Count("G") == 2 ? $"Grey ({pair}): two grey copies — every foal turns grey"
                        : call.Count("G") == 1 ? $"Grey ({pair}): one grey copy — half its foals turn grey" : $"Grey ({pair}): will not turn grey";
                case "trpm1":
                    return call.Count("LP") == 2 ? $"Leopard complex ({pair}): two copies — every foal carries it"
                        : call.Count("LP") == 1 ? $"Leopard complex ({pair}): one copy" : $"Leopard complex ({pair})";
                case "patn1":
                    return call.Contains("PATN1") ? $"PATN1 ({pair}): boosts leopard spotting" : $"PATN1 ({pair})";
                case "pmel":
                    return call.Count("Z") == 2 ? $"Silver ({pair}): two silver copies — every foal is silver (it shows on black and bay)"
                        : call.Count("Z") == 1 ? $"Silver ({pair}): one silver copy (it only shows on black and bay)" : $"Silver ({pair}): no silver";
                case "slc36a1":
                    return call.Count("Ch") == 2 ? $"Champagne ({pair}): two champagne copies — every foal is champagne"
                        : call.Count("Ch") == 1 ? $"Champagne ({pair}): one champagne copy" : $"Champagne ({pair}): no champagne";
                case "pattern.mitf":
                    return call.Count("SW1") == 2 ? $"Splash white ({pair}): two splash copies — every foal is splashed"
                        : call.Count("SW1") == 1 ? $"Splash white ({pair}): one splash copy" : $"Splash white ({pair}): no splash";
                case "pattern.eca3":
                    if (call.Count("SB1") == 2) return $"KIT region ({pair}): two sabino copies — mostly white, every foal is sabino";
                    if (call.Count("SB1") == 1) return $"KIT region ({pair}): one sabino copy";
                    if (call.Count("TO") == 2) return $"KIT region ({pair}): two tobiano copies — every foal is tobiano";
                    if (call.Count("TO") == 1 && call.Count("Rn") == 1) return $"KIT region ({pair}): one tobiano and one roan copy";
                    if (call.Count("TO") == 1) return $"KIT region ({pair}): one tobiano copy";
                    if (call.Count("Rn") == 2) return $"KIT region ({pair}): two roan copies — every foal is roan";
                    if (call.Count("Rn") == 1) return $"KIT region ({pair}): one roan copy";
                    return $"KIT region ({pair}): no pattern";
                default:
                    return $"{locus} ({pair})";
            }
        }
    }
}
