# Saved births and foal development

The lifecycle uses `StableDayService.Preview` and `AtomicSaveStore.CommitEndDay`, the same calendar and transaction used for care and daily costs. Preview reads the current snapshot without writes or random draws. Confirmation recalculates against the locked on-disk revision; care, costs, lifecycle outcomes, calendar and completion evidence publish as one save. Exact retry returns the original saved result.

## Compressed pacing

| Milestone | Saved game timing |
| --- | --- |
| Planned birth | Conception +4 days |
| Mare recovery ends | Actual birth +2 days |
| Foal becomes youngster | Actual birth +3 days |
| Youngster becomes adult | Actual birth +6 days |
| Adult reaches breeding age | Actual birth +8 days, with other eligibility checks |

The persisted calendar is zero-based; the screen displays one-based season days. Countdown text uses completed days remaining. Closing the application does not advance time. Stage and eligibility calculations use recorded conception/birth anchors; a juvenile missing its birth record cannot be assigned an invented age.

Birth activates the offspring already stored by the conception transaction. Its horse ID, phased genome, reproductive role, inherited/developmental factors, appearance seed, resolver version, origin and parents remain the same. The mare loses that pregnancy link and receives the recovery anchor. The placeholder name `Unborn foal` becomes `New foal`; other prepared names are preserved. Naming changes the current canonical name without editing historical parent/conception evidence. All four RNG streams remain unchanged by birth, growth and naming.

## Care and capacity

The reviewed care plan covers player-owned, active-stable horses born at the start of the day. The arriving newborn enters that set from the following day. Birth does not introduce another conception fee. Ordinary assistance, the wallet floor and the care rules remain those of the stable-day service.

Each committed conception already contains its consumed reservation and projected stage windows. The lifecycle validates the supported saved policy and that evidence before using it. A child contributes one stage obligation: pregnancy/family, foal/family, youngster or adult. Its adult occupancy replaces its future reservation in that projection. The mare remains an existing adult occupant throughout. The immutable conception capacity evidence is retained rather than consumed again.

This is a trusted committed-hold projection, not a facility capacity manager. Construction, adoption, home transfers and lease returns still require their own authoritative management rules. Missing or conflicting family/capacity evidence blocks the next day visibly. An overdue birth cannot be backdated to a calendar day that never completed.

## Compatibility and playable proof

The versioned save extension retains old daily-care receipt hashes and explicitly decodes genuine v1/v2 files. Migration preserves existing horses, conception evidence, wallet history, care and random streams; it does not invent historical lifecycle events. Each already-born committed horse receives its own captured legacy baseline. Its old age counter continues from that saved value, while stage changes and eligibility use the recorded birth day. New births derive age from their committed conception date. Generic same-day saves cannot rewrite those baselines, lifecycle fields or daily history.

The supported conception policy is `cozy.policy.v1` with `calendar.v1` and the 4/3/3 schedule. Other recorded policy/calendar versions remain loadable where their data is otherwise valid, but End Day refuses to interpret their offspring using current lifecycle rules.

The **Foaling demo** is a separately saved sandbox seeded from a historical committed test conception. Its source fixture and provenance are in `game/data/stable/`. It supports a text-only pregnancy → birth → naming → saved reload → youngster → adult playtest while the real catalog remains gated. No horse rendering or animation changes are part of this package. Runtime evidence and the actual persistence review are recorded in `STATUS.md` when verified.
