# Project status

**Last updated:** 2026-10-14, D-039–D-043 publication approved; review branch and CI verification in progress.
**Working branch:** `gpt/stable-life-upgrades` from the local hair checkpoint `4838a56`; owner approved push, merge to `main`, and a new Mac download on 2026-10-14. Remote `main` was verified at the original baseline `4091290` before publication. **Previous branch history:** `claude/loving-dijkstra-82tjzx` (animation playback fixes, fast-forwarded from `claude/eloquent-davinci-o1hhr6` c223a2e, which contains `main`). D-012 work continues on `claude/eloquent-davinci-o1hhr6`. Previous: `claude/relaxed-ride-9c5tto`. It contains every other active branch: `codex/horse-production-library`, which itself contains `claude/godot-horse-visual-pilot` and `claude/great-euler-hq83x3`, plus a merge of `rea/horse-coat-preview`. The animation archive (`archive/horse-animation-experiments-2026-10-10`) is intentionally not merged.
**History:** the full checkpoint log up to 2026-10-12 is in [`docs/history/STATUS_LOG_to_2026-10-12.md`](docs/history/STATUS_LOG_to_2026-10-12.md). Search it with `grep -n '^#'`. Keep this page short: replace sections instead of stacking new ones on top.

## D-044 generated concept artwork checkpoint (2026-10-18)

Owner authorized saving generated assets and integrating them into the games. Review branch `codex/higgsfield-portrait-gallery`, based on playable `main` at `93f569b`. Adds Extras concept-art gallery with three preserved originals and a cropped stable backdrop; owned-horse genetic portraits remain unchanged. Source hashes, defects and unverified release rights are recorded. No save schema or domain changes.

Verified on cloud: 342/342 domain tests (single-node run), game warnings-as-errors build 0 warnings/errors, frozen-spec validator, 3 original-integrity tests and 31 headless gallery navigation/layout checks. Final headless run exited cleanly after explicit audio teardown. Native rendered verification remains unavailable in this cloud display environment; CI includes rendered gallery captures. Initial solution-wide parallel test invocation hit a sandbox pipe error; targeted single-node domain run passed.

Next: publish this review branch and verify visual CI before merge or distribution. No release build or merge is claimed.

## D-040–D-043 local stable-life review (2026-10-14)

Owner approved reversible pasture, visible personalities/social interactions, optional hands-on grooming with quick care retained, and weather/landscape atmosphere. The planner is excluded. This branch builds on D-039 at `4838a56`. The owner approved publishing and merging on 2026-10-14 after reviewing the delivery. No purchase, new generated asset or save-schema change.

- **D-040 pasture:** the roster's Pasture dialog moves owned adults between the active stable and free cooperative pasture. Horses remain owned with their family, coat, bond, training, tack and care history. Returning needs a free active place; unborn foals and pending holds still reserve places. Pregnant/recovering mares and dependent young stay together. Atomic transfers are destination-idempotent, and generic saves cannot bypass eligibility. Pasture horses age without active upkeep or care changes and return to normal activity eligibility.
- **D-041 yard life:** personality-shaped reactions, deterministic companion approaches/greetings and gentle yielding use the existing actors and animation constraints. No inherited genetics, bond reward, saved social graph or care mechanics change.
- **D-042 grooming:** “Groom by hand” shows the horse's real coat with three removable dusty patches. Pointer/touch strokes and accessible buttons share progress. Cancel/Escape saves nothing. Finish uses the existing once-per-day brush bond; the original quick Brush and daily-care toggle remain. Save-response failures reload/reconcile instead of applying a second brush, and superseded sessions close safely.
- **D-043 atmosphere:** layered landscape/path colour, bounded foliage wind and weather-driven puddles/ripples extend existing shaders and yard presentation without extra draw surfaces or particles.
- **Verified:** full Release domain suite **342/342**, including 23 pasture cases; game warnings-as-errors build **0 warnings/errors**; validator OK; grooming-session probe and real StableLifeCapture checks for cancel/reopen, pointer input, accessible buttons, interrupted saves before/after atomic replacement, stale eligibility, exactly-once brush and quick care. Red/green evidence retained for Escape, uncertain-response reconciliation and stale-session closure.
- **Yard verification:** atmosphere 62 checks, social rendered probe 166,524 checks, locomotion 71,379 checks over 390 simulated seconds. Rendered evidence uses fixed-clock captures with settled sky lighting.
- **Evidence:** `docs/stable/evidence/stable-life-2026-10-14/` (pasture/grooming/screens and verification logs), `docs/visual/evidence/stable-life-2026-10-14/yard/` (weather, personalities, interaction and locomotion probes). Primary verification logs: `verification/domain-tests-fresh.log`, `verification/game-build.log`, `verification/stable-life-headless.log`. The older stable-day smoke passes its state assertions but emits the existing audio-shutdown warnings; the new stable-life probe explicitly tears down test audio and exits cleanly.
- **Next:** publish the combined review branch, verify GitHub CI, merge to `main`, then verify the automatically generated Mac artifact and deliver its exact download link. No Mac installation is requested.

