# Stable yard integration

Goal: show a curated selection of the approved collection in the existing stable presentation.

Architecture: one reusable `StableYard.tscn`, instantiated by `Sets.Stable()`. Copy only the selected, byte-identical GLBs into the game resource root; embedded textures stay intact. Use existing Godot 4.7.2 .NET and C# code. Keep props outside the checked four-cycle horse route and leave barn doors clear. No new care, inventory, riding, physics, or save behavior.

- [x] Place 12 unique props in coherent barn-side feeding, grooming and seating groups; reuse planters and buckets where useful.
- [x] Import and render the actual stable scene; check meshes, ground placement, scale, intersections and walking-route clearance.
- [x] Run strict C# build, existing domain tests, spec validator, main Walk smoke and stable captures.
- [ ] Review the diff, document results in STATUS.md, push and merge after CI. An updated Mac export is a separate preview if native launch/signing cannot be verified here.

Alternatives: putting all 30 props in view would clutter this small set and load unnecessary textures. A separate gallery already exists. A small selection in the shared stable scene is the useful integration now.
