# Phased inheritance (P1-04)

`MeiosisService` is the single production inheritance implementation. It reads the same immutable `InheritanceModel` for exact enumeration and seeded sampling. Each homolog keeps ordered calls; a gamete chooses a starting homolog and changes source only at explicitly modeled boundaries. One gamete from each actual parent forms the child. No averaging, mutation, viability filtering, phenotype-based reroll, inherited training or generation bonus exists.

## Supported model and evidence

`CoreGeneticsCatalog` carries source metadata, versioned allele sets and 32 explicitly fictional binary game markers in eight four-marker blocks. The markers and their 10% adjacent transition fractions are invented game parameters, not equine genetic findings. Quantitative marker-to-trait effects are P1-06 work.

Biological calls include the frozen fixture scope: E/e, A/a, N/Cr/Prl at one SLC45A2 locus, D/nd1/nd2, silver, champagne, a simplified grey state, LP and PATN1. The pattern-region baseline is represented explicitly. Nonbaseline KIT/tobiano/MITF/PAX3/EDNRB haplotypes are rejected, not independently toggled. MC1R and the fixed ECA3 pattern baseline share a block. Its zero transition is a slice restriction with no segregating pattern variant, not a claimed empirical recombination rate. The catalog does not implement a complete physical chromosome map. Other locus blocks are supported model abstractions; expanding multi-variant linkage requires a new reviewed map/catalog version, not a guessed distance.

The handoff's source references support the narrow coat rules; source metadata here is not a claim of new scientific review or full family release. Advanced alleles can exist in tests/internal simulation while their visual results remain unavailable. D-001's separate live release gate applies in P1-06/P1-07.

## Exact and sampled outputs

Exact probabilities are reduced BigInteger fractions. Allele-identical gamete paths are aggregated with a representative provenance path; a real sampled gamete records its actual source indices and crossover boundaries. Exact block paths and complete outcome distributions have explicit limits; exceeding a limit throws `ExactEnumerationLimitException`, never a normalized partial table.

A restrictive haplotype catalog must be closed under every enabled transition for the submitted parent. Otherwise the whole operation is unsupported, before sampling. This prevents outcome-dependent rejection from secretly changing published odds. Unknown alleles, missing blocks, mismatched versions and unsupported cis combinations fail closed.

Purpose labels are fixed: `gameteA`, `gameteB`, `appearance`, `development`; each gamete uses a local `gamete/block/<blockId>` stream. Ordering unrelated blocks cannot consume another block's random numbers. `ConceptionDraft` is deterministic computation only. It neither persists a pregnancy nor debits money/advances a save stream. Transaction and atomic save acceptance remain later work.

## Evidence

xUnit reads `handoff/GENETICS_FIXTURES.json` directly through the existing copy-to-output harness. It checks the synthetic 45/45/5/5 linked fixture exactly, zero/free-recombination endpoints, shared cream/pearl outcomes, allele provenance, 10,000 deterministic samples against exact probabilities (six binomial standard deviations plus two counts), unsupported states, enumeration limits and preview-isolated draft bytes. Coat-label probabilities are completed by the resolver fixture adapter in P1-06. No frozen fixture is edited.
