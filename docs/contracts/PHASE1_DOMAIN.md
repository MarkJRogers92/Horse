# Phase-1 domain implementation contract

P0-03 implements the narrowed scope in STATUS.md and review A3/A4. D-003 selects Godot; none of these types reference Godot or Unity. The frozen `handoff/` is not edited.

## Mechanical path mapping

The approved headless source path is `src/Cloverbrook.Domain/`. Old task-board `Packages/com.cloverbrook.domain/Runtime/<area>` ownership maps to that area's subfolder here; `Tests/Domain/<area>` maps to `tests/Cloverbrook.Domain.Tests/<area>`. This does not expand feature scope. GPT owns shared contracts during this continuation; Mark remains integration lead.

## C# 9 representation choices

Distinct immutable, Guid-backed identity value objects replace the proposal's C# 10 `readonly record struct` notation. Empty IDs are rejected; equality includes the concrete type. No `DateOnly`, file-scoped namespace, `required`, `IsExternalInit` shim, immutable-collection package or engine reference is needed. Collections are copied into private storage and returned as read-only wrappers; immutable element types must not expose mutable child state.

## Determinism v1

`Canonical.Bytes` writes a little-endian Int32 field count, then for each field its UTF-8 byte length as Int32 LE and its bytes. Callers include a type/version discriminator, sort unordered maps ordinally, and format numbers invariantly. `Canonical.Hash` is lowercase SHA-256 of these bytes, not a hash of JSON property order or .NET object hash codes.

`Seed128` is two UInt64 words rendered as 32 lower-case hex digits, high word first. `Seeds.Derive` hashes fields `cloverbrook.seed.v1`, seed, purpose, index and takes the first 128 hash bits. `CounterRng` takes the high 64 bits of a derivation with purpose `<purpose>/draw` and the current counter, then increments. Exhaustion throws instead of wrapping. Arbitrary-size bounds use masked rejection sampling; exact rational events compare a uniform integer against the numerator. Golden vectors were independently calculated using Python hashlib/struct and are checked by xUnit.

There is no global RNG. Breeding, world, competition and cosmetics have distinct purpose-tagged states. Forecasts use a local stream (or exact enumeration), never a persisted authoritative stream. `Advance` returns a new immutable state. A future transaction service, not these values, owns atomic persistence of that advance with the breeding event.

`Rational` uses reduced BigInteger fractions. `default(Rational)` means 0/1. Arithmetic is exact, including deep pedigrees; conversions to double are presentation/sampling-test conveniences only. Probability boundaries validate [0,1].

## Scope gates

Phase-1 records are the next P0-03 sub-step. Forecast knowledge projection, transaction execution, atomic file saving, economics and race/lease records remain in their later tasks. A pure conception draft must never be described as a persisted breeding transaction. No matrix class becomes live merely because its genetics tests pass.