## D-039 review branch — procedural mane and tail (2026-10-14)

Owner authorized dot-cloud implementation and native Astra Extra High assistance, then approved push and merge after visual review on 2026-10-14. Baseline is `40912902b4de3327de3807071f6264c065f6b319`; other ongoing Horse work is out of scope.

- Runtime-generated layered locks preserve original hair cores/tack landmarks, skinning (including eight-influence adult Walk), blend shapes, body UVs and every hair colour/pattern uniform.
- The underlayer no longer interprets projected source UVs as strand coordinates. Foal hair is shorter and based on its actual crest/dock geometry. No speculative extra neck-bone rotations.
- Local branch `gpt/mane-rework`; no domain, save or source asset changes. Meshy spend: 0 of the owner-approved 150-credit maximum.
- Final-source revalidation: 319/319 domain tests, game build 0 warnings/errors, validator OK; 1,465,366 hair checks, 3,708 transition checks and 95,442 yard checks passed. Final rendered smoke and stable-day/lifecycle/breeding captures are retained.
- Eight-horse 720p software-renderer comparison: median frame time +27.0%, p95 +32.3%, draw calls 70 to 102. **Performance gate is not cleared; no target Mac GPU measurement.** The live yard has six slots.
- Review limitations: smoother opposite-side core and stepped foal locks during deep grazing. Full findings: `docs/visual/evidence/mane-hair-2026-10-14/REVIEW.md`. Source/review package excludes preexisting assets and is based on the original handoff commit. Publishing is now approved; the original review below retains its verification-time context.
- Decision: `docs/decisions/D-039-mane-hair.md`. Review evidence: `docs/visual/evidence/mane-hair-2026-10-14/`.

## What the game does today (Godot 4.7.2 .NET, `game/`)
- **Manage stable:** a saved stable with daily care, running costs and **50 coins/day of club support income** (D-004, D-008).
  - The player can breed their own horses for a 50-coin fee (D-006); the foal is then born, named and grows up (D-005). One atomic save, with exact-retry safety.
  - Each horse card has **Details**: visible coat, family, recorded DNA tests and care, from the domain `HorseProfiles`.
  - **Pip the rabbit** (drawn in code, talks with a typewriter bubble) runs a skippable six-step guide from meeting the horses to the first foal, and greets players on the main menu.
  - **Practice gallop** is a free, deterministic 800 m race driven by developed traits (D-009). Each horse's first gallop of the day saves gallop training (D-011).
  - **Selling:** adult horses you bred can be sold to the Willowbrook Riding Club from Details (D-010). Starter horses stay.
  - **UI:** every screen uses the shared `CozyTheme`: Nunito font (OFL, `game/assets/fonts/`), puffy pill buttons, hand-drawn toggles and checkboxes, ribbon-titled dialogs with Pip peeking in.
