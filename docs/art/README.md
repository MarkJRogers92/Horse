# Generated artwork integration

See D-047 for the implementation boundary. The original source archives remain in the owner's Library. This review branch includes only 45 selected unique images; rejected, misleading, duplicate and superseded sources are documented in generated-art-audit.json, rather than inflating the runtime asset payload.

## Where the art appears

| Existing screen | Illustration | Presentation |
|---|---|---|
| Review breeding plan | 60 breeding planning | Small accent beside title; live parent previews retained |
| Birth announcement | 40 nursery | Small accent beside title; live foal preview retained |
| Sale offer | 57 marketplace | Full square vignette and native text |
| Practice gallop | 56 covered arena | Full square vignette; hidden during race playback |
| Daily review | 39 courtyard | Full square vignette and native text |
| Stable Shop | 49 tack room B | Small accent beside title |
| Dress up | 58 customization | Small accent beside title; actual tack preview retained |
| Cooperative pasture | 52 turnout | Small accent beside title |
| Extras → Concept portraits | All 45 selected | Breeds, young horses, stable life, coat studies; six per page |

The 21 adult breed illustrations are labeled as studies. Haflinger, Icelandic, Connemara and Percheron adult studies are held for correction, not silently filled with another breed. No illustration is a genetic reference or a portrait of a saved horse. Decorative coat studies use observable colour descriptions, rather than the unverified intended genetic labels. Detailed QA instructions remain in provenance, not the player-facing cards.

## Texture budget

Per D-048, the shipped PNGs are 768 px lossless copies (40.4 MiB, down from 141.5 MiB of full-size originals). The originals stay in the owner's Library archive and git history; each catalog entry keeps `source_sha256`/`source_width`/`source_height`. Godot's committed import settings still cap runtime dimensions at 768 square as a guard for future additions. Only six gallery cards are instantiated at a time; old page nodes are detached and freed. Eight contextual illustrations are mounted in existing dialogs. No eager texture catalog/static texture cache is introduced. This budget does not include the existing game assets, which are unchanged. Measured imported texture payload is 31.39 MiB (down from 101.76 MiB before the cap). Final export package impact requires a real export and is not claimed from source size alone.

## Validation status

Headless probes verify resources, full-image fitting mode, pointer pass-through, bounded gallery navigation/compact layout, and actual dialog construction. The visual CI workflow captures representative gallery and filled shop/pasture/daily-review dialogs. Native rendered verification is pending; cloud sandbox cannot open X display sockets. Do not equate headless layout checks with visual approval.

Commercial-release rights are unverified, as in the earlier concept gallery. No new paid generations or third-party license assertion.
