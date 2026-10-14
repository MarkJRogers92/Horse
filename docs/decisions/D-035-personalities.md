# D-035 — Horse personalities

Every horse has one of six personalities. It is derived from the save and never stored, so there is **no save schema change** and every
existing save already has personalities.

- **Derivation (`Personality`, domain).** The trait is `Canonical.Hash("personality.v1", purpose, horseId, appearanceSeed)` modulo six.
  A horse with a parent on record takes one parent's trait 60% of the time (`Personality.Inherits`, a hash roll from the foal's own id; a
  second roll picks which parent), recursing up the pedigree. Otherwise, or when no parent is in the stable's records, it is a fresh roll
  (`Personality.Fresh`). Traits: Cuddly, Cheeky, Shy, Greedy, Brave, Dreamy.
- **API.** `Personality.For(state, horse)` returns `PersonalityInfo` (id, label, an emoji-free description such as
  "Cuddly — loves pats and brushing", `LovesCarrots`, Pip lines and quirk lines, with `{name}` placeholders).
  `Personality.Reactions(trait, kind)` gives three care-reaction lines per trait and action.
- **Effects (all small).**
  - Bond gain multipliers (applied to the base gain before diminishing returns): Cuddly x1.25 for pat and brush; Greedy x1.3 for carrot
    and festival treat; Shy x0.8 while bond is 4000 or less (two hearts), then x1.2; Cheeky, Brave and Dreamy x1.0.
    `HorseBond.Version` stays `bond.v1`: only the amount added changes, stored bond values are plain numbers and stay valid.
    `HorseBond.GainToday(state, horse, kind, day)` reports the personality-adjusted gain; the old overload is the neutral gain.
  - Favourite treat: Greedy and Cheeky love carrots most (`LovesCarrots`; their carrot reactions say "loves carrots!").
  - Show: Brave horses get +1 on the judge's whim (0-5, capped at 5), for the player's horses and visiting rivals alike.
    `HorseShow.Version` is unchanged: a show is judged live from the same inputs.
  - The practice gallop is untouched.
- **Game.** Details shows "Personality: ..." under the coat line; roster cards append the trait to the stage line (card height
  unchanged); the care bubble status uses a personality reaction (deterministic by day); Pip gives an occasional personality tip;
  the yard shows a tiny emote above the name tag on a slow per-horse cadence (heart for Cuddly, also while focused; sparkle Dreamy,
  "!" Brave, "~" Cheeky, ellipsis Shy, "?" Greedy), one billboard label per horse and no animation; journal ribbon "Getting to Know You"
  for having all six personalities in the stable.
- **Tests.** `PersonalityTests`: determinism, even fresh distribution, the 60% inheritance rate, the modifier table, gains applied by
  `CommitInteraction`, and no extra stored keys.
