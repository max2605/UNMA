# UNMA v0.9.24 – Instrument historian and directed ETA

UNMA v0.9.24 turns every instrument into a session historian. One shared view
combines the retained game-time chart with descriptive statistics, a robust
linear trend, and a deliberately bounded ETA toward the configured scale.

## What changed in v0.9.24

- **HIST** is available on every edgewise meter, round gauge, digital display,
  CRT, nixie tube, and paper recorder.
- Select one game day, one game month, one game year, ten years, one century,
  or the complete retained session. Chart and analysis use the same inclusive
  game-time window.
- The historian shows current, minimum, average, maximum, rate per game month,
  and the R-squared quality of the linear fit.
- Reliable rising trends project toward the scale maximum; falling trends
  project toward the scale minimum.
- Insufficient data, stable movement, unreliable fits, and ETAs beyond 100
  game years are stated explicitly instead of presenting false precision.
- A game-clock rollback starts a new in-memory history epoch, preventing
  samples from a future timeline from entering the analysis.
- A missing analysis does not hide the chart or current value when those are
  still available.

## Compatibility and safety

- Captain of Industry: **0.8.6c**
- UNMA: **0.9.24**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Can be added to or removed from existing saves.
- Existing configurations require no schema migration. Historian samples are
  intentionally session-only and restart when the world is loaded again.

## Download and documentation

Download the current package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

Read the process, judge the trend, and keep the operator in control.
