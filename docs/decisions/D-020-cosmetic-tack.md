# D-020 — Cosmetic tack (dress-up)

Owner instruction 2026-10-13: "keep going" (standing permission to refine and add features).

Players can now personalise their horses, which suits the cozy direction (the owner's wife wanted
something less like a text game).

- **Domain (`Tack`).**
  - Two slots: saddle pad and mane bow. Each has six pastel colours.
  - Three colours unlock from what happened in the stable. They are derived from the save, so there
    is no inventory to keep:
    - a gold pad and a gold bow after winning the Willowbrook Cup;
    - a daisy pad after the first homebred foal.
  - Choices live in the horse's existing `HorseRecord.CosmeticState`. The save codec already persists
    that field, so the save schema does not change.
- **Saving.** `AtomicSaveStore.CommitTack` is free.
  - It checks the owner, the save revision and that the item is valid and unlocked.
  - It goes through `SaveSnapshotUnlocked`, so the ledger, care and conceptions provably stay unchanged.
  - Wearing what is already worn is a no-op.
- **Presentation.**
  - `Tack.Layer` makes a canonical cosmetic layer ("bow=bow.rose;pad=pad.mint"). It is passed to
    `ResolveForPresentation`, so portrait and descriptor cache keys change with the look while the
    coat and markings stay the same.
  - `HorseTack` (game) draws the pieces inside the one `HorsePresenter`:
    - **Pad:** a curved two-tone sheet (cream trim) draped on the back at the saddle position.
    - **Bow:** two loops, a knot and two tails on the poll.
  - **Placement:**
    - Pieces are placed in skeleton space from the skin's bind pose and measured from the body's own
      vertices. A bone-alias table covers the production, walk and foal rigs.
    - Each piece is then attached to the nearest spine bone (or the head), so it follows every
      animation.
    - The pad grows with the chest-width and muscle channels so it never sinks into wide bodies.
  - `VisualCatalog` no longer reports known tack layers as "ignored".
  - `scenes/tools/TackPreview.tscn` renders every family at neutral and at both combined shape
    extremes, idle and walking, from two views.
- **UI.** A "Dress up" button in Details opens a dialog:
  - a large live portrait;
  - swatch rows for the pad and the bow, plus "none";
  - locked colours greyed, with a tooltip saying how to unlock them.

  Every change saves at once and updates the yard and portraits.
