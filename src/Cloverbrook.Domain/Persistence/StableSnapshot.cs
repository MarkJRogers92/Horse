using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class LegacyLifecycleBaseline
    {
        public LegacyLifecycleBaseline(HorseId horseId, LifeStage stage, int ageDays, int recordedDay, int birthDay,
            ReproductiveRole role, int recoveryUntilDay)
        {
            HorseId = Check.NotNull(horseId, nameof(horseId));
            if (stage == LifeStage.Unborn || !Enum.IsDefined(typeof(LifeStage), stage)) throw new ArgumentOutOfRangeException(nameof(stage));
            Stage = stage; AgeDays = Check.NonNegative(ageDays, nameof(ageDays)); RecordedDay = Check.NonNegative(recordedDay, nameof(recordedDay));
            BirthDay = Check.NonNegative(birthDay, nameof(birthDay));
            if (!Enum.IsDefined(typeof(ReproductiveRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            Role = role; RecoveryUntilDay = Check.NonNegative(recoveryUntilDay, nameof(recoveryUntilDay));
        }
        public HorseId HorseId { get; }
        public LifeStage Stage { get; }
        public int AgeDays { get; }
        public int RecordedDay { get; }
        public int BirthDay { get; }
        public ReproductiveRole Role { get; }
        public int RecoveryUntilDay { get; }
    }

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
        public const string SchemaVersion = "cloverbrook.save.v3";
        public StableSnapshot(SaveHeader header, OwnerId playerOwnerId, IEnumerable<HorseRecord> horses,
            long openingCoinBalance, ulong capacityRevision, IEnumerable<CoinLedgerEntry>? ledger = null,
            IEnumerable<BreedingCapacityPlan>? pendingCapacityPlans = null, IEnumerable<PreparedConception>? conceptions = null,
            StableDailyState? dailyState = null, IEnumerable<LegacyLifecycleBaseline>? legacyLifecycleBaselines = null)
        {
            Header = Check.NotNull(header, nameof(header)); PlayerOwnerId = Check.NotNull(playerOwnerId, nameof(playerOwnerId));
            Horses = Snapshot.List(Check.NotNull(horses, nameof(horses)).OrderBy(h => h.HorseId.ToString(), StringComparer.Ordinal));
            if (openingCoinBalance < 0) throw new ArgumentOutOfRangeException(nameof(openingCoinBalance));
            OpeningCoinBalance = openingCoinBalance; CapacityRevision = capacityRevision;
            Ledger = Snapshot.List((ledger ?? Array.Empty<CoinLedgerEntry>()).OrderBy(l => l.Id.ToString(), StringComparer.Ordinal));
            PendingCapacityPlans = Snapshot.List((pendingCapacityPlans ?? Array.Empty<BreedingCapacityPlan>()).OrderBy(p => p.ReservationId.ToString(), StringComparer.Ordinal));
            Conceptions = Snapshot.List((conceptions ?? Array.Empty<PreparedConception>()).OrderBy(c => c.Commit.BreedingEventIndex));
            DailyState = dailyState ?? StableDailyState.Empty;
            LegacyLifecycleBaselines = Snapshot.List((legacyLifecycleBaselines ?? Array.Empty<LegacyLifecycleBaseline>()).OrderBy(b => b.HorseId.ToString(), StringComparer.Ordinal));
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
        public IReadOnlyList<LegacyLifecycleBaseline> LegacyLifecycleBaselines { get; }
        public StableLifecycleCapacityProjection LifecycleCapacity => StableLifecycleCapacityProjection.From(this);
        public StableSnapshot WithHeader(SaveHeader header) => new StableSnapshot(header, PlayerOwnerId, Horses, OpeningCoinBalance, CapacityRevision, Ledger, PendingCapacityPlans, Conceptions, DailyState, LegacyLifecycleBaselines);
        public StableSnapshot WithHorses(IEnumerable<HorseRecord> horses) => new StableSnapshot(Header, PlayerOwnerId, horses, OpeningCoinBalance, CapacityRevision, Ledger, PendingCapacityPlans, Conceptions, DailyState, LegacyLifecycleBaselines);
        public StableSnapshot WithDailyState(StableDailyState dailyState) => new StableSnapshot(Header, PlayerOwnerId, Horses, OpeningCoinBalance, CapacityRevision, Ledger, PendingCapacityPlans, Conceptions, dailyState, LegacyLifecycleBaselines);
        internal StableSnapshot WithPendingCapacityPlans(IEnumerable<BreedingCapacityPlan> plans) => new StableSnapshot(Header, PlayerOwnerId, Horses,
            OpeningCoinBalance, CapacityRevision, Ledger, plans, Conceptions, DailyState, LegacyLifecycleBaselines);
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
            Require(DailyState.Receipts.Select(r => r.IncomeTransactionId).Distinct().Count() == DailyState.Receipts.Count
                && DailyState.Receipts.All(r => !DailyState.Receipts.Any(other => other.Request.OperationId.Equals(r.IncomeTransactionId)
                    || other.CostTransactionId.Equals(r.IncomeTransactionId))),
                "Daily-care income transaction identities are reused.");
            Require(DailyState.Receipts.All(r => StableDailyPolicy.IsSupported(r.Request.PolicyVersion)), "A daily-care receipt uses an unknown policy version.");
            Require(PendingCapacityPlans.Select(p => p.ReservationId).Distinct().Count() == PendingCapacityPlans.Count
                && PendingCapacityPlans.Select(p => p.BreedingId).Distinct().Count() == PendingCapacityPlans.Count, "Duplicate pending capacity hold.");
            Require(Conceptions.Select(c => c.Commit.BreedingCommitId).Distinct().Count() == Conceptions.Count
                && Conceptions.Select(c => c.Commit.OffspringHorseId).Distinct().Count() == Conceptions.Count
                && Conceptions.Select(c => c.Commit.CostTransactionId).Distinct().Count() == Conceptions.Count
                && Conceptions.Select(c => c.Capacity.ReservationId).Distinct().Count() == Conceptions.Count, "A conception identity, cost or hold was reused.");
            Require(LegacyLifecycleBaselines.Select(b => b.HorseId).Distinct().Count() == LegacyLifecycleBaselines.Count,
                "A legacy lifecycle baseline is duplicated.");
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
            foreach (var receipt in DailyState.Receipts)
            {
                if (receipt.IncomeCoins == 0) Require(!ledger.ContainsKey(receipt.IncomeTransactionId), "A daily receipt without income cannot have an income credit.");
                else Require(ledger.TryGetValue(receipt.IncomeTransactionId, out var credit) && credit.Kind == StableDayTransaction.IncomeLedgerKind
                    && credit.BreedingId == null && credit.DeltaCoins == receipt.IncomeCoins, "A daily income credit is missing or inconsistent with its receipt.");
            }
            foreach (var credit in Ledger.Where(l => l.Kind == StableDayTransaction.IncomeLedgerKind))
                Require(DailyState.Receipts.Any(r => r.IncomeCoins > 0 && r.IncomeTransactionId.Equals(credit.Id)
                    && credit.DeltaCoins == r.IncomeCoins), "Orphan daily income credit.");
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
                if (child.LifeState.Stage != LifeStage.Unborn)
                    Require(child.ReproductionState.Role == receipt.UnbornAtCommit.ReproductionState.Role,
                        "A born foal must retain its committed reproductive role.");
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
            foreach (var baseline in LegacyLifecycleBaselines)
                Require(horses.TryGetValue(baseline.HorseId, out var baselineHorse) && baselineHorse.OriginCommitId != null
                    && baselineHorse.BirthDay == baseline.BirthDay && baselineHorse.ReproductionState.Role == baseline.Role
                    && baseline.RecordedDay <= Header.CalendarDay,
                    "A legacy lifecycle baseline no longer matches its canonical born horse.");
            ValidateLifecycleHistory(horses, receipts);
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
        internal static bool SameLifecycleState(HorseRecord a, HorseRecord b) => a.OwnerId.Equals(b.OwnerId)
            && a.LifeState.Stage == b.LifeState.Stage && a.LifeState.DaysSinceConception == b.LifeState.DaysSinceConception
            && a.LifeState.VisualAge == b.LifeState.VisualAge && a.BirthDay == b.BirthDay
            && a.ReproductionState.Role == b.ReproductionState.Role
            && Equals(a.ReproductionState.PregnancyId, b.ReproductionState.PregnancyId)
            && a.ReproductionState.RecoveryUntilDay == b.ReproductionState.RecoveryUntilDay;
        private static bool SameCare(HorseCareState a, HorseCareState b) => a.Nutrition == b.Nutrition && a.Bedding == b.Bedding
            && a.Turnout == b.Turnout && a.Grooming == b.Grooming;

        private void ValidateLifecycleHistory(IReadOnlyDictionary<HorseId, HorseRecord> horses,
            IReadOnlyDictionary<BreedingCommitId, PreparedConception> conceptions)
        {
            var births = new Dictionary<HorseId, StableLifecycleEvent>();
            var growth = new Dictionary<(HorseId, LifecycleEventKind), StableLifecycleEvent>();
            foreach (var receipt in DailyState.Receipts)
            foreach (var lifecycleEvent in receipt.LifecycleEvents)
            {
                Require(lifecycleEvent.EffectiveDay == receipt.ToDay, "A lifecycle event is outside its committed EndDay.");
                if (lifecycleEvent.Kind == LifecycleEventKind.Birth)
                {
                    if (lifecycleEvent.BreedingEventId == null || !conceptions.TryGetValue(lifecycleEvent.BreedingEventId, out var conception))
                        throw new SaveIntegrityException("A birth event does not match a known conception.");
                    Require(conception.Commit.OffspringHorseId.Equals(lifecycleEvent.HorseId), "A birth event does not match its committed offspring.");
                    var mareId = conception.ParentAAtCommit.ReproductionState.Role == ReproductiveRole.Mare
                        ? conception.Commit.ParentAId : conception.Commit.ParentBId;
                    Require(lifecycleEvent.RelatedHorseId!.Equals(mareId) && lifecycleEvent.EffectiveDay == conception.Commit.PlannedBirthDay,
                        "A birth event does not match its committed mare or planned birth day.");
                    Require(!births.ContainsKey(lifecycleEvent.HorseId), "A foal has more than one recorded birth.");
                    births.Add(lifecycleEvent.HorseId, lifecycleEvent);
                }
                else
                {
                    var key = (lifecycleEvent.HorseId, lifecycleEvent.Kind);
                    Require(!growth.ContainsKey(key), "A horse has a duplicate growth transition."); growth.Add(key, lifecycleEvent);
                }
            }
            foreach (var conception in Conceptions)
            {
                var child = horses[conception.Commit.OffspringHorseId];
                var hasBirth = births.ContainsKey(child.HorseId);
                var hasGrowth = growth.Keys.Any(k => k.Item1.Equals(child.HorseId));
                Require(child.LifeState.Stage != LifeStage.Unborn || (!hasBirth && !hasGrowth),
                    "An unborn horse cannot retain birth or growth events.");
            }
            foreach (var conception in Conceptions)
            {
                var child = horses[conception.Commit.OffspringHorseId];
                if (child.LifeState.Stage == LifeStage.Unborn) continue;
                if (!births.TryGetValue(child.HorseId, out var birth))
                {
                    var baseline = LegacyLifecycleBaselines.SingleOrDefault(b => b.HorseId.Equals(child.HorseId));
                    if (baseline == null && (Header.SchemaVersion == "cloverbrook.save.v1" || Header.SchemaVersion == "cloverbrook.save.v2"))
                        continue; // Exact old decoder; migration captures this state before re-encoding.
                    Require(baseline != null, "A born committed foal is missing its durable birth event or a captured legacy baseline.");
                    // Genuine v2 saves could persist a canonical born HorseRecord
                    // before lifecycle events existed. Preserve that exact per-horse
                    // state; later growth still requires new lifecycle receipts.
                    Require(child.BirthDay == baseline!.BirthDay && child.ReproductionState.Role == baseline.Role
                        && child.ReproductionState.RecoveryUntilDay >= baseline.RecoveryUntilDay
                        && child.LifeState.DaysSinceConception == checked(baseline.AgeDays + Header.CalendarDay - baseline.RecordedDay),
                        "A legacy born foal no longer matches its captured age, birth or role baseline.");
                    var legacyMareId = conception.ParentAAtCommit.ReproductionState.Role == ReproductiveRole.Mare
                        ? conception.Commit.ParentAId : conception.Commit.ParentBId;
                    Require(horses.TryGetValue(legacyMareId, out var legacyMare) && legacyMare.ReproductionState.Role == ReproductiveRole.Mare
                        && legacyMare.ReproductionState.RecoveryUntilDay >= checked(baseline.BirthDay + 2),
                        "A legacy foaling record is missing the mare's recovery boundary.");
                    var youngsterDayLegacy = checked(child.BirthDay!.Value + 3); var adultDayLegacy = checked(child.BirthDay.Value + 6);
                    var expectedLegacyStage = Header.CalendarDay < youngsterDayLegacy ? LifeStage.Foal
                        : Header.CalendarDay < adultDayLegacy ? LifeStage.Youngster : LifeStage.Adult;
                    Require(child.LifeState.Stage == expectedLegacyStage, "A legacy born foal's current stage does not match its saved birth anchor.");
                    Require(growth.ContainsKey((child.HorseId, LifecycleEventKind.FoalToYoungster)) == (youngsterDayLegacy > baseline.RecordedDay && Header.CalendarDay >= youngsterDayLegacy)
                        && growth.ContainsKey((child.HorseId, LifecycleEventKind.YoungsterToAdult)) == (adultDayLegacy > baseline.RecordedDay && Header.CalendarDay >= adultDayLegacy),
                        "A legacy born foal's post-migration growth history is incomplete or premature.");
                    continue;
                }
                Require(birth.EffectiveDay == child.BirthDay, "A born committed foal is missing its durable birth event.");
                Require(child.LifeState.DaysSinceConception == checked(Header.CalendarDay - conception.Commit.ConceptionDay),
                    "A committed offspring's age does not match its conception and calendar anchors.");
                var youngsterDay = checked(child.BirthDay!.Value + 3); var adultDay = checked(child.BirthDay.Value + 6);
                var expectedStage = Header.CalendarDay < youngsterDay ? LifeStage.Foal
                    : Header.CalendarDay < adultDay ? LifeStage.Youngster : LifeStage.Adult;
                Require(child.LifeState.Stage == expectedStage, "A born foal's saved stage is inconsistent with its recorded growth schedule.");
                Require(growth.ContainsKey((child.HorseId, LifecycleEventKind.FoalToYoungster)) == (Header.CalendarDay >= youngsterDay)
                    && growth.ContainsKey((child.HorseId, LifecycleEventKind.YoungsterToAdult)) == (Header.CalendarDay >= adultDay),
                    "A born foal's growth history is incomplete or premature.");
                Require(birth!.EffectiveDay >= conception.Commit.PlannedBirthDay, "A foal was born before its planned due day.");
            }
            foreach (var transition in growth.Values)
            {
                if (births.TryGetValue(transition.HorseId, out var birth))
                    Require(horses[transition.HorseId].BirthDay == birth.EffectiveDay, "A growth transition's birth anchor disagrees with the horse record.");
                else Require(horses.TryGetValue(transition.HorseId, out var legacyHorse) && legacyHorse.BirthDay.HasValue
                    && (LegacyLifecycleBaselines.Any(b => b.HorseId.Equals(transition.HorseId)) || legacyHorse.OriginCommitId == null), "A growth transition has no recorded birth.");
                var birthDay = birth?.EffectiveDay ?? horses[transition.HorseId].BirthDay!.Value;
                var expectedDay = checked(birthDay + (transition.Kind == LifecycleEventKind.FoalToYoungster ? 3 : 6));
                Require(transition.EffectiveDay == expectedDay, "A growth transition is outside its scheduled day.");
            }
            foreach (var mareBirths in births.Values.GroupBy(e => e.RelatedHorseId!))
            {
                var mareId = mareBirths.Key;
                Require(horses.TryGetValue(mareId, out var mare) && mare.ReproductionState.Role == ReproductiveRole.Mare
                    && mare.ReproductionState.RecoveryUntilDay >= checked(mareBirths.Max(e => e.EffectiveDay) + 2),
                    "A birth history is missing the mare's saved recovery boundary.");
            }
        }
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
                state.PendingCapacityPlans.Where(p => !p.ReservationId.Equals(request.ReservationId)), state.Conceptions.Concat(new[] { prepared }), state.DailyState,
                state.LegacyLifecycleBaselines);
            return new ConceptionStateChange(next, prepared, false);
        }
    }
}
