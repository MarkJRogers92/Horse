# Stable yard integration — November 11, 2026

Twelve reviewed models appear as thirteen static instances in the actual stable set. The menu, stable visit and stable-mode viewer reuse this set. The saved stable management screen remains its existing UI; this change does not add a free-roaming controller or interactions.

Four actual Godot 4.7.2 .NET Compatibility/llvmpipe captures show the barn, establishing view, overview and low paddock view. The supplied PNGs are engine renders, not generated images. The original first render exposed grass through the props; the final version clears vegetation from the sandy barn apron. A bounds check caught a wheelbarrow/feed-sack overlap, repaired before these final captures.

`yard-bounds.json` records nonempty finite imported geometry, ground placement (25 mm above the original terrain, on the apron), the grooming case resting on its 550 mm trunk, and clearance from the conservative horse-route volume x=[-1.5,5.5], z=[-1,1], y=[0,2.7]. Pairwise bounds in barn coordinates have no volume overlap above 5 mm. All thirteen instances pass. `capture-report.json` contains four captures and zero failures.

All twelve runtime GLBs match the reviewed collection byte for byte. Generated extracted JPEGs are ignored and regenerated from embedded maps at import; GLB import settings are tracked. Strict C# build, 190 domain tests and frozen-spec validator passed. Main four-family Walk/Idle smoke also passed. Ordinary Compatibility screen-AA and software-display warnings do not change these results.

No native Mac application was rebuilt or launch-tested for this pass. These are source integration and Linux runtime checks, not a finished riding/training game or whole-game performance certification.
