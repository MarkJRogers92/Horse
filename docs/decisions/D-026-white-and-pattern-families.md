# D-026 — Tobiano, roan, progressive grey, leopard complex and cream-dun stacking

Owner request 2026-10-13: "yes continue with what you need to completely finish the system for now". This builds on
the D-025 body map.

## Decision

Six matrix classes are released (`releasedBy: "D-026"` in `docs/contracts/COAT_SUPPORT_MATRIX.json` and its game
copy):

| Class | Layers | Genetics |
| --- | --- | --- |
| `dunalino_family` | cream_single + dun | Cr/N with D (dunalino, dunskin, smoky grullo) |
| `double_cream_dun` | cream_double + dun | Cr/Cr with D (cremello, perlino and smoky cream dun) |
| `tobiano_overlay` | white_tobiano | `pattern.eca3` TO, dominant |
| `roan_overlay` | roan | `pattern.eca3` Rn, dominant |
| `grey_overlay` | grey_age | `stx17` G, dominant, progressive |
| `leopard_complex_overlay` | leopard_complex | `trpm1` LP; `patn1` PATN1 widens the blanket to leopard |

Overlays compose over every phase-1 base, over the dilutions and over each other. Silver, champagne and pearl stay
unreleased and still refuse every combination; the silver probe now proves "never substitute" in the viewer, the
smoke test and the captures.

This also fixes a live bug. Starter bay duns × cream fair horses could already breed dunalino-family or
double-cream-dun foals, which rendered as a silhouette because those classes were unreleased.

## Genetics (domain)

- **The KIT region.**
  - `pattern.eca3` (the KIT-associated region on ECA3) gains the alleles `TO` and `Rn`. `baseline` stays first,
    because `SupportedAlleleIds[0]` is the default call.
  - The `eca3.core` block keeps its layout, with MC1R and zero recombination as a documented slice restriction, so
    every existing save validates unchanged. Tobiano and roan really are linked to extension; the zero is not an
    empirical estimate.
  - One haplotype carries at most one of TO and Rn.
  - Homozygotes are viable (modern evidence for both).
- **Resolver (still `phenotype.v1`).** The change is additive: every genome that resolved before resolves
  identically.
  - Grey and LP are no longer refused with cream or dun.
  - New layers and classes are added for TO and Rn.
- **Grey age curve `grey.v2`** (artistic, labelled):
  - foals keep their birth colour;
  - adults start at 3000 and add 55 per game day, capped below 9000: steel grey, dapple grey around day 40, light
    grey, then flea-bitten after about a game year;
  - seniors are 9000 or more.
- **Labels follow horse-people naming:** "Bay tobiano", "Bay roan", "Blue roan" (black), "Red roan" (chestnut),
  "Chestnut blanket appaloosa" / "… leopard appaloosa", "Black (progressive grey)", "Dunskin", "Perlino dun".
- **Fair.** About one offer in three carries a pattern:
  - tobiano 12% (15% of those TO/TO), roan 8%, grey 10%, leopard complex 8% (half with PATN1);
  - patterns are drawn from their own random stream, so each week's base coat, name, sex and abilities are unchanged
    for existing saves;
  - "Eye-catching pattern" adds +90 (tobiano, leopard) or +60 (roan, grey) to the price.
  - Cup rivals share the generator.

## Rendering (`horse_coat.gdshader`, `horse_hair.gdshader`)

All four patterns are drawn on the body map, so they are seamless and follow the skin while the horse walks or
changes shape.

- **Grey:**
  - white hairs arrive on the head first, then the coat becomes steel or rose grey;
  - next comes dapple grey: light rings on a darker net, warped so they are organic, not polka dots;
  - then light grey, and finally flea-bitten white with coloured specks;
  - lower legs grey last and skin stays dark;
  - the mane and tail grey a little later than the body.
- **Roan:** white hairs evenly through the body. The head, mane, tail and lower legs stay solid, and dark hair runs
  up the legs.
- **Tobiano:**
  - smooth, rounded white crossing the back between the withers and the tail;
  - white legs and white hooves under them;
  - a coloured head (normal face markings still apply);
  - coloured flanks and chest that shrink as the white grows;
  - the amount of white is per horse (minimal to mostly white, usually moderate);
  - some locks of the mane and tail are white.
- **Leopard complex:**
  - a white blanket over the hips and loin with dark oval spots;
  - the blanket size is per horse;
  - PATN1 widens it to a full leopard, white legs included;
  - striped hooves.

LP dosage art (fewspot, snowcap) is not modelled. It is noted in the matrix row.

Per-horse amounts come from `CoatLook.PatternAmounts(realisationKey)`, deterministic like the D-024 shade.

## Game integration

- `visual_catalog.json` and the walk-preview catalog implement the four layers and gain six cream-dun recipes.
- `VisualCatalog.Check` accepts LP when `leopard_complex` is implemented.
- The fixture export (`VisualDescriptorFixtureTests`) adds one reference per D-026 class at adult and foal ages (38
  fixtures). The gallery, viewer and captures list them automatically.
- The Journal's coat collection grows to 19. A pattern fills its own page and the colour underneath it.

## Sources

UC Davis VGL test pages:

- [Tobiano](https://vgl.ucdavis.edu/test/tobiano)
- [Roan](https://vgl.ucdavis.edu/test/roan)
- [Grey](https://vgl.ucdavis.edu/test/gray_copy_number)
- [Leopard complex](https://vgl.ucdavis.edu/test/leopard-complex)
- [PATN1](https://vgl.ucdavis.edu/test/appaloosa-pattern-1)
- [Dun](https://vgl.ucdavis.edu/test/dun)

Pattern placement follows the standard breed-registry descriptions (APHA tobiano, ApHC blanket and leopard) and the
grey ageing sequence (steel → dapple → flea-bitten).

## Verification

See STATUS for this checkpoint. Evidence: `docs/visual/evidence/patterns-2026-10-13/`.