- **Foaling demo:** a separate sandbox save that shows a committed pregnancy through to birth and growth.
- **Horse viewer:** Light and Stock adults and the foal with coats and markings, plus a neutral Walk and Idle (D-007, Meshy-based library). A macOS export exists; it still has the earlier animation.
- **Domain (`src/`, pure C#):** phased coat genetics, pedigree, eligibility, forecasts (including the gray base-coat class), conception, the save codec, the atomic store, horse profiles and the practice gallop.

## Task board position
Live per-task status is in [`docs/TASK_STATUS.json`](docs/TASK_STATUS.json); the frozen board still says "proposed".

| Done (scoped slices) | Partly done | Not started |
| --- | --- | --- |
| P0-03, P1-04–P1-07, P1-09, P2-02, P2-07 | P0-02, P1-01/02/03/08 (D-007 art), P1-10, P2-01 (Details), P2-03 (income), P2-05 (practice gallop), P2-06 (owned parents), P2-08 (guide) | P2-04 estate, P2-09, P3+ |

## Current work: D-012 scene-first stable hub (owner request 2026-10-13)
Goal: stop the stable looking like a text game. (1) portraits of every horse, (2) a 3D yard home screen with the player's horses, scenery from Meshy.
- [x] D-012 decision record, Meshy budget 300 credits (check in with owner at 150). Ledger: `art/scenery/meshy/ledger.json`.
- [x] `tools/meshy/meshy.py` (key is an environment network secret, never in the repo). Barn generated (30 credits).
- [x] Domain `PhenotypeResolver.ResolveForStablePreview` + test (214/214 locally). No matrix promotion.
- [x] Fence, oak tree, hay props, water trough generated (30 credits each). **Spend is 150/300: owner check-in reached; ask before any further generation** (tool refuses without `--past-checkpoint`). Textures resized to 1024 (42 MB to 6 MB).
- [x] Portraits: `HorsePortraitStudio` (private SubViewport through HorsePresenter, cached by descriptor) + `portrait_card.gdshader`. Shown on roster cards, care sections and Details.
- [x] 3D yard hub: `StableYard` (Meshy scenery with procedural fallbacks, saved horses in paddock slots, click a horse to open Details and focus the camera). New layout: top bar, roster left, care/breeding right, Pip top centre, bottom dock. Captures wait for portraits.
- Verified locally: 214/214 tests, game `-warnaserror` build, stable-day, lifecycle and breeding captures all OK. Evidence: `docs/stable/evidence/2026-10-13-hub/` (00_before vs 01_hub).
- [x] Owner raised the budget to about 500 after the check-in and merged to `main` (07e91e5). Spent 300/500: saddle stand, signpost (real "Cloverbrook Stables" text via Label3D), show jump, wheelbarrow; a flowering bush was rejected (shard foliage, files not kept).
- [x] Breeding review shows mare ♥ stallion = ? foal portraits; sale offer shows the horse. Pip sits beneath dialogs.
- [x] Yard life: horses rest, then turn in place (Walk is refused for non-neutral body shapes by the checked-walk rule, so travel only happens for neutral horses). Raked-sand shader, grass tufts and wildflowers. `--hub-preview=DIR` writes live yard screenshots (game time) plus a UI-free view.
- [x] Practice gallop drawn as a racetrack (`RaceLane`): sand lanes, rails, chequered finish, bobbing portrait tokens, place rosettes. Replays the same domain frames.
- [x] End-of-day transition: golden hour, night card ("Good morning! Season N · Day N" + birth news), sunrise; yard sun dims with it. Skipped in captures; `--hub-preview` records it (05-07).
- Owner tip: Meshy's web app gives 4 free retries per task; the API charges each attempt. For assets that need retries, hand the owner prompts for the web app and import their GLBs (Drive or GitHub upload).
- [x] Title screen rebuilt on the yard with three sample horses (neutral fixtures, so they can walk), big Play button, Extras row (viewer, coat studio). `--smoke-test` keeps its original stage. `--menu-preview=FILE` screenshots it.
- [x] Illustrated icon set from Meshy text-to-image (3 credits each, `meshy.py image`): feed, bedding, turnout, grooming, coins, day, breeding, gallop, sell, end day, foal. Used on HUD pills, care toggles and main buttons. "Review day" button renamed "End the day". Spent 333/500.
- [x] Dialog header illustrations (Meshy images, 3 credits each, `game/assets/ui/headers`): breeding (pair portraits sit on the art), details, sale, gallop, day review; birth art ready. `CozyTheme.DialogHeader` + `rounded_image.gdshader`. Spent 351/500.
- [x] Birth celebration: after the night card, "A foal is born!" dialog with the stall illustration, foal portrait, parents and coat, confetti and a name box (uses the existing rename save). Live play only. `--hub-preview=DIR --foaling-demo --hub-birth` records it.
- Verified locally at this checkpoint: 214/214 tests, validator OK, game `-warnaserror` build, all three stable captures, main-menu smoke test. Evidence refreshed in `docs/stable/evidence/2026-10-13-hub/`.
- [x] Yard feel: floating name tags with hover highlight and pointer cursor, heart/sparkle burst when comfort care is switched on, drifting blossom petals, Meshy planter boxes and a bench (30 each). Spent 411/500.
- [x] D-013 (owner approved "all 4"): phase-1 coats released in the matrix (game copy + validator check; stable uses the released path, preview workaround removed). Walk allowed with every body shape after the extreme-shape audit; ground speed scales with height. Smoke test updated. 213/213 tests.
- [x] D-014: original synthesized music, ambience and SFX (`tools/audio/synth.py`, `game/assets/audio`), `GameAudio` with auto button clicks and a ♪ mute toggle; sounds on care, day end, coins, birth, sale and the race.
- [x] D-015: Blender-authored gallop (Light + Stock, animation-only GLBs) and a 3D practice race (`RaceView`) above the lanes. Review sheet in `docs/visual/evidence/gallop-2026-10-13/`.
- [x] Polish: breeding pickers show names under MARE/STALLION captions, gallop picker shows portraits, race camera stops short of the line so the finish is in view, shutdown leak warnings fixed. Meshy tool now enforces the owner's 350-credit reserve (balance 863). A topiary attempt failed at Meshy (foliage mesh repair), not charged.
- [x] D-016: seasons in the yard (spring blossom, summer, autumn leaves, winter snow; HUD "Spring · Day N" + year) and a Stable Journal of 12 ribbons derived from the save, with a "New ribbon" toast.
- [x] Title screen shows the player's own saved horses (read-only `AtomicSaveStore.Load`), "Continue"/"New stable", Pip greets them by name, in the saved season. Settings window (music and effects volume on separate buses, fullscreen), remembered in `user://settings.cfg`; themed sliders.
- [x] Details shows ability stars (Speed, Acceleration, Stamina, Calm) after a horse's first gallop; daily state-driven tips replace the fixed note after the guide; Meshy stable block in the yard and a pavilion beside the race finish (30 each, balance 803). Static texture/scene caches are released on exit.
- [x] Name your stable (shown on the signpost, top bar and title; `stable.cfg` beside the save, live play only), "Idea" name suggestions for foals and stables, results podium after the gallop, roster-card hover lights the horse's name tag.
- [x] D-017: weekly Horse Fair. Domain `HorseFair` gives three deterministic adult offers per week, each with a released phase-1 coat and its own ability markers. `AtomicSaveStore.CommitPurchase` writes one idempotent ledger debit plus a founder record, and the save rejects orphan purchases. A "Horse fair" dock button opens portrait cards with a Buy button. Journal gains a Coat collection tab (13 coats, first horse per coat) and the "Fair Find" and "Colourful Herd" ribbons. Meshy header and icons (fair, journal) cost 9 credits; balance 794. 218/218 tests.
- [x] D-018: daily yard weather (presentation only, deterministic per save and day). Sunny, cloudy and rain (snow in winter) change the sky, fog, sun and shadow softness, add rain streaks, and show in the HUD ("YEAR 1 · RAINY"). A synthesized rain ambience crossfades with birdsong. New effects: a ribbon sparkle, a journal page swish and a hoof clop when you click a horse. `--hub-preview` now also captures the weather, the fair, a purchase and both journal tabs. Verified locally: 218/218 tests, validator, `-warnaserror` build, all three stable captures OK, smoke OK. Evidence 16–19 in `docs/stable/evidence/2026-10-13-hub/`.
- [x] D-019: weekly Willowbrook Cup. One adult horse a week races five visiting rivals for 160/90/45 coins (20 entry). The race uses the practice-gallop physics; the field improves week by week. The save holds deterministic ledger entries with integrity checks, and entering counts as the day's training. The gallop dialog has a Practice / Cup switch showing the visiting field, and the race plays after it is saved. Adds a "Cup Champion" ribbon and a Pip tip. Meshy fair tent (opens the fair) and trophy stand (appears after a win) in the yard; balance 734. 222/222 tests.
- [x] D-020: free cosmetic tack. Saddle pads and mane bows come in 6 pastels, plus gold (after a cup win) and daisy (after a first homebred foal). They are stored in `CosmeticState` and changed with `CommitTack`. `HorseTack` draws them on every rig (production, walk, foal), placed from the bind pose and body vertices and attached to bones, and it follows the body-width morphs. "Dress up" in Details opens swatches and a full-body portrait. `scenes/tools/TackPreview.tscn` is the visual review. 225/225 tests.
- [x] D-021: hands-on care. Click a horse for Pat, Brush or Carrot; each raises bond once a day and shows hearts. Bond makes horses calmer and quicker in gallops (practice-gallop v3). Hearts appear on cards and in Details, plus a Best Friends ribbon and a Pip tip.
- [x] D-022: Stable Shop. Build 2 extra stalls (450/800); the yard gets an annex and capacity rises for breeding and the fair. 11 decorations (5 new Meshy models) stand at 8 yard spots, with Move. Purchases are validated ledger debits, so there is no save-format change. 229/229 tests.
- [x] D-023: photo mode (orbit, zoom, next subject, Snap to Pictures/Cloverbrook Stables). Merged to main (b224c5f) after green CI.
- [x] D-024: coat review. Each horse gets its own deterministic shade within its colour (light to dark bay, sorrel to liver), plus sooty and dapple amounts (`CoatVariation.cs`). Cream and perlino colours recalibrated, the dun dorsal stripe sharpened, leg bars limited to the knee and hock, and dun heads darkened. `scenes/tools/CoatGallery.tscn` is a contact-sheet tool. Roadmap in D-024; first item is trunk canonical coordinates. Evidence: `docs/visual/evidence/coats-2026-10-13/`.
- [x] D-025: body map. `BodyMap.cs` adds rest-pose body coordinates (CUSTOM0 u/h/s from the canonical UV2 landmarks) to every Body mesh at load, cached per mesh, with no asset change. The coat shader uses them for seamless round dapples, a mottled sooty gradient from the topline, a dun dorsal stripe wide enough to see, a faint shoulder stripe and leg bars moved above the knee and around the hock. CoatGallery gains `--only/--family/--debug/--set`. This is the base for roan, pinto, appaloosa and grey (needs domain work). Verified locally: 229/229 tests, `-warnaserror` build, smoke OK, all three stable captures OK. Evidence: `docs/visual/evidence/bodymap-2026-10-13/`.
- [x] D-026: tobiano, roan, progressive grey and leopard complex (LP, PATN1) are released as overlays over every phase-1 base, plus cream-dun stacking (dunalino family, double cream dun). This fixes starter dun × cream foals that showed as silhouettes. Genetics: `pattern.eca3` gains TO/Rn with no block-layout change, so saves validate unchanged; the resolver is additive (still phenotype.v1); grey.v2 age curve. The fair offers patterns about one horse in three (own random stream, price bonus). Body-map shaders for all four patterns, grey/white locks in manes and tails, 6 cream-dun recipes, 38 reference fixtures, a 19-coat Journal collection, and a silver probe replacing the grey probe. 242 domain tests.
- [x] D-027: the breeding review lists foal coat possibilities, most likely first, with cozy wording and honest ranges from what the player can see (no hidden genes leak). Domain `FoalCoatOdds` uses visible-consistent scenarios and per-block enumeration and runs off the main thread. Details explains grey, tobiano, roan and appaloosa. 245 domain tests. Verified locally: `-warnaserror` build, smoke OK, all three stable captures OK (the breeding capture asserts the list), and the capture sets (clay, sheet, detail, appearance, markings, turntable, pair, stable, anim, probes, foal_idle) all OK. Evidence 27.
- [x] Mac download: the artifact always has the fixed name `Cloverbrook-Stables-macOS`, the installer always uses `~/Applications` (macOS blocks Terminal writes to /Applications), and INSTALL.txt has a one-line install/update command. Owner runs the game from Home > Applications.
- [x] D-028: DNA tests at the vet (60 coins, once per horse) reveal a horse's true coat calls into `PlayerKnowledge.RevealedCalls`, so foal coat odds become exact; Details button, friendly "Known genetics" wording, Gene Detective ribbon, Pip tip; no save schema change (`docs/decisions/D-028-dna-tests.md`).
- [x] D-029: weekly Coat & Turnout Show. `HorseShow` domain (judge's card out of 100, five deterministic rivals, 15-coin entry, 120/70/35 prizes), `CommitShowEntry` with ledger validation, a Horse show dialog, four new ribbons and a Pip tip. See `docs/decisions/D-029-coat-and-turnout-show.md`.
- [x] D-030: livelier yard (presentation only). Resting horses graze, lift their heads, flick ears and swish tails (`HorseIdleLife` bone modifier, zero during Walk and swaps); foals stay within 2.5 m of their mare; horses shelter on the barn side in rain. `YardWalkProbe` extended. See `docs/decisions/D-030-livelier-yard.md`.
- Verified D-028 to D-030 together: 257 domain tests, `-warnaserror` build, smoke OK, all three stable captures OK, and the hub preview (24 to 28: DNA button and results, show entry and result, grazing). Evidence: `docs/stable/evidence/2026-10-13-hub/d028-30_*`. Status messages now stay within two lines in the dock.
- [x] Owner-reported fixes: the care bubble that could not close once all three actions were used (inverted clamp threw before the bubble was tracked) and top-bar buttons cut off past the music toggle (Foaling demo is now an icon, so Menu fits). Verified in the hub preview (07d, plus an asserted close) and in a 1280x800 day capture; smoke OK.
- [x] D-031: Pip's daily tasks. Three deterministic tasks a day (two care tasks at 10 coins, one harder at 15: love every horse, or the week's show or cup on a fair week's first day) with a streak bonus of 5 coins per consecutive day (max 35). Domain `DailyTasks`, `AtomicSaveStore.CommitDailyTaskReward`, ledger credits validated by `StableSnapshot`, no schema change. A checklist card under Pip's bubble and two journal ribbons. See `docs/decisions/D-031-daily-tasks.md`.
- [x] D-033: more dress-up. Four new free cosmetic slots on every rig: turnout rug (navy, forest, cranberry, lavender, cream, plus a winter-only Snowflake rug; it hides the saddle pad), halter, braids (button or ribbon) and tail bow. The Dress up dialog scrolls six swatch rows beside the portrait, "Fashion Show" journal ribbon, `TackPreview` takes filters. No save schema change; pad/bow-only horses keep their layer string. See `docs/decisions/D-033-more-dress-up.md`, evidence `docs/visual/evidence/tack-2026-10-14/`.
- [x] D-032: seasonal festivals. A festival in the third week of each season (Blossom Fair, Summer Picnic, Harvest Festival, Winter Lights) with a once-a-day festival treat (bond like a carrot, stored in TrainingState, `CommitFestivalTreat`), four reward tack items unlocked by treats on 3 days (flower crown, sunflower tail bow, harvest braids, fairy-light halter), yard decorations, a festival pill, toast and Pip tips, a festival daily task and two journal ribbons. No schema change. See `docs/decisions/D-032-seasonal-festivals.md`.
- Verified D-031 to D-033 together: 270 domain tests, `-warnaserror` full rebuilds, smoke OK, all three stable captures OK, the hub preview (four festival yards) and the `--hub-festival` run (toast, treat button, festival task, crown unlocked, Menu visible). Festival tack rendered on all four families. Evidence: `docs/stable/evidence/2026-10-13-hub/d032_*`.
- [x] D-035: horse personalities (Cuddly, Cheeky, Shy, Greedy, Brave, Dreamy), derived from id and appearance seed with 60% parent inheritance, no save change. Small bond multipliers (Cuddly x1.25 pat/brush, Greedy x1.3 carrot/treat, Shy x0.8 then x1.2 after 2 hearts), Brave +1 show judge whim; trait in Details and roster cards, personality care reactions, Pip tips, yard emotes, "Getting to Know You" ribbon. See `docs/decisions/D-035-personalities.md`.
- [x] D-034: family tree. Domain `FamilyTree` and `CoatStory` (visible-phenotype coat sentence; hidden genes only for DNA-tested parents), a Family button in Details opening a three-generation portrait tree (click a relative to move up, sold horses marked), and a "Three Generations" journal ribbon. No save change. Captures `24b_family_tree.png` (hub preview) and `06b_family_tree.png` (breeding).
- [x] D-036: polish pass. Grazing now reaches the grass (muzzle 0.04 to 0.13 m, shoulder dip and foreleg stance, `GrazePreview` tool), fuller turnout rugs (withers to croup, chest strap, surcingle, binding), fold-away task card (`tasks.cfg`), and synthesized horse sounds (nicker, whinny, stamp, chew). Evidence in `docs/visual/evidence/polish-2026-10-14/`.
- [x] D-037: the rarer coat families are released. Silver, champagne, pearl and cream-pearl, sabino-1 (SB1) and splash white (SW1) compose over every base, dilution and D-026 pattern; eight new matrix classes, additive `phenotype.v1` (4320 previously-resolving genomes verified identical), foal odds, DNA test, fair offers, Journal (25 coats) and body-map shaders. See `docs/decisions/D-037-rarer-coat-families.md`.
- [x] D-038: coat realism. Bay is red-brown with black points (dark end `#4a2615`), chestnut copper with a liver end `#5a2e1c`, silver manes creamy flaxen as thin strands, and the SW1/SW1 splash edge is lower with a gentle irregular wave. Verified locally: 319/319 domain tests, validator OK, game `-warnaserror` build (0 warnings), smoke OK; renders sampled for colour and checked by eye. Evidence: `docs/visual/evidence/coat-realism-2026-10-14/`. See `docs/decisions/D-038-coat-realism.md`.
- [ ] Next (owner request 2026-10-14, after usage resets ~Oct 19): mane and tail rework. Current cards look patchy (neck shows through gaps, flaxen/silver manes read as blotches). Plan: denser layered cards with better coverage and a dark/coat-coloured underlayer, strand texture and directional sheen in `horse_hair.gdshader`, plus the deferred D-038 hair-texture normal layer on the coat.
- [ ] Owner: bush/foliage via the Meshy web app's free retries (prompt in the hand-off); everything else in the D-012–D-015 list is done.

## Animation playback fixes (owner request 2026-10-13)
Fixes how the existing Idle/Walk clips are played; no new clip, mesh or rig, and clip deformation is unchanged (owner art review still pending). Details: `docs/visual/HORSE_ANIMATIONS.md`, last section.
- [x] Pace matches ground speed: `HorsePresenter.SetPace`/`EffectiveSpeedMetersPerSecond`; yard horses ease in and out and slow for turns and arrival, so feet no longer skate.
- [x] Walk starts/stops use the rest-pose bridge (`TransitionTo`), now for both adult families as well as the foal, instead of reloading the model.
- [x] `HorsePresenter.CanWalk`, missing-clip warning, idle breathing desynchronised across yard horses, viewer veil across the bounded walk route's wrap.
- [x] New `YardWalkProbe`; `FoalTransitionProbe` now covers adults; both run in `visual.yml`.
- Verified locally (Godot 4.7.2 .NET, llvmpipe, this cloud container): game `-warnaserror` build; `YardWalkProbe` 30276 checks (51 walks in 240 simulated seconds, max pace error 1.3e-5 m/s, worst Walk hand-over foot slip 5 mm) and it fails (model swap outside the bridge) when run against the previous `StableYard`; `FoalTransitionProbe` 3646 checks over all four families; rendered main-scene smoke (`[smoke] OK all four family mappings ...`); stable-day, lifecycle and breeding captures OK; domain suite 214/214; validator OK. Adult rest swap: 0.19 mm raw (probe bound 0.5 mm for adults, foals stay 0.1 mm), 0.003 mm weight-normalised (unchanged 10 micrometre gate). Remote CI not run yet for this branch.
- Visual CI on `323f6c0` (the final code) passed; the earlier WIP checkpoints `50f6546`, `6b742b5` and `af00dac` failed the visual workflow because of probe bugs fixed in later commits (`af00dac`: a speed-bound bug in `YardWalkProbe`). `6b071d1` merges `main` `8daa372` into this branch (no conflicts; yard probe 30276 checks pass on the merged code).
- **Coordination (2026-10-13):** the "Game UI visual polish" session on `claude/eloquent-davinci-o1hhr6` was asked by the owner to add walking for every horse (lifting `HasNeutralWalkShape`) and a gallop, using Blender/Meshy. Proposed split: that session owns assets, Blender/Meshy and new clips (`game/assets/horse/**`); this branch owns runtime playback code and the probes. It was told to keep `HorsePresenter.CanWalk` as the single gate, scale ground speed by model scale before allowing shaped walks, and run `YardWalkProbe`, `FoalTransitionProbe` and the smoke test. Merging either branch into `main` is the owner's call.
- Not done: trot/canter/gallop/graze clips and Walk for non-neutral body shapes (need the D-007 art track), a 3D horse in the practice gallop, horse-to-horse avoidance while wandering. Open question for the owner: the catalog uses the `foal-walk-anatomy` profile speed (0.4828 m/s) while the shipped foal Walk GLB matches the `foal-walk-hind-geometry` candidate; confirm that pairing is intended.

## Next steps (before D-012)
1. **`main` branch and Mac install:** `main` is created from this branch once visual CI is green (owner-approved 2026-10-12). Every push to `main` runs `.github/workflows/macos-build.yml`, which uploads `Cloverbrook-Stables-macOS-<run>` (universal, ad-hoc signed, not notarized; see INSTALL.txt inside) to the run's Artifacts. It can also be started by hand from the Actions tab. The owner must make `main` the default branch in GitHub settings.
2. Rest of P2-03: more buyers with weekly demand, buying horses, visitor activities.
3. P2-05 settlement: entry fees and prizes need a persisted event record (save schema v4) through its own decision record. The practice kernel's parameters can be reused.
4. P2-01: adoption, and inspection in 3D through the horse presenter.
5. **Owner decision:** keep large evidence out of git. The `.git` pack is about 280 MB, mostly `docs/visual/evidence`. Options are Git LFS (has storage quotas) or keeping only CI artifact links. Shrinking existing history would rewrite it.

## Rules that still hold
- D-001 to D-011 are accepted.
- No frozen `handoff/` edits, no real coat-matrix promotion, and no purchases or deployments without owner instruction.
- One canonical horse record, RNG, resolver and save.
- Before pushing, run `python3 tools/validate_handoff.py` and `dotnet test Cloverbrook.sln -c Release`. For UI changes, also run the game build and the three stable captures.

## Running checks in a cloud session
- Microsoft's download host is blocked, but Ubuntu's `dotnet-sdk-8.0` apt package works.
- Godot 4.7.2 .NET downloads from GitHub releases (the same URL as `.github/workflows/visual.yml`).
- `apt-get install mesa-vulkan-drivers` provides the llvmpipe Vulkan driver CI uses. Run each stable capture with `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run ... --rendering-driver vulkan`.
- The breeding capture refuses an output folder that already exists.

## Latest verification
| Commit | What | Result |
| --- | --- | --- |
| `b278537` | REA gray forecast merge | Domain [38309981689](https://github.com/MarkJRogers92/Horse/actions/runs/38309981689) and visual [38309981500](https://github.com/MarkJRogers92/Horse/actions/runs/38309981500) passed |
| `7347f07`–`a2b5070` | Income, Details, guide, practice gallop | Domain CI passed on each push; visual CI results are checked before `main` is created |
| this commit | Sales, training, capture fail-fast, Pip and cozy UI | Local: 213/213 Release tests, game `-warnaserror` build, validator OK. Local Godot Vulkan: stable-day (practice + training), lifecycle, and breeding (sale for 335 coins, final coins 889) captures pass. Main-menu smoke test (`[smoke] OK all four family mappings`) passes. |

## Completion handoff — 2026-10-12 (Claude, second pass)
- **Domain:** `HorseMarket` (D-010) and `PracticeTraining` (D-011), plus store commands `CommitSale` and `RecordPractice`. The planner ignores sold horses for population and capacity. No save-format change.
- **UI:**
  - `CozyTheme` now styles every control type and provides `Dialog`, `Ribbon` and procedural icons.
  - `Bunny` and `BunnySpeaker` are in `game/scripts/UI/Bunny.cs`.
  - All dialogs (review, breeding, Details, practice, sale) share one style.
  - The guide is voiced by Pip. Captures check `_guideStep`.
  - The main menu is restyled.
- **Captures:** a refused setup now exits 1 immediately. Stable-day writes 6 screenshots; breeding writes 9, including the offer and after-sale views.
- **Not done:** recovery dialog still uses Godot's window chrome (rarely shown); horse viewer and appearance studio panels use the theme but were not redesigned.

## Completion handoff — 2026-10-13 (Claude, D-012 to D-015)
- **What changed:** scene-first stable (3D Meshy yard, portraits, click-to-inspect, name tags, care cheers, petals), illustrated icons and dialog headers, breeding/sale portraits, birth celebration, sunset day transition, title screen in the yard, phase-1 coats released (D-013), shaped Walk for every horse (D-013), original synthesized audio (D-014), Blender gallop + 3D practice race (D-015).
- **Tools:** `tools/meshy/meshy.py` (generate / image / balance; ledger `art/scenery/meshy/ledger.json`; refuses below the owner's 350-credit reserve), `tools/audio/synth.py`, `tools/horse/author_gallop.py` (bpy 5.2.2), `scenes/tools/WalkShapeAudit.tscn`, stable flags `--hub-preview=DIR [--foaling-demo --hub-birth]`, menu `--menu-preview=FILE`.
- **Cloud session setup:** `apt-get install dotnet-sdk-8.0 mesa-vulkan-drivers xvfb libgl1 libegl1 libegl-mesa0`; Godot 4.7.2 mono from GitHub releases; `pip install bpy==5.2.2`; Meshy key is a network secret on `api.meshy.ai` (downloads from `assets.meshy.ai`).
- **Known limits:** gallop is stylised and not IK-planted; foals have no gallop; Meshy foliage fails (bush/topiary) — use the web app's free retries; audio is synthesized (a licensed score can replace the files by name).

## D-045 feed trough checkpoint (2026-10-18)

D-044 gallery merged to main as 4449bd9 after domain and visual CI passed; rendered
gallery screenshots inspected. Current branch feat/feed-trough-scenery adds one
Blender-authored decorative trough through the existing scenery loader, with
original source and reproducible builder. No care/save/domain behavior changes.
Validation: two binary/registration integrity tests pass; game build has zero
warnings/errors; Godot 4.7.2 runtime yard check passes (new textured prop and
existing water trough both present). Initial runtime check raced asset import;
a rerun after import completed passed. Next: review actual
yard placement and publish the isolated branch for review.

D-045 additional asset: prepared wooden bucket placed beside supplies using the same loader. Height0.60m, 5,007triangles (seven above requested target), source norms retained. Existing care and water trough unchanged. Local game build passes with zero warnings/errors; final combined yard and integrity checks recorded below after execution.
Final combined validation: game build 0 warnings/errors, two integrity tests pass,
and FEED_TROUGH_AND_BUCKET_YARD_OK from Godot 4.7.2 after complete asset import.

## D-045 porch placement correction (2026-10-18)

Both asset PRs merged after CI passed. A dedicated real-renderer close-up found
these new props intersecting the barn porch at y=0. Ray/triangle checks measured
porch surfaces at y=0.3058 and 0.3095. Raise both to y=0.31 and move the bucket
left of the trough to avoid the saddle rack. Updated integrity test failed before
the fix, passed after; game build 0 warnings/errors and combined yard check pass.
Focused rendered recheck and CI are pending on qa/scenery-closeup.


## D-046 training equipment integration (2026-10-19)

- Owner approved the prepared props/jump-kit practice-area integration. Isolated branch `codex/practice-training-props` starts at porch-fix main `0fbc0fc`; the pre-existing dirty scenery checkout is preserved.
- Replaced the old decorative show jump with the native-scale jump assembly, two ground poles, one low cavaletti, three cones and a barrel. A small mown footprint keeps low poles visible; the fair tent moves 0.5m right. All equipment stays outside the paddock and clear of the barn path, purchased decor slots and existing scenery.
- No gameplay/collision/nav, save, domain, race, genetics or shared-contract changes. Prepared Blender sources and reusable jump GLBs are retained. Barrel reference-provider commercial rights remain unverified.
- Verified locally: 342/342 Release domain tests (`-m:1 -nr:false`), warnings-as-errors game build, Python scenery tests, handoff validator, and real-yard Godot headless placement probe. Red/green tests demonstrated missing-asset and missing-instance failures before implementation.
- Rendered validation is pending CI: local Xvfb sockets are blocked. The dedicated `Training prop placement` workflow captures a close-up plus normal yard-camera context and publishes `training-prop-placement`. Read `docs/decisions/D-046-training-prop-scenery.md` for scope and `docs/visual/evidence/training-props-2026-10-19/README.md` for reproduction.
- Handoff: implementation complete, review evidence/CI pending. Next: inspect CI screenshots, adjust any visual placement issues, verify terminal CI, and leave a draft PR for owner review. Do not merge or deploy.
