# UNMA v0.9.23 – Escalation and safe operator attention

UNMA v0.9.23 lets a persistent custom alarm escalate once after a selected
amount of Captain of Industry game time. The escalated state can use a higher
severity, a different sound, and a deliberately limited operator action.

## What changed in v0.9.23

- Custom alarms can escalate after an **AFTER** duration to a strictly higher
  severity.
- The escalation sound may inherit the base sound or use any available sound.
- Escalation starts a new occurrence, requiring acknowledgement again and
  leaving occurrence-bound audio snooze behind.
- An optional action opens the matching UNMA panel and scrolls to the alarm.
- A second action may also end only the temporary five-minute mute. It never
  moves the camera, opens an entity inspector, changes global audio, alters a
  per-slot snooze, acknowledges an alarm, or controls a machine.
- Built-in system-alarm stages expose the same safe actions when an already
  active alarm advances to a new stage.
- A bounded queue removes stale or acknowledged requests and selects the most
  severe, strongest, newest valid occurrence.
- Existing worlds migrate with escalation and actions disabled while all
  v0.9.22 timing memories continue unchanged.

## Compatibility and safety

- Captain of Industry: **0.8.6c**
- UNMA: **0.9.23**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Can be added to or removed from existing saves.
- Existing configurations migrate automatically. Escalation and operator
  actions remain opt-in, and saved alarm-timing state is preserved.

## Download and documentation

Download the current package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

Escalate the persistent fault, bring the right panel forward, and keep the
operator in control.
