using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    // D-032 seasonal festivals: windows, the festival treat, reward tack unlocks.
    public sealed class FestivalTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cloverbrook-festival-" + Guid.NewGuid().ToString("N"));
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

        private static StableSnapshot AdvanceTo(AtomicSaveStore store, StableSnapshot state, int day)
        {
            while (state.Header.CalendarDay < day) state = EndDay(store, state);
            return state;
        }

        private static HorseRecord Horse(StableSnapshot s, HorseId id) => s.Horses.Single(h => h.HorseId.Equals(id));

        [Fact]
        public void EachSeasonHasAFestivalInItsThirdWeek()
        {
            foreach (var day in Enumerable.Range(0, 224))
            {
                var festival = Festivals.Current(day);
                var inSeason = day % 28;
                Assert.Equal(inSeason >= 14 && inSeason <= 20, festival != null);
                if (festival == null) continue;
                Assert.Equal(day / 112, festival.Year);
                Assert.Equal(20 - inSeason + 1, festival.DaysRemaining);
                Assert.Equal(new[] { "spring", "summer", "autumn", "winter" }[day / 28 % 4], festival.Id);
            }
            Assert.Equal("Blossom Fair", Festivals.Current(14)!.Name); Assert.Equal("Flower garland", Festivals.Current(14)!.TreatLabel);
            Assert.Equal("Summer Picnic", Festivals.Current(28 + 14)!.Name); Assert.Equal("Watermelon slice", Festivals.Current(42)!.TreatLabel);
            Assert.Equal("Harvest Festival", Festivals.Current(56 + 20)!.Name); Assert.Equal("Crunchy apple", Festivals.Current(76)!.TreatLabel);
            Assert.Equal("Winter Lights", Festivals.Current(84 + 17)!.Name); Assert.Equal("Peppermint", Festivals.Current(101)!.TreatLabel);
            Assert.Equal(1, Festivals.Current(112 + 14)!.Year); Assert.Equal("Blossom Fair", Festivals.Current(126)!.Name);
            Assert.Equal(7, Festivals.Current(14)!.DaysRemaining); Assert.Equal(1, Festivals.Current(20)!.DaysRemaining);
        }

        [Fact]
        public void NextReportsTheComingFestival()
        {
            Assert.Equal(("spring", 14), (Festivals.Next(0).Id, Festivals.Next(0).DaysUntilStart));
            Assert.Equal(1, Festivals.Next(13).DaysUntilStart);
            // During a festival Next is the following season's.
            Assert.Equal(("summer", 28), (Festivals.Next(14).Id, Festivals.Next(14).DaysUntilStart));
            Assert.Equal(("summer", 22), (Festivals.Next(20).Id, Festivals.Next(20).DaysUntilStart));
            Assert.Equal(("summer", 21), (Festivals.Next(21).Id, Festivals.Next(21).DaysUntilStart));
            // After the winter festival the next one is next year's spring.
            Assert.Equal(("spring", 21, 1), (Festivals.Next(105).Id, Festivals.Next(105).DaysUntilStart, Festivals.Next(105).Year));
        }

        [Fact]
        public void TreatsAreOnlyAvailableDuringTheFestivalAndOncePerHorsePerDay()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First().HorseId;
            Assert.Contains("no festival", Festivals.BlockReason(state, horse));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitFestivalTreat(state.PlayerOwnerId, horse, state.Header.SaveRevision));
            state = AdvanceTo(store, state, 13);
            Assert.Throws<StableCommandRejectedException>(() => store.CommitFestivalTreat(state.PlayerOwnerId, horse, state.Header.SaveRevision));
            state = EndDay(store, state);
            Assert.Equal(14, state.Header.CalendarDay);
            Assert.Null(Festivals.BlockReason(state, horse));
            var other = state.Horses.First(h => !h.HorseId.Equals(horse)).HorseId;

            var before = HorseBond.Bond(Horse(state, horse)); var coins = state.Coins; var ledger = state.Ledger.Count;
            state = store.CommitFestivalTreat(state.PlayerOwnerId, horse, state.Header.SaveRevision);
            Assert.Equal(before + 450, HorseBond.Bond(Horse(state, horse)));
            Assert.Equal(coins, state.Coins); Assert.Equal(ledger, state.Ledger.Count);
            Assert.Contains("flower garland", Festivals.BlockReason(state, horse));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitFestivalTreat(state.PlayerOwnerId, horse, state.Header.SaveRevision));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitFestivalTreat(state.PlayerOwnerId, horse, state.Header.SaveRevision - 1));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitFestivalTreat(state.PlayerOwnerId, new HorseId(Guid.NewGuid()), state.Header.SaveRevision));
            // Another horse is untouched, and the ordinary carrot is a separate once-a-day action.
            Assert.Equal(0, HorseBond.Bond(Horse(state, other)));
            Assert.Null(Festivals.BlockReason(state, other));
            Assert.Null(HorseBond.BlockReason(state, horse, "carrot"));
            // The treat raises bond with the same diminishing returns as the carrot.
            var carrotFirst = store.CommitInteraction(state.PlayerOwnerId, horse, "carrot", state.Header.SaveRevision);
            Assert.Equal(before + 450 + 400 * (10000 - (before + 450)) / 10000, HorseBond.Bond(Horse(carrotFirst, horse)));
            Assert.Throws<StableCommandRejectedException>(() => store.CommitFestivalTreat(state.PlayerOwnerId, horse, carrotFirst.Header.SaveRevision));
        }

        [Fact]
        public void TheCountGrowsAcrossDaysAndTheRewardUnlocksAtThreeAndStaysUnlocked()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First().HorseId;
            Assert.False(Festivals.RewardUnlocked(state, "spring"));
            Assert.Contains("3 days of the Blossom Fair", Tack.Find("bow.blossom")!.UnlockHint);
            Assert.NotNull(Tack.BlockReason(state, horse, Tack.BowSlot, "bow.blossom"));
            state = AdvanceTo(store, state, 14);
            for (var i = 1; i <= 3; i++)
            {
                Assert.False(Festivals.RewardUnlocked(state, "spring"));
                state = store.CommitFestivalTreat(state.PlayerOwnerId, horse, state.Header.SaveRevision);
                var festival = Festivals.Current(state.Header.CalendarDay)!;
                Assert.Equal(i, Festivals.TreatDays(Horse(state, horse), festival));
                Assert.Equal(i, Horse(state, horse).TrainingState.Get("fest_0_spring"));
                if (i < 3) state = EndDay(store, state);
            }
            Assert.True(Festivals.RewardUnlocked(state, "spring"));
            Assert.False(Festivals.RewardUnlocked(state, "summer"));
            Assert.Null(Tack.BlockReason(state, horse, Tack.BowSlot, "bow.blossom"));
            state = store.CommitTack(state.PlayerOwnerId, horse, Tack.BowSlot, "bow.blossom", state.Header.SaveRevision);
            Assert.Equal("bow=bow.blossom", Tack.Layer(Horse(state, horse)));
            // The festival ends and the reward stays; a different horse may be the one that wears it.
            state = AdvanceTo(store, state, 21);
            Assert.Null(Festivals.Current(state.Header.CalendarDay));
            Assert.True(Festivals.RewardUnlocked(state, "spring"));
            var other = state.Horses.First(h => !h.HorseId.Equals(horse)).HorseId;
            Assert.Null(Tack.BlockReason(state, other, Tack.BowSlot, "bow.blossom"));
            Assert.Contains("no festival", Festivals.BlockReason(state, horse));
        }

        [Fact]
        public void TwoDaysDoNotUnlockAndOtherSeasonsStayLocked()
        {
            var (store, state) = Starter();
            var horse = state.Horses.First().HorseId;
            state = AdvanceTo(store, state, 14);
            state = store.CommitFestivalTreat(state.PlayerOwnerId, horse, state.Header.SaveRevision);
            state = EndDay(store, state);
            state = store.CommitFestivalTreat(state.PlayerOwnerId, horse, state.Header.SaveRevision);
            Assert.Equal(2, Festivals.BestTreatDays(state, "spring"));
            Assert.False(Festivals.RewardUnlocked(state, "spring"));
            Assert.Equal(0, Festivals.RewardsUnlocked(state));
            Assert.All(new[] { "bow.blossom", "tail.sunflower", "braids.harvest", "halter.fairy" }, id => Assert.False(Tack.Find(id)!.IsUnlocked(state)));
            Assert.Equal(new[] { "bow", "tail", "braids", "halter" }, new[] { "bow.blossom", "tail.sunflower", "braids.harvest", "halter.fairy" }.Select(id => Tack.Find(id)!.Slot));
        }

        [Fact]
        public void TreatsSurviveASaveRoundTripAndTheDailyTaskUsesThem()
        {
            var (store, state) = Starter();
            state = AdvanceTo(store, state, 14);
            var task = DailyTasks.For(state).Single(t => t.Id.StartsWith("treat:"));
            Assert.Equal(1, task.Slot); Assert.Equal(10, task.Reward);
            Assert.Contains("flower garland", task.Title);
            Assert.False(task.IsDone(state));
            var target = state.Horses.Single(h => h.HorseId.ToString() == task.Id.Split(':')[1]).HorseId;
            state = store.CommitFestivalTreat(state.PlayerOwnerId, target, state.Header.SaveRevision);
            Assert.True(DailyTasks.For(state).Single(t => t.Id == task.Id).IsDone(state));
            Assert.Equal(3, DailyTasks.For(state).Count);
            var reloaded = new AtomicSaveStore(SavePath).Load().State;
            Assert.Equal(1, Horse(reloaded, target).TrainingState.Get("fest_0_spring"));
            Assert.True(Festivals.DoneToday(Horse(reloaded, target), reloaded.Header.CalendarDay));
            Assert.Equal(HorseBond.Bond(Horse(state, target)), HorseBond.Bond(Horse(reloaded, target)));
            Assert.Contains("flower garland", Festivals.BlockReason(reloaded, target));
            var claimed = store.CommitDailyTaskReward(reloaded.PlayerOwnerId, task.Id, reloaded.Header.SaveRevision);
            Assert.Equal(reloaded.Coins + 10, claimed.Coins);
            // Outside the festival the task list has no treat.
            var later = AdvanceTo(store, claimed, 21);
            Assert.DoesNotContain(DailyTasks.For(later), t => t.Id.StartsWith("treat:"));
        }
    }
}
