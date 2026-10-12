using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-008: v2 daily policy credits club support at End Day; v1 receipts stay income-free.
    public sealed class StableIncomeTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-income-test-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private static StableDayService Legacy() => new StableDayService(new StableCalendar(), new StableDailyPolicy(StableDailyPolicy.LegacyVersion));
        private static CarePlan Plan(StableSnapshot state) => new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId)
            && h.HomeAssignment == HomeKind.ActiveStable && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
        private static EndDayRequest Request(StableSnapshot state, string version) => new EndDayRequest(new TransactionId(Guid.NewGuid()),
            state.PlayerOwnerId, state.Header.SaveRevision, state.Header.CalendarDay, Plan(state), version);
        private AtomicSaveStore Store() { Directory.CreateDirectory(directory); return new AtomicSaveStore(SavePath); }
        private static StableSnapshot WithLedger(StableSnapshot s, System.Collections.Generic.IEnumerable<CoinLedgerEntry> ledger) =>
            new StableSnapshot(s.Header, s.PlayerOwnerId, s.Horses, s.OpeningCoinBalance, s.CapacityRevision, ledger, s.PendingCapacityPlans,
                s.Conceptions, s.DailyState, s.LegacyLifecycleBaselines);

        [Fact]
        public void PolicyVersionsAreExplicitAndOnlyV2PaysForANonemptyStable()
        {
            Assert.Equal(StableDailyPolicy.Version, new StableDailyPolicy().PolicyVersion);
            Assert.Throws<ArgumentException>(() => new StableDailyPolicy("stable.dailycare.v999"));
            Assert.Equal(50, StableDailyPolicy.IncomeFor(StableDailyPolicy.Version, 2));
            Assert.Equal(0, StableDailyPolicy.IncomeFor(StableDailyPolicy.Version, 0));
            Assert.Equal(0, StableDailyPolicy.IncomeFor(StableDailyPolicy.LegacyVersion, 2));
        }

        [Fact]
        public void ClubSupportPaysTodaysUpkeepBeforeAssistanceIsNeeded()
        {
            var state = SaveTestData.Initial(0); var preview = new StableDayService().Preview(state, Plan(state));
            Assert.True(preview.Allowed); Assert.Equal(50, preview.IncomeCoins); Assert.Equal(36, preview.WalletDebitCoins);
            Assert.Equal(0, preview.AssistanceCoins); Assert.Equal(14, preview.ProjectedCoins);
            var store = Store(); store.Initialize(state);
            var committed = store.CommitEndDay(Request(state, StableDailyPolicy.Version), new StableDayService());
            Assert.Equal(14, committed.State.Coins); Assert.Equal(50, committed.Receipt.IncomeCoins); Assert.Equal(36, committed.Receipt.WalletDebitCoins);
            var credit = Assert.Single(committed.State.Ledger, l => l.Kind == "stable-income");
            Assert.Equal(50, credit.DeltaCoins); Assert.Equal(committed.Receipt.IncomeTransactionId, credit.Id);
            Assert.Equal(-36, Assert.Single(committed.State.Ledger, l => l.Kind == "stable-day").DeltaCoins);
            Assert.Equal(14, new AtomicSaveStore(SavePath).Load().State.Coins);
        }

        [Fact]
        public void ExactRetryDoesNotPayIncomeTwice()
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial()); var request = Request(before, StableDailyPolicy.Version);
            var first = store.CommitEndDay(request, new StableDayService()); var bytes = File.ReadAllBytes(SavePath);
            var replay = new AtomicSaveStore(SavePath).CommitEndDay(request, new StableDayService());
            Assert.True(replay.Replayed); Assert.Equal(bytes, File.ReadAllBytes(SavePath)); Assert.Equal(first.State.Coins, replay.State.Coins);
            Assert.Single(replay.State.Ledger, l => l.Kind == "stable-income");
        }

        [Fact]
        public void LegacyDaysStayIncomeFreeAndLaterV2DaysEarnOnTheSameSave()
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial());
            var legacyDay = store.CommitEndDay(Request(before, StableDailyPolicy.LegacyVersion), Legacy());
            Assert.Equal(0, legacyDay.Receipt.IncomeCoins); Assert.Equal(64, legacyDay.State.Coins);
            Assert.DoesNotContain(legacyDay.State.Ledger, l => l.Kind == "stable-income");
            var v2Day = store.CommitEndDay(Request(legacyDay.State, StableDailyPolicy.Version), new StableDayService());
            Assert.Equal(78, v2Day.State.Coins);
            var reloaded = new AtomicSaveStore(SavePath).Load().State;
            Assert.Equal(new[] { StableDailyPolicy.LegacyVersion, StableDailyPolicy.Version }, reloaded.DailyState.Receipts.Select(r => r.Request.PolicyVersion));
            Assert.Equal(new long[] { 0, 50 }, reloaded.DailyState.Receipts.Select(r => r.IncomeCoins));
            Assert.Equal(78, reloaded.Coins);
        }

        [Fact]
        public void ServiceRejectsARequestFromAnotherPolicyVersion()
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial());
            var exception = Assert.Throws<EndDayRejectedException>(() => store.CommitEndDay(Request(before, StableDailyPolicy.LegacyVersion), new StableDayService()));
            Assert.Equal("policy_unknown", exception.Code); Assert.Equal(100, new AtomicSaveStore(SavePath).Load().State.Coins);
        }

        [Fact]
        public void MissingAlteredOrOrphanIncomeCreditsAreRejected()
        {
            var store = Store(); var before = store.Initialize(SaveTestData.Initial());
            var v2 = store.CommitEndDay(Request(before, StableDailyPolicy.Version), new StableDayService()).State;
            var credit = v2.Ledger.Single(l => l.Kind == "stable-income");
            Assert.Throws<SaveIntegrityException>(() => WithLedger(v2, v2.Ledger.Where(l => !l.Id.Equals(credit.Id))));
            Assert.Throws<SaveIntegrityException>(() => WithLedger(v2, v2.Ledger.Select(l => l.Id.Equals(credit.Id)
                ? new CoinLedgerEntry(l.Id, 500, "stable-income") : l)));
            Assert.Throws<SaveIntegrityException>(() => WithLedger(v2, v2.Ledger.Concat(new[]
                { new CoinLedgerEntry(new TransactionId(Guid.NewGuid()), 50, "stable-income") })));

            store = new AtomicSaveStore(Path.Combine(directory, "legacy.clover")); before = store.Initialize(SaveTestData.Initial());
            var legacy = store.CommitEndDay(Request(before, StableDailyPolicy.LegacyVersion), Legacy()).State;
            var receipt = legacy.DailyState.Receipts.Single();
            Assert.Throws<SaveIntegrityException>(() => WithLedger(legacy, legacy.Ledger.Concat(new[]
                { new CoinLedgerEntry(receipt.IncomeTransactionId, 50, "stable-income") })));
        }
    }
}
