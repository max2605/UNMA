# UNMA v0.9.26 – Incident Lens and alarm pressure

UNMA v0.9.26 adds a dashboard-only Incident Lens for recognizing temporal
alarm bursts without inventing a root cause. It combines scope-aware active
clusters with a deliberately separate global pressure indicator, and every
result remains read-only.

## What changed in v0.9.26

- Active alarms whose raise times are no more than two game days apart are
  grouped into a temporal incident cluster.
- **FIRST SIGNAL** means only the earliest observed member. Temporal
  correlation is not a confirmed cause, root cause, or dependency.
- Clusters respect **ALL**, **UNASSIGNED**, and every operational-area filter.
- Global pressure covers the last ten game days regardless of the selected
  scope. Notice, Warning, Critical, and Emergency occurrences contribute 1,
  2, 4, and 8 points respectively.
- Pressure is **NORMAL** below 8, **ELEVATED** from 8, **STORM** from 16, and
  **SEVERE** from 32. The summary also separates recent occurrences from
  distinct alarm IDs.
- **EXPAND** shows up to six incident cards and eight members per card while
  retaining counts for additional results.
- **FOCUS** navigates only to a still-visible member and its object where
  available. It never acknowledges, hides, clears, deletes, or silences an
  alarm and never changes history or audio state.

## Performance and safety

- Incident results are transient derivations from current alarm and history
  snapshots. No result or new configuration field is persisted.
- The UI requests at most one snapshot per frame and filter.
- A revision-bound immutable cache prevents unchanged frames from rescanning
  history. Global pressure uses at most the newest 8,192 occurrences, while
  sorting and analysis run outside the alarm lock. Continuous revisions fall
  back to a coherent uncached result after at most two attempts so rendering
  always progresses.
- Captain of Industry: **0.8.6c**
- UNMA: **0.9.26**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Can be added to or removed from existing saves.
- No new schema migration is required for 0.9.26.

## Download and documentation

Download the current package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

Correlate the timeline, keep causality honest, and leave alarm state untouched.
