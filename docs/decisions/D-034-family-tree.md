# D-034 — Family tree and coat story

Owner request. Read-only and derived from the save, so there is no save schema change and no new stored state.

- **Domain.** `FamilyTree.For(state, horseId)` returns the horse, its sire and dam and four grandparents (`FamilyNode`: name, visible coat label from
  `PhenotypeResolver.VisibleLabel`, bond hearts, founder and sold flags). The dam is the recorded mare; an unknown or unrecorded parent is a null slot.
  Sold horses stay in the save, so they still appear, marked sold.
- **Coat story (`CoatStory.Pick`).** One short sentence, using only what the player can see on the parents (their visible coat).
  A parent's hidden genes are used only when that parent has had a DNA test (`DnaTest.IsTested`).
  - Tobiano, roan, grey, leopard and dun are dominant, so they must come from a parent: "Tobiano, like her dam Maple." / "Roan, from both ... and ...".
    If neither parent showed it, the line says so ("A surprise ..."); if a parent is unknown, nothing is claimed.
  - Cream: from a parent showing cream, else "carried it hidden" (or the tested carrier is named).
  - Chestnut needs red from both parents: a chestnut parent plus "the other parent carried red too", or two hidden carriers.
  - Founders and unborn foals have no story (the founder card says "Founding horse").
- **Game.** A "Family" button in Details (the button row was tightened so it still fits the 640 px dialog) opens a ribbon dialog with portrait cards joined by
  lines: horse, sire and dam, four grandparents, with "?" cards for unknown slots. Clicking a relative opens their tree; Back returns.
  `--hub-preview` captures `24b_family_tree.png`; the stable breeding capture writes `06b_family_tree.png` for the real foal.
- **Journal.** New ribbon "Three Generations": raise a foal whose grandparents were born at your stable.
