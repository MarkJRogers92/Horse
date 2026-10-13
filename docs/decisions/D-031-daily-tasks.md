# D-031 — Pip's daily tasks

Owner instruction 2026-10-13: standing permission to refine and add features. Gives every day a small, cozy goal.

- **Rules (domain, `DailyTasks`, version `daily-tasks.v1`).** Each calendar day `DailyTasks.For(state)` returns three tasks chosen by hashing
  `Canonical.Hash(version, save id, day, candidate)` (highest hash wins, so small state changes rarely reshuffle the list). Slots 0 and 1 are
  care tasks (pat, brush or carrot a named horse, via `HorseBond.DoneToday`), 10 coins each. Slot 2 is the harder one, 15 coins: "Show some love
  to every horse" (needs two or more horses), or on the first day of a fair week "Enter this week's Coat & Turnout Show" or "Race in this
  week's Willowbrook Cup" (only when a horse can enter, or the entry already exists). At most one coin-spending task a day. `IsDone` is derived
  from the save and never stored.
- **Left out on purpose.** DNA-test, saddle pad and bow, and decoration tasks were not used. Their offer depends on the very thing they change
  (a horse stops being untested), so finishing the task would remove it from the list, and the save has no per-day marker to tell "done today"
  from "done long ago". The show and cup avoid this by being offered only on a week's first day, when nothing can have been entered earlier
  that week.
- **Claiming.** `AtomicSaveStore.CommitDailyTaskReward(owner, taskId, expectedRevision)` publishes a `daily-task` credit whose id derives from
  save, day and slot, only if the task is in today's list, done and unclaimed. A retry for an already collected task returns the current save.
  The claim that completes all three also publishes a `daily-streak` credit of 5 coins times the streak length (today plus the consecutive
  earlier days with all three claimed), capped at 7 days (35 coins). The streak is derived from the ledger.
- **Validation (`StableSnapshot`).** Every `daily-task` credit id must belong to a (day up to today, slot 0-2) and carry that slot's amount (10, 10
  or 15). A `daily-streak` credit must belong to a day up to today whose three slots are all credited, and equal 5 coins times that day's streak
  length (at most 35, one per day by id). As with the Cup and Show, past days' task lists are not re-derived: the state that made them possible
  has changed, so the check is on shape. Existing saves load unchanged (no schema change).
- **UI.** A "Pip's tasks today" card under Pip's bubble (x 368-824, from y 202): check box, title, coin reward and a Claim button once done, then
  "Collected". "Streak: N days" with a heart. Hidden while the first-session guide is showing (until it finishes or is skipped) and in the
  foaling demo. Journal ribbons: Pip's Helper (all three in a day) and Busy Week (a 7-day streak).
