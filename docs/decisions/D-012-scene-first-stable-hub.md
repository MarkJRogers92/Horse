# D-012 — Scene-first stable hub, horse portraits and Meshy scenery

Accepted by owner request on 2026-10-13.

## Question
Players' first impression of the stable is "a text game": every gameplay screen is
panels of words, while the real 3D horses only appear in a separate viewer. How do
we make the game look like a polished Steam horse game (owner reference: Rival Stars)?

## Chosen result
1. **Horse portraits everywhere.** Each horse's real coat, markings and life stage are
   rendered through the one `HorsePresenter` into a portrait texture. Portraits appear on
   roster cards, Details, breeding (mare and stallion side by side) and the sale offer.
   Portraits never substitute a coat: unsupported descriptors show a neutral silhouette.
2. **3D stable yard as the home screen.** The stable screen gets a live 3D yard behind
   the UI: barn, paddock, fences, props, sky and warm lighting, with the player's own
   horses standing and walking in it. Clicking a horse selects it and opens its card.
   The HUD becomes a top bar (season, day, coins) and a bottom dock of large buttons.
   This is presentation only: the domain, save format and day flow are unchanged.
3. **Meshy for scenery.** Environment props are generated with the owner's Meshy
   account through its web API (`tools/meshy/meshy.py`). The API key is a network
   secret injected by the environment proxy; it is never stored in the repository or
   printed.

## Showing saved horses before any coat is released
No coat class in `COAT_SUPPORT_MATRIX.json` is released yet, so the released presentation
path refuses every real horse, and the stable has been text-only. Portraits and yard horses
use `PhenotypeResolver.ResolveForStablePreview` instead. It runs the same genetics and
recipe resolution but skips the release check, and tags descriptors with
`stable-preview.unreleased.v1`. It does **not** promote the matrix, change what founders or
NPC stock may carry, or bypass the renderer's own refusals (unimplemented layers, recipes
or meshes still show a silhouette). Releasing coats remains a separate owner decision.

## Budget
- At most **300 Meshy credits** for this decision (owner, 2026-10-13). Check in with
  the owner at **150** cumulative before spending further.
- Balance before work: 1,164 credits. No subscription, credit purchase or other paid
  service is authorized.
- Every task is recorded in `art/scenery/meshy/ledger.json` with its prompt, task IDs
  and the actual balance before and after. Spending is measured from the balance, not
  from published price tables.
- The D-007 horse budget is separate and unchanged.

## Licensing
Meshy assets generated on the owner's paid plan are owned by the owner and may be used
commercially (Meshy help-centre articles cited in D-007). Generations are made private
where the API allows. No third-party images are uploaded; prompts are authored text.

## Consequences
- Generated GLBs live in `game/assets/scenery/` with a provenance entry per asset.
- The existing procedural `Sets.Stable()` remains the fallback when an asset is missing,
  so CI and captures keep working.
- Stable captures gain hub screenshots; visual CI stays the acceptance gate.
