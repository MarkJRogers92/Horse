using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>One of Pip's three tasks for the day.</summary>
    public sealed class DailyTask
    {
        internal DailyTask(int slot, string id, string title, long reward, Func<StableSnapshot, bool> isDone)
        { Slot = slot; Id = id; Title = title; Reward = reward; _isDone = isDone; }
        private readonly Func<StableSnapshot, bool> _isDone;
        /// <summary>0-2; the last slot is the harder one.</summary>
        public int Slot { get; }
        /// <summary>Stable text such as "pat:{horseId}", "love:all", "show:week" or "cup:week".</summary>
        public string Id { get; }
        public string Title { get; }
        public long Reward { get; }
        /// <summary>Derived from the save alone (never stored).</summary>
        public bool IsDone(StableSnapshot state) => _isDone(Check.NotNull(state, nameof(state)));
    }

    /// <summary>
    /// D-031 Pip's daily tasks. Each calendar day Pip offers three small tasks (10, 10 and 15 coins) chosen deterministically
    /// from the save and the day, plus a streak bonus when all three are claimed on consecutive days. Whether a task is done is
    /// derived from the save (bond day markers, this week's show and cup entries); claiming writes one idempotent ledger credit
    /// whose id comes from the save, the day and the slot, so a task can be claimed only once. No save schema change.
    ///
    /// A task must not vanish when finishing it changes the state it was offered for, so only tasks whose offer does not depend
    /// on the thing they change are used: care tasks (a day marker), "love every horse", and the show and cup, which are offered
    /// only on the first day of a fair week (nothing can have been entered earlier that week). DNA tests and tack were left out
    /// for that reason. Candidates are ranked by hash of save, day and candidate (rendezvous hashing), so a small change in the
    /// candidate set rarely reshuffles today's list.
    /// </summary>
    public static class DailyTasks
    {
        public const string Version = "daily-tasks.v1";
        public const string TaskLedgerKind = "daily-task";
        public const string StreakLedgerKind = "daily-streak";
        public const int TasksPerDay = 3;
        public const long EasyReward = 10;
        public const long HardReward = 15;
        public const long StreakStep = 5;
        public const int MaxStreak = 7;
        public const long MaxStreakCredit = StreakStep * MaxStreak;

        public static long RewardForSlot(int slot) => slot == TasksPerDay - 1 ? HardReward : EasyReward;
        public static bool IsRewardAmount(long coins) => coins == EasyReward || coins == HardReward;

        private static TransactionId Id(string kind, SaveId saveId, int day, params string[] more) => new TransactionId(Guid.ParseExact(
            Canonical.Hash(new[] { kind, Check.NotNull(saveId, nameof(saveId)).ToString(), day.ToString(CultureInfo.InvariantCulture) }.Concat(more)).Substring(0, 32), "N"));
        public static TransactionId TaskCreditId(SaveId saveId, int day, int slot) => Id("daily-tasks.task.v1", saveId, day, slot.ToString(CultureInfo.InvariantCulture));
        public static TransactionId StreakCreditId(SaveId saveId, int day) => Id("daily-tasks.streak.v1", saveId, day);

        private static IReadOnlyList<HorseRecord> Horses(StableSnapshot state) => state.Horses
            .Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && !HorseMarket.IsSold(h) && h.LifeState.Stage != LifeStage.Unborn)
            .OrderBy(h => h.HorseId.ToString(), StringComparer.Ordinal).ToArray();

        private static string Score(StableSnapshot state, string candidate) => Canonical.Hash(Version, state.Header.SaveId.ToString(),
            state.Header.CalendarDay.ToString(CultureInfo.InvariantCulture), candidate);

        private static DailyTask Care(int slot, string kind, HorseRecord horse)
        {
            var name = horse.Name; var id = horse.HorseId;
            var title = kind switch
            {
                "pat" => $"Give {name} a pat",
                "brush" => $"Brush {name} until they gleam",
                _ => $"Bring {name} a carrot",
            };
            return new DailyTask(slot, kind + ":" + id, title, RewardForSlot(slot),
                s => s.Horses.FirstOrDefault(h => h.HorseId.Equals(id)) is HorseRecord h && HorseBond.DoneToday(h, kind, s.Header.CalendarDay));
        }

        private static DailyTask FestivalTreat(int slot, FestivalInfo festival, HorseRecord horse)
        {
            var id = horse.HorseId;
            return new DailyTask(slot, Festivals.TreatKind + ":" + id, $"Give {horse.Name} a {festival.TreatLower}", RewardForSlot(slot),
                s => s.Horses.FirstOrDefault(h => h.HorseId.Equals(id)) is HorseRecord h && Festivals.DoneToday(h, s.Header.CalendarDay));
        }

        private static DailyTask LoveAll(int slot) => new DailyTask(slot, "love:all", "Show some love to every horse", RewardForSlot(slot),
            s => { var all = Horses(s); return all.Count > 0 && all.All(h => HorseBond.Interactions.Any(k => HorseBond.DoneToday(h, k, s.Header.CalendarDay))); });

        private static bool FirstDayOfWeek(StableSnapshot state) => state.Header.CalendarDay % HorseFair.DaysPerWeek == 0;

        /// <summary>Today's three tasks (none only when the player has no born horse to care for).</summary>
        public static IReadOnlyList<DailyTask> For(StableSnapshot state)
        {
            Check.NotNull(state, nameof(state));
            var horses = Horses(state);
            if (horses.Count == 0) return Array.Empty<DailyTask>();
            var easy = horses.SelectMany(h => HorseBond.Interactions.Select(k => (Kind: k, Horse: h)))
                .OrderBy(c => Score(state, c.Kind + ":" + c.Horse.HorseId), StringComparer.Ordinal).ToList();
            var first = easy[0];
            // Prefer a second task for a different horse and kind; with a single horse any other interaction will do.
            var rest = easy.Skip(1).ToList();
            var different = rest.FindIndex(c => !c.Horse.HorseId.Equals(first.Horse.HorseId) && c.Kind != first.Kind);
            var second = rest[different < 0 ? 0 : different];
            var tasks = new List<DailyTask> { Care(0, first.Kind, first.Horse), Care(1, second.Kind, second.Horse) };
            // D-032: during a festival week the second task is the festival treat (its offer depends only on the day and the horse).
            if (Festivals.Current(state.Header.CalendarDay) is { } festival)
            {
                var pick = horses.OrderBy(h => Score(state, "treat:" + h.HorseId), StringComparer.Ordinal).First();
                tasks[1] = FestivalTreat(1, festival, pick);
            }
            // Slot 2, the harder one. At most one coin-spending task, and only on the first day of a fair week.
            var hard = new List<(string Key, Func<DailyTask> Make)>();
            if (horses.Count >= 2) hard.Add(("love:all", () => LoveAll(2)));
            if (FirstDayOfWeek(state))
            {
                if (HorseShow.EnteredThisWeek(state) || horses.Any(h => HorseShow.BlockReason(state, h.HorseId) == null))
                    hard.Add(("show:week", () => new DailyTask(2, "show:week", "Enter this week's Coat & Turnout Show", HardReward, HorseShow.EnteredThisWeek)));
                if (ClubRace.EnteredThisWeek(state) || horses.Any(h => ClubRace.BlockReason(state, h.HorseId) == null))
                    hard.Add(("cup:week", () => new DailyTask(2, "cup:week", "Race in this week's Willowbrook Cup", HardReward, ClubRace.EnteredThisWeek)));
            }
            if (hard.Count > 0) tasks.Add(hard.OrderBy(h => Score(state, h.Key), StringComparer.Ordinal).First().Make());
            else
            {
                var third = easy.Skip(1).First(c => c.Kind + ":" + c.Horse.HorseId != tasks[0].Id && c.Kind + ":" + c.Horse.HorseId != tasks[1].Id);
                tasks.Add(Care(2, third.Kind, third.Horse));
            }
            return tasks;
        }

        /// <summary>Has this slot's reward been paid for the given day?</summary>
        public static bool IsClaimed(StableSnapshot state, int day, int slot) =>
            Check.NotNull(state, nameof(state)).Ledger.Any(l => l.Id.Equals(TaskCreditId(state.Header.SaveId, day, slot)));
        public static bool IsClaimedToday(StableSnapshot state, DailyTask task) => IsClaimed(state, state.Header.CalendarDay, Check.NotNull(task, nameof(task)).Slot);

        private static bool AllClaimed(StableSnapshot state, int day) => day >= 0 && Enumerable.Range(0, TasksPerDay).All(s => IsClaimed(state, day, s));

        /// <summary>Consecutive fully claimed days ending at the given day (inclusive), capped at seven.</summary>
        private static int RunEndingOn(StableSnapshot state, int day)
        {
            var length = 0;
            while (length < MaxStreak && AllClaimed(state, day - length)) length++;
            return length;
        }

        /// <summary>Streak length counting the given day, which must be fully claimed (yesterday's run plus one, capped at seven).</summary>
        public static int StreakLengthOn(StableSnapshot state, int day) => Math.Min(MaxStreak, 1 + RunEndingOn(state, day - 1));

        /// <summary>The streak shown to the player: today's run if all three are claimed, otherwise the run that ended yesterday.</summary>
        public static int CurrentStreak(StableSnapshot state)
        {
            Check.NotNull(state, nameof(state));
            var day = state.Header.CalendarDay;
            return RunEndingOn(state, AllClaimed(state, day) ? day : day - 1);
        }

        public static long StreakCreditOn(StableSnapshot state, int day) =>
            Check.NotNull(state, nameof(state)).Ledger.SingleOrDefault(l => l.Id.Equals(StreakCreditId(state.Header.SaveId, day)))?.DeltaCoins ?? 0;

        /// <summary>Why this task cannot be claimed now, or null.</summary>
        public static string? BlockReason(StableSnapshot state, string taskId)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(taskId, nameof(taskId));
            var task = For(state).FirstOrDefault(t => t.Id == taskId);
            if (task == null) return "that is not one of today's tasks.";
            if (IsClaimedToday(state, task)) return "you already collected that reward today.";
            if (!task.IsDone(state)) return "that task is not finished yet.";
            return null;
        }

        internal static StableSnapshot ApplyClaim(StableSnapshot state, OwnerId requester, string taskId, ulong expectedRevision)
        {
            if (!state.PlayerOwnerId.Equals(requester)) throw new StableCommandRejectedException("ownership", "Only the stable owner can collect Pip's rewards.");
            if (state.Header.SaveRevision != expectedRevision) throw new StableCommandRejectedException("stale_save", "The stable changed; reload first.");
            var block = BlockReason(state, taskId);
            if (block != null) throw new StableCommandRejectedException("not_available", "Pip says " + block);
            var task = For(state).Single(t => t.Id == taskId); var day = state.Header.CalendarDay;
            var ledger = new List<CoinLedgerEntry>(state.Ledger) { new CoinLedgerEntry(TaskCreditId(state.Header.SaveId, day, task.Slot), task.Reward, TaskLedgerKind) };
            var withClaim = new StableSnapshot(state.Header, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance, state.CapacityRevision, ledger,
                state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
            if (AllClaimed(withClaim, day))
                ledger.Add(new CoinLedgerEntry(StreakCreditId(state.Header.SaveId, day), StreakStep * StreakLengthOn(withClaim, day), StreakLedgerKind));
            var header = SaveHeaders.Copy(state.Header, revision: checked(state.Header.SaveRevision + 1), schema: StableSnapshot.SchemaVersion, payloadHash: "unwritten");
            return new StableSnapshot(header, state.PlayerOwnerId, state.Horses, state.OpeningCoinBalance, state.CapacityRevision, ledger,
                state.PendingCapacityPlans, state.Conceptions, state.DailyState, state.LegacyLifecycleBaselines);
        }

        /// <summary>
        /// Save validation. Past days' task lists cannot be re-derived (the state that made them possible has changed), like the
        /// Cup and Show, so this checks shape: every credit's id belongs to a (day up to today, slot), the amount is the slot's reward,
        /// and a streak credit exists only for a day with all three task credits and equals 5 coins per streak day (at most 35).
        /// </summary>
        internal static string? LedgerProblem(StableSnapshot state)
        {
            var tasks = state.Ledger.Where(l => l.Kind == TaskLedgerKind).ToList();
            var streaks = state.Ledger.Where(l => l.Kind == StreakLedgerKind).ToList();
            if (tasks.Count == 0 && streaks.Count == 0) return null;
            var taskSlots = new Dictionary<TransactionId, int>(); var streakDays = new Dictionary<TransactionId, int>();
            for (var day = 0; day <= state.Header.CalendarDay; day++)
            {
                for (var slot = 0; slot < TasksPerDay; slot++) taskSlots[TaskCreditId(state.Header.SaveId, day, slot)] = slot;
                streakDays[StreakCreditId(state.Header.SaveId, day)] = day;
            }
            foreach (var entry in tasks)
                if (entry.BreedingId != null || !taskSlots.TryGetValue(entry.Id, out var slot) || entry.DeltaCoins != RewardForSlot(slot))
                    return "A daily task reward does not belong to a day and slot of this stable.";
            foreach (var entry in streaks)
                if (entry.BreedingId != null || !streakDays.TryGetValue(entry.Id, out var day) || !AllClaimed(state, day)
                    || entry.DeltaCoins != StreakStep * StreakLengthOn(state, day) || entry.DeltaCoins > MaxStreakCredit)
                    return "A daily streak bonus does not match three claimed tasks on consecutive days.";
            return null;
        }
    }
}
