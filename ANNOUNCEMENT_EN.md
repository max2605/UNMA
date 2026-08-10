# UNMA v0.9.25 – Operational alarm areas

UNMA v0.9.25 adds user-defined operational areas for large annunciator setups.
Areas organize global panels and provide focused HOME, acknowledgement, and
next-alarm workflows without duplicating or changing alarm, history, historian,
or audio state.

## What changed in v0.9.25

- Select **ALL**, **UNASSIGNED**, or a named area above the panel bar.
- A filtered HOME collects active alarms from the area's member panels and
  deduplicates them into one tile per underlying alarm.
- The selected area chip reports panel, active-alarm, and unacknowledged-alarm
  counts.
- **AREA ACK** and **AREA NEXT** stay inside the selected view, while
  **MASTER ACK** remains an explicit global action.
- Acknowledgement remains global alarm state. If the same alarm is visible in
  several areas, acknowledging it in one area acknowledges it everywhere
  without creating another occurrence or history event.
- **MANAGE AREAS** creates, renames, reorders, and deletes areas through one
  atomic draft. Deleting an area only moves its panels to **UNASSIGNED**.
- Panel settings assign an area directly. Panel clones inherit their source
  area; new panels inherit a concrete current area and otherwise start
  unassigned.
- Unsaved panel and area drafts are protected before closing or switching.
  Narrow windows and 200-percent content scale use stacked, scrollable layouts
  so the required controls remain reachable.

## Compatibility and safety

- Captain of Industry: **0.8.6c**
- UNMA: **0.9.25**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Can be added to or removed from existing saves.
- Schema 20 migrates every existing panel as **UNASSIGNED** and preserves the
  previous **ALL** behavior. Areas never duplicate alarm state, history, or
  audio.

## Download and documentation

Download the current package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

One alarm state, many useful operating views.
