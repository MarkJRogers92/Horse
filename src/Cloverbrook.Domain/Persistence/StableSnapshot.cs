using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class SaveIntegrityException : IOException
    {
        public SaveIntegrityException(string message) : base(message) { }
        public SaveIntegrityException(string message, Exception inner) : base(message, inner) { }
    }
    public sealed class SaveCompatibilityException : Exception
    {
        public SaveCompatibilityException(string message) : base(message) { }
    }

    public sealed class CoinLedgerEntry
    {
        public CoinLedgerEntry(TransactionId id, long deltaCoins, string kind, BreedingCommitId? breedingId = null)
        {
            Id = Check.NotNull(id, nameof(id)); DeltaCoins = deltaCoins; Kind = Check.Text(kind, nameof(kind)); BreedingId = breedingId;
            if ((kind == "breeding") != (breedingId != null) || (kind == "breeding" && deltaCoins > 0)) throw new ArgumentException("A breeding debit must identify its event; other entry kinds cannot impersonate breeding.");
        }
        public TransactionId Id { get; }
        public long DeltaCoins { get; }
        public string Kind { get; }
        public BreedingCommitId? BreedingId { get; }
    }

    // One persistent root. This contains the existing canonical domain records,
    // not a second horse/genetics model. Confirmed capacity plans come from a
    // trusted adapter; this slice is not a building/calendar capacity manager.
    public sealed class StableSnapshot
    {
        public const string SchemaVersion = "cloverbrook.save.v2";
        public StableSnapshot(SaveHeader header, OwnerId playerOwnerId, IEnumerable<HorseRecord> horses,
            long openingCoinBalance, ulong capacityRevision, IEnumerable<CoinLedgerEntry>? ledger = null,
            IEnumerable<BreedingCapacityPlan>? pendingCapacityPlans = null, IEnumerable<PreparedConception>? conceptions = null,
            StableDailyState? dailyState = null)
        {
            Header = Check.NotNull(header, nameof(header)); PlayerOwnerId = Check.NotNull(playerOwnerId, nameof(playerOwnerId));
            Horses = Snapshot.List(Check.NotNull(horses, nameof(horses)).OrderBy(h => h.HorseId.ToString(), StringComparer.Ordinal));
            if (openingCoinBalance < 0) throw new ArgumentOutOfRangeException(nameof(openingCoinBalance));
            OpeningCoinBalance = openingCoinBalance; CapacityRevision = capacityRevision;
            Ledger = Snapshot.List((ledger ?? Array.Empty<CoinLedgerEntry>()).OrderBy(l => l.Id.ToString(), StringComparer.Ordinal));
            PendingCapacityPlans = Snapshot.List((pendingCapacityPlans ?? Array.Empty<BreedingCapacityPlan>()).OrderBy(p => p.ReservationId.ToString(), StringComparer.Ordinal));
            Conceptions = Snapshot.List((conceptions ?? Array.Empty<PreparedConception>()).OrderBy(c => c.Commit.BreedingEventIndex));
            DailyState = dailyState ?? StableDailyState.Empty;
            try { Coins = Ledger.Aggregate(openingCoinBalance, (coins, entry) => checked(coins + entry.DeltaCoins)); }
            catch (OverflowException e) { throw new SaveIntegrityException("The coin ledger overflows the supported integer balance.", e); }
            Validate();
        }
        public SaveHeader Header { get; }
        public OwnerId PlayerOwnerId { get; }
        public IReadOnlyList<HorseRecord> Horses { get; }
        public long OpeningCoinBalance { get; }
        public long Coins { get; }
        public ulong CapacityRevision { get; }
        public IReadOnlyList<CoinLedgerEntry> Ledger { get; }
        public IReadOnlyList<BreedingCapacityPlan> PendingCapacityPlans { get; }
        public IReadOnlyList<PreparedConception> Conceptions { get; }
        public StableDailyState DailyState { get; }
        public StableSnapshot WithHeader(SaveHeader header) => new StableSnapshot(header, PlayerOwnerId, Horses, OpeningCoinBalance, CapacityRevision, Ledger, PendingCapacityPlans, Conceptions, DailyState);
        public StableSnapshot WithHorses(IEnumerable<HorseRecord> horses) => new StableSnapshot(Header, PlayerOwnerId, horses, OpeningCoinBalance, CapacityRevision, Ledger, PendingCapacityPlans, Conceptions, DailyState);
        public StableSnapshot WithDailyState(StableDailyState dailyState) => new StableSnapshot(Header, PlayerOwnerId, Horses, OpeningCoinBalance, CapacityRevision, Ledger, PendingCapacityPlans, Conceptions, dailyState);
        private static void Require(bool condition, string message) { if (!condition) throw new SaveIntegrityException(message); }
        private void Validate()
        {
            Require(Coins >= 0, "Coin balance cannot be negative.");
            Require(Horses.Select(h => h.HorseId).Distinct().Count() == Horses.Count, "Duplicate horse identity in save.");
            Require(Ledger.Select(l => l.Id).Distinct().Count() == Ledger.Count, "Duplicate coin transaction identity.");
            Require(DailyState.Receipts.Select(r => r.Request.OperationId).Distinct().Count() == DailyState.Receipts.Count
                && DailyState.Receipts.All(r => r.ToDay <= Header.CalendarDay && r.Request.Requester.Equals(PlayerOwnerId)), "Daily-care receipt history is invalid for this owner or calendar.");
            Require(DailyState.Receipts.All(r => !Ledger.Any(l => l.Id.Equals(r.Request.OperationId))), "Daily-care operation IDs cannot collide with wallet ledger IDs.");
            Require(DailyState.Receipts.Select(r => r.CostTransactionId).Distinct().Count() == DailyState.Receipts.Count
                && DailyState.Receipts.All(r => !r.Request.OperationId.Equals(r.CostTransactionId)
                    && !DailyState.Receipts.Any(other => other.Request.OperationId.Equals(r.CostTransactionId))),
                "Daily-care receipt transaction identities are reused.");
            Require(PendingCapacityPlans.Select(p => p.ReservationId).Distinct().Count() == PendingCapacityPlans.Count
                && PendingCapacityPlans.Select(p => p.BreedingId).Distinct().Count() == PendingCapacityPlans.Count, "Duplicate pending capacity hold.");
            Require(Conceptions.Select(c => c.Commit.BreedingCommitId).Distinct().Count() == Conceptions.Count
                && Conceptions.Select(c => c.Commit.OffspringHorseId).Distinct().Count() == Conceptions.Count
                && Conceptions.Select(c => c.Commit.CostTransactionId).Distinct().Count() == Conceptions.Count
                && Conceptions.Select(c => c.Capacity.ReservationId).Distinct().Count() == Conceptions.Count, "A conception identity, cost or hold was reused.");
            Require(Header.RngStreams.Breeding.EventCounter == Header.LastCommittedBreedingEventIndex
                && Header.LastCommittedBreedingEventIndex == (ulong)Conceptions.Count, "Breeding stream and durable conception sequence disagree.");
            Require(Header.SaveRevision >= (ulong)Conceptions.Count && CapacityRevision >= (ulong)Conceptions.Count, "Conception revisions cannot run ahead of the save.");
            var horses = Horses.ToDictionary(h => h.HorseId); var receipts = Conceptions.ToDictionary(c => c.Commit.BreedingCommitId);
            var ledger = Ledger.ToDictionary(l => l.Id);
            foreach (var pair in DailyState.HorseCare)
                Require(horses.ContainsKey(pair.Key), "Daily care references a horse absent from the canonical stable.");
            foreach (var receipt in DailyState.Receipts)
            {
                Require(receipt.CostTransactionId.Equals(StableDayService.CostId(receipt.Request.OperationId)), "A daily-care transaction ID is not the deterministic ID for its command.");
                if (receipt.WalletDebitCoins == 0) Require(!ledger.ContainsKey(receipt.CostTransactionId), "A zero-cost daily receipt cannot have a wallet debit.");
                else Require(ledger.TryGetValue(receipt.CostTransactionId, out var dailyDebit) && dailyDebit.Kind == "stable-day"
                    && dailyDebit.BreedingId == null && dailyDebit.DeltaCoins == -receipt.WalletDebitCoins,
                    "A daily-care debit is missing or inconsistent with its receipt.");
            }
            foreach (var debit in Ledger.Where(l => l.Kind == "stable-day"))
                Require(DailyState.Receipts.Any(r => r.WalletDebitCoins > 0 && r.CostTransactionId.Equals(debit.Id)
                    && debit.DeltaCoins == -r.WalletDebitCoins), "Orphan daily-care wallet debit.");
            var horsesById = Horses.ToDictionary(h => h.HorseId);
            var lastCare = new Dictionary<HorseId, HorseCareState>();
            var lastDay = (int?)null; var lastExpectedRevision = (ulong?)null;
            foreach (var receipt in DailyState.Receipts)
            {
                Require(receipt.Request.ExpectedRevision < Header.SaveRevision, "A daily-care receipt is from a future save revision.");
                Require(!lastExpectedRevision.HasValue || receipt.Request.ExpectedRevision > lastExpectedRevision.Value,
                    "Daily-care command revisions are not strictly increasing.");
                Require(!lastDay.HasValue || receipt.FromDay == lastDay.Value, "Daily-care receipts have a calendar gap or overlap.");
                lastDay = receipt.ToDay; lastExpectedRevision = receipt.Request.ExpectedRevision;
                var assigned = receipt.Request.Plan.Assignments.Select(a => a.HorseId).ToHashSet();
                Require(receipt.HorseOutcomes.Select(o => o.HorseId).ToHashSet().SetEquals(assigned), "A daily-care receipt does not describe every assigned horse exactly once.");
                Require(receipt.Request.Plan.Assignments.All(a => a.Feed && a.Bedding), "A daily-care receipt omitted a required basic-care assignment.");
                var expectedCareCost = checked((long)assigned.Count * StableDailyPolicy.FeedCoinsPerHorse);
                var expectedOperationsCost = assigned.Count == 0 ? 0 : StableDailyPolicy.OperationsCoinsPerDay;
                Require(receipt.CareCostCoins == expectedCareCost && receipt.OperationsCostCoins == expectedOperationsCost,
                    "A daily-care receipt does not match the supported daily costs.");
                foreach (var outcome in receipt.HorseOutcomes)
                {
                    Require(horsesById.ContainsKey(outcome.HorseId), "A daily-care result references a horse absent from the canonical stable.");
                    Require(lastCare.TryGetValue(outcome.HorseId, out var prior) ? SameCare(prior, outcome.Before) : SameCare(HorseCareState.Initial, outcome.Before),
                        "Daily-care history is discontinuous for a horse.");
                    lastCare[outcome.HorseId] = outcome.After;
                }
            }
            if (DailyState.Receipts.Count != 0)
            {
                Require(lastDay == Header.CalendarDay, "The stable calendar differs from its most recent daily-care receipt.");
                foreach (var pair in lastCare)
                    Require(DailyState.TryGet(pair.Key, out var latest) && SameCare(latest, pair.Value), "The latest care result is missing from the saved care root.");
                foreach (var pair in DailyState.HorseCare)
                    Require(lastCare.TryGetValue(pair.Key, out var latest) ? SameCare(latest, pair.Value) : SameCare(HorseCareState.Initial, pair.Value),
                        "The saved care root differs from its receipt history.");
            }
            else
                Require(DailyState.HorseCare.All(pair => SameCare(pair.Value, HorseCareState.Initial)),
                    "A daily-care value cannot change before the first recorded EndDay receipt.");
            try { _ = new PedigreeGraph(Horses.Select(h => new PedigreeNode(h.HorseId, h.Parentage))); }
            catch (ArgumentException e) { throw new SaveIntegrityException("Invalid saved pedigree.", e); }
            foreach (var plan in PendingCapacityPlans)
                Require(plan.CapacityRevision <= CapacityRevision && !receipts.ContainsKey(plan.BreedingId)
                    && !Conceptions.Any(c => c.Capacity.ReservationId.Equals(plan.ReservationId)), "A pending hold is from the future or was already consumed.");
            for (var i = 0; i < Conceptions.Count; i++)
            {
                var receipt = Conceptions[i]; var commit = receipt.Commit;
                Require(commit.BreedingEventIndex == (ulong)i + 1 && receipt.BeforeBreedingStream.EventCounter == (ulong)i, "Noncontiguous breeding events.");
                Require(receipt.BeforeBreedingStream.Seed == Header.RngStreams.Breeding.Seed
                    && receipt.BeforeBreedingStream.AlgorithmVersion == Header.RngStreams.Breeding.AlgorithmVersion, "A conception belongs to a different breeding stream.");
                Require(receipt.Request.ExpectedSaveRevision < Header.SaveRevision && receipt.Capacity.CapacityRevision < CapacityRevision,
                    "A receipt has not been applied to the recorded revisions.");
                Require(receipt.Request.Requester.Equals(PlayerOwnerId), "A receipt debits a different owner's wallet.");
                Require(commit.ConceptionDay <= Header.CalendarDay, "A conception is dated after the current calendar.");
                Require(Header.RetainedResolverVersions.Contains(commit.OffspringResolverVersion), "A conception's resolver version was not retained.");
                Require(horses.ContainsKey(commit.ParentAId) && horses.ContainsKey(commit.ParentBId) && horses.ContainsKey(commit.OffspringHorseId), "A committed parent or offspring record is missing.");
                Require(SameInheritedIdentity(horses[commit.ParentAId], receipt.ParentAAtCommit) && SameInheritedIdentity(horses[commit.ParentBId], receipt.ParentBAtCommit)
                    && SameInheritedIdentity(horses[commit.OffspringHorseId], receipt.UnbornAtCommit), "Saved inheritance or identity differs from its immutable conception evidence.");
                Require(horses[commit.ParentAId].Revision >= commit.ParentARevision && horses[commit.ParentBId].Revision >= commit.ParentBRevision,
                    "Current parents precede their historical conception revisions.");
                Require(ledger.TryGetValue(commit.CostTransactionId, out var entry) && entry.Kind == "breeding"
                    && commit.BreedingCommitId.Equals(entry.BreedingId) && entry.DeltaCoins == -receipt.Terms.CostCoins, "The conception debit is missing, duplicated or inconsistent.");
                var roles = new[] { receipt.ParentAAtCommit.ReproductionState.Role, receipt.ParentBAtCommit.ReproductionState.Role };
                Require(roles.Contains(ReproductiveRole.Mare) && roles.Contains(ReproductiveRole.Stallion)
                    && receipt.ParentAAtCommit.ReproductionState.PregnancyId == null && receipt.ParentBAtCommit.ReproductionState.PregnancyId == null,
                    "The conception's original reproductive state is invalid.");
                var mareId = receipt.ParentAAtCommit.ReproductionState.Role == ReproductiveRole.Mare ? commit.ParentAId : commit.ParentBId;
                var child = horses[commit.OffspringHorseId];
                if (child.LifeState.Stage == LifeStage.Unborn)
                    Require(commit.BreedingCommitId.Equals(horses[mareId].ReproductionState.PregnancyId)
                        && child.ReproductionState.Role == receipt.UnbornAtCommit.ReproductionState.Role, "An unborn foal must retain its pregnancy link and original role.");
                else Require(child.BirthDay.HasValue && child.BirthDay.Value >= commit.PlannedBirthDay && child.BirthDay.Value <= Header.CalendarDay,
                    "A born offspring needs its actual nonpremature birth record.");
            }
            foreach (var horse in Horses)
            {
                Require(Header.RetainedResolverVersions.Contains(horse.ResolverVersion), "A horse's resolver version was dropped from the save.");
                if (horse.OriginCommitId != null)
                    Require(receipts.TryGetValue(horse.OriginCommitId, out var origin) && origin.Commit.OffspringHorseId.Equals(horse.HorseId), "Orphan or incorrect conception origin.");
                if (horse.LifeState.Stage == LifeStage.Unborn) Require(horse.OriginCommitId != null, "An unborn horse requires a committed origin.");
                if (horse.ReproductionState.PregnancyId != null)
                {
                    Require(receipts.TryGetValue(horse.ReproductionState.PregnancyId, out var pregnancy), "A pregnancy references an unknown conception.");
                    var mare = pregnancy!.ParentAAtCommit.ReproductionState.Role == ReproductiveRole.Mare ? pregnancy.ParentAAtCommit : pregnancy.ParentBAtCommit;
                    Require(mare.HorseId.Equals(horse.HorseId) && horses[pregnancy.Commit.OffspringHorseId].LifeState.Stage == LifeStage.Unborn,
                        "Pregnancy state does not belong to this mare and unborn foal.");
                }
            }
            foreach (var entry in Ledger.Where(l => l.Kind == "breeding"))
                Require(entry.BreedingId != null && receipts.TryGetValue(entry.BreedingId, out var receipt) && receipt.Commit.CostTransactionId.Equals(entry.Id), "Orphan breeding debit.");
        }
        internal static bool SameInheritedIdentity(HorseRecord a, HorseRecord b) => a.HorseId.Equals(b.HorseId)
            && a.Genome.GenomeHash == b.Genome.GenomeHash && a.ResolverVersion == b.ResolverVersion && a.AppearanceSeed == b.AppearanceSeed
            && a.DevelopmentalFactors.Hash == b.DevelopmentalFactors.Hash && a.InheritedGamePotential.Hash == b.InheritedGamePotential.Hash
            && Equals(a.Parentage.ParentAId, b.Parentage.ParentAId) && Equals(a.Parentage.ParentBId, b.Parentage.ParentBId)
            && a.Parentage.FounderBackground?.AssumptionId == b.Parentage.FounderBackground?.AssumptionId
            && a.Parentage.FounderBackground?.InbreedingCoefficient == b.Parentage.FounderBackground?.InbreedingCoefficient
            && a.OriginRecord == b.OriginRecord && Equals(a.OriginCommitId, b.OriginCommitId);
        private static bool SameCare(HorseCareState a, HorseCareState b) => a.Nutrition == b.Nutrition && a.Bedding == b.Bedding
            && a.Turnout == b.Turnout && a.Grooming == b.Grooming;
    }

    internal static class SaveHeaders
    {
        internal static SaveHeader Copy(SaveHeader h, ulong? revision = null, RngStreams? streams = null, ulong? eventIndex = null,
            string? schema = null, IEnumerable<string>? history = null, string? payloadHash = null, int? calendarDay = null) =>
            new SaveHeader(h.SaveId, h.SaveMode, schema ?? h.SchemaVersion, h.GameBuildVersion, revision ?? h.SaveRevision, calendarDay ?? h.CalendarDay,
                h.CalendarProfileVersion, h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion, h.DefaultOffspringResolverVersion,
                h.RetainedResolverVersions, streams ?? h.RngStreams, eventIndex ?? h.LastCommittedBreedingEventIndex, history ?? h.MigrationHistory, payloadHash ?? h.PayloadHash);
    }

    public sealed class ConceptionStateChange
    {
        internal ConceptionStateChange(StableSnapshot state, PreparedConception receipt, bool replayed) { State = state; Receipt = receipt; Replayed = replayed; }
        public StableSnapshot State { get; }
        public PreparedConception Receipt { get; }
        public bool Replayed { get; }
    }
    public static class ConceptionTransaction
    {
        // A pure all-or-nothing state transition. The filesystem adapter must
        // publish this complete state before describing the result as durable.
        public static ConceptionStateChange Apply(StableSnapshot state, ConceptionRequest request, ConceptionService service)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(request, nameof(request)); Check.NotNull(service, nameof(service));
            var recorded = state.Conceptions.SingleOrDefault(c => c.Commit.BreedingCommitId.Equals(request.EventId));
            if (recorded != null)
            {
                if (recorded.Request.RequestHash != request.RequestHash) throw new ConceptionRejectedException("idempotency_conflict", "This command ID already belongs to a different request.");
                return new ConceptionStateChange(state, recorded, true);
            }
            if (!state.PlayerOwnerId.Equals(request.Requester)) throw new ConceptionRejectedException("wallet_owner", "The request is not authorized for this saved wallet.");
            var capacity = state.PendingCapacityPlans.SingleOrDefault(p => p.ReservationId.Equals(request.ReservationId));
            if (capacity == null) throw new ConceptionRejectedException("capacity_missing", "The authoritative save has no such pending hold.");
            var prepared = service.Prepare(state.Header, state.Horses, state.Coins, state.CapacityRevision, request, capacity);
            var mare = prepared.PregnantMare;
            var horses = state.Horses.Select(h => h.HorseId.Equals(mare.HorseId) ? mare : h).Concat(new[] { prepared.UnbornAtCommit });
            var streams = state.Header.RngStreams;
            var header = SaveHeaders.Copy(state.Header, checked(state.Header.SaveRevision + 1),
                new RngStreams(prepared.AfterBreedingStream, streams.Cosmetic, streams.World, streams.Competition), prepared.Commit.BreedingEventIndex, payloadHash: "unwritten");
            var next = new StableSnapshot(header, state.PlayerOwnerId, horses, state.OpeningCoinBalance, checked(state.CapacityRevision + 1),
                state.Ledger.Concat(new[] { new CoinLedgerEntry(prepared.Commit.CostTransactionId, -prepared.Terms.CostCoins, "breeding", prepared.Commit.BreedingCommitId) }),
                state.PendingCapacityPlans.Where(p => !p.ReservationId.Equals(request.ReservationId)), state.Conceptions.Concat(new[] { prepared }), state.DailyState);
            return new ConceptionStateChange(next, prepared, false);
        }
    }
}
