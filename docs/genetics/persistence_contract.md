# P1-09 — one saved stable and atomic conception state

## Snapshot boundary
StableSnapshot contains the existing SaveHeader, canonical HorseRecords, an opening wallet balance plus immutable typed ledger entries, confirmed capacity projections and PreparedConception history. Coin balance is derived rather than independently editable. This is the minimal production state root for the Phase-1 proof, not a separate horse model or complete economy/building simulation.

Cross-record validation rejects duplicate identities or transactions, orphan births/pregnancies, missing or inconsistent breeding debits, invalid pedigrees, dropped resolver versions and a breeding stream that disagrees with the contiguous committed event sequence. Historical parent/unborn records remain unchanged while current names, knowledge, training and permitted lifecycle state can advance. Inherited identity, genome, appearance seed, original development, origin and pedigree links cannot be silently rewritten.

## Conception and durability
ConceptionTransaction.Apply checks an existing command ID first. The exact original request returns the stored receipt without rechecking obsolete quotes or drawing randomness. A different payload under the same ID is a conflict. A new command validates the saved pending capacity hold and delegates eligibility/meiosis to ConceptionService. It produces one new state containing the debit, consumed hold, pregnancy, unborn horse, receipt and breeding-stream advance. No input is mutated on success or rejection.

The pure transition and PreparedConception are not durability claims. The implemented AtomicSaveStore.CommitConception is the production entry point for writing the entire transition to disk under the exclusive writer lock. It returns StoredConception only after publication or when an identical receipt already exists in the verified primary. A retry after an interrupted acknowledgment is safe without another foal or charge.

StableSaveCodec is the single explicit bounded wire codec; see save_wire_format.md. AtomicSaveStore handles same-directory replacement, last-known-good backups, expected revisions and acknowledged recovery; see atomic_save_store.md. No future schema or renderer implementation is silently substituted. The numeric codec uses the same 512-character invariant rational-component limit on writing and reading; exceeding it is an explicit error, not rounding.

## Integration and evidence
StableBreedingQueries is the public read/preview facade over these records, not a second implementation. See godot_domain_api.md for the exact usage example and public-knowledge boundary. The combined source a15266e3 passed 147/147 tests and the specification validator in GitHub Actions run 38094410593 (job 114337226950). The persistence source checkpoint 51f26128 had already passed 138/138 in run 38094262761. Both actual logs were inspected.

Coverage includes public forecast -> conception -> real file -> fresh store/reload -> exact retry, byte-stable serialization, historical parents, corrupted/future files, rolling backup replacement and injected pre/post-publication failures. The combined suite additionally exercises adversarial checksum-valid mutation and a 3,000-horse save/closure budget.

Inspected Actions evidence is Linux .NET 8. The concurrent integration checkpoint additionally records a local macOS run; neither is an actual power-cut/process-kill experiment or Windows certification. External stud authorization, the full calendar/building capacity manager, marketplace and birth UI remain later tasks. Test-only released coat rows never modify the real matrix; playable breeding remains gated on approved art.
