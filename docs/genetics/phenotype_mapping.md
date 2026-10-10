# Phenotype mapping and release boundary (P1-06)

## One authoritative resolver

`PhenotypeResolver.ResolveGenetics` validates the exact catalog/layout and the horse's pinned resolver version before interpreting genotype. It returns either a deterministic `PhenotypeResolution` or explicit `Unsupported`. No missing allele, unknown resolver, unsupported pattern haplotype or unimplemented interaction is replaced with a nearby color. This is an internal genetic interpretation, not a player-knowledge projection and not approval to render live stock.

`ResolveForPresentation` additionally consumes the shared coat matrix. It requires every necessary class and every non-common layer to be fully released, then creates one renderer-only descriptor. Material recipes, label, body family and bounded morph values all come from the same resolved state. Matrix and asset versions enter the cache key. No current matrix class has released art, so the real matrix intentionally returns Unsupported for every live descriptor. Test-only promoted matrices never modify that file.

## Expression order

1. MC1R e/e gives chestnut pigment regardless of retained ASIP. Otherwise A expresses bay points, a/a black.
2. The two SLC45A2 calls jointly resolve normal, single cream, double cream, pearl or cream/pearl compound. N/Prl is a retained carrier state, not visible pearl. Diagnostic camel-case labels match the frozen fixture; normal display labels never expose hidden carrier status.
3. Dun adds body dilution and primitive markings. nd1 without D adds faint primitive markings but no dun body dilution. Existing base/cream recipes remain. Cream+dun needs the separate dunalino family; double cream+dun requires an explicit class that is not yet in the matrix, so presentation is unsupported.
4. Silver changes black-pigment hair presentation, but is masked on chestnut while Z remains inheritable. LP is required before PATN1 coverage has any effect. Catalogue state and appearance are not interchangeable.
5. Simple champagne, grey and LP internal prototypes have separate recipe/overlay requirements. The current matrix has no released classes for them. Grey's 0/5000/9000 foal/adult/senior curve and LP coverage strengths are declared artistic parameters, not a clinical clock or a measured phenotype predictor. Grey G is the documented simplified catalog state, not a complete copy-number simulation.
6. Advanced modifier combinations are deliberately Unsupported until a versioned expression rule and tests exist. A stacked layer is not accepted as proof that a complicated combination is scientifically modeled. Baseline-only KIT/pattern regions remain enforced by the inheritance catalog.
7. Face/leg white and shading use permanent artistic realization layers. Their key depends on the fixed appearance seed and resolver version, never camera, age, mesh family, cosmetic ribbon or LOD. Actual matching across real meshes is still an art acceptance test, not demonstrated by domain tests.

## Fictional inherited traits and bodies

32 virtual binary markers feed 16 traits. Each pair contributes bounded centered integer effects (500 and 350 units); speed has a turning trade-off, power an endurance trade-off. All effects and linkage are invented game parameters. Coat loci, pedigree generation count, parental/current training, prizes and market rarity contribute nothing to inherited ability. The model provides potential/development values, not a racing engine.

Fixed developmental factors are limited to known trait keys and +/-500; the optional seed-derived generator supplies +/-200. Body morphs stay within +/-2000 in 1/10000-style normalized units. Two logical body families (standard/compact) derive from chest-width vs body-length parameters and map to separately named juvenile/adult assets. These IDs are requirements for future assets, not a claim that meshes exist or that anatomy has been visually approved.

## Live reachability boundary

`LiveCoatGuard.CheckPair` enumerates every supported outcome of a concrete pairing before a draw. It refuses a pairing if an outcome is genetically unsupported, requires unreleased art, or exact enumeration exceeds its explicit limit. It never samples until a drawable foal appears. Tests demonstrate that separately supported cream and dun parents can create an unreleased combined class.

This is a concrete-pair safety primitive, NOT the full multi-generation founder/NPC closure proof. No live founder/NPC catalog has been authored; an empty or unreleased catalog is not presented as a successful release gate. The catalog-wide closure test, knowledge-limited forecast/commit API, atomic transactions and real save/reload remain P1-07/P1-09. Every supported saved cohort must retain its resolver version.

## Fixtures and current limitations

The xUnit suite enumerates every concrete genetics fixture directly from the unmodified frozen JSON; an unknown kind/validation case fails rather than being skipped. It checks exact phenotype and genotype probabilities, masked variants and linked gametes, plus all six pedigree coefficients and three pedigree validation cases. Added slice cases cover dun, nd1, base+cream composition, deterministic drafts, grey age presentation, immutable markings, bounds and matrix gates.

The strings under `crossSystemAcceptanceProposals` are future integration requirements, not all implemented tests: disk save/reload, transaction idempotence, actual-vs-hypothetical player forecasts, market transfer and medical-policy content still belong to their owning later tasks. Classical roan and nonbaseline pattern alleles are not silently claimed implemented. A passing headless descriptor test is not a horse render or a playable game.
