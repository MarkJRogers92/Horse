# P1-09 — one saved stable and atomic conception state

## Snapshot boundary
StableSnapshot contains the existing SaveHeader, canonical HorseRecords, an opening wallet balance plus immutable typed ledger entries, confirmed capacity projections and PreparedConception history. Coin balance is derived rather than independently editable. This is the minimal production state root for the Phase-1 proof, not a separate horse model or complete economy/building simulation.

Cross-record validation rejects duplicate identities or transactions, orphan births/pregnancies, missing or inconsistent breeding debits, invalid pedigrees, dropped resolver versions and a breeding stream that disagrees with the contiguous committed event sequence. Historical parent/unborn records remain unchanged while current names, knowledge, training and permitted lifecycle state can advance. Inherited identity, genome, appearance seed, original development, origin and pedigree links cannot be silently rewritten.

ConceptionTransaction.Apply checks an existing command ID first. The exact original request returns the stored receipt without rechecking obsolete quotes or drawing randomness. A different payload under the same ID is a conflict. A new command validates the saved pending capacity hold and delegates eligibility/meiosis to ConceptionService. It produces one new state containing the debit, consumed hold, pregnancy, unborn horse, receipt and breeding-stream advance. No input is mutated on success or rejection.

This sub-step is an in-memory atomic transition, not yet a durable disk write. The next P1-09 sub-step adds the explicit bounded wire codec, same-directory replace/backup filesystem adapter and failure-injection tests. No future schema or renderer implementation is silently substituted. External stud authorization, the full calendar/building capacity manager, marketplace and birth UI remain later tasks.
