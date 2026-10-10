# Narrowed Phase-1 record contracts (P0-03)

These executable C# contracts implement the six-record subset requested by STATUS.md. They are immutable snapshots, not command services or a shipped save format. The reviewed handoff stays frozen.

## Genome / identity ownership

`HorseRecord` owns enduring ID, actual parentage, origin/conception reference, append-once birth value, phased genome, resolver version, appearance seed and fixed developmental factors. Renaming returns a new revision without replacing these fields. Training, knowledge, cosmetics, housing and reproductive state are separate snapshots. The later management tasks will extend care, bond, album and competition history; no fake race/lease contracts are introduced here.

The proposal's sparse chromosomes/segments are normalized at supported linkage-block granularity as `PhasedBlockPair`: block ID, explicit biological/synthetic tag, ordered locus IDs, and two homolog arrays. This preserves cis/trans phase rather than saving independent per-locus random switches. Blocks do NOT assert whole real chromosomes or measured recombination distances. The versioned inheritance model must validate which loci can share each block and what transitions are supported. Nonbaseline KIT/tobiano variants cannot be enabled using a default independent-locus assumption.

Each locus belongs to one block. `GenomeSnapshot` defensively copies and sorts blocks ordinally while retaining homolog order; its canonical hash includes alleles, phase, biological/synthetic separation and both catalog/model versions. Parent and crossover provenance is retained in `GameteSnapshot`/`BreedingCommit`, with a separate provenance hash, rather than conflated with genotype identity. `GenomeSnapshot.FromGametes` only combines matching versioned layouts; it does not choose, mutate, filter or reroll alleles.

## Knowledge and art boundary

`PlayerKnowledgeRecord` contains only revealed calls and known ancestors, with its own cache hash. It is not the authoritative genome. A caller must not give `HorseRecord` or diagnostic `Phenotype` directly to a knowledge-limited UI; projection and tested forecast API restrictions are P1-07 work.

`Phenotype` holds one resolved interpretation, including diagnostic carrier labels, base pigment, layer IDs, bounded body parameters, age expression and associations. `HorseVisualDescriptor` contains only renderer recipes/parameters plus the D-001 coat class and capability version, never a genome/horse reference. Marking identity depends on the original appearance seed and pinned resolver, not age, mesh/LOD, scene, cosmetics or camera. A later art gate must approve descriptors; constructing a descriptor alone does not release a coat.

## Precision / provenance

Quantized vectors use integer units in [-10000,10000], with trait-specific interpretation defined by the trait model. Physiological genes and fictional traits remain distinct in `VariantCatalogRecord`. Fictional markers require an abstraction notice and cannot have a real gene/chromosome claim. Biological catalog entries require narrow source references. Review dates use ISO strings rather than unsupported DateOnly.

## Transaction / save boundary

`BreedingCommit` is a result contract with fixed parent IDs/revisions/genome hashes, gametes, unborn child identity/genome, event seed, derived appearance seed, schedule, reservation and cost transaction IDs. It checks structural consistency. The command layer must additionally validate allele provenance against the actual parents, eligibility, revisions and funds, then atomically persist the complete event, debit, reservations, unborn horse and stream advance. No such command or persistence service is claimed in P0-03.

`SaveHeader` preserves save mode, revisions, pacing versions, retained per-horse resolver versions, independent stream states and migration history. Atomic writes, cross-record integrity, idempotence and actual serialized migrations remain P1-09. Legacy records must not be silently recolored on loading. RaceEntrySnapshot and InstitutionalLease remain deferred as specified in the review.
