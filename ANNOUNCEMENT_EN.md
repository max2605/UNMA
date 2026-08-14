# UNMA v0.10.5 – Feedback-driven annunciator controls

Released: **2026-08-14**

UNMA v0.10.5 makes alarm floods easier to control, keeps the annunciator
stable and compact, and adds optional automatic pausing without changing the
public extension API or world-save schema.

## Audio control and identification

- **SOUNDS · ON/OFF** immediately mutes every alarm and preview sound for the
  current session without acknowledging, hiding, or changing alarms.
- Only active, unacknowledged alarms remain audible. A cleared `KG` event stops
  sounding immediately while staying available for acknowledgement and
  history.
- A blue card outline, **SOUNDS** marker, and clickable banner identify the
  exact severity, name, and stable ID of the alarm that is playing.
- Audio can optionally remain muted whenever the game is paused.

## Stable and compact annunciator

- HOME now sorts by severity and stable alarm ID, so acknowledgement, sequence,
  and time do not make otherwise unchanged cards jump around.
- Direct **COLUMNS −/+** controls persist the panel's column count, while **CARDS ·
  COMPACT/REGULAR** switches main and detached boards between 104- and
  142-pixel card heights for the current session.
- Notification behavior retains its object and prototype context when changed,
  and profile selectors show an explicit persistent `[X]` or `[ ]` state.

## Auto-pause, profiles, and pollution

- Optional auto-pause reacts once to each new, unacknowledged occurrence at or
  above a configurable severity. Vanilla, system, custom, and external sources
  can be enabled independently.
- The cross-save profile now defaults to
  `%APPDATA%\Captain of Industry\UNMA\profiles\default.json`. A legacy
  `%LOCALAPPDATA%` profile is copied atomically without deletion, and an
  explicit file or directory override remains available.
- Pollution remains part of **SYSTEM > HEALTH > POLLUTION CRITICAL**. Its
  factory condition is a pollution/waste contribution of `≤ −5` points.

## Compatibility and verification

- Captain of Industry: **0.8.6c**
- UNMA: **0.10.5**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Public extension API and assembly binding: **V1**
- World persistence schema: **20**; no migration is required
- Can be added to or removed from existing saves

All 21 language catalogs remain synchronized. MultiLangLib remains an external
dependency and is not bundled in the UNMA archive. The release passed 138,744
core assertions, the warning-free Release build, and the IL/reflection,
localization, rollback, and encoding checks.

## Download and documentation

Download the package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

One board, one history, one coherent operating picture.
