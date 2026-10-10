# D-001 — Build genetics and horse visuals in parallel, gated by one coat support matrix

| Field | Value |
| --- | --- |
| Decision ID | D-001 |
| Date | 2026-10-10 |
| Status | **Accepted** by owner, 2026-10-10 |
| Owner | integration_lead |

## Question

Should the genetics simulation be finished before the horse generation and rendering pipeline, or should both be built at the same time?

## Chosen result

**Build them in parallel, but not as two independent tracks.** Both tracks work against three shared artifacts that are agreed before either track writes production code:

1. **Coat support matrix** (`docs/contracts/COAT_SUPPORT_MATRIX.json`). It lists every coat class, the render layers it needs, and a separate `geneticStatus`, `visualStatus` and `releaseStatus` for each class. **A class is live only when `releaseStatus <= min(geneticStatus, visualStatus)`.** `tools/validate_handoff.py` checks this rule.
2. **HorseVisualDescriptor** (already in `handoff/CONTRACT_CATALOG.json`). This is the only data the renderer receives. The renderer never looks at genotype.
3. **Visual fixture set.** Each coat class in the matrix has a neutral-lighting reference and a genotype that produces it. The genetics track uses these genotypes as test inputs, and the art track uses them as render targets.

### Mechanisms that prevent the "500 combinations vs. 40 drawable" problem

- **Render layers, not combinations.** The renderer stacks about 15 layers (base pigment, single cream, double cream, dun, white masks, grey age, and so on). The number of possible genotypes doesn't matter. The art cost is the number of layers plus a short, explicit list of layer interactions that need custom work (`interactionsNeedingBespokeArt`). Those interactions are where the real risk is, so they get scheduled and tested first.
- **Reachability test (domain unit test).** This test starts from the live founder and NPC catalog, lists every coat class that live breeding can produce, and fails if any of them is not `released`. The genetics engine can model more than the renderer supports, for example in tests or a sandbox, but those extra coats can never appear in a player's save.
- **No silent fallback.** If the resolver gets a class it cannot render, it returns an explicit `Unsupported` result. It never substitutes a nearby colour. A wrong colour would give the player a false view of the horse's inheritance, which is worse than a visible error.
- **Contact-sheet render.** The visual fixture harness (P1-03) renders every class marked `prototype` or higher as one grid image in the neutral scene. That image is reviewed whenever the matrix changes, so gaps show up in a single picture.

## Why parallel, not genetics first

- The two tracks don't need each other's code. They only need the shared contracts above. The genetics engine is pure C# and can be tested without Unity. The art track can render against hand-written descriptors.
- If genetics were built first, the art team would sit idle, and the catalog would grow past what can be drawn, which is exactly the risk GPT identified.
- If visuals were built first, the art team would be guessing which layers the genetics need, and the descriptor contract would get rewritten.
- The real critical path is the **hero horse asset** (mesh, rig, UVs, masks), not the genetics. The genetics slice (base coats, cream, dun, pedigree, commit, save) is about one to two weeks of focused domain work with exact fixtures. So genetics should not hold anything up, and it should not run far ahead of the art either.

## Alternatives considered

- **Genetics first.** Rejected: the catalog could outgrow what the renderer can draw, and the visual gate would come too late.
- **Visuals first.** Rejected: the mask and layer vocabulary would be invented without knowing the genetic needs.
- **Fully independent parallel tracks.** Rejected: this is the 500-vs-40 risk.

## Order of work (slice scope = matrix phase 1, 13 classes)

1. **Day 0–2, together:** lock the matrix's phase-1 classes, layer list and anatomical mask regions, and freeze the `HorseVisualDescriptor` fields for phase 1.
2. **Genetics track:** phased genome → meiosis → resolver → descriptor, with every phase-1 class reachable from a fixture genotype. Then add pedigree, commit and save.
3. **Art track:** hero adult mesh with mask regions → layer shader → contact sheet of the 13 classes → foal.
4. **Join:** `CommitBreeding` → foal descriptor → the same presenter in the viewer, the thumbnail and the stable. This is the Phase 1 exit gate.
5. Each later family (tobiano, then the phase 5 families) is added in one place: one matrix row, one fixture, one layer, then a contact-sheet review.

## Affected contracts, tasks and saves

- New contract: `COAT_SUPPORT_MATRIX.json`. `HorseVisualDescriptor` should get a `coatClassId` field and a `visualCapabilityVersion` field.
- P0-03 and P0-04 jointly produce the phase-1 matrix. P1-03 adds the contact sheet. P1-06 adds the `Unsupported` result. P1-07 adds the reachability test.
- Saves: none exist yet, so there is no migration.

## Acceptance evidence

The reachability test passes, the contact sheet shows every `released` class, and the validator passes.
