using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class StableDayService
    {
        private readonly StableCalendar calendar;
        private readonly StableDailyPolicy policy;
        public StableDayService() : this(new StableCalendar(), new StableDailyPolicy()) { }
        public StableDayService(StableCalendar calendar, StableDailyPolicy policy)
        { this.calendar = Check.NotNull(calendar, nameof(calendar)); this.policy = Check.NotNull(policy, nameof(policy)); }
        public string CalendarProfileVersion => calendar.ProfileVersion;
        public string PolicyVersion => policy.PolicyVersion;

        public DailyPreview Preview(StableSnapshot state, CarePlan plan)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(plan, nameof(plan));
            var from = state.Header.CalendarDay;
            if (state.Header.SaveRevision == ulong.MaxValue) return Block(from, "The save revision cannot advance beyond its supported range.");
            int to;
            try { to = calendar.AdvanceOneDay(from); }
            catch (OverflowException) { return Block(from, "The calendar cannot advance beyond its supported range."); }
            if (state.Header.CalendarProfileVersion != calendar.ProfileVersion) return Block(from, "This save uses an unsupported calendar profile.");

            var horses = state.Horses.ToDictionary(h => h.HorseId);
            if (plan.Assignments.Any(a => !horses.ContainsKey(a.HorseId))) return Block(from, "The care plan includes a horse that is not in this stable.");
            var active = state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable
                && h.LifeState.Stage != LifeStage.Unborn).ToArray();
            if (plan.Assignments.Any(a => !active.Any(h => h.HorseId.Equals(a.HorseId))))
                return Block(from, "Care choices can only be assigned to active-stable horses.");
            foreach (var horse in active)
            {
                var assignment = plan.For(horse.HorseId);
                if (assignment == null) return Block(from, "Every active-stable horse needs an explicit care assignment.");
                if (!assignment.Feed || !assignment.Bedding) return Block(from, "Feeding and clean bedding are required for every active-stable horse.");
            }

            var commits = state.Conceptions.ToDictionary(c => c.Commit.OffspringHorseId);
            var lifecycleEvents = new List<StableLifecycleEvent>();
            foreach (var horse in state.Horses)
            {
                if (horse.Revision == ulong.MaxValue || (horse.OriginCommitId == null && horse.LifeState.DaysSinceConception == int.MaxValue))
                    return Block(from, "A horse's age or record revision cannot advance beyond its supported range.");
                if (horse.OriginCommitId != null)
                {
                    if (!commits.TryGetValue(horse.HorseId, out var ageOrigin)) return Block(from, "A committed foal has no reliable conception record.");
                    if (ageOrigin.Commit.CalendarProfileVersion != calendar.ProfileVersion)
                        return Block(from, "This horse was conceived under a calendar profile the current game cannot safely interpret.");
                    if (ageOrigin.Commit.CompatibilityRuleVersion != "cozy.policy.v1"
                        && ageOrigin.Commit.CompatibilityRuleVersion != StableBreedingPlanner.PolicyVersion)
                        return Block(from, "This horse uses breeding rules the current game cannot safely interpret.");
                    try
                    {
                        var baseline = state.LegacyLifecycleBaselines.SingleOrDefault(b => b.HorseId.Equals(horse.HorseId));
                        var projectedAge = baseline == null ? checked(to - ageOrigin.Commit.ConceptionDay)
                            : checked(baseline.AgeDays + checked(to - baseline.RecordedDay));
                        if (projectedAge < 0 || (baseline == null && to < ageOrigin.Commit.ConceptionDay))
                            return Block(from, "A committed foal's age anchor is inconsistent with the calendar.");
                    }
                    catch (OverflowException) { return Block(from, "A committed foal's age exceeds the supported range."); }
                }
                if (horse.LifeState.Stage == LifeStage.Unborn)
                {
                    if (!commits.TryGetValue(horse.HorseId, out var origin)) return Block(from, "An unborn foal has no reliable conception record.");
                    if (to > origin.Commit.PlannedBirthDay) return Block(from, "This pregnancy is past its recorded due day; the game cannot safely backdate a birth.");
                    if (to == origin.Commit.PlannedBirthDay)
                    {
                        var mareReference = Mare(origin);
                        var mare = mareReference != null && horses.TryGetValue(mareReference.HorseId, out var currentMare) ? currentMare : null;
                        if (mare == null || mare.ReproductionState.PregnancyId == null
                            || !mare.ReproductionState.PregnancyId.Equals(origin.Commit.BreedingCommitId))
                            return Block(from, "A due pregnancy no longer has its authoritative mare link.");
                        if (mare.HomeAssignment != horse.HomeAssignment)
                            return Block(from, "The mare and foal must be in the same home before birth can be processed.");
                        if (!HasValidLifecycleCapacity(origin)) return Block(from, "The original foaling and growth reservation does not cover the saved lifecycle dates.");
                        try { _ = checked(origin.Commit.PlannedBirthDay + 6); _ = checked(to - origin.Commit.ConceptionDay); }
                        catch (OverflowException) { return Block(from, "The foal's saved age or growth schedule exceeds the supported range."); }
                        lifecycleEvents.Add(new StableLifecycleEvent(LifecycleEventKind.Birth, horse.HorseId, to, LifeStage.Unborn,
                            LifeStage.Foal, mare.HorseId, origin.Commit.BreedingCommitId));
                        try { _ = checked(to + 2); }
                        catch (OverflowException) { return Block(from, "The mare's recovery date exceeds the supported calendar range."); }
                    }
                }
                else if (horse.LifeState.Stage == LifeStage.Foal || horse.LifeState.Stage == LifeStage.Youngster)
                {
                    if (!horse.BirthDay.HasValue) return Block(from, "A young horse has no recorded birth day for its growth schedule.");
                    int threshold;
                    try { threshold = checked(horse.BirthDay.Value + (horse.LifeState.Stage == LifeStage.Foal ? 3 : 6)); }
                    catch (OverflowException) { return Block(from, "A young horse's growth schedule exceeds the supported calendar range."); }
                    if (to > threshold) return Block(from, "This young horse is past a saved growth boundary; the game cannot safely skip a stage transition.");
                    if (to == threshold)
                    {
                        var kind = horse.LifeState.Stage == LifeStage.Foal ? LifecycleEventKind.FoalToYoungster : LifecycleEventKind.YoungsterToAdult;
                        var nextStage = horse.LifeState.Stage == LifeStage.Foal ? LifeStage.Youngster : LifeStage.Adult;
                        if (horse.OriginCommitId != null && (!commits.TryGetValue(horse.HorseId, out var origin)
                            || !HasValidLifecycleCapacity(origin)))
                            return Block(from, "The original foaling and growth reservation is unavailable for this transition.");
                        lifecycleEvents.Add(new StableLifecycleEvent(kind, horse.HorseId, to, horse.LifeState.Stage, nextStage));
                    }
                }
            }

            var careCost = checked((long)active.Length * StableDailyPolicy.FeedCoinsPerHorse);
            var operationsCost = active.Length == 0 ? 0 : StableDailyPolicy.OperationsCoinsPerDay;
            var totalCost = checked(careCost + operationsCost);
            var walletDebit = Math.Min(state.Coins, totalCost);
            var assistance = totalCost - walletDebit;
            var projectedCoins = state.Coins - walletDebit;
            var outcomes = new List<HorseCareOutcome>();
            foreach (var horse in active)
            {
                var before = state.DailyState.For(horse.HorseId); var assignment = plan.For(horse.HorseId)!;
                var after = new HorseCareState(HorseCareState.Change(before.Nutrition, 900), HorseCareState.Change(before.Bedding, 900),
                    HorseCareState.Change(before.Turnout, assignment.Turnout ? 1200 : 250), HorseCareState.Change(before.Grooming, assignment.Grooming ? 1000 : 250));
                outcomes.Add(new HorseCareOutcome(horse.HorseId, before, after));
            }
            var previewId = new TransactionId(Guid.ParseExact(Canonical.Hash("stable.end-day.preview.v1", state.Header.SaveId.ToString(),
                state.Header.SaveRevision.ToString(CultureInfo.InvariantCulture), from.ToString(CultureInfo.InvariantCulture), plan.RequestHash).Substring(0, 32), "N"));
            var request = new EndDayRequest(previewId, state.PlayerOwnerId, state.Header.SaveRevision, from, plan, StableDailyPolicy.Version);
            var costId = CostId(request.OperationId);
            var orderedLifecycleEvents = lifecycleEvents.OrderBy(e => e.HorseId.ToString(), StringComparer.Ordinal).ToArray();
            var receipt = new DailyCareReceipt(request, from, to, careCost, operationsCost, walletDebit, assistance, costId, outcomes, orderedLifecycleEvents);
            return new DailyPreview(true, null, from, to, careCost, operationsCost, assistance, projectedCoins, outcomes, receipt, orderedLifecycleEvents);
        }

        private static HorseRecord? Mare(PreparedConception conception)
        {
            if (conception.ParentAAtCommit.ReproductionState.Role == ReproductiveRole.Mare) return conception.ParentAAtCommit;
            if (conception.ParentBAtCommit.ReproductionState.Role == ReproductiveRole.Mare) return conception.ParentBAtCommit;
            return null;
        }

        private static bool HasValidLifecycleCapacity(PreparedConception conception)
        {
            var commit = conception.Commit;
            int pregnancyDays;
            try { pregnancyDays = checked(commit.PlannedBirthDay - commit.ConceptionDay); }
            catch (OverflowException) { return false; }
            if (pregnancyDays < 1) return false;
            // The original reservation is rechecked against its recorded gestation and
            // the supported three-day juvenile stages. It remains one obligation and
            // is never consumed or re-reserved at birth or growth.
            try
            {
                var validationPolicy = new BreedingPolicy("stable.lifecycle.capacity-check.v1", Rational.One, true,
                    breedableDay: 12, pregnancyDays: pregnancyDays, foalDays: 3, youngsterDays: 3);
                return conception.Capacity.Validate(commit.ConceptionDay, validationPolicy).Count == 0;
            }
            catch (OverflowException) { return false; }
        }

        internal DailyCareReceipt PrepareReceipt(StableSnapshot state, EndDayRequest request)
        {
            if (request.PolicyVersion != policy.PolicyVersion) throw new EndDayRejectedException("policy_unknown", "This daily-care rules version is not supported.");
            if (request.FromDay != state.Header.CalendarDay) throw new EndDayRejectedException("stale_day", "The stable calendar changed; reload before ending the day.");
            if (request.ExpectedRevision != state.Header.SaveRevision) throw new EndDayRejectedException("stale_save", "The saved stable changed; reload before ending the day.");
            if (!request.Requester.Equals(state.PlayerOwnerId)) throw new EndDayRejectedException("wallet_owner", "This request is not authorized for the saved stable.");
            var preview = Preview(state, request.Plan);
            if (!preview.Allowed) throw new EndDayRejectedException("day_blocked", preview.BlockReason ?? "The calendar cannot advance.");
            var receipt = preview.Receipt!;
            return new DailyCareReceipt(request, receipt.FromDay, receipt.ToDay, receipt.CareCostCoins, receipt.OperationsCostCoins,
                receipt.WalletDebitCoins, receipt.AssistanceCoins, CostId(request.OperationId), receipt.HorseOutcomes, receipt.LifecycleEvents);
        }

        internal static TransactionId CostId(TransactionId operationId) => new TransactionId(Guid.ParseExact(
            Canonical.Hash("stable.daily-care.cost.v1", operationId.ToString()).Substring(0, 32), "N"));
        private static DailyPreview Block(int day, string reason) => new DailyPreview(false, reason, day, day, 0, 0, 0, 0, Array.Empty<HorseCareOutcome>(), null);
    }

    public sealed class EndDayRejectedException : Exception
    {
        public EndDayRejectedException(string code, string message) : base(message) { Code = Check.Text(code, nameof(code)); }
        public string Code { get; }
    }

    public sealed class EndDayStateChange
    {
        internal EndDayStateChange(StableSnapshot state, DailyCareReceipt receipt, bool replayed)
        { State = state; Receipt = receipt; Replayed = replayed; }
        public StableSnapshot State { get; }
        public DailyCareReceipt Receipt { get; }
        public bool Replayed { get; }
    }

    internal static class StableDayTransaction
    {
        internal static EndDayStateChange Apply(StableSnapshot state, EndDayRequest request, StableDayService service)
        {
            var receipt = service.PrepareReceipt(state, request);
            if (state.DailyState.Receipts.Any(r => r.Request.OperationId.Equals(request.OperationId)))
                throw new EndDayRejectedException("idempotency_conflict", "This command ID already belongs to another daily-care request.");
            if (state.Ledger.Any(l => l.Id.Equals(request.OperationId) || l.Id.Equals(receipt.CostTransactionId)))
                throw new EndDayRejectedException("ledger_id_conflict", "This command's transaction identity is already used in the wallet history.");

            var care = state.DailyState.HorseCare.ToDictionary(p => p.Key, p => p.Value);
            foreach (var horse in state.Horses)
                if (!care.ContainsKey(horse.HorseId)) care.Add(horse.HorseId, HorseCareState.Initial);
            foreach (var outcome in receipt.HorseOutcomes) care[outcome.HorseId] = outcome.After;
            foreach (var lifecycleEvent in receipt.LifecycleEvents.Where(e => e.Kind == LifecycleEventKind.Birth))
                if (!care.ContainsKey(lifecycleEvent.HorseId)) care.Add(lifecycleEvent.HorseId, HorseCareState.Initial);
            var nextDaily = new StableDailyState(care, state.DailyState.Receipts.Concat(new[] { receipt }));
            var conceptionByHorse = state.Conceptions.ToDictionary(c => c.Commit.OffspringHorseId);
            var birthByMare = receipt.LifecycleEvents.Where(e => e.Kind == LifecycleEventKind.Birth).ToDictionary(e => e.RelatedHorseId!);
            var transitions = receipt.LifecycleEvents.ToDictionary(e => e.HorseId);
            var nextHorses = state.Horses.Select(h =>
            {
                var stage = h.LifeState.Stage;
                var visualAge = h.LifeState.VisualAge;
                var reproduction = h.ReproductionState;
                var birthDay = h.BirthDay;
                var name = h.Name;
                if (transitions.TryGetValue(h.HorseId, out var transition))
                {
                    stage = transition.ToStage;
                    visualAge = transition.ToStage == LifeStage.Adult ? VisualAge.Adult : VisualAge.Foal;
                    if (transition.Kind == LifecycleEventKind.Birth)
                    {
                        birthDay = transition.EffectiveDay;
                        if (name == "Unborn foal") name = "New foal";
                    }
                }
                if (birthByMare.TryGetValue(h.HorseId, out var foaling))
                    reproduction = new ReproductionState(ReproductiveRole.Mare, null, checked(foaling.EffectiveDay + 2));
                var legacyBaseline = state.LegacyLifecycleBaselines.SingleOrDefault(b => b.HorseId.Equals(h.HorseId));
                var ageDays = legacyBaseline != null ? checked(legacyBaseline.AgeDays + checked(receipt.ToDay - legacyBaseline.RecordedDay))
                    : conceptionByHorse.TryGetValue(h.HorseId, out var conception)
                    ? checked(receipt.ToDay - conception.Commit.ConceptionDay)
                    : checked(h.LifeState.DaysSinceConception + 1);
                return new HorseRecord(h.HorseId, name, h.Parentage, h.Genome, h.ResolverVersion, h.AppearanceSeed,
                    h.OwnerId, new LifeState(stage, ageDays, visualAge), reproduction,
                    h.DevelopmentalFactors, h.InheritedGamePotential, h.TrainingState, h.PlayerKnowledge, h.CosmeticState, h.HomeAssignment,
                    h.OriginRecord, h.OriginCommitId, birthDay, checked(h.Revision + 1));
            }).ToList();
            var header = SaveHeaders.Copy(state.Header, revision: checked(state.Header.SaveRevision + 1), schema: StableSnapshot.SchemaVersion,
                payloadHash: "unwritten", calendarDay: receipt.ToDay);
            var ledger = receipt.WalletDebitCoins == 0 ? state.Ledger : state.Ledger.Concat(new[]
            { new CoinLedgerEntry(receipt.CostTransactionId, -receipt.WalletDebitCoins, "stable-day") });
            var next = new StableSnapshot(header, state.PlayerOwnerId, nextHorses, state.OpeningCoinBalance, state.CapacityRevision,
                ledger, state.PendingCapacityPlans, state.Conceptions, nextDaily, state.LegacyLifecycleBaselines);
            return new EndDayStateChange(next, receipt, false);
        }
    }
}
