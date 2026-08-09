# UNMA User Guide

UNMA (Universal Alarm Annunciator) adds a configurable industrial annunciator panel to Captain of Industry. It mirrors game notifications, monitors health, food, and workers, and lets you create alarms for individual buildings, vehicles, storages, conveyors, and pipes.

Supported game version: **Captain of Industry 0.8.6c**
Required dependency: **MultiLangLib 0.1.0 or newer**

## Installation

1. Place the complete `UNMA` folder in the Captain of Industry `Mods` directory.
2. Install and enable MultiLangLib 0.1.0 or newer.
3. Enable UNMA in the game's mod menu.
4. Load or start a game.

UNMA can be added to and removed from an existing save. Its settings and alarm rules are stored separately for each game world.

## Quick start

1. Press **F8** to open or close UNMA. You can also use the compact launcher on the left side of the screen.
2. Open the **ANNUNCIATOR** (`MELDETAFEL`) tab.
3. Select **HOME** to see all alarms that are currently active.
4. Select another panel to see its permanent alarm slots, including inactive and cleared alarms.
5. Press **MASTER QUIT / ACKNOWLEDGE** (`MASTER QUIT / QUITTIEREN`) to acknowledge all new or cleared-but-unacknowledged alarms and silence their sounds.
6. Open **HISTORY** (`VERLAUF`) to review previous alarm events.

The launcher is only visible while the main UNMA window is closed. Drag its arrow handle vertically to move it away from other HUD elements. Its position is saved.

## Understanding alarm states

| Code | Meaning | Display behavior |
| --- | --- | --- |
| `K` | Alarm has occurred and is not acknowledged | Flashes in its active color and repeats its sound |
| `KQ` | Alarm has occurred and is acknowledged | Remains active without repeating the sound |
| `KG` | Alarm has cleared but is not acknowledged | Flashes with black text on a white background |
| `KGQ` | Alarm has cleared and is acknowledged | Completed history entry |

Acknowledging an alarm does not remove its active color while the cause still exists. Completed `KGQ` entries remain in **HISTORY** until you explicitly delete them.

## Creating an alarm for a building or vehicle

Some controls currently use German fallback text even when English is selected. Their on-screen German labels are shown in parentheses below.

1. Select the building, storage, vehicle, conveyor, or pipe in the game.
2. Click the golden **UNMA alarm bell** in its inspector.
3. In the entity panel, click **+ NEW ALARM** (`+ NEUE MELDUNG`).
4. Enter the alarm text and choose its severity, active color, sound, and acknowledgement behavior.
5. Select a metric, such as stored quantity, capacity, fill level, or a product-specific amount.
6. Select one of the six comparison operators and enter the target value.
7. Add the condition to the rule.
8. Optionally select one or more global panels to show the same alarm there.
9. Save the alarm.

Double-click the resulting alarm slot to edit the rule later. The small arrow in an entity alarm slot centers the camera on the related object and opens its inspector.

### Relative percentage conditions

Select **% OF** and then choose a reference metric to compare a value with a capacity or another value. For example:

```text
Stored quantity % OF Storage capacity < 5
```

UNMA evaluates this as `current value / reference value x 100`. If the reference value is missing, zero, or negative, the condition is shown as unavailable and does not trigger.

### Rules involving several entities

1. Add the first condition in the alarm editor.
2. Select another entity in the game while leaving the editor open.
3. Click **USE CURRENT GAME SELECTION** (`AKTUELLE SPIEL-AUSWAHL ÜBERNEHMEN`) in the editor.
4. Add the next condition.
5. Choose **AND** if every condition must be true, or **OR** if any condition may trigger the alarm.
6. Save the rule.

If an entity used by a multi-entity rule is permanently demolished or destroyed, UNMA removes the complete rule so its logic cannot silently change.

## Panels

- **HOME** is a live overview. It shows only currently active alarms (`K` and `KQ`) and does not store permanent slot assignments.
- Global panels contain permanent, freely ordered alarm slots.
- Entity panels belong to one specific building or vehicle and are opened with the golden alarm bell.
- Use **+ PANEL** in **OPTIONS** (`OPTIONEN`) to create a global panel.
- Use the gear button next to a panel to change its name, columns, filters, automatic alarm sources, and slot order.
- Building alarms can be linked to global panels without creating a duplicate alarm state.
- Panels can be detached and moved inside the main UNMA window.

## Sounds and Vanilla notifications

Open **SOUNDS** (`TÖNE`) to configure known Vanilla and external-mod alarm types.

- Set an object-bound Vanilla alarm to normal, silent, or hidden either for
  only that object or for all objects of the same prototype.
- Choose its UNMA sound.
- Choose whether it is acknowledged automatically when it clears.

Silent alarms remain active and are logged without UNMA audio. Hidden alarms are also removed from HOME and active counters, while their history remains complete. Object-specific rules override prototype rules. These settings do **not** disable or modify the game's original notification.

UNMA includes a warning bell, an industrial horn, a motor siren, and several synthesized signals. To add a custom sound, copy a PCM WAV or Ogg Vorbis file to:

```text
UNMA/Sounds/
```

Restart the game after adding sound files. Use only audio that you created or are licensed to redistribute and use.

## System alarms

The **SYSTEM** tab contains the built-in health, food, and worker monitoring. Each alarm can be enabled, edited, or restored to its default settings.

The game's health value is not a normal 0-100 percent scale. A value of `10` is neutral, and a health-related population loss begins below `0`. UNMA therefore uses the completed monthly value and combines it with the available worker reserve when determining severity.

By default, **EMERGENCY** is reserved for an active hunger or health death spiral. Worker shortages escalate only to **CRITICAL**.

## Options and configuration

The **OPTIONS** tab lets you scale the complete UNMA interface from 75 to 200 percent. UNMA blocks clicks, drags, and mouse-wheel input inside its windows from affecting the game world behind them.

The mod's `config.json` provides these startup defaults:

| Option | Default | Purpose |
| --- | ---: | --- |
| `showOnGameStart` | `true` | Open UNMA after loading a game |
| `enableAudio` | `true` | Repeat alarm sounds until acknowledgement |
| `audioVolumePercent` | `65` | Set UNMA sound volume from 0 to 100 percent |
| `pollIntervalMs` | `500` | Set the interval for evaluating custom rules |
| `enableSystemAlarms` | `true` | Monitor health, food, and workers automatically |

## Saved data

UNMA stores world-specific data in `unma-world-<GameId>.json`. Panel layouts, custom rules, acknowledgements, and cleared-but-unacknowledged alarms survive saving and reloading.

If a configuration file is damaged, UNMA creates a backup and replaces it with safe defaults. Historical completed events remain available unless you delete them in **HISTORY**.

## Troubleshooting

- **UNMA does not load:** Confirm that UNMA and MultiLangLib are both enabled and compatible with the supported game version.
- **The launcher is missing:** Press **F8**. The launcher is hidden while the main UNMA window is open.
- **An alarm type is missing from HOME:** Open **SOUNDS** and check its object and prototype behavior. Some types appear in the list only after UNMA has encountered them once.
- **A product metric is missing:** Product-specific metrics are discovered from products currently or previously present in the selected object. A completely unused empty object initially exposes only general quantity and fill metrics.
- **A custom sound is missing:** Check that it is a supported PCM WAV or Ogg Vorbis file in `UNMA/Sounds`, then restart the game.
- **A percentage rule never triggers:** Verify that its reference metric exists and is greater than zero.

For integration with other mods, see [the external mod API documentation](docs/external-mod-api.md).
