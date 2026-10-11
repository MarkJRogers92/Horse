using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class StableBreedingPlannerTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-stable-breeding-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", name));
        private AtomicSaveStore Store(Action<SaveWriteStage>? fault = null) => new AtomicSaveStore(SavePath, faultInjector: fault);
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private StableSnapshot Starter(string fixture = "old_management_initial_v3.clvr") => new StableSaveCodec().Decode(Fixture(fixture));

        private static (HorseId Mare, HorseId Stallion) Parents(StableSnapshot state) =>
            (state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Mare).HorseId,
                state.Horses.Single(h => h.ReproductionState.Role == ReproductiveRole.Stallion).HorseId);

        private static BreedingCommitId Event(int n) => new BreedingCommitId(Guid.Parse("ce000000-0000-0000-0000-" + n.ToString("D12")));

        private static StableSnapshot AdvanceOneDay(AtomicSaveStore store, StableSnapshot state)
        {
            var plan = new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId)
                && h.HomeAssignment == HomeKind.ActiveStable && h.LifeState.Stage != LifeStage.Unborn)
                .Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
            var preview = new StableDayService().Preview(state, plan);
            Assert.True(preview.Allowed, preview.BlockReason);
            return store.CommitEndDay(preview.Receipt!.Request, new StableDayService()).State;
        }

        [Theory]
        [InlineData("old_management_initial_v3.clvr", 0, 500)]
        [InlineData("old_management_care_v3.clvr", 1, 464)]
        public void StarterUpgradePreservesCurrentSaveAndIsIdempotent(string fixture, int day, long coins)
        {
            Directory.CreateDirectory(directory); var bytes = Fixture(fixture); File.WriteAllBytes(SavePath, bytes);
            var store = Store(); var before = store.Load().State;
            var original = before.Horses.ToDictionary(h => h.HorseId);
            var upgraded = store.EnableStarterBreeding(before.PlayerOwnerId, before.Header.SaveRevision);
            Assert.Equal(day, upgraded.Header.CalendarDay); Assert.Equal(coins, upgraded.Coins);
            Assert.Equal(before.DailyState.Receipts.Select(r => r.ReceiptHash), upgraded.DailyState.Receipts.Select(r => r.ReceiptHash));
            Assert.Equal(before.Header.RngStreams.Breeding.Seed, upgraded.Header.RngStreams.Breeding.Seed);
            Assert.Equal(before.Header.RngStreams.Breeding.EventCounter, upgraded.Header.RngStreams.Breeding.EventCounter);
            Assert.Contains(StableBreedingPlanner.StarterMigrationMarker, upgraded.Header.MigrationHistory);
            Assert.Equal(2, upgraded.Horses.Count);
            foreach (var horse in upgraded.Horses)
            {
                var old = original[horse.HorseId];
                Assert.Equal(old.Name, horse.Name); Assert.Equal(old.Genome.GenomeHash, horse.Genome.GenomeHash);
                Assert.Equal(old.AppearanceSeed, horse.AppearanceSeed);
                Assert.Equal(12, horse.LifeState.DaysSinceConception); Assert.Null(horse.BirthDay);
                Assert.Equal(old.Revision + 1, horse.Revision);
            }
            var savedBytes = File.ReadAllBytes(SavePath);
            var replay = store.EnableStarterBreeding(before.PlayerOwnerId, 0);
            Assert.Equal(upgraded.Header.SaveRevision, replay.Header.SaveRevision);
            Assert.Equal(savedBytes, File.ReadAllBytes(SavePath));
            Assert.Equal(ReproductiveRole.Mare, upgraded.Horses.Single(h => h.AppearanceSeed == new Seed128(
                before.Header.RngStreams.Breeding.Seed.High, before.Header.RngStreams.Breeding.Seed.Low ^ 0x4a554e49504552UL)).ReproductionState.Role);
        }

        [Fact]
        public void PreviewIsPureAndCommitUsesOneDurableTransientHoldWithExactRetry()
        {
            Directory.CreateDirectory(directory); var initial = Starter(); File.WriteAllBytes(SavePath, Fixture("old_management_initial_v3.clvr"));
            var store = Store(); var upgraded = store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision);
            var before = File.ReadAllBytes(SavePath); var planner = new StableBreedingPlanner(); var (mare, stallion) = Parents(upgraded);
            var quote = planner.Preview(upgraded, mare, stallion, Event(1));
            var quoteAgain = planner.Preview(upgraded, mare, stallion, Event(1));
            Assert.True(quote.IsEligible, string.Join("; ", quote.Rejections.Select(r => r.Message)));
            Assert.Equal(quote.Request.RequestHash, quoteAgain.Request.RequestHash);
            Assert.Equal(50, quote.CostCoins); Assert.Equal(4, quote.PlannedBirthDay); Assert.Equal(4, quote.CurrentCapacity);
            Assert.Equal(2, quote.CurrentCapacityUsed); Assert.Equal(3, quote.FutureCapacityUsed);
            Assert.Contains(quote.Warnings, warning => warning.Code == "unknown_ancestry");
            Assert.Equal(0UL, upgraded.Header.RngStreams.Breeding.EventCounter); Assert.Equal(before, File.ReadAllBytes(SavePath));

            var committed = store.CommitStableBreeding(quote.Request, planner);
            Assert.True(committed.IsDurablyCommitted); Assert.False(committed.Replayed);
            Assert.Equal(450, committed.State.Coins); Assert.Equal(1UL, committed.State.Header.RngStreams.Breeding.EventCounter);
            Assert.Empty(committed.State.PendingCapacityPlans); Assert.Equal(3, committed.State.Horses.Count);
            Assert.Equal(StableBreedingPlanner.PolicyVersion, committed.Receipt.Commit.CompatibilityRuleVersion);
            Assert.Equal(4, committed.Receipt.Commit.PlannedBirthDay);
            var saved = File.ReadAllBytes(SavePath);
            var retry = store.CommitStableBreeding(quote.Request, planner);
            Assert.True(retry.Replayed); Assert.Equal(saved, File.ReadAllBytes(SavePath));
            Assert.Equal(committed.Receipt.ReceiptHash, new StableSaveCodec().Decode(saved).Conceptions.Single().ReceiptHash);
            var conflicting = new ConceptionRequest(quote.Request.EventId, quote.Request.Requester, quote.Request.ParentBId,
                quote.Request.ParentAId, quote.Request.ExpectedSaveRevision, quote.Request.ExpectedParentBRevision,
                quote.Request.ExpectedParentARevision, quote.Request.ExpectedCapacityRevision, quote.Request.ReservationId,
                quote.Request.TermsVersion);
            Assert.Equal("idempotency_conflict", Assert.Throws<ConceptionRejectedException>(
                () => store.CommitStableBreeding(conflicting, planner)).Code);
        }

        [Fact]
        public void FutureAndCurrentCapacityCountEveryPendingHoldAndCanonicalHorseOnce()
        {
            var baseState = Starter(); var store = Store(); Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_management_initial_v3.clvr"));
            var upgraded = store.EnableStarterBreeding(baseState.PlayerOwnerId, baseState.Header.SaveRevision);
            var firstHold = Capacity(Event(20), Reservation(Event(20)), upgraded.CapacityRevision);
            var onePending = new StableSnapshot(upgraded.Header, upgraded.PlayerOwnerId, upgraded.Horses, upgraded.OpeningCoinBalance,
                upgraded.CapacityRevision, upgraded.Ledger, new[] { firstHold }, upgraded.Conceptions, upgraded.DailyState);
            var (mare, stallion) = Parents(onePending); var planner = new StableBreedingPlanner();
            var withOne = planner.Preview(onePending, mare, stallion, Event(21));
            Assert.True(withOne.IsEligible, string.Join("; ", withOne.Rejections.Select(r => r.Message)));
            Assert.Equal(3, withOne.CurrentCapacityUsed); Assert.Equal(4, withOne.FutureCapacityUsed);

            var secondHold = Capacity(Event(22), Reservation(Event(22)), upgraded.CapacityRevision);
            var twoPending = new StableSnapshot(upgraded.Header, upgraded.PlayerOwnerId, upgraded.Horses, upgraded.OpeningCoinBalance,
                upgraded.CapacityRevision, upgraded.Ledger, new[] { firstHold, secondHold }, upgraded.Conceptions, upgraded.DailyState);
            var overbooked = planner.Preview(twoPending, mare, stallion, Event(23));
            Assert.False(overbooked.IsEligible); Assert.Contains(overbooked.Rejections, issue => issue.Code == "capacity_future");
            Assert.Equal(5, overbooked.FutureCapacityUsed);

            var malformed = new BreedingCapacityPlan(Event(24), Reservation(Event(24)), upgraded.CapacityRevision,
                Array.Empty<ReservedCapacityWindow>());
            var unsupported = new StableSnapshot(upgraded.Header, upgraded.PlayerOwnerId, upgraded.Horses, upgraded.OpeningCoinBalance,
                upgraded.CapacityRevision, upgraded.Ledger, new[] { malformed }, upgraded.Conceptions, upgraded.DailyState);
            var blocked = planner.Preview(unsupported, mare, stallion, Event(25));
            Assert.False(blocked.IsEligible); Assert.Contains(blocked.Rejections, issue => issue.Code == "pending_capacity");
        }

        [Fact]
        public void SecondPairingWaitsForBirthRecoveryAndCannotExceedFourHorseCapacity()
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_management_initial_v3.clvr"));
            var store = Store(); var state = store.EnableStarterBreeding(Starter().PlayerOwnerId, 0);
            var planner = new StableBreedingPlanner(); var (mare, stallion) = Parents(state);
            var first = planner.Preview(state, mare, stallion, Event(30)); Assert.True(first.IsEligible, string.Join("; ", first.Rejections.Select(r => r.Message)));
            state = store.CommitStableBreeding(first.Request, planner).State;
            var duringPregnancy = planner.Preview(state, mare, stallion, Event(31));
            Assert.False(duringPregnancy.IsEligible); Assert.Contains(duringPregnancy.Rejections, issue => issue.Code == "pregnant");
            while (state.Header.CalendarDay < 6) state = AdvanceOneDay(store, state);
            var afterRecovery = planner.Preview(state, mare, stallion, Event(32));
            Assert.True(afterRecovery.IsEligible, string.Join("; ", afterRecovery.Rejections.Select(r => r.Message)));
            Assert.Equal(3, afterRecovery.CurrentCapacityUsed); Assert.Equal(4, afterRecovery.FutureCapacityUsed);
            state = store.CommitStableBreeding(afterRecovery.Request, planner).State;
            while (state.Header.CalendarDay < 10) state = AdvanceOneDay(store, state);
            Assert.Equal(4, state.Horses.Count);
            var fullStable = planner.Preview(state, mare, stallion, Event(33));
            Assert.False(fullStable.IsEligible);
            Assert.Contains(fullStable.Rejections, issue => issue.Code == "capacity_current" || issue.Code == "capacity_future");
            Assert.Equal(5, fullStable.FutureCapacityUsed);
        }

        [Fact]
        public void SupportPolicyRejectsNormalReleaseSavesAndStarterMigrationDoesNotGuess()
        {
            var sandbox = Starter(); var planner = new StableBreedingPlanner();
            var header = sandbox.Header;
            var standardHeader = new SaveHeader(header.SaveId, SaveMode.Standard, header.SchemaVersion, header.GameBuildVersion, header.SaveRevision,
                header.CalendarDay, header.CalendarProfileVersion, header.CareMode, header.ActiveVariantCatalogVersion, header.ActiveInheritanceModelVersion,
                header.DefaultOffspringResolverVersion, header.RetainedResolverVersions, header.RngStreams, header.LastCommittedBreedingEventIndex,
                header.MigrationHistory, header.PayloadHash);
            var standard = new StableSnapshot(standardHeader, sandbox.PlayerOwnerId, sandbox.Horses, sandbox.OpeningCoinBalance,
                sandbox.CapacityRevision, sandbox.Ledger, sandbox.PendingCapacityPlans, sandbox.Conceptions, sandbox.DailyState);
            var ids = standard.Horses.Select(h => h.HorseId).ToArray();
            Assert.False(planner.Preview(standard, ids[0], ids[1], Event(2)).IsEligible);
            Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_management_initial_v3.clvr"));
            var store = Store(); var current = store.Load().State; var renamedHorse = current.Horses[0];
            store.RenameHorse(current.PlayerOwnerId, renamedHorse.HorseId, current.Header.SaveRevision, renamedHorse.Revision, "Changed");
            var changed = store.Load().State;
            var upgraded = store.EnableStarterBreeding(changed.PlayerOwnerId, changed.Header.SaveRevision);
            Assert.Contains(upgraded.Horses, h => h.Name == "Changed");
            Assert.Equal(2, upgraded.Horses.Count); Assert.Contains(StableBreedingPlanner.StarterMigrationMarker, upgraded.Header.MigrationHistory);
        }

        [Fact]
        public void QuoteExplainsWalletRoleAndCalendarRejectionsAndCommitRejectsStaleQuote()
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_management_initial_v3.clvr"));
            var store = Store(); var initial = store.Load().State; var upgraded = store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision);
            var planner = new StableBreedingPlanner(); var (mare, stallion) = Parents(upgraded);
            var noFunds = new StableSnapshot(upgraded.Header, upgraded.PlayerOwnerId, upgraded.Horses, 49, upgraded.CapacityRevision,
                upgraded.Ledger, upgraded.PendingCapacityPlans, upgraded.Conceptions, upgraded.DailyState);
            Assert.Contains(planner.Preview(noFunds, mare, stallion, Event(40)).Rejections, issue => issue.Code == "funds");
            Assert.Contains(planner.Preview(upgraded, stallion, mare, Event(41)).Rejections, issue => issue.Code == "reproductive_roles");

            var h = upgraded.Header;
            var overflowHeader = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, h.SaveRevision, int.MaxValue,
                h.CalendarProfileVersion, h.CareMode, h.ActiveVariantCatalogVersion, h.ActiveInheritanceModelVersion,
                h.DefaultOffspringResolverVersion, h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex,
                h.MigrationHistory, h.PayloadHash);
            var overflow = new StableSnapshot(overflowHeader, upgraded.PlayerOwnerId, upgraded.Horses, upgraded.OpeningCoinBalance,
                upgraded.CapacityRevision, upgraded.Ledger, upgraded.PendingCapacityPlans, upgraded.Conceptions, upgraded.DailyState);
            Assert.Contains(planner.Preview(overflow, mare, stallion, Event(42)).Rejections, issue => issue.Code == "calendar_overflow");

            var quote = planner.Preview(upgraded, mare, stallion, Event(43)); Assert.True(quote.IsEligible);
            var founder = upgraded.Horses.Single(horse => horse.HorseId.Equals(mare));
            store.RenameHorse(upgraded.PlayerOwnerId, mare, upgraded.Header.SaveRevision, founder.Revision, "Juniper Two");
            var ex = Assert.Throws<ConceptionRejectedException>(() => store.CommitStableBreeding(quote.Request, planner));
            Assert.Equal("quote_mismatch", ex.Code); Assert.Empty(store.Load().State.Conceptions);
        }

        [Fact]
        public void ANewlyBirthedMareCannotBreedUntilHerRecordedRecoveryDay()
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_management_initial_v3.clvr"));
            var store = Store(); var state = store.EnableStarterBreeding(Starter().PlayerOwnerId, 0);
            var planner = new StableBreedingPlanner(); var (mare, stallion) = Parents(state);
            state = store.CommitStableBreeding(planner.Preview(state, mare, stallion, Event(50)).Request, planner).State;
            while (state.Header.CalendarDay < 4) state = AdvanceOneDay(store, state);
            var recoveryQuote = planner.Preview(state, mare, stallion, Event(51));
            Assert.False(recoveryQuote.IsEligible); Assert.Contains(recoveryQuote.Rejections, issue => issue.Code == "recovery");
            while (state.Header.CalendarDay < 6) state = AdvanceOneDay(store, state);
            var ready = planner.Preview(state, mare, stallion, Event(52));
            Assert.True(ready.IsEligible, string.Join("; ", ready.Rejections.Select(issue => issue.Message)));
        }

        [Fact]
        public void FailedAtomicPublicationLeavesOnlyReplayableBeforeOrAfterState()
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(SavePath, Fixture("old_management_initial_v3.clvr"));
            var store = Store(); var before = store.Load().State; var upgraded = store.EnableStarterBreeding(before.PlayerOwnerId, before.Header.SaveRevision);
            var (mare, stallion) = Parents(upgraded); var planner = new StableBreedingPlanner();
            var request = planner.Preview(upgraded, mare, stallion, Event(3)).Request;
            var original = File.ReadAllBytes(SavePath);
            var faulted = new AtomicSaveStore(SavePath, faultInjector: stage => { if (stage == SaveWriteStage.BeforeReplace) throw new IOException("fault before publication"); });
            Assert.Throws<IOException>(() => faulted.CommitStableBreeding(request, planner)); Assert.Equal(original, File.ReadAllBytes(SavePath));
            var afterReplace = new AtomicSaveStore(SavePath, faultInjector: stage => { if (stage == SaveWriteStage.AfterReplace) throw new IOException("fault after publication"); });
            Assert.Throws<IOException>(() => afterReplace.CommitStableBreeding(request, planner));
            Assert.NotEqual(original, File.ReadAllBytes(SavePath)); Assert.Single(Store().Load().State.Conceptions);
            var replay = store.CommitStableBreeding(request, planner); Assert.True(replay.Replayed);
        }

        private static BreedingCapacityPlan Capacity(BreedingCommitId eventId, CapacityReservationId reservationId, ulong revision) =>
            new BreedingCapacityPlan(eventId, reservationId, revision, new[]
            {
                new ReservedCapacityWindow(CapacityStage.Pregnancy, 0, 4, 4, 2, 0, true),
                new ReservedCapacityWindow(CapacityStage.Foal, 4, 7, 4, 2, 0, true),
                new ReservedCapacityWindow(CapacityStage.Youngster, 7, 10, 4, 2, 0, true),
                new ReservedCapacityWindow(CapacityStage.Adult, 10, null, 4, 2, 0, true)
            });

        private static CapacityReservationId Reservation(BreedingCommitId eventId) => new CapacityReservationId(Guid.ParseExact(
            Canonical.Hash("stable.text-breeding.reservation.v1", eventId.ToString()).Substring(0, 32), "N"));
    }
}
