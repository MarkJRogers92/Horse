using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-031 Pip's daily tasks.
    public sealed class DailyTasksTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-daily-" + Guid.NewGuid().ToString("N"));
        private string SavePath => Path.Combine(directory, "stable.clover");
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private (AtomicSaveStore Store, StableSnapshot State) Starter()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(SavePath, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Stable", "Fixtures", "old_management_initial_v3.clvr")));
            var store = new AtomicSaveStore(SavePath); var initial = store.Load().State;
            return (store, store.EnableStarterBreeding(initial.PlayerOwnerId, initial.Header.SaveRevision));
        }

        private static StableSnapshot EndDay(AtomicSaveStore store, StableSnapshot state)
        {
            var plan = new CarePlan(state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable
                && h.LifeState.Stage != LifeStage.Unborn).Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
            var preview = new StableDayService().Preview(state, plan);
            Assert.True(preview.Allowed, preview.BlockReason);
            return store.CommitEndDay(preview.Receipt!.Request, new StableDayService()).State;
        }

        private static StableSnapshot With(StableSnapshot s, params CoinLedgerEntry[] extra) => new StableSnapshot(s.Header, s.PlayerOwnerId, s.Horses, s.OpeningCoinBalance,
            s.CapacityRevision, s.Ledger.Concat(extra), s.PendingCapacityPlans, s.Conceptions, s.DailyState, s.LegacyLifecycleBaselines);

        // Does whatever each of today's tasks asks, including entering the show or cup when the harder slot is one of them.
        private static StableSnapshot FinishAll(AtomicSaveStore store, StableSnapshot state)
        {
            foreach (var task in DailyTasks.For(state).ToList())
            {
                if (task.IsDone(state)) continue;
                var parts = task.Id.Split(':');
                if (parts[0] == "love")
                {
                    foreach (var h in state.Horses.Where(h => h.LifeState.Stage != LifeStage.Unborn && !HorseMarket.IsSold(h)).ToList())
                        if (!HorseBond.Interactions.Any(k => HorseBond.DoneToday(state.Horses.Single(x => x.HorseId.Equals(h.HorseId)), k, state.Header.CalendarDay)))
                            state = store.CommitInteraction(state.PlayerOwnerId, h.HorseId, "pat", state.Header.SaveRevision);
                }
                else if (parts[0] == "show")
                {
                    var horse = state.Horses.First(h => HorseShow.BlockReason(state, h.HorseId) == null).HorseId;
                    state = store.CommitShowEntry(state.PlayerOwnerId, horse, state.Header.SaveRevision, HorseShow.EntryFee).State;
                }
                else if (parts[0] == "cup")
                {
                    var horse = state.Horses.First(h => ClubRace.BlockReason(state, h.HorseId) == null).HorseId;
                    state = store.CommitClubRace(state.PlayerOwnerId, horse, state.Header.SaveRevision).State;
                }
                else state = store.CommitInteraction(state.PlayerOwnerId, state.Horses.Single(h => h.HorseId.ToString() == parts[1]).HorseId, parts[0], state.Header.SaveRevision);
            }
            return state;
        }

        private static StableSnapshot ClaimAll(AtomicSaveStore store, StableSnapshot state)
        {
            foreach (var task in DailyTasks.For(state).ToList())
                state = store.CommitDailyTaskReward(state.PlayerOwnerId, task.Id, state.Header.SaveRevision);
            return state;
        }

        [Fact]
        public void TasksAreDeterministicPerDayAndAlwaysThreeForAStarterSave()
        {
            var (store, state) = Starter();
            var a = DailyTasks.For(state); var b = DailyTasks.For(state);
            Assert.Equal(DailyTasks.TasksPerDay, a.Count);
            Assert.Equal(a.Select(t => t.Id), b.Select(t => t.Id));
            Assert.Equal(a.Select(t => t.Id).Distinct().Count(), a.Count);
            Assert.Equal(new long[] { 10, 10, 15 }, a.Select(t => t.Reward));
            Assert.All(a, t => Assert.False(string.IsNullOrWhiteSpace(t.Title)));
            Assert.All(a, t => Assert.False(t.IsDone(state)));
            var next = state; var lists = new[] { string.Join("|", a.Select(t => t.Id)) }.ToList();
            for (var i = 0; i < 4; i++)
            {
                next = EndDay(store, next);
                var list = DailyTasks.For(next);
                Assert.Equal(3, list.Count); Assert.Equal(3, list.Select(t => t.Id).Distinct().Count());
                lists.Add(string.Join("|", list.Select(t => t.Id)));
            }
            Assert.True(lists.Distinct().Count() > 1, "the list should change from day to day");
        }

        [Fact]
        public void ACareTaskCompletesAfterTheInteractionAndIsClaimedOnce()
        {
            var (store, state) = Starter();
            var task = DailyTasks.For(state)[0];
            Assert.True(task.Id.StartsWith("pat:") || task.Id.StartsWith("brush:") || task.Id.StartsWith("carrot:"));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitDailyTaskReward(state.PlayerOwnerId, task.Id, state.Header.SaveRevision)); // not finished
            var parts = task.Id.Split(':');
            state = store.CommitInteraction(state.PlayerOwnerId, state.Horses.Single(h => h.HorseId.ToString() == parts[1]).HorseId, parts[0], state.Header.SaveRevision);
            Assert.Equal(task.Id, DailyTasks.For(state)[0].Id);
            Assert.True(DailyTasks.For(state)[0].IsDone(state));
            var coins = state.Coins;
            var claimed = store.CommitDailyTaskReward(state.PlayerOwnerId, task.Id, state.Header.SaveRevision);
            Assert.Equal(coins + 10, claimed.Coins);
            Assert.True(DailyTasks.IsClaimedToday(claimed, DailyTasks.For(claimed)[0]));
            var again = store.CommitDailyTaskReward(claimed.PlayerOwnerId, task.Id, claimed.Header.SaveRevision);
            Assert.Equal(claimed.Coins, again.Coins); Assert.Equal(claimed.Header.SaveRevision, again.Header.SaveRevision);
            Assert.Throws<StableCommandRejectedException>(() => store.CommitDailyTaskReward(claimed.PlayerOwnerId, "pat:" + Guid.NewGuid().ToString("N"), claimed.Header.SaveRevision));
            Assert.Equal(claimed.Coins, new AtomicSaveStore(SavePath).Load().State.Coins);
        }

        [Fact]
        public void TheThirdClaimAddsAStreakBonusThatGrowsOverConsecutiveDays()
        {
            var (store, state) = Starter();
            for (var day = 1; day <= 8; day++)
            {
                state = FinishAll(store, state);
                var tasks = DailyTasks.For(state);
                Assert.Equal(3, tasks.Count);
                Assert.All(tasks, t => Assert.True(t.IsDone(state), t.Id));
                var before = state.Coins; var expectedStreak = Math.Min(day, 7);
                state = ClaimAll(store, state);
                Assert.Equal(tasks.Sum(t => t.Reward) + 5 * expectedStreak, state.Coins - before);
                Assert.Equal(5 * expectedStreak, DailyTasks.StreakCreditOn(state, state.Header.CalendarDay));
                Assert.Equal(expectedStreak, DailyTasks.CurrentStreak(state));
                state = new AtomicSaveStore(SavePath).Load().State; // the saved ledger validates
                state = EndDay(store, state);
                Assert.Equal(expectedStreak, DailyTasks.CurrentStreak(state)); // yesterday's run is still shown
            }
            Assert.Equal(DailyTasks.MaxStreakCredit, state.Ledger.Where(l => l.Kind == DailyTasks.StreakLedgerKind).Max(l => l.DeltaCoins));
        }

        [Fact]
        public void ASkippedDayBreaksTheStreak()
        {
            var (store, state) = Starter();
            state = ClaimAll(store, FinishAll(store, state));
            state = EndDay(store, state); // a day with nothing done
            state = EndDay(store, state);
            Assert.Equal(0, DailyTasks.CurrentStreak(state));
            state = ClaimAll(store, FinishAll(store, state));
            Assert.Equal(5, DailyTasks.StreakCreditOn(state, state.Header.CalendarDay));
        }

        [Fact]
        public void ForgedDailyTaskCreditsAreRejected()
        {
            var (_, state) = Starter();
            var save = state.Header.SaveId; var day = state.Header.CalendarDay;
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(new TransactionId(Guid.NewGuid()), 10, DailyTasks.TaskLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(DailyTasks.TaskCreditId(save, day, 0), 1000, DailyTasks.TaskLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(DailyTasks.TaskCreditId(save, day, 0), 15, DailyTasks.TaskLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(DailyTasks.TaskCreditId(save, day + 5, 0), 10, DailyTasks.TaskLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(DailyTasks.TaskCreditId(save, day, 3), 10, DailyTasks.TaskLedgerKind)));
            Assert.Throws<SaveIntegrityException>(() => With(state, new CoinLedgerEntry(DailyTasks.StreakCreditId(save, day), 5, DailyTasks.StreakLedgerKind)));
            var full = new[] { new CoinLedgerEntry(DailyTasks.TaskCreditId(save, day, 0), 10, DailyTasks.TaskLedgerKind),
                new CoinLedgerEntry(DailyTasks.TaskCreditId(save, day, 1), 10, DailyTasks.TaskLedgerKind),
                new CoinLedgerEntry(DailyTasks.TaskCreditId(save, day, 2), 15, DailyTasks.TaskLedgerKind) };
            Assert.Throws<SaveIntegrityException>(() => With(state, full.Concat(new[] { new CoinLedgerEntry(DailyTasks.StreakCreditId(save, day), 35, DailyTasks.StreakLedgerKind) }).ToArray()));
            Assert.Throws<SaveIntegrityException>(() => With(state, full.Concat(new[] { new CoinLedgerEntry(DailyTasks.StreakCreditId(save, day), 5, DailyTasks.StreakLedgerKind),
                new CoinLedgerEntry(new TransactionId(Guid.NewGuid()), 5, DailyTasks.StreakLedgerKind) }).ToArray()));
            Assert.Equal(state.Coins + 40, With(state, full.Concat(new[] { new CoinLedgerEntry(DailyTasks.StreakCreditId(save, day), 5, DailyTasks.StreakLedgerKind) }).ToArray()).Coins);
        }
    }
}
