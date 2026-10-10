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
        // Trusted domain updates for naming, training, calendar and confirmed
        // capacity projections. Cannot bypass conception or edit a coin history.
        // Adoption/market commands will need their own validated transitions.
        public StableSnapshot SaveSnapshot(StableSnapshot replacement, ulong expectedRevision)
        {
            Check.NotNull(replacement, nameof(replacement)); using var held = Lock(); var current = RequirePrimary();
            if (current.Header.SaveRevision != expectedRevision) throw new ConceptionRejectedException("stale_save", "The on-disk revision changed; reload before saving.");
            if (!current.Header.SaveId.Equals(replacement.Header.SaveId) || !current.PlayerOwnerId.Equals(replacement.PlayerOwnerId)
                || replacement.Header.SaveRevision != checked(expectedRevision + 1) || replacement.Header.CalendarDay < current.Header.CalendarDay
                || replacement.CapacityRevision < current.CapacityRevision) throw new SaveIntegrityException("A replacement changed save identity or rewound a revision/calendar.");
            if (!current.Conceptions.Select(c => c.ReceiptHash).SequenceEqual(replacement.Conceptions.Select(c => c.ReceiptHash))
                || current.OpeningCoinBalance != replacement.OpeningCoinBalance || LedgerKey(current) != LedgerKey(replacement))
                throw new SaveIntegrityException("Generic saves cannot create/rewrite conception or wallet history.");
            var before = current.Header.RngStreams.Breeding; var after = replacement.Header.RngStreams.Breeding;
            if (before.Seed != after.Seed || before.EventCounter != after.EventCounter || before.AlgorithmVersion != after.AlgorithmVersion)
                throw new SaveIntegrityException("Generic saves cannot advance or reseed breeding randomness.");
            var oldHorses = current.Horses.ToDictionary(h => h.HorseId);
            if (oldHorses.Count != replacement.Horses.Count || replacement.Horses.Any(h => !oldHorses.TryGetValue(h.HorseId, out var old)
                    || !StableSnapshot.SameInheritedIdentity(old, h) || h.Revision < old.Revision))
                throw new SaveIntegrityException("Generic saves cannot add/remove horses, rewrite inheritance or rewind horse revisions.");
            return Publish(replacement, preserveCurrentAsBackup: true);
        }
        private static string LedgerKey(StableSnapshot state) => Canonical.Hash(state.Ledger.Select(l => Canonical.Hash(l.Id.ToString(), l.Kind,
            l.DeltaCoins.ToString(CultureInfo.InvariantCulture), l.BreedingId?.ToString() ?? "")));

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
