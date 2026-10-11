using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>The distinct persisted daily-care root; it never replaces canonical horse records.</summary>
    public sealed class StableDailyState
    {
        public StableDailyState(IEnumerable<KeyValuePair<HorseId, HorseCareState>>? horseCare = null, IEnumerable<DailyCareReceipt>? receipts = null)
        {
            var inputCare = (horseCare ?? Array.Empty<KeyValuePair<HorseId, HorseCareState>>()).ToArray();
            if (inputCare.Any(p => p.Key == null || p.Value == null)) throw new ArgumentException("Horse care entries cannot be null.", nameof(horseCare));
            var care = inputCare.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal).ToArray();
            if (care.Select(p => p.Key).Distinct().Count() != care.Length) throw new ArgumentException("Duplicate horse care identity.", nameof(horseCare));
            HorseCare = Array.AsReadOnly(care);
            var inputReceipts = (receipts ?? Array.Empty<DailyCareReceipt>()).ToArray();
            if (inputReceipts.Any(r => r == null)) throw new ArgumentException("Daily-care receipts cannot be null.", nameof(receipts));
            Receipts = Snapshot.List(inputReceipts.OrderBy(r => r.FromDay).ThenBy(r => r.Request.OperationId.ToString(), StringComparer.Ordinal));
            if (Receipts.Select(r => r.Request.OperationId).Distinct().Count() != Receipts.Count
                || Receipts.Select(r => r.FromDay).Distinct().Count() != Receipts.Count) throw new ArgumentException("Duplicate daily-care operation or day identity.", nameof(receipts));
        }
        public IReadOnlyList<KeyValuePair<HorseId, HorseCareState>> HorseCare { get; }
        public IReadOnlyList<DailyCareReceipt> Receipts { get; }
        public HorseCareState For(HorseId horseId) => HorseCare.FirstOrDefault(p => p.Key.Equals(horseId)).Value ?? HorseCareState.Initial;
        public bool TryGet(HorseId horseId, out HorseCareState state)
        { var item = HorseCare.FirstOrDefault(p => p.Key.Equals(horseId)); state = item.Value!; return item.Key != null; }
        public static StableDailyState Empty { get; } = new StableDailyState();
    }
}
