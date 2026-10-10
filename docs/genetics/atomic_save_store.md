# P1-09 — production filesystem publication and recovery

## Operations
AtomicSaveStore.Initialize refuses to overwrite either a primary or backup. Load verifies the explicit codec, payload checksums and cross-record invariants. CommitConception reads the real primary while holding the exclusive writer lock, applies the existing conception transition and publishes the entire result. Exact retries return the stored receipt before stale-state checks, without another debit, foal or stream advance. Conflicting command-ID reuse remains an error.

SaveSnapshot is a trusted non-breeding update path for naming/training/calendar/capacity projections. It requires the exact on-disk revision plus a one-step candidate revision and preserves the horse identity set, inherited state, wallet history, conception history and breeding stream. It cannot bypass conception or serve as an unvalidated adoption/market command. Those future systems must supply their own validated state transitions.

## Write protocol
Every cooperating store uses the same persistent `.lock` file opened with FileShare.None; disposing the handle releases the lock, but the file is not deleted (avoiding separate-inode lock races). New bytes are encoded and decoded for integrity before publication. A unique temporary is created beside the primary, written, and flushed with FileStream.Flush(true). File.Replace publishes over an existing primary and retains its last-known-good bytes at `.bak`; File.Move handles the first save. The primary is never truncated in place. Exceptions and stale requests do not cause a fallback overwrite. Orphan temporaries are ignored, not promoted by timestamps.

Fault injection exercises half-written and fully written temporaries, flushing, pre-replace interruption and the important ambiguous post-replace boundary. When a write was published but the caller did not receive success, restarting and retrying the same command returns the original committed receipt. There is no second charge or random draw.

## Recovery is acknowledged, never silent rollback
A checksum-invalid or missing primary can yield a verified backup candidate with NeedsRecoveryAcknowledgment. Commands refuse to write until the owner/UI explicitly acknowledges that exact save ID, revision and payload hash. RecoverFromBackup revalidates under the lock, quarantines a corrupt primary and publishes the good backup without replacing it with corrupt bytes. It refuses to roll back a healthy newer primary. Unknown future formats or absent schema/model implementations are compatibility failures, not corruption, so they never trigger an older-backup fallback. If neither copy is valid, no empty replacement stable is created.

## Guarantees and limits
This is a real BCL filesystem implementation tested with actual temporary files, not an in-memory mock. The initial evidence is Linux GitHub Actions with .NET 8; Windows/macOS filesystem and Godot integration tests remain release gates. Flush and same-directory replacement address tested process-interruption boundaries; this is not a universal power-loss, directory-fsync, network-filesystem or hostile out-of-process writer guarantee. The API expects a trusted local save path and cooperating writers. Checksums are not encryption or multiplayer anti-cheat. No actual legacy release exists; migration coverage uses explicit synthetic fixtures, not a claimed old-version compatibility guarantee.

API references: Microsoft File.Replace (System.IO) and FileStream.Flush(Boolean):
https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace
https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush

## Headless integration proof
The integration test forecasts a public known-parent cross, commits the actual authoritative parents to disk, opens another store instance and verifies the same phased foal genome, permanent appearance seed, developmental factors, parentage and commit hash. A forecast opened between operations cannot change the outcome. All art-release rows in these end-to-end tests are explicitly in-memory TEST-ONLY data. Real live breeding remains blocked until the actual renderer/assets receive approval.
