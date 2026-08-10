# UNMA v0.9.22 – Stable alarm timing and per-slot audio snooze

UNMA v0.9.22 gives operators control over when an alarm becomes active, when
it clears, and which single alarm should stay quiet for a while. All timing is
based on Captain of Industry's game calendar and remains independent from
frame rate or wall-clock time.

## What changed in v0.9.22

- Every custom alarm has an activation delay, reset delay, and optional
  minimum active time.
- Every built-in system-alarm stage has the same independent timing controls.
- Numeric conditions support hysteresis for all six comparison operators,
  preventing noisy repeated transitions near a threshold.
- Existing worlds keep their immediate behavior because all migrated values
  default to zero.
- Running timers and condition latches persist per world and resume safely
  after loading. Game-clock rollback is handled without inheriting invalid
  elapsed time.
- Display-only edits such as text, color, and sound no longer reset active
  timing state or create artificial history events.

Alarm tiles now provide a compact audio action between acknowledgement and
navigation:

- **Z** snoozes that slot for one game month.
- **R** resumes its audio immediately.
- Aggregated object slots apply the action to every represented occurrence.
- Snooze never acknowledges, hides, clears, or changes the sound assignment of
  an alarm, and it does not alter counters or history.
- A genuinely new alarm occurrence is audible again even if an older sequence
  was snoozed.

## Compatibility and safety

- Captain of Industry: **0.8.6c**
- UNMA: **0.9.22**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Can be added to or removed from existing saves.
- Existing configurations migrate automatically with immediate timing
  defaults; runtime timing state is saved only for matching rule definitions.

## Download and documentation

Download the current package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

Debounce the threshold, snooze the noisy slot, and keep the factory moving.
