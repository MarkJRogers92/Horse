# D-018 — Daily weather in the yard

Owner instruction 2026-10-13: "continue refining/adding features, visuals, systems, UI … use your own judgement".

- **Presentation only.** Weather changes how the yard looks and sounds. It does not affect care, costs,
  breeding or the gallop. It writes nothing to the save and does not use the domain RNG streams.
- **Deterministic.** Each day's weather comes from `Canonical.Hash("yard-weather.v1", SaveId, day)`, so
  reloading shows the same sky.
  - Rain chance by season: spring 22%, summer 12%, autumn 26%, winter 22% (in winter this is snow).
  - Cloudy chance: 25% in every season.
  - The first morning is always sunny.
- **Look:**
  - Clouds and rain grey the procedural sky, thicken the fog and dim and cool the sun.
  - Rain shows streaks falling over the paddock, and blossom and leaves stop drifting.
  - A winter "rain" day is a snowy one, with thicker snowfall.
  - The day transition's dawn rises into the new day's weather.
- **HUD.** The year pill reads, for example, "YEAR 1 · RAINY", "CLOUDY", "SUNNY", "SNOWY" or "CRISP".
- **Sound.** `tools/audio/synth.py rain` synthesizes an original rain loop (`ambience_rain.ogg`).
  `GameAudio.Ambience` crossfades between the yard birdsong and the rain.
- **More sound.** `synth.py extras` adds three effects:
  - a ribbon sparkle when the "new ribbon" toast appears;
  - a page swish when the Journal opens or changes tab;
  - a hoof clop when you click a horse in the yard.
