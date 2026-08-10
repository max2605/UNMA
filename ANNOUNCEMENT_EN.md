# UNMA v0.9.19 – Operator Controls and Configurable Keybinds

UNMA v0.9.19 makes alarm response faster and safer. Operators can acknowledge
one visible slot, one panel, or the complete system, cycle directly through
unacknowledged alarms, and keep every action available through the optional
Keybind Framework.

## What changed in v0.9.19

- Every unacknowledged tile now has its own **Q** button.
- **PANEL ACK** affects only alarms represented by the displayed panel, while
  **MASTER ACK** remains the explicit global action.
- **NEXT ALARM** cycles through the current panel, scrolls the selected tile
  into view, and focuses the associated game object when one is available.
- Aggregated object slots acknowledge all of their underlying real events and
  update each matching history sequence correctly.
- Alarm audio can be muted for five real-time minutes without acknowledging or
  clearing any alarm.
- Entity metadata now survives slot aggregation, improving navigation from
  dashboards and projected object panels.

## Optional Keybind Framework integration

With **Keybind Framework 2.0.2 or newer**, UNMA registers primary and secondary
bindings for the main window, global acknowledgement, next-alarm navigation,
and five-minute audio mute. The safe built-in defaults remain **F8** and
**Left Shift + F8**; global acknowledgement and mute start unbound. UNMA works
normally when the framework is not installed.

## Safer releases

Release builds no longer deploy into the active mod directory unless the
developer explicitly passes `-Deploy`. Deterministic packaging, archive
verification, language parity checks, and dependency-free core tests are now
scripted and covered by GitHub Actions.

## Compatibility

- Captain of Industry: **0.8.6c**
- UNMA: **0.9.19**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Can be added to or removed from existing saves.

## Download and safe update

Download the current package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating. World data
(`unma-world-<GameId>.json` and backups) and user-added sounds live in that
folder but are not included in the release ZIP.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

Keep the alarms loud when needed, quiet when intended, and the coal moving.
