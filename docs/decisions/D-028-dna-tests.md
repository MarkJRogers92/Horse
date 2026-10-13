# D-028 — DNA tests at the vet

Owner direction 2026-10-13: let players pay to learn a horse's real colour genes, so the breeding review's foal coat odds (D-027) can become exact.

## Decision

A DNA test costs 60 coins, once per horse. It reveals the horse's true calls at the coat loci the game uses (mc1r, asip,
slc45a2, tbx3, stx17, trpm1, patn1, pattern.eca3) and stores them in the existing `PlayerKnowledge.RevealedCalls`.
No save schema change. `FoalCoatOdds` already filters scenarios by revealed calls, so the odds narrow by themselves.

**Domain** (`src/Cloverbrook.Domain/Stable/Care/DnaTest.cs`)

- `DnaTest.Version = "dna-test.v1"`, `Price`, `Loci`, `TestId(horseId)` (deterministic, so a retry never pays twice).
- `BlockReason` explains in friendly words: not your horse, sold, unborn, already tested, not enough coins.
- `AtomicSaveStore.CommitDnaTest(requester, horseId, expectedRevision, quotedPrice)` publishes one `dna-test` ledger debit and the
  horse record with its calls (Revision + 1, known ancestors kept). A retry for an already-tested horse returns the current save.
- `StableSnapshot` validation: every `dna-test` debit is exactly the price and belongs to a tested horse, and no revealed call may
  contradict the genome (unordered match, only for loci the genome has). Old saves load unchanged.
- `DnaTest.Describe(locus, call)` gives short friendly wording; `KnownGeneticCall.Pair` exposes the call for it.

**Game**

- Details has a "DNA test · 60" button (disabled with a tooltip when blocked); it runs off the main thread, plays "ribbon", then
  reopens Details. "Known genetics" lists the friendly lines, and the empty state says a test costs 60 coins and makes odds exact.
- Journal ribbon "Gene Detective" (DNA-test three horses). Pip tip once two adults live at home and none is tested.

## Tests

`DnaTestTests`: exact calls and a 60-coin debit; second test refused and retry idempotent; reload round-trips; forged calls, wrong
amounts and orphan debits raise `SaveIntegrityException`; two tested D/D duns give "Bay dun" at exactly 100% where untested gave
ranges; friendly wording.
