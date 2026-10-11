using System;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class EndDayRequest
    {
        public EndDayRequest(TransactionId operationId, OwnerId requester, ulong expectedRevision, int fromDay, CarePlan plan, string policyVersion)
        {
            OperationId = Check.NotNull(operationId, nameof(operationId)); Requester = Check.NotNull(requester, nameof(requester));
            ExpectedRevision = expectedRevision; FromDay = Check.NonNegative(fromDay, nameof(fromDay)); Plan = Check.NotNull(plan, nameof(plan));
            PolicyVersion = Check.Text(policyVersion, nameof(policyVersion));
            RequestHash = Canonical.Hash("stable.end-day.v1", OperationId.ToString(), Requester.ToString(), expectedRevision.ToString(CultureInfo.InvariantCulture),
                FromDay.ToString(CultureInfo.InvariantCulture), Plan.RequestHash, PolicyVersion);
        }
        public TransactionId OperationId { get; }
        public OwnerId Requester { get; }
        public ulong ExpectedRevision { get; }
        public int FromDay { get; }
        public CarePlan Plan { get; }
        public string PolicyVersion { get; }
        public string RequestHash { get; }
    }

    public sealed class DailyCareReceipt
    {
        public DailyCareReceipt(EndDayRequest request, int fromDay, int toDay, long careCostCoins, long operationsCostCoins,
            long walletDebitCoins, long assistanceCoins, TransactionId costTransactionId, System.Collections.Generic.IEnumerable<HorseCareOutcome> horseOutcomes)
        {
            Request = Check.NotNull(request, nameof(request)); FromDay = Check.NonNegative(fromDay, nameof(fromDay)); ToDay = Check.NonNegative(toDay, nameof(toDay));
            if (ToDay != checked(FromDay + 1) || request.FromDay != FromDay) throw new ArgumentException("An EndDay receipt advances exactly one requested day.");
            long totalCost, provided;
            try { totalCost = checked(careCostCoins + operationsCostCoins); provided = checked(walletDebitCoins + assistanceCoins); }
            catch (OverflowException e) { throw new ArgumentException("Receipt amounts exceed the supported wallet range.", nameof(careCostCoins), e); }
            if (careCostCoins < 0 || operationsCostCoins < 0 || walletDebitCoins < 0 || assistanceCoins < 0 || provided != totalCost
                || walletDebitCoins > totalCost) throw new ArgumentOutOfRangeException(nameof(careCostCoins));
            CareCostCoins = careCostCoins; OperationsCostCoins = operationsCostCoins; WalletDebitCoins = walletDebitCoins; AssistanceCoins = assistanceCoins;
            CostTransactionId = Check.NotNull(costTransactionId, nameof(costTransactionId));
            HorseOutcomes = Snapshot.List(horseOutcomes);
            if (HorseOutcomes.Select(h => h.HorseId).Distinct().Count() != HorseOutcomes.Count) throw new ArgumentException("Duplicate horse care result.", nameof(horseOutcomes));
            ReceiptHash = Canonical.Hash(new[] { "stable.daily-care-receipt.v1", request.RequestHash, FromDay.ToString(CultureInfo.InvariantCulture),
                ToDay.ToString(CultureInfo.InvariantCulture), careCostCoins.ToString(CultureInfo.InvariantCulture), operationsCostCoins.ToString(CultureInfo.InvariantCulture),
                WalletDebitCoins.ToString(CultureInfo.InvariantCulture), AssistanceCoins.ToString(CultureInfo.InvariantCulture), CostTransactionId.ToString() }
                .Concat(HorseOutcomes.SelectMany(h => new[] { h.HorseId.ToString(), Values(h.Before), Values(h.After) })));
        }
        public EndDayRequest Request { get; }
        public int FromDay { get; }
        public int ToDay { get; }
        public long CareCostCoins { get; }
        public long OperationsCostCoins { get; }
        public long WalletDebitCoins { get; }
        public long AssistanceCoins { get; }
        public TransactionId CostTransactionId { get; }
        public System.Collections.Generic.IReadOnlyList<HorseCareOutcome> HorseOutcomes { get; }
        public string ReceiptHash { get; }
        private static string Values(HorseCareState s) => string.Join(",", s.Nutrition.ToString(CultureInfo.InvariantCulture), s.Bedding.ToString(CultureInfo.InvariantCulture),
            s.Turnout.ToString(CultureInfo.InvariantCulture), s.Grooming.ToString(CultureInfo.InvariantCulture));
    }

    public sealed class DailyPreview
    {
        internal DailyPreview(bool allowed, string? blockReason, int fromDay, int toDay, long careCost, long operationsCost, long assistance,
            long projectedCoins, System.Collections.Generic.IEnumerable<HorseCareOutcome> outcomes, DailyCareReceipt? receipt)
        { Allowed = allowed; BlockReason = blockReason; FromDay = fromDay; ToDay = toDay; CareCostCoins = careCost; OperationsCostCoins = operationsCost;
            AssistanceCoins = assistance; ProjectedCoins = projectedCoins; HorseOutcomes = Snapshot.List(outcomes); Receipt = receipt; }
        public bool Allowed { get; }
        public string? BlockReason { get; }
        public int FromDay { get; }
        public int ToDay { get; }
        public long CareCostCoins { get; }
        public long OperationsCostCoins { get; }
        public long AssistanceCoins { get; }
        public long ProjectedCoins { get; }
        public System.Collections.Generic.IReadOnlyList<HorseCareOutcome> HorseOutcomes { get; }
        public DailyCareReceipt? Receipt { get; }
    }
}
