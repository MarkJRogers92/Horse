using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Cloverbrook.Domain
{
    public enum SaveWriteStage { TemporaryPartWritten, TemporaryWritten, TemporaryFlushed, BeforeReplace, AfterReplace }
    public sealed class SaveBusyException : IOException
    { public SaveBusyException(Exception inner) : base("Another writer holds this save. Retry after that operation finishes.", inner) { } }
    public sealed class SaveLoadResult
    {
        internal SaveLoadResult(StableSnapshot state, bool needsRecovery, string notice, string? quarantinedPath = null)
        { State = state; NeedsRecoveryAcknowledgment = needsRecovery; Notice = notice; QuarantinedPath = quarantinedPath; }
        public StableSnapshot State { get; }
        public bool NeedsRecoveryAcknowledgment { get; }
        public string Notice { get; }
        public string? QuarantinedPath { get; }
    }
    public sealed class SaveRecoveryRequiredException : IOException
    {
        public SaveRecoveryRequiredException(SaveLoadResult candidate) : base("The primary save is damaged or missing. Explicitly acknowledge recovery before making changes.")
        { RecoveryCandidate = candidate; }
        public SaveLoadResult RecoveryCandidate { get; }
    }
    public sealed class StoredConception
    {
        internal StoredConception(StableSnapshot state, PreparedConception receipt, bool replayed) { State = state; Receipt = receipt; Replayed = replayed; }
        public StableSnapshot State { get; }
        public PreparedConception Receipt { get; }
        public bool Replayed { get; }
        public bool IsDurablyCommitted => true;
    }
    public sealed class StoredEndDay
    {
        internal StoredEndDay(StableSnapshot state, DailyCareReceipt receipt, bool replayed) { State = state; Receipt = receipt; Replayed = replayed; }
        public StableSnapshot State { get; }
        public DailyCareReceipt Receipt { get; }
        public bool Replayed { get; }
        public bool IsDurablyCommitted => true;
    }

    // All cooperating writers share one persistent lock inode. Do not delete the
    // lockfile on disposal: unlinking it can permit two independent locks.
    // Publication uses a flushed same-directory temporary and File.Replace/Move,
    // never a truncate-and-rewrite of the current save. See documented limits on
    // platform/power-loss guarantees; failures are tested on actual files.
    public sealed class AtomicSaveStore
    {
        private readonly StableSaveCodec codec;
        private readonly Action<SaveWriteStage>? faultInjector;
        public AtomicSaveStore(string path, StableSaveCodec? codec = null, Action<SaveWriteStage>? faultInjector = null)
        {
            SavePath = Path.GetFullPath(Check.Text(path, nameof(path)));
            if (string.IsNullOrEmpty(Path.GetFileName(SavePath))) throw new ArgumentException("The save path must name a file.", nameof(path));
            this.codec = codec ?? new StableSaveCodec(); this.faultInjector = faultInjector;
        }
        public string SavePath { get; }
        public string BackupPath => SavePath + ".bak";
        public string LockPath => SavePath + ".lock";
        private FileStream Lock()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
            try { return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException e) { throw new SaveBusyException(e); }
        }
        public StableSnapshot Initialize(StableSnapshot initial)
        {
            Check.NotNull(initial, nameof(initial)); using var held = Lock();
            if (File.Exists(SavePath) || File.Exists(BackupPath)) throw new InvalidOperationException("A primary or backup already exists; initialization never overwrites it.");
            return Publish(initial, preserveCurrentAsBackup: false);
        }
        public SaveLoadResult Load() { using var held = Lock(); return LoadUnlocked(); }
        private SaveLoadResult LoadUnlocked()
        {
            try { return new SaveLoadResult(Read(SavePath), false, "Primary save verified."); }
            catch (Exception e) when (e is SaveIntegrityException || e is FileNotFoundException)
            {
                if (!File.Exists(BackupPath)) throw new SaveIntegrityException("No valid primary save and no backup are available. No empty replacement was created.", e);
                var backup = Read(BackupPath); // corruption/compatibility errors remain visible
                return new SaveLoadResult(backup, true, "Verified backup revision " + backup.Header.SaveRevision.ToString(CultureInfo.InvariantCulture)
                    + " is available. Recovery can discard changes after that checkpoint; acknowledgment is required before writing.");
            }
        }
        private StableSnapshot RequirePrimary()
        {
            var loaded = LoadUnlocked(); if (loaded.NeedsRecoveryAcknowledgment) throw new SaveRecoveryRequiredException(loaded); return loaded.State;
        }
        private StableSnapshot Read(string path)
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length < StableSaveCodec.FrameHeaderBytes || input.Length > StableSaveCodec.MaxPayloadBytes + StableSaveCodec.FrameHeaderBytes)
                throw new SaveIntegrityException("Save file length is outside the supported bounds.");
            var bytes = new byte[(int)input.Length]; var position = 0;
            while (position < bytes.Length)
            {
                var read = input.Read(bytes, position, bytes.Length - position);
                if (read == 0) throw new SaveIntegrityException("Save file was truncated while reading."); position += read;
            }
            if (input.ReadByte() != -1) throw new SaveIntegrityException("Save file grew while reading.");
            return codec.Decode(bytes);
        }
        public StoredConception CommitConception(ConceptionRequest request, ConceptionService service)
        {
            using var held = Lock(); var state = RequirePrimary();
            var result = ConceptionTransaction.Apply(state, request, service);
            if (result.Replayed) return new StoredConception(state, result.Receipt, true);
            var saved = Publish(result.State, preserveCurrentAsBackup: true);
            return new StoredConception(saved, saved.Conceptions.Single(c => c.Commit.BreedingCommitId.Equals(request.EventId)), false);
        }

        public StoredConception CommitStableBreeding(ConceptionRequest request, StableBreedingPlanner planner)
        {
            Check.NotNull(request, nameof(request)); Check.NotNull(planner, nameof(planner));
            using var held = Lock(); var state = RequirePrimary();
            var existing = state.Conceptions.SingleOrDefault(c => c.Commit.BreedingCommitId.Equals(request.EventId));
            if (existing != null)
            {
                if (existing.Request.RequestHash != request.RequestHash)
                    throw new ConceptionRejectedException("idempotency_conflict", "This pairing ID already belongs to a different request.");
                return new StoredConception(state, existing, true);
            }

            var quote = planner.Preview(state, request.ParentAId, request.ParentBId, request.EventId);
            if (quote.Request.RequestHash != request.RequestHash)
                throw new ConceptionRejectedException("quote_mismatch", "The saved stable no longer matches this quote; review the current pairing.");
            if (!quote.IsEligible)
            {
                var first = quote.Rejections[0];
                throw new ConceptionRejectedException(first.Code, string.Join("; ", quote.Rejections.Select(issue => issue.Message)));
            }
            var nextEventIndex = checked(state.Header.RngStreams.Breeding.EventCounter + 1);
            var costId = ConceptionService.CostTransactionId(state.Header, nextEventIndex);
            if (state.Ledger.Any(entry => entry.Id.Equals(costId))
                || state.DailyState.Receipts.Any(receipt => receipt.Request.OperationId.Equals(costId)
                    || receipt.CostTransactionId.Equals(costId) || receipt.IncomeTransactionId.Equals(costId)))
                throw new ConceptionRejectedException("ledger_id_conflict", "This pairing's deterministic cost identity already appears in saved wallet or care history.");

            // The hold exists only in this locked, in-memory transaction input.
            // ConceptionTransaction consumes it before the single durable publish.
            var withTransientHold = state.WithPendingCapacityPlans(state.PendingCapacityPlans.Concat(new[] { quote.Capacity }));
            var result = ConceptionTransaction.Apply(withTransientHold, request, planner.Service);
            if (result.State.PendingCapacityPlans.Any(p => p.ReservationId.Equals(request.ReservationId)))
                throw new SaveIntegrityException("The stable breeding transaction left its temporary capacity hold behind.");
            var saved = Publish(result.State, preserveCurrentAsBackup: true);
            return new StoredConception(saved, saved.Conceptions.Single(c => c.Commit.BreedingCommitId.Equals(request.EventId)), false);
        }

        public StableSnapshot EnableStarterBreeding(OwnerId requester, ulong expectedRevision)
        {
            Check.NotNull(requester, nameof(requester)); using var held = Lock(); var current = RequirePrimary();
            var markerPresent = current.Header.MigrationHistory.Contains(StableBreedingPlanner.StarterMigrationMarker, StringComparer.Ordinal);
            if (markerPresent)
            {
                ValidateUpgradedStarterPair(current, requester);
                return current;
            }
            if (!current.PlayerOwnerId.Equals(requester))
                throw new StableCommandRejectedException("ownership", "Only the owner can enable starter breeding.");
            if (current.Header.SaveRevision != expectedRevision)
                throw new StableCommandRejectedException("stale_save", "The stable changed; reload before enabling breeding.");
            if (current.Header.SaveMode != SaveMode.Sandbox || current.Header.GameBuildVersion != StableBreedingPlanner.GameBuildVersion
                || current.Header.CalendarProfileVersion != StableBreedingPlanner.CalendarProfileVersion)
                throw new StableCommandRejectedException("unsupported_save", "Starter breeding is available only in the stable-management Sandbox save.");
            if (current.Horses.Count != 2 || current.Conceptions.Count != 0 || current.PendingCapacityPlans.Count != 0)
                throw new StableCommandRejectedException("ambiguous_starters", "This save is not the untouched two-horse starter stable; no migration was applied.");

            var seed = current.Header.RngStreams.Breeding.Seed;
            var expectedJuniper = new Seed128(seed.High, seed.Low ^ 0x4a554e49504552UL);
            var expectedMaple = new Seed128(seed.High ^ 0x4d41504c45UL, seed.Low);
            var juniper = current.Horses.SingleOrDefault(h => h.AppearanceSeed == expectedJuniper);
            var maple = current.Horses.SingleOrDefault(h => h.AppearanceSeed == expectedMaple);
            if (juniper == null || maple == null || juniper.HorseId.Equals(maple.HorseId))
                throw new StableCommandRejectedException("ambiguous_starters", "The starter identities do not match the saved founder markers; no migration was applied.");

            foreach (var horse in current.Horses)
            {
                if (!horse.OwnerId.Equals(requester) || horse.HomeAssignment != HomeKind.ActiveStable
                    || horse.OriginRecord != "stable-day-starter" || horse.OriginCommitId != null || horse.BirthDay.HasValue
                    || horse.LifeState.Stage != LifeStage.Adult || horse.ReproductionState.Role != ReproductiveRole.NonBreeding
                    || horse.ReproductionState.PregnancyId != null || horse.ReproductionState.RecoveryUntilDay != 0
                    || horse.Parentage.ParentAId != null || horse.Parentage.ParentBId != null)
                    throw new StableCommandRejectedException("ambiguous_starters", "This save contains breeding history or changed starter records; no migration was applied.");
            }

            var changed = current.Horses.Select(h =>
            {
                var role = h.AppearanceSeed == expectedJuniper ? ReproductiveRole.Mare : ReproductiveRole.Stallion;
                var age = Math.Max(12, h.LifeState.DaysSinceConception);
                return new HorseRecord(h.HorseId, h.Name, h.Parentage, h.Genome, h.ResolverVersion, h.AppearanceSeed, h.OwnerId,
                    new LifeState(LifeStage.Adult, age, h.LifeState.VisualAge), new ReproductionState(role), h.DevelopmentalFactors,
                    h.InheritedGamePotential, h.TrainingState, h.PlayerKnowledge, h.CosmeticState, h.HomeAssignment,
                    h.OriginRecord, h.OriginCommitId, h.BirthDay, checked(h.Revision + 1));
            }).ToArray();
            var history = current.Header.MigrationHistory.Concat(new[] { StableBreedingPlanner.StarterMigrationMarker });
            var header = SaveHeaders.Copy(current.Header, revision: checked(current.Header.SaveRevision + 1), history: history, payloadHash: "unwritten");
            var migrated = new StableSnapshot(header, current.PlayerOwnerId, changed, current.OpeningCoinBalance,
                current.CapacityRevision, current.Ledger, current.PendingCapacityPlans, current.Conceptions, current.DailyState,
                current.LegacyLifecycleBaselines);
            return Publish(migrated, preserveCurrentAsBackup: true);
        }

        private static void ValidateUpgradedStarterPair(StableSnapshot state, OwnerId requester)
        {
            if (!state.PlayerOwnerId.Equals(requester) || state.Header.SaveMode != SaveMode.Sandbox
                || state.Header.GameBuildVersion != StableBreedingPlanner.GameBuildVersion
                || state.Header.CalendarProfileVersion != StableBreedingPlanner.CalendarProfileVersion)
                throw new StableCommandRejectedException("unsupported_save", "The starter breeding migration marker does not match a supported stable.");
            var seed = state.Header.RngStreams.Breeding.Seed;
            var juniper = new Seed128(seed.High, seed.Low ^ 0x4a554e49504552UL);
            var maple = new Seed128(seed.High ^ 0x4d41504c45UL, seed.Low);
            if (state.Horses.Count(h => h.OriginRecord == "stable-day-starter" && h.OwnerId.Equals(requester)
                && h.AppearanceSeed == juniper && h.ReproductionState.Role == ReproductiveRole.Mare) != 1
                || state.Horses.Count(h => h.OriginRecord == "stable-day-starter" && h.OwnerId.Equals(requester)
                    && h.AppearanceSeed == maple && h.ReproductionState.Role == ReproductiveRole.Stallion) != 1)
                throw new StableCommandRejectedException("invalid_migration_marker", "The saved starter migration marker is inconsistent with its founder records.");
        }
        public StoredEndDay CommitEndDay(EndDayRequest request, StableDayService service)
        {
            Check.NotNull(request, nameof(request)); Check.NotNull(service, nameof(service)); using var held = Lock(); var state = RequirePrimary();
            var existing = state.DailyState.Receipts.SingleOrDefault(r => r.Request.OperationId.Equals(request.OperationId));
            if (existing != null)
            {
                if (existing.Request.RequestHash != request.RequestHash)
                    throw new EndDayRejectedException("idempotency_conflict", "This command ID already belongs to a different daily-care request.");
                return new StoredEndDay(state, existing, true);
            }
            var costId = StableDayService.CostId(request.OperationId); var incomeId = StableDayService.IncomeId(request.OperationId);
            if (request.OperationId.Equals(costId) || request.OperationId.Equals(incomeId)
                || state.Ledger.Any(entry => entry.Id.Equals(request.OperationId) || entry.Id.Equals(costId) || entry.Id.Equals(incomeId)))
                throw new EndDayRejectedException("ledger_id_conflict", "This command's transaction identity is already used in the wallet history.");
            var result = StableDayTransaction.Apply(state, request, service);
            var saved = Publish(result.State, preserveCurrentAsBackup: true);
            return new StoredEndDay(saved, saved.DailyState.Receipts.Single(r => r.Request.OperationId.Equals(request.OperationId)), false);
        }
        // Trusted same-day updates for naming, training and confirmed capacity
        // projections. Cannot advance the calendar or rewrite conception, wallet,
        // daily-care or legacy lifecycle provenance. Adoption/market commands need transitions.
        public StableSnapshot SaveSnapshot(StableSnapshot replacement, ulong expectedRevision)
        {
            Check.NotNull(replacement, nameof(replacement)); using var held = Lock(); var current = RequirePrimary();
            return SaveSnapshotUnlocked(current, replacement, expectedRevision);
        }

        public StableSnapshot RenameHorse(OwnerId requester, HorseId horseId, ulong expectedSaveRevision,
            ulong expectedHorseRevision, string name)
        {
            Check.NotNull(requester, nameof(requester)); Check.NotNull(horseId, nameof(horseId));
            var normalizedName = (name ?? throw new ArgumentNullException(nameof(name))).Trim();
            if (normalizedName.Length == 0 || normalizedName.Length > 40)
                throw new StableCommandRejectedException("invalid_name", "A horse name must contain between 1 and 40 characters.");
            using var held = Lock(); var current = RequirePrimary();
            if (!current.PlayerOwnerId.Equals(requester)) throw new StableCommandRejectedException("ownership", "Only the stable owner can rename this horse.");
            if (current.Header.SaveRevision != expectedSaveRevision) throw new StableCommandRejectedException("stale_save", "The stable changed; reload before renaming.");
            var horse = current.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
            if (horse == null) throw new StableCommandRejectedException("horse_missing", "This horse is no longer in the stable.");
            if (!horse.OwnerId.Equals(requester)) throw new StableCommandRejectedException("ownership", "Only the horse's owner can rename it.");
            if (horse.LifeState.Stage == LifeStage.Unborn) throw new StableCommandRejectedException("not_born", "A foal can be named after birth.");
            if (horse.Revision != expectedHorseRevision) throw new StableCommandRejectedException("stale_horse", "This horse changed; reload before renaming.");
            if (horse.Name == normalizedName) return current;
            var horses = current.Horses.Select(h => h.HorseId.Equals(horseId) ? h.Rename(normalizedName) : h).ToArray();
            var replacement = current.WithHorses(horses).WithHeader(SaveHeaders.Copy(current.Header,
                revision: checked(current.Header.SaveRevision + 1), schema: StableSnapshot.SchemaVersion, payloadHash: "unwritten"));
            return SaveSnapshotUnlocked(current, replacement, expectedSaveRevision);
        }

        private StableSnapshot SaveSnapshotUnlocked(StableSnapshot current, StableSnapshot replacement, ulong expectedRevision)
        {
            if (current.Header.SaveRevision != expectedRevision) throw new ConceptionRejectedException("stale_save", "The on-disk revision changed; reload before saving.");
            if (!current.Header.SaveId.Equals(replacement.Header.SaveId) || !current.PlayerOwnerId.Equals(replacement.PlayerOwnerId)
                || replacement.Header.SaveRevision != checked(expectedRevision + 1) || replacement.Header.CalendarDay != current.Header.CalendarDay
                || replacement.CapacityRevision < current.CapacityRevision) throw new SaveIntegrityException("A replacement changed save identity or rewound a revision/calendar.");
            if (!current.Conceptions.Select(c => c.ReceiptHash).SequenceEqual(replacement.Conceptions.Select(c => c.ReceiptHash))
                || current.OpeningCoinBalance != replacement.OpeningCoinBalance || LedgerKey(current) != LedgerKey(replacement)
                || DailyKey(current.DailyState) != DailyKey(replacement.DailyState)
                || LifecycleBaselineKey(current) != LifecycleBaselineKey(replacement))
                throw new SaveIntegrityException("Generic saves cannot rewrite conception, wallet, care or legacy lifecycle history.");
            var before = current.Header.RngStreams.Breeding; var after = replacement.Header.RngStreams.Breeding;
            if (before.Seed != after.Seed || before.EventCounter != after.EventCounter || before.AlgorithmVersion != after.AlgorithmVersion)
                throw new SaveIntegrityException("Generic saves cannot advance or reseed breeding randomness.");
            var oldHorses = current.Horses.ToDictionary(h => h.HorseId);
            if (oldHorses.Count != replacement.Horses.Count || replacement.Horses.Any(h => !oldHorses.TryGetValue(h.HorseId, out var old)
                    || !StableSnapshot.SameInheritedIdentity(old, h) || h.Revision < old.Revision || !StableSnapshot.SameLifecycleState(old, h)))
                throw new SaveIntegrityException("Generic saves cannot add/remove horses, rewrite inheritance or rewind horse revisions.");
            return Publish(replacement, preserveCurrentAsBackup: true);
        }
        private static string LedgerKey(StableSnapshot state) => Canonical.Hash(state.Ledger.Select(l => Canonical.Hash(l.Id.ToString(), l.Kind,
            l.DeltaCoins.ToString(CultureInfo.InvariantCulture), l.BreedingId?.ToString() ?? "")));
        private static string DailyKey(StableDailyState daily) => Canonical.Hash(
            daily.Receipts.Select(r => r.ReceiptHash).Concat(daily.HorseCare.Select(p => Canonical.Hash(p.Key.ToString(),
                p.Value.Nutrition.ToString(CultureInfo.InvariantCulture), p.Value.Bedding.ToString(CultureInfo.InvariantCulture),
                p.Value.Turnout.ToString(CultureInfo.InvariantCulture), p.Value.Grooming.ToString(CultureInfo.InvariantCulture)))));
        private static string LifecycleBaselineKey(StableSnapshot state) => Canonical.Hash(state.LegacyLifecycleBaselines.Select(b => Canonical.Hash(
            b.HorseId.ToString(), b.Stage.ToString(), b.AgeDays.ToString(CultureInfo.InvariantCulture), b.RecordedDay.ToString(CultureInfo.InvariantCulture),
            b.BirthDay.ToString(CultureInfo.InvariantCulture), b.Role.ToString(), b.RecoveryUntilDay.ToString(CultureInfo.InvariantCulture))));

        public SaveLoadResult RecoverFromBackup(SaveId expectedSaveId, ulong expectedBackupRevision, string expectedPayloadHash)
        {
            Check.NotNull(expectedSaveId, nameof(expectedSaveId)); Check.Text(expectedPayloadHash, nameof(expectedPayloadHash)); using var held = Lock();
            StableSnapshot? healthy = null;
            try { healthy = Read(SavePath); }
            catch (Exception e) when (e is SaveIntegrityException || e is FileNotFoundException) { }
            // A newer/unknown primary throws compatibility before reaching here.
            if (healthy != null)
            {
                if (healthy.Header.SaveId.Equals(expectedSaveId) && healthy.Header.SaveRevision == expectedBackupRevision && healthy.Header.PayloadHash == expectedPayloadHash)
                    return new SaveLoadResult(healthy, false, "This exact recovery checkpoint is already the healthy primary.");
                throw new InvalidOperationException("A healthy primary will not be rolled back to its backup.");
            }
            var backup = Read(BackupPath);
            if (!backup.Header.SaveId.Equals(expectedSaveId) || backup.Header.SaveRevision != expectedBackupRevision || backup.Header.PayloadHash != expectedPayloadHash)
                throw new SaveIntegrityException("The recovery candidate changed; inspect and acknowledge the exact backup again.");
            string? quarantine = null;
            if (File.Exists(SavePath))
            {
                quarantine = SavePath + ".corrupt." + Guid.NewGuid().ToString("N");
                File.Copy(SavePath, quarantine, false);
                using var evidence = new FileStream(quarantine, FileMode.Open, FileAccess.ReadWrite, FileShare.None); evidence.Flush(true);
            }
            // Never overwrite a valid backup with the corrupt primary during recovery.
            var restored = Publish(backup, preserveCurrentAsBackup: false);
            return new SaveLoadResult(restored, false, "Acknowledged backup restored. Changes after that checkpoint were not invented or silently merged.", quarantine);
        }
        private StableSnapshot Publish(StableSnapshot state, bool preserveCurrentAsBackup)
        {
            var bytes = codec.Encode(state); var verified = codec.Decode(bytes);
            var temporary = SavePath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var halfway = bytes.Length / 2; output.Write(bytes, 0, halfway); faultInjector?.Invoke(SaveWriteStage.TemporaryPartWritten);
                    output.Write(bytes, halfway, bytes.Length - halfway); faultInjector?.Invoke(SaveWriteStage.TemporaryWritten);
                    output.Flush(true); faultInjector?.Invoke(SaveWriteStage.TemporaryFlushed);
                }
                faultInjector?.Invoke(SaveWriteStage.BeforeReplace);
                if (File.Exists(SavePath)) File.Replace(temporary, SavePath, preserveCurrentAsBackup ? BackupPath : null);
                else File.Move(temporary, SavePath);
                faultInjector?.Invoke(SaveWriteStage.AfterReplace);
                return verified;
            }
            finally
            {
                // A process kill can leave this file behind; Load ignores all
                // temporaries rather than guessing that one was committed.
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
