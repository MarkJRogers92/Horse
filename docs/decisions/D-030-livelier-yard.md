# D-030 — A livelier yard

Presentation only (game side). No domain, save or contract change.

## Decision

Yard horses get small procedural life on top of the checked Idle clip, and settle by weather and family.

- **Grazing.** After 8 to 20 s of cumulative standing rest a horse lowers its head for 4 to 8 s (eased in 1.5 s, out 1.1 s,
  a small chew bob). A graze already under way finishes before the horse walks on. Occasional small head lifts (1.6 to 2.6 s,
  every 14 to 30 s) and single ear flicks (every 5 to 12 s) fill the gaps.
- **Tail swish.** A gentle travelling sway along the tail chain, with a stronger swish every 6 to 14 s.
- **How.** `HorseIdleLife` is a `SkeletonModifier3D` child of the Idle rig's `Skeleton3D`, so it runs after the AnimationPlayer
  and costs about 15 bone writes per resting horse per frame. Bones driven (production rigs): neck `Neck_Base, Neck, Neck_Mid,
  Neck_Upper, Neck_Top, Head_Base, Head` (the foal lacks Mid/Upper/Top), tail `Tail_01..Tail_05`, ears `Ear_L, Ear_R`.
  Axes are derived from the rest pose, so no per-rig constants.
- **Safety with the checked walk.** It exists only on the Idle model of a presenter that opted in (`EnableIdleLife`, called by
  `StableYard` when wandering; portraits and fixed captures never do). The yard clears `Calm` before any walk or turn and waits
  for the offsets to fade before starting the Idle/Walk bridge; `TransitionTo` also clears it, and it is never attached to a Walk
  model. So offsets are zero during Walk and every model swap. Photo mode keeps it.
- **Foals follow their mare.** `StableDay.Hub` passes foal id to mare id (a parent with the Mare role who is also in the yard).
  A foal's wander targets stay within 2.5 m of her (or of where she is heading), and a resting foal sets off when she walks away.
- **Rain shelter.** On rain days (snow in winter uses the same state) spot choice picks, from the usual candidates, the one
  nearest the barn and stable block side (most negative Z). Resting horses are nudged to move when the rain starts.

## Checks

`YardWalkProbe` now also asserts: no idle life on a Walk model, zero offset in every bridge, horses graze, the foal stays near
its mare, and horses shelter toward the barn in rain, on top of the existing pace and no-teleport checks.

## Not done

Lying down: no rest or lie clip exists, and no new skeletal animation is invented.
