# D-017 — A weekly Horse Fair

Owner instruction 2026-10-13: "continue refining/adding features, visuals, systems, UI … use your own judgement".

Starter horses are both bay dun, so a new stable shows one coat until breeding. The fair lets players
buy horses with any coat that D-013 released, which gives coins a purpose and brings variety to the yard.

- **Offers (domain, `HorseFair`).** Each calendar week (day / 7) has three adult horses. Every offer is
  derived only from `Canonical.Hash("horse-fair.v1", SaveId, week, slot)`. Reloading, retrying or
  playing on another machine shows the same horses, and each slot shows a different coat.
  - **Coat:** each genome starts at the catalog baseline. Its MC1R, ASIP, SLC45A2 and TBX3 calls
    come from one of the 13 released phase-1 genotypes; dominant calls are sometimes heterozygous,
    which adds hidden breeding variety. Every other real locus stays at baseline.
  - **Abilities:** the fictional trait markers are randomised so fair horses differ in ability.
  - **Other details:** mare or stallion, a name from a pool, and an age of 10–19 days as an adult.
  - **Price:** 220–900 coins. It is a base price, adjusted for running ability from developed traits
    and for coat rarity (cream and dun dilutions cost more). The card shows these reasons.
- **Purchase.** `AtomicSaveStore.CommitPurchase` checks five things, then publishes atomically:
  - the requester is the owner;
  - the save revision is unchanged;
  - the offer is still listed;
  - the quoted price still matches;
  - there is a free stall (4 slots, the same count breeding uses) and enough coins.

  A purchase writes one `horse-purchase` ledger debit, whose ID is derived from the horse ID, and adds
  the offer's exact `HorseRecord` with origin `horse-fair-purchase` and no parents. A retry with the
  same terms returns the current save. `StableSnapshot` validation rejects an orphan debit or a bought
  horse without its debit.
- **Not resold.** The D-010 club buys only horses bred at the stable. That rule is unchanged, and bought
  horses say so. A bought horse is otherwise an ordinary founder: it gets daily care, gallops, breeds,
  and shows in the yard and the roster.
- **UI.** A "Horse fair" button in the bottom dock opens a dialog with:
  - a Meshy illustrated header and Pip's greeting, including the days until new horses arrive;
  - three cards, each with the real portrait, name, coat label, MARE/STALLION and price reasons;
  - a Buy button on each card that is disabled with the reason when there is no free stall or not
    enough coins.

  A purchase plays the coin sound and cheers the new horse in the yard. A new "Fair Find" ribbon joins
  the Journal.
- **Assets.** Meshy text-to-image created `header_fair`, `icon_fair` and `icon_journal` (9 credits;
  ledger updated). The Journal button now uses its own icon.
