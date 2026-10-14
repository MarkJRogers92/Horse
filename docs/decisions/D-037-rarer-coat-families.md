# D-037 — Silver, champagne, pearl, sabino-1 and splash white

Follows D-026. Silver, champagne and pearl were genetically modelled but refused in every combination, and sabino and
splash had no alleles. All are released now as overlays or dilutions over every phase-1 base, over the cream and dun
stacking, and over the D-026 patterns.

## Decision

Eight matrix classes are released (`releasedBy: "D-037"` in `docs/contracts/COAT_SUPPORT_MATRIX.json` and its
byte-identical game copy):

| Class | Layers | Genetics |
| --- | --- | --- |
| `silver_overlay` | silver | `pmel` Z, dominant; visible only on black and bay, masked on chestnut |
| `champagne_overlay` | champagne | `slc36a1` Ch, dominant |
| `pearl` | base_pigment, pearl | `slc45a2` Prl/Prl, recessive |
| `cream_pearl` | base_pigment, pearl | `slc45a2` Cr/Prl, the pseudo-double-dilute |
| `sabino1_overlay` | white_sabino1 | `pattern.eca3` SB1, dominant |
| `sabino1_double` | white_sabino1_double | SB1/SB1, sabino white (mostly white) |
| `splash_overlay` | white_splash | `pattern.mitf` SW1, dominant |
| `splash_double` | white_splash_double | SW1/SW1, extensive, blue eyes |

The dosage layers are separate layer ids so the descriptor needs no new field (a het horse requires only the first
layer; a homozygote also requires the second).

## Genetics (domain)

- `pattern.eca3` gains `SB1` after `TO` and `Rn`. The `eca3.core` block keeps its layout and gains the haplotypes
  E/SB1 and e/SB1 (zero recombination is still a documented slice restriction). `pattern.mitf` gains `SW1` after
  `baseline`. Only alleles are added, so every existing save validates unchanged.
- The resolver stays `phenotype.v1` and additive. `RarerCoatFamilyTests.EveryGenomeThatResolvedBeforeResolvesIdentically`
  hashes 4320 genomes (phase-1 loci, patterns, hidden pearl carriers) against a digest taken from the pre-D-037 resolver
  (f5ac025).
- The "advanced modifier" refusal is gone. Colour-changing layers compose without multiplying recipes:
  - silver and champagne over any dilution are layers on that dilution's recipe;
  - champagne alone has its own dilution string (`champagne`) and three recipes (gold, amber, classic);
  - pearl and cream-pearl have recipes alone and with dun (12 in all);
  - champagne over cream, pearl or dun keeps the stronger dilution's skin and eyes.
- Labels: "Silver black", "Silver bay", "Gold/Amber/Classic champagne", "Champagne buckskin", "Pearl bay",
  "Cream pearl black" (the pseudo-double-dilute, named for the colour underneath so it is never confused with a true
  cremello), "Bay sabino", "Bay sabino white", "Bay splash", "Bay splash white".
- An exhaustive test (20,736 genomes) resolves every combination of the coat loci with the real matrix and checks that
  each recipe, hair, skin, eye and layer exists in `game/data/visual_catalog.json`.

## Breeding forecast, DNA test, fair

- `FoalCoatOdds` adds options for pmel, slc36a1, pattern.mitf and the full KIT haplotype set. Hidden silver (on a
  chestnut) and hidden pearl carriers are explored only when a parent shows silver or pearl, like PATN1. Typical
  pairings take 10-300 ms.
- `DnaTest` reveals pmel, slc36a1 and pattern.mitf too. A horse tested before D-037 (eight loci) still counts as tested,
  so ledger validation of old saves is unchanged.
- The fair's pattern stream becomes `pattern.v2`: about 36% of offers carry one (tobiano 8, roan 5, grey 6, leopard 5,
  silver 2, champagne 3, pearl 2, sabino 3, splash 2). Silver needs a black or bay base and pearl a coat without cream,
  so the offer keeps its base class. Rare coats add +100 to the price.

## Rendering

- Silver: the catalog's `silverRecipes` give a chocolate, strongly dappled body on black and lighter chocolate points on
  bay, scaled by how dark the recipe is (nothing on creams); the mane and tail are flaxen-silver with dark strands.
- Champagne: lower roughness, a little metal and warm grazing glints, freckled pink skin round the muzzle and eyes, amber
  eyes, darker foals.
- Pearl and cream-pearl: pale apricot / very pale coats with pale skin; cream-pearl has blue-green eyes.
- Sabino-1: white climbs from below (stockings, belly, chin, broad irregular blaze) with speckled roaned edges and
  vertical spikes; SB1/SB1 is nearly all white. Splash: legs and belly "dipped" to a smooth crisp wavy line, a broad crisp
  blaze, coloured topline and ears; SW1/SW1 is far more extensive with blue eyes (a share of SW1/N horses also show blue
  eyes, a per-horse artistic roll). Extents come from `CoatLook.RareAmounts`.
- The silver probe becomes `probe.unknown-layer`; the unknown-recipe probe uses a recipe that really does not exist.

## Sources

UC Davis VGL pages: silver, champagne, pearl (coat colour overview), sabino-1 (KIT), splashed white (MITF), tobiano.

## Verification

See STATUS. Evidence: `docs/visual/evidence/coats-2026-10-14/d037_new_coats.jpg`.
