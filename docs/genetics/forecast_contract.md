# P1-07 — knowledge-limited coat forecasts

## Authority boundary
`ForKnownParents` accepts only typed horse IDs, already revealed calls, an optional already-public coat observation, and an explicitly disclosed population allele scope. It cannot receive an authoritative HorseRecord or GenomeSnapshot. Do not populate observations by reading a hidden-genotype diagnostic; an unidentified foal's grey status, for example, is not an observed test result. Public pools describe what is possible in a versioned population, not frequencies. No live scope/catalog is enabled by this implementation.

The service enumerates every allowed phased diplotype consistent with the public calls. Unphased laboratory calls do not magically reveal linkage phase. Known ancestors remain in the knowledge cache key but do not invent genotype calls. Biological alleles never default to normal. Excessive ambiguity returns Unavailable; it does not truncate the set or quietly assign equal prior weights.

When more than one parent genotype is possible, each possible pairing has its own exact distribution. The UI receives the minimum and maximum conditional probability for each visible outcome. Ranges need not sum to one. An outcome has an ExactProbability only when every admissible scenario agrees. No hidden-parent hash participates in the cache key.

`Hypothetical` is a separate API with immutable Hypothetical authority. It can inspect caller-entered genomes but can never become evidence about the selected real horses or supply a conception outcome. Forecasts never debit funds, reserve housing, advance a breeding stream, or commit a child.

## One inheritance implementation
Both paths call the production MeiosisService and PhenotypeResolver. Coat-only analysis substitutes neutral fictional markers after validating the model, because those markers have no coat effects in phenotype.v1. It does not predict inherited athletic potential or a specific body from this neutral representation. Exemplars explicitly say that body and markings are illustrative.

Small scenarios are exact rational arithmetic. A single fully specified scenario exceeding the exact budget falls back to deterministic Monte Carlo; explicit sampling is also available. Sample metadata includes the count, empirical estimates and approximate 95% pointwise Wilson score intervals. These are not simultaneous guarantees, and unobserved categories remain possible. Sampling never fabricates priors over unknown parental genotypes. Unsupported genetic expression stays an outcome bucket rather than disappearing or being rerolled.

Example descriptors use only purpose-derived forecast seeds. They pass through the unchanged release matrix and return Unsupported while no real art is released. They are never the committed foal. Cache keys include public evidence/scope, algorithm, options and full matrix support content, not just a caller-supplied version string.

## Scope / integration
P1-07 files map the frozen Runtime/Breeding/Forecasts and Tests/Domain/Forecasts paths into the accepted headless src/tests layout. Full multi-generation catalog closure and the minimal conception API are the next sub-step. P1-09 owns durable atomic saving; P2 owns the complete gameplay transaction, market and lifecycle UI. The frozen handoff is unchanged.
