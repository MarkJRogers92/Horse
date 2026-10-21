# D-048: Ship runtime-size illustration copies

Owner approved on 2026-10-20. Amends D-047's "preserve source bytes" rule for
`game/assets/ui/illustrations/` only.

The 45 curated illustrations were committed at full size (1536 and 1254 px,
141.5 MiB) although their committed Godot import settings already cap them at
768 px. The extra pixels never reach the player, while every checkout and CI run
downloaded them.

- Replace each shipped PNG with a 768 x 768 lossless Lanczos copy (40.4 MiB total).
  The original files remain in the owner's Library archive and in git history.
- `catalog.json` records the copy's `sha256`, `width` and `height`, plus
  `source_sha256`, `source_width` and `source_height` for the archived original.
  Gallery `frame_inset` values are rescaled to the 768 px copies.
- The `size_limit=768` import cap stays as a guard for future additions.
- Not done: Git LFS or a history rewrite. LFS would spend GitHub LFS bandwidth on
  every CI checkout, and a rewrite needs a force-push across all branches. Full
  clones keep the old blobs in history; current checkouts and CI do not download them.

No change to which images ship, where they appear, labels, domain code or saves.
Commercial-release rights remain unverified, as in D-047.

## Verification

The Python art tests check copy hashes, 768 px dimensions and the source-hash
fields. The gallery and contextual-art Godot probes run unchanged. See STATUS.md.
